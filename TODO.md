# Avocado — Decisions & Roadmap

Case management (« suivi de dossier ») for French solo lawyers. Simple, modern, open-source, self-hosted.

This file records **decisions already made** and the **build order**. It is the source of truth for the
spec; anything not written here is not decided.

---

## 1. Product intent

- Target user: **avocat solo** (starting practice), and by extension their peers.
- Replaces the daily-use parts of Gestisoft / Secib, which are expensive and unpleasant to use.
- **UI/UX is the product differentiator.** Lawyers will judge it in the first 30 seconds.
- Self-hosted first. A cheap SaaS (5–10 €/month, cost-recovery only) only if there is traction.
- License: **AGPL-3.0** — see [LICENSE](LICENSE).

### Non-goals (explicit)

| Not doing | Why |
|---|---|
| Generating invoices / Factur-X | Lawyers already have an invoicing platform. Avocado produces the *content* to bill, not the invoice. |
| RPVA / e-Barreau integration | Closed system: certificate on a physical key behind a VPN, no third-party API. |
| Multi-device sync | Backups cover the real need. Sync is the reason a SaaS would exist, not a v1 feature. |
| Migration from Gestisoft/Secib | Not needed for v1. Revisit if peers adopt it. |
| Telemetry | None. Update check is opt-out. Worth advertising to this audience. |
| Multi-user / ACL | Deferred, but the encryption envelope is designed so it can be added without re-encrypting. |

---

## 2. Stack — locked

| Layer | Choice |
|---|---|
| Backend | **ASP.NET Core Minimal API**, C# |
| ORM | **EF Core** (SQLite provider) |
| Storage | **SQLite + SQLCipher**, one file = one vault |
| Frontend | **React + TypeScript** (Tailwind + shadcn/ui) |
| Desktop shell | **Electron** |
| License | AGPL-3.0 |

### Architectural rule: the shell holds no logic

The Electron shell may only: open a window, start/stop the backend process, auto-update, and provide OS
integration (file dialogs, notifications, tray). **Everything else goes through the same HTTP API a SaaS
would expose.** The shell must be replaceable in a weekend.

### Localhost API hardening

- [ ] Backend binds `127.0.0.1` only, on a **random port**.
- [ ] Shell injects a **per-launch bearer token**; API rejects requests without it.
- [ ] Rationale: without this, any web page in any browser on the machine can call the API
      (localhost CSRF / DNS rebinding).

---

## 3. Storage: the vault

A vault is a **folder**. Configure a folder, and that's the install.

```
MonCabinet/
├── avocado.db        # SQLCipher — dossiers, contacts, journal, temps, index (FTS5)
├── blobs/            # encrypted documents, content-addressed
├── vault.json        # KDF salt/params + wrapped DEKs (list, not scalar)
└── backups/          # rolling encrypted snapshots
```

- Structured data in SQLite; **large files as external encrypted blobs** (better streaming + backup
  granularity). Blob refs live in the DB.
- Full-text search via **FTS5** — works normally, since SQLCipher decrypts pages in memory.

### EF Core + SQLCipher gotchas (do these or lose the data)

- [ ] Use **`SQLitePCLRaw.bundle_e_sqlcipher`**, not `bundle_e_sqlite3`. The wrong bundle writes
      **plaintext with no error**.
