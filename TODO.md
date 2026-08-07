# Avocado, Decisions & Roadmap

Case management (« suivi de dossier ») for French solo lawyers. Simple, modern, open-source, self-hosted.

This file records **decisions already made** and the **build order**. It is the source of truth for the
spec; anything not written here is not decided.

---

## 1. Product intent

- Target user: **avocat solo** (starting practice), and by extension their peers.
- Replaces the daily-use parts of Gestisoft / Secib, which are expensive and unpleasant to use.
- **UI/UX is the product differentiator.** Lawyers will judge it in the first 30 seconds.
- Self-hosted first. A cheap SaaS (5–10 €/month, cost-recovery only) only if there is traction.
- License: **AGPL-3.0**, see [LICENSE](LICENSE).

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

## 2. Stack, locked

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
├── avocado.db        # SQLCipher, dossiers, contacts, journal, temps, index (FTS5)
├── blobs/            # encrypted documents, content-addressed
├── vault.json        # KDF salt/params + wrapped DEKs (list, not scalar)
└── backups/          # rolling encrypted snapshots
```

- Structured data in SQLite; **large files as external encrypted blobs** (better streaming + backup
  granularity). Blob refs live in the DB.
- Full-text search via **FTS5**, works normally, since SQLCipher decrypts pages in memory.

### EF Core + SQLCipher gotchas (do these or lose the data)

- [ ] Use **`SQLitePCLRaw.bundle_e_sqlcipher`**, not `bundle_e_sqlite3`. The wrong bundle writes
      **plaintext with no error**.
- [ ] Do **not** use the connection-string `Password=` keyword (it issues `PRAGMA key='...'`, a *string*
      key run through SQLCipher's own KDF). Pass the raw 256-bit DEK: `PRAGMA key = "x'<hex>'"`,
      executed on the connection before any other command.
- [ ] Key every connection via a **`DbConnectionInterceptor`** on open. Pooled connections come back
      unkeyed → sporadic "file is not a database".
- [ ] **Copy the vault to `backups/` before every `Database.Migrate()`.** SQLite DDL is transactional so
      a *failed* migration rolls back, but a *successful* botched migration is unrecoverable, and this
      is the user's only copy of their practice.

### Longevity guarantee

- [ ] First-class, **CI-tested** « Tout exporter » → plain folder of PDFs + JSON/CSV.
- [ ] This is the answer to "what if Avocado dies", and doubles as RGPD portability (art. 20) and the
      déontologie obligation to hand the dossier back to the client.

---

## 4. Encryption

**Envelope scheme.** A single random 256-bit **DEK** encrypts everything and never changes (so nothing is
ever re-encrypted). The DEK is stored **wrapped** by one or more **KEKs**, each wrapping is an unlock path.

`vault.json` stores a **list** of wrapped DEKs from day one, even with two entries. That list is what makes
multi-user possible later with no re-encryption.

### Unlock paths

1. **Device key (default, and the only one most users see).** Double-click, the app opens. **No
   passphrase.** DEK wrapped by a KEK that never leaves the machine:
   - **Windows**, DPAPI, scoped to the current user account.
   - **macOS / Linux**, a random machine key in the user's config directory at `0600`, outside the
     vault folder. *(`FileDeviceKeyStore`)*
   - [ ] **TODO: macOS Keychain** via Security.framework (`SecItemAdd` / `SecItemCopyMatching`), which
     adds per-application ACLs on top of the login password. Deliberately not written yet: ~250 lines
     of CoreFoundation interop guarding the key to the whole practice, unverifiable from a Windows dev
     box, where a bug is either silent data loss or a silent hole. What it would add over the current
     store is protection from another process running as the same user, which the threat model below
     already excludes. `IDeviceKeyStore` is unchanged when it lands, and existing vaults keep working
     through their recovery key.
2. **Recovery file.** A recovery KEK wrapping the same DEK. This is the disaster path.
3. **Passphrase (opt-in, off by default).** For users who want a prompt at launch.

### Threat model, write this in the README, don't oversell

Protects against: **stolen laptop drive, stolen backup file, stolen NAS, curious cloud-sync provider.**
Does **not** protect against malware or anyone already logged into the user's OS session, same model as
Signal Desktop and Chrome. That session already has a password.

The device key lives outside the vault folder on every platform, so copying the vault, to a backup, a
USB stick, a synced folder, never carries the means to open it. On a machine with FileVault, BitLocker
or LUKS on, a stolen disk is covered too; without full-disk encryption, the device key is readable from
the raw disk, and so, for practical purposes, is a DPAPI master key. Full-disk encryption is part of the
recommended setup, not an optional extra.

### Recovery UX, the hard design problem

Lawyers lose codes and store them in `.txt` on the desktop. **Recovery is a file and a sheet of paper,
never 24 words to memorise.**

- [ ] Setup wizard: non-dismissable step, she picks **before** the app opens:
  - **Print it** → one-page PDF: large QR code + human-readable Base32 underneath.
    *"Rangez cette feuille avec vos documents importants."* Lawyers trust paper and have filing cabinets.
  - **Save to USB key** → enumerate removable drives and offer them **by name**
    (« Enregistrer sur E:\, Clé USB SanDisk »).
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
  everything, the SaaS becomes a hosting concern, not an architecture.
- Honest and verifiable claim to users: *« votre dossier est un fichier chiffré séparé, avec sa propre clé. »*

- [ ] Build the `IVaultStore` seam + `TenantContext` now; resolve it to "the one local vault" on desktop.

---

## 6. Architecture: vertical slices

Layered projects buy nothing at this size. One file per use case, request DTO, validation, handler and
`MapXxx()` endpoint together.

```
src/
├── Avocado.Vault/                  # shared kernel, NOT a slice
│   ├── Crypto/                     # AEAD, KDF
│   ├── Keys/                       # DEK/KEK envelope, OS keychain, recovery file
│   ├── Storage/                    # SQLCipher connection, interceptor, migrate-with-backup
│   ├── Blobs/                      # content-addressed encrypted blob store
│   └── IVaultStore.cs              # the tenancy seam
├── Avocado.Server/
│   ├── Features/
│   │   ├── Contacts/  Matters/  Activities/  Documents/
│   │   ├── Deadlines/  Time/  Billing/
│   │   ├── Search/                 # read-only, queries FTS5 directly
│   │   └── Export/                 # reads everything
│   ├── Data/AvocadoDbContext.cs
│   └── Program.cs
└── Avocado.Cli/                    # recover / export / verify-backup
```

- **The vault is a separate assembly** because it is the security boundary (making "what touches keys"
  auditable), it needs its own test suite, and **the CLI must use it without the web host**, when the app
  won't start, `avocado recover` is the difference between "annoying" and "her practice is gone".
- **No MediatR.** Minimal API endpoints calling handlers via DI give identical cohesion without the pipeline
  indirection; middleware and endpoint filters already cover the cross-cutting concerns. (Its licensing also
  went commercial.)
- **The DbContext is horizontal and that's fine.** One context, but each `IEntityTypeConfiguration<T>` lives
  in its slice's `Infrastructure/`; `ApplyConfigurationsFromAssembly` collects them.
- **Join entities live with their aggregate root** (`MatterParty` in `Matters/`).
- **Tenancy plumbing lives in the composition root, once**: `TenantContext` → `IVaultStore.Open(id)` →
  keyed `SqliteConnection` → `AvocadoDbContext`. On desktop `TenantContext` is a constant. No slice ever
  thinks about it.

### Slice layout, vertical slices are not "everything in one file"

Every slice has the same shape. A file holds one thing.

```
Features/Billings/
├── BillingInvoice.cs                       one entity per file
├── BillingLedgerEntry.cs
├── Enums/                                  one enum per file
├── ValueObjects/BillingSummary.cs
├── Infrastructure/                         EF configurations, and any other infrastructure concern
│   ├── BillingInvoiceConfiguration.cs
│   └── BillingLedgerEntryConfiguration.cs
└── Endpoints/
    ├── BillingEndpoints.cs                 routing only
    ├── ListInvoices.cs                     one file per endpoint
    └── Dtos/                               request and response shapes shared across endpoints
```

Three naming rules, all of them about avoiding collisions rather than aesthetics:

1. **Namespaces are plural, always**, `Billings`, not `Billing`, even where English resists it. A
   namespace sharing a name with a type in it is a permanent source of ambiguity.
2. **Types are prefixed with the slice name in the singular**, `BillingInvoice`, not `Invoice`;
   `BillingLedgerEntry`, not `LedgerEntry`. Slices are effectively bounded contexts, and two of them
   will eventually both want an `Entry` or a `Summary`.
3. **Sub-namespaces follow the folders**: `Avocado.Server.Features.Billings.Infrastructure`.

Table names stay domain-natural (`invoices`, `ledger_entries`), the prefix solves a C# problem, and
carrying it into SQL buys nothing.

### Naming: code English, UI French

Nothing French crosses the API boundary. The backend sends enum keys (`IncomingLetter`); the React side owns
a single `fr.ts` label map. That rule is what stops the two languages drifting into each other.

| French | Code | Note |
|---|---|---|
| Dossier | `Matter` | The term of art in legal software. Not `Case`. |
| Tiers | `Contact` | It's an address book; *tiers* only means "third party" generically. |
| Pièce | `Exhibit` | Exact equivalent. |
| Temps passé | `TimeEntry` | |
| Échéance | `Deadline` | Lossy, also covers audiences, so `Type` includes `Hearing`. |
| Facturation | `Invoice` | |
| Provision | a `LedgerEntry` | Legal-English term of art: *retainer*. |
| Débours | a `LedgerEntry` | Term of art: *disbursement*. |
| N° RG | `CourtCaseNumber` | |

`Activity`, not `JournalEntry`: *journal* and *ledger* are both accounting words, and this entity has
nothing to do with accounting.

- [ ] Keep [docs/GLOSSARY.md](docs/GLOSSARY.md) current, when she reports « le bordereau ne marche pas »,
      the mapping to the English type has to live somewhere.

---

## 6b. Data model

```
Contact       Type (Individual | Organisation)
              [individual:   Civility, LastName, FirstName, DateOfBirth]
              [organisation: LegalName, Siren, LegalForm]
              Email, Phone, Address, Notes