- [ ] Do **not** use the connection-string `Password=` keyword (it issues `PRAGMA key='...'`, a *string*
      key run through SQLCipher's own KDF). Pass the raw 256-bit DEK: `PRAGMA key = "x'<hex>'"`,
      executed on the connection before any other command.
- [ ] Key every connection via a **`DbConnectionInterceptor`** on open. Pooled connections come back
      unkeyed → sporadic "file is not a database".
- [ ] **Copy the vault to `backups/` before every `Database.Migrate()`.** SQLite DDL is transactional so
      a *failed* migration rolls back — but a *successful* botched migration is unrecoverable, and this
      is the user's only copy of their practice.

### Longevity guarantee

- [ ] First-class, **CI-tested** « Tout exporter » → plain folder of PDFs + JSON/CSV.
- [ ] This is the answer to "what if Avocado dies", and doubles as RGPD portability (art. 20) and the
      déontologie obligation to hand the dossier back to the client.

---

## 4. Encryption

**Envelope scheme.** A single random 256-bit **DEK** encrypts everything and never changes (so nothing is
ever re-encrypted). The DEK is stored **wrapped** by one or more **KEKs** — each wrapping is an unlock path.

`vault.json` stores a **list** of wrapped DEKs from day one, even with two entries. That list is what makes
multi-user possible later with no re-encryption.

### Unlock paths

1. **Device key (default, and the only one most users see).** DEK wrapped by a KEK held in the OS keychain
   — **DPAPI** on Windows, **Keychain** on macOS. Double-click, the app opens. **No passphrase.**
2. **Recovery file.** A recovery KEK wrapping the same DEK. This is the disaster path.
3. **Passphrase (opt-in, off by default).** For users who want a prompt at launch.

### Threat model — write this in the README, don't oversell

Protects against: **stolen laptop drive, stolen backup file, stolen NAS, curious cloud-sync provider.**
Does **not** protect against malware or anyone already logged into the user's OS session — same model as
Signal Desktop and Chrome. That session already has a password.

### Recovery UX — the hard design problem

Lawyers lose codes and store them in `.txt` on the desktop. **Recovery is a file and a sheet of paper,
never 24 words to memorise.**

- [ ] Setup wizard: non-dismissable step, she picks **before** the app opens:
  - **Print it** → one-page PDF: large QR code + human-readable Base32 underneath.
    *"Rangez cette feuille avec vos documents importants."* Lawyers trust paper and have filing cabinets.
  - **Save to USB key** → enumerate removable drives and offer them **by name**
    (« Enregistrer sur E:\ — Clé USB SanDisk »).
- [ ] **Regenerate a fresh recovery file in one click** from Settings, any time the app still opens.
      This narrows total loss to *machine dead **AND** recovery file lost*.
- [ ] **Quarterly verification**: ask her to produce the file, do a **real test-unwrap**, nag until done.
      Recovery systems fail because nobody ever checked the artifact works.
- [ ] Wizard copy must use this framing: the recovery file is **what makes backups restorable**. A backup
      encrypted with a DEK that only exists in DPAPI on a drowned laptop is a useless file.

---

## 5. Tenancy: multi-tenant service, single-tenant storage

Decided now even though SaaS is hypothetical, because it costs nothing today and cannot be retrofitted.

**One SQLite file per lawyer, one DEK per lawyer.**

- The `WHERE tenant_id = …`-forgotten bug class **cannot exist**. That is the #1 SaaS breach category, and
  for data under **secret professionnel** it is the one that would end the project.
- RGPD erasure = delete a file. Export = send a file. Backup = copy a file.
- **The desktop app opens one vault; a server opens N.** Same `IVaultStore`, same migration loop, same
  everything — the SaaS becomes a hosting concern, not an architecture.
- Honest and verifiable claim to users: *« votre dossier est un fichier chiffré séparé, avec sa propre clé. »*

- [ ] Build the `IVaultStore` seam + `TenantContext` now; resolve it to "the one local vault" on desktop.

---

## 6. Data model

**Documents are not a journal type.** Collapsing them loses the bordereau feature. The journal is a *stream
of events*; a document is an *object with a life* — it arrives once, then gets re-scanned, communicated to
the adverse party, deposited at court, cited in conclusions. Several journal entries, one document.

```
Tiers            (personne physique | morale, N roles across dossiers:
                  client, partie adverse, confrère, magistrat, expert,
                  commissaire de justice, notaire)

Dossier          (réf auto 2026-0042, client(s), adverses + leurs avocats,
                  nature, juridiction, n° RG, date d'ouverture, statut)
├── JournalEntry (date, type, sens entrant/sortant, tiers?, texte, → Document[])
├── Document     (fichier/blob, type, nom, date)        — any file
├── Piece        (numéro, → Document)                   — a Document promoted to a numbered exhibit
├── Bordereau    (date, destinataire, → Piece[])        — immutable snapshot → PDF
├── Echeance     (audience, délai, date, rappel)
├── TempsPasse   (date, durée, tâche, facturable o/n, taux)
├── Provision    (date, montant)                        — advance payments received
├── Debours      (date, montant, nature)                — costs advanced on the dossier
└── Facturation  (date, montant HT, réf externe, payé o/n)  — tracking only, no generation
```

Key distinction in French procedure: **pièces are numbered and communicated with a bordereau; conclusions
and courriers are not pièces.** So `Document` is the file, and `Piece` is "this document, promoted to
exhibit n°7 in this dossier". The bordereau is an immutable snapshot of "pièces 1–12 communicated on date X".

---

## 7. v1 feature scope

- [ ] **Tiers** — personnes physiques & morales, with roles. Company auto-fill via
      **`recherche-entreprises.api.gouv.fr`** (open, no API key, good autocomplete; INSEE Sirene needs a
      key and adds little). Must degrade gracefully offline, and be **disableable entirely** for users who
      want zero outbound traffic.
- [ ] **Dossiers** — the central object.
- [ ] **Journal** — appels, mails, RDV, notes. This *is* « le suivi ».
      **Make it the single fastest interaction in the app.**
- [ ] **Documents** — drag & drop, typage, PDF preview.
- [ ] **Pièces + bordereau de communication** — numbering + PDF generation.
      Small to build, daily pain, this is the "oh, this is better than Gestisoft" moment.
- [ ] **Échéances / agenda** — audiences et délais, exposed as a **read-only ICS feed** so it lands on her
      phone. Read-only feed, not two-way sync: 10× cheaper, 90% of the value.
- [ ] **Temps passé** — timer + manual entry, taux horaire, facturable o/n.
- [ ] **Détail à facturer** — per-dossier: temps passé + débours − provisions déjà reçues = reste à
      facturer. **Not** an invoice; the content to paste into her invoicing platform.
      Without provisions tracking this number is simply wrong.
- [ ] **Recherche globale ⌘K** — instant, fuzzy, across everything. Alone, this will feel like a different
      century than Gestisoft.

### Backup (v1, not optional)

- [ ] Write a **single encrypted backup file to a user-chosen folder**. She points it at her existing
      Google Drive / OneDrive / Dropbox sync folder. **No OAuth** — works with every provider, a NAS, or a
      USB key, and nothing to maintain. Native providers only if someone actually asks.
- [ ] **Detect and refuse** a live vault located inside a sync folder — SQLite + mid-write sync = corruption.
      Backups go to the cloud; the vault never does.
- [ ] Restore wizard, requires the recovery file. **Tested in CI.**
- [ ] Warn if no backup in 7 days.

---

## 8. Deferred (post-v1)

- Modèles de courriers / publipostage (DOCX templating) — high value, likely v1.5.
- Calcul automatique des délais de procédure — high value **and** high liability. Advisory only, with an
  explicit disclaimer, if ever.
- CARPA / maniement de fonds.
- Export FEC / comptable.
- OCR + full-text of scanned PDFs.
- Portail client.
- Contrôle de conflits d'intérêts à la création d'un dossier.
- Multi-utilisateur + ACL.

---

## 9. Compliance notes

- **RGPD**: while self-hosted only, no obligation falls on the project as an OSS author. She remains
  *responsable de traitement* (her registre, her problem). If a SaaS ever ships, a real **contrat de
  sous-traitance (art. 28)**, a subprocessor list, EU hosting and breach notification become mandatory —
  even at 5 €/month.
- **Secret professionnel**: hosting jurisdiction and subprocessor list matter more than an E2EE badge.
  A browser SaaS cannot be honestly E2EE (it kills server-side search, PDF generation, password reset) —
  if it ships, it is *encrypted at rest with per-tenant keys*, and the claim must be worded as such.
- **No warranty.** Relevant above all if procedural-deadline calculation is ever built.
- **Trademark**: INPI/EUIPO search on "Avocado" (classes 9/42) before commissioning branding. Not blocking.

---

## 10. Build order

The vault is the only part that is genuinely hard to change later. Everything else is CRUD that can be
rewritten in an afternoon.

1. **Vault core** — envelope encryption, EF Core + SQLCipher wiring, migration-with-backup,
   `IVaultStore` seam.
2. **Recovery wizard** — print / USB, regenerate, quarterly verification.
3. **Backup + restore**, tested in CI.
4. Design system from Claude Design → see [docs/DESIGN-BRIEF.md](docs/DESIGN-BRIEF.md).
5. Electron shell + hardened localhost API.
6. Domain CRUD: Tiers → Dossiers → Journal → Documents.
7. Pièces + bordereau.
8. Temps passé + détail à facturer.
9. Échéances + ICS feed.
10. Recherche globale ⌘K.
11. « Tout exporter ».