Matter        Reference, Name, Description, OpenedOn, ClosedOn?,
              HourlyRateCents, CourtCaseNumber?
MatterParty   MatterId, ContactId, IsClient, Role (free text)

Activity      MatterId, OccurredAt, Type, ContactId?, Subject, Body
              Type: Call | IncomingEmail | OutgoingEmail | IncomingLetter
                  | OutgoingLetter | Meeting | Note | Hearing | Other

Document      MatterId, ActivityId?, BlobHash, FileName, SizeBytes,
              MimeType, DocumentDate, AddedAt,
              ExhibitNumber?, ExhibitLabel?

Deadline      MatterId, Date, Time?, Type, Label, RemindDaysBefore, IsDone

TimeEntry     MatterId, Date, DurationMinutes, Task, IsBillable,
              HourlyRateCentsOverride?, ActivityId?

LedgerEntry   MatterId, Date, AmountCents (signed), Label
Invoice       MatterId, Date, AmountExclVatCents, ExternalReference,
              IsPaid, PaidOn?
```

Eleven tables became nine. The decisions behind them:

- **`HourlyRateCents` is non-nullable and snapshotted at matter creation.** Never resolve it dynamically,
  a cabinet raising its rate must not silently reprice two years of history. `TimeEntry`'s nullable override
  falls back to the matter's frozen rate, so it cannot drift either.
- **`ClosedOn == null` *is* the status.** No status column needed for v1.
- **`MatterParty.Role` is free text** so a new role never needs a release, but `IsClient` stays structural,
  otherwise "who is this matter for" and "who do I bill" are unanswerable and the matter list has no
  Client column.
- **Direction is folded into `Activity.Type`**, not a separate field. It's meaningless for calls and notes,
  but for letters « envoyé le 12/03 » vs « reçu le 15/03 » starts délais and proves diligence.
- **`Exhibit` collapsed into `Document`** as two nullable columns, the relationship is 1:1. In French
  procedure, pièces are evidence *communicated to the other side*, numbered, and cited in conclusions
  (« la pièce n°7 »), with a label written for the judge, « Contrat de travail de M. Dupont du 12 mars
  2019 », not `scan_003.pdf`. Conclusions and client correspondence are never pièces.
- **`CourtCaseNumber` (n° RG)** is nullable, conseil, rédaction d'actes and transactions never go to court.
  It's kept for one reason: when the greffe calls they say the RG number, not the client's name. It is a
  **search key**. *(pending her confirmation)*
- **`Document.ActivityId?`** is a nullable FK, not a join table.

### The billing boundary, one rule

The fuzzy part, and where getting her workflow wrong makes the headline number silently wrong:

> **`Invoice`** = what has been billed (has an external ref and a paid state).
> **`LedgerEntry`** = money that moved *without* an invoice, provision reçue sans facture, débours,
> correction. Signed: **positive = received from the client, negative = advanced on the matter**.

Never enter the same money as both. Then:

```
Left to bill = Σ(billable time) − Σ(ledger entries) − Σ(invoiced)
```

- [ ] Never expose a raw signed field in the UI. Two buttons, **Encaissement** / **Débours**, that set the
      sign. Otherwise débours get entered as positive and every balance is wrong.
- [ ] Confirm this rule with her specifically; accounting habits vary a lot between practices.

### Storage footguns

- [ ] **Money as `long` cents, never `decimal`.** SQLite has no decimal type; EF stores it as TEXT, and then
      `ORDER BY amount` sorts lexicographically. Durations as `int` minutes for the same reason.
- [ ] **`Guid.CreateVersion7()` for PKs**, not int identity. Monotonic so index locality is fine, and it
      costs nothing today while removing a painful migration if two vaults are ever merged, her old data is
      imported, or a second user is added.
- [ ] **`DateTimeOffset` for activity timestamps** (ISO 8601 TEXT sorts correctly), **`DateOnly` for
      deadlines**, a délai has no time, an audience does, hence the separate nullable `Time`.

---

## 7. v1 feature scope

- [ ] **Tiers**, personnes physiques & morales, with roles. Company auto-fill via
      **`recherche-entreprises.api.gouv.fr`** (open, no API key, good autocomplete; INSEE Sirene needs a
      key and adds little). Must degrade gracefully offline, and be **disableable entirely** for users who
      want zero outbound traffic.
- [ ] **Dossiers**, the central object.
- [ ] **Journal**, appels, mails, RDV, notes. This *is* « le suivi ».
      **Make it the single fastest interaction in the app.**
- [ ] **Documents**, drag & drop, typage, PDF preview, **numérotation des pièces** (numéro + libellé).
- [ ] **Échéances / agenda**, audiences et délais, exposed as a **read-only ICS feed** so it lands on her
      phone. Read-only feed, not two-way sync: 10× cheaper, 90% of the value.
- [ ] **Temps passé**, timer + manual entry, taux horaire, facturable o/n. Logging « appel client, 20 min »
      must create the activity **and** the time entry in one keystroke (`TimeEntry.ActivityId`). In
      Gestisoft those are two separate screens, which is why lawyers under-record billable time.
      This single link is probably the highest-value thing in the model.
- [ ] **Détail à facturer**, per-matter: `Σ(billable time) − Σ(ledger) − Σ(invoiced)`. **Not** an invoice;
      the content to paste into her invoicing platform.
- [ ] **Recherche globale ⌘K**, instant, fuzzy, across everything. Alone, this will feel like a different
      century than Gestisoft.

### Backup (v1, not optional)

- [ ] Write a **single encrypted backup file to a user-chosen folder**. She points it at her existing
      Google Drive / OneDrive / Dropbox sync folder. **No OAuth**, works with every provider, a NAS, or a
      USB key, and nothing to maintain. Native providers only if someone actually asks.
- [ ] **Detect and refuse** a live vault located inside a sync folder, SQLite + mid-write sync = corruption.
      Backups go to the cloud; the vault never does.
- [ ] Restore wizard, requires the recovery file. **Tested in CI.**
- [ ] Warn if no backup in 7 days.

---

## 8. Deferred (post-v1)

- **Bordereau de communication de pièces**, she says it isn't mandatory, so it's out of v1. Because
  `Document.ExhibitNumber` / `ExhibitLabel` are kept, adding it later is **purely additive**: two tables
  (`ExhibitList` + `ExhibitListLine`, a frozen snapshot so a bordereau sent in March still shows what was
  actually sent in March) and a PDF. No migration of existing data. PDF via **QuestPDF** (Community
  License free under $1M revenue).
- Modèles de courriers / publipostage (DOCX templating), high value, likely v1.5.
- Calcul automatique des délais de procédure, high value **and** high liability. Advisory only, with an
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
  sous-traitance (art. 28)**, a subprocessor list, EU hosting and breach notification become mandatory,
  even at 5 €/month.
- **Secret professionnel**: hosting jurisdiction and subprocessor list matter more than an E2EE badge.
  A browser SaaS cannot be honestly E2EE (it kills server-side search, PDF generation, password reset),
  if it ships, it is *encrypted at rest with per-tenant keys*, and the claim must be worded as such.
- **No warranty.** Relevant above all if procedural-deadline calculation is ever built.
- **Trademark**: INPI/EUIPO search on "Avocado" (classes 9/42) before commissioning branding. Not blocking.

---

## 10. Build order

The vault is the only part that is genuinely hard to change later. Everything else is CRUD that can be
rewritten in an afternoon.

1. **Vault core**, envelope encryption, EF Core + SQLCipher wiring, migration-with-backup,
   `IVaultStore` seam.
2. **Recovery wizard**, print / USB, regenerate, quarterly verification.
3. **Backup + restore**, tested in CI.
4. Design system from Claude Design → see [docs/DESIGN-BRIEF.md](docs/DESIGN-BRIEF.md).
5. Electron shell + hardened localhost API.
6. Domain CRUD: `Contact` → `Matter` + `MatterParty` → `Activity` → `Document`.
   **These seven tables are a working, useful app on their own**, with `Deadline` and `TimeEntry` below.
7. `TimeEntry` (incl. the one-keystroke activity+time link) → « détail à facturer ».
8. `Deadline` + ICS feed.
9. `LedgerEntry` + `Invoice`, additive, nothing above depends on them.
10. Recherche globale ⌘K.
11. « Tout exporter ».
