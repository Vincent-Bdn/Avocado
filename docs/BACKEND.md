# The backend

`src/Avocado.Server` is an ASP.NET Core Minimal API on .NET 10. It is not a server anyone deploys: it
is a process the desktop shell starts and stops, listening on loopback on a port the OS picks.

`src/Avocado.Vault` is the library underneath it that owns everything to do with encryption.
`src/Avocado.Cli` is a small tool that operates on a vault without a window.

---

## Building and running

```bash
dotnet build                               # everything
dotnet test                                # the vault's 99 tests
dotnet run --project src/Avocado.Server    # the service on its own, no window
```

Run on its own it reads three environment variables:

| Variable | Default | What it is |
|---|---|---|
| `AVOCADO_VAULT` | `~/Documents/Avocado` | The vault folder |
| `AVOCADO_WORKING_DIR` | `%LOCALAPPDATA%/Avocado/working` | Where documents are decrypted while open |
| `AVOCADO_API_TOKEN` | random per launch | The bearer token every request must carry |
| `AVOCADO_PORT` | `0`, the OS picks | Useful when you want a stable port to `curl` |

```bash
AVOCADO_API_TOKEN=diag AVOCADO_PORT=45999 dotnet run --project src/Avocado.Server
curl -s -H "Authorization: Bearer diag" http://127.0.0.1:45999/api/dashboard | jq
```

### Publishing

Executables publish **self-contained and single-file**, because a lawyer installing Avocado must never
be told to install a .NET runtime first:

```bash
dotnet publish src/Avocado.Server -c Release -r win-x64
```

That behaviour lives in `Directory.Build.targets`, **not** `.props`. MSBuild imports `.props` before
the project body, where `OutputType` has not been set yet, so the condition would silently never match
and the publish would quietly come out framework-dependent. Supported RIDs are in
`.github/workflows/ci.yml`: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.

### Cutting a version

Releasing is a button, not a tag pushed from a laptop. **Actions → CI → Run workflow**, choose what to
publish, and the run works out the number itself:

| Choice | From `v1.0.0-beta.3` | From `v1.2.3` |
|---|---|---|
| `beta` | `v1.0.0-beta.4` | `v1.2.4-beta.1` |
| `patch` | `v1.0.0`, the betas were leading here | `v1.2.4` |
| `minor` | `v1.1.0` | `v1.3.0` |
| `major` | `v2.0.0` | `v2.0.0` |

The tag is created by the run, **after the tests are green**, so a tag that exists is a tag that
built. Cutting one by hand means it exists before anything is proved, and that whoever remembers the
last number decides the next one.

That builds the six platforms, asserts each binary really bundled the runtime (a framework-dependent
publish looks fine right up until someone without .NET runs it), smoke tests the three the runners can
execute, and publishes the GitHub release from those same artifacts rather than from a second build.
One archive per platform holding the binary and the licence, named `avocado-cli-<tag>-<rid>`, plus
`SHA256SUMS` across everything attached.

In parallel, the `desktop` job builds what a lawyer actually installs: `Avocado.Server` published for
the target RID into `artifacts/backend`, then electron-builder over `app/`, producing an NSIS installer
and a zip on Windows, a `.dmg` and a zip on macOS, an AppImage and a tarball on Linux x64. Five
platforms, not six: cross-building an AppImage for `linux-arm64` needs emulation and breaks often, and
that user can run the CLI or build from source.

Those artifacts are named `Avocado-<os>-<arch>.<ext>` with **no version in the name**, deliberately.
The site's download buttons are plain links to `/releases/latest/download/Avocado-win-x64.exe`, which
only resolves if the name is identical in every release. Renaming them breaks every download button on
`site/`.

Nothing is signed. Windows SmartScreen and macOS Gatekeeper therefore object, and
`site/installation.html` walks users through it. Adding signing later is credentials in the environment
plus `identity` and `certificateFile` in `app/package.json`, not a different pipeline.

A beta is marked a pre-release on GitHub, which matters more than it looks:
`/releases/latest/download/` skips pre-releases, so the site keeps pointing at the last stable build
while a beta is out. That is the intended behaviour, and it only holds if the flag is right.

CI otherwise runs on pull requests, not on every push to `main`. A pull request runs the tests and
nothing else: the version job is skipped, and everything downstream needs it, so no runner spends
twenty minutes packaging installers for a change that is not being shipped.

From a terminal, if you prefer: `gh workflow run ci.yml -f bump=beta`.

---

## Lifecycle

```mermaid
sequenceDiagram
    participant E as Electron main
    participant B as Avocado.Server
    participant V as Vault folder

    E->>B: spawn, env: AVOCADO_VAULT, AVOCADO_WORKING_DIR,<br/>AVOCADO_API_TOKEN, AVOCADO_PORT=0
    B->>V: VaultSession.TryResume()
    alt a vault exists and this machine can unlock it
        V-->>B: OpenVault
        B->>V: migrate if needed, snapshot first
    else absent or locked
        B-->>B: State = Absent | Locked
    end
    B->>B: Kestrel listens on 127.0.0.1:0
    B-->>E: stdout: AVOCADO_READY {"url":…,"token":…,"vaultState":…}
    E->>E: open the window, hand the handshake to the renderer
    Note over E,B: … the session …
    E->>B: kill on before-quit
```

Three things this shape buys, each of which was a bug first:

**The service starts even when there is no vault.** It has to: the setup wizard is served by it.
`VaultReadyMiddleware` answers `503` for everything except `/api/vault/*` and `/health` while the
vault is shut, and the renderer reads the state from `/api/vault/status` rather than inferring it from
a failure.

**The port is 0 and travels in the handshake.** A fixed port collides with whatever else the machine
is running. Note `Listen(IPAddress.Loopback, 0)` rather than `ListenLocalhost(0)`: the latter binds
both IPv4 and IPv6 and therefore rejects port 0 outright, since it cannot guarantee the same free port
on both.

**Logging goes to stdout and the shell forwards all of it.** The handshake is matched by marker
(`AVOCADO_READY `) rather than by reading the first line, and the reader stays open for the life of the
process. Closing it after the handshake, which it once did, silently discarded every log line from
then on, which makes anything that happens after startup impossible to diagnose from the window.

### Shutdown

`before-quit` kills the child. On Windows that is a hard terminate, so `IHostedService.StopAsync` may
not run. Everything that must survive that is written to be idempotent and reconciled at the next
launch, see `DocumentWorkspace`.

---

## The API

Routing lives in one `*Endpoints.cs` per slice and does nothing else; every handler is its own file.

```
GET    /health
GET    /api/vault/status              prepare · commit · discard · unlock · recovery-key
GET    /api/matters                   ?status=&search=&sort=&deadline=&clientId=&skip=&take=
POST   /api/matters                   PUT /{id} · /close · /reopen · /favourite · /parties
GET    /api/matters/{id}/activities   documents · deadlines · time-entries · billing
POST   /api/matters/{id}/invoices/from-time
GET    /api/invoices/{id}/detail.xlsx
POST   /api/documents/{id}/open       close · resolve · exhibit
GET    /api/documents/workspace
GET    /api/templates                 POST · PUT /{id} · /{id}/content
GET    /api/contacts                  PUT /{id} · /{id}/attachment
GET    /api/dashboard · /api/search · /api/deadlines · /api/settings
```

**Route templates must name the parameter the handler takes.** A mismatch does not fail at startup:
minimal APIs fall back to binding the value from the query string, find nothing, and answer `400` with
an empty body that says nothing at all. This cost an afternoon once.

Enums cross the wire as **names**, never integers, the front end owns the French labels and maps from
keys like `IncomingLetter`, so a renumbering here would silently relabel history.

Failures answer `ProblemDetails` in French, through `Hosting/FailureDetails.cs`. The framework's
default, *An error occurred while processing your request.*, is in English on a screen that is
otherwise entirely French, and says nothing about what to do. The case that actually happens, a file
held open by Word, is a `409` that says exactly that.

---

## Data access

`VaultDbContextFactory.Create(vaultId)` is the only way to get a `DbContext`. It resolves the vault
through `IVaultStore` and hands EF an already-keyed connection.

- **`Pooling=false`.** Microsoft.Data.Sqlite pools by connection string, and a pooled handle comes back
  already keyed; re-issuing `PRAGMA key` on it misbehaves, and in a multi-tenant build a handle keyed
  for one vault must never be reachable from another.
- **`contextOwnsConnection: true`**, since every context holds a real file handle.
- The package is **`Microsoft.EntityFrameworkCore.Sqlite.Core`**, never `…Sqlite`. The full package
  drags in `SQLitePCLRaw.bundle_e_sqlite3`, plain SQLite. Two bundles in one process means whichever
  registers first wins, and if that is `e_sqlite3` then `PRAGMA key` is a **no-op** and the whole
  practice is written in plaintext with no error at all. `VaultDatabase` asserts
  `PRAGMA cipher_version` at every open so a regression fails loudly.

### Migrations

```bash
dotnet ef migrations add <Name> --project src/Avocado.Server --output-dir Data/Migrations
```

`VaultMigrator` **takes a snapshot before migrating**, always, and names it in the failure message.
SQLite DDL is transactional, so a migration that *fails* rolls itself back; the dangerous case is one
that succeeds and is wrong, which nothing can undo. This is the user's only copy of their practice.

> **Adding a non-nullable column to a populated table needs a backfill in the same migration.**
> SQLite fills existing rows with the column's default, and EF's default for a string column is the
> empty string. Timestamps are stored as ISO-8601 text, so a `DateTimeOffset` column added without a
> backfill made every pre-existing row throw `String '' was not recognized as a valid DateTime` before
> a handler ever saw it. See `20260807071431_BackfillDocumentTimestamps`.

### Things EF will not do that this codebase works around

- **SQLite cannot `ORDER BY` a `DateTimeOffset`** through EF's default mapping. `UtcTimestampConverter`
  stores ISO-8601 UTC text, which sorts correctly as text and stays legible in a database viewer.
- **EF cannot translate an `ORDER BY` over a record built in a `Select`**, nor see through a helper
  method. Order the entity query *before* projecting, and inline the subqueries.
- **No max over correlated subqueries.** `MatterTouch` reads five timestamps as five columns and
  combines them in memory rather than asking SQLite for the greatest.
- **A computed property EF is told to `Ignore`** (`Contact.DisplayName`) cannot appear in a `Select`.
  It compiles and fails at runtime; materialise first, project second.

---

## The document workspace

`Features/Documents/Workspace/DocumentWorkspace.cs` is a `BackgroundService`. It decrypts a document
into the machine-local working directory, hands the path to the shell, and re-encrypts every save back
into the vault.

**It polls; it does not watch.** Word does not write documents in place, it creates `~$name.docx` and
a scratch file, then renames over the original, so a `FileSystemWatcher` sees a delete-and-create
dance it has to be taught to read through, and on Windows it silently drops events when its buffer
overflows. A 1.5-second comparison of *(length, last write, then hash)* has none of those failure
modes. The hash is what stops a version being created every time Word rewrites an untouched file.

```mermaid
stateDiagram-v2
    [*] --> Closed
    Closed --> Open: POST /open<br/>decrypt, register
    Open --> Open: bytes changed<br/>wait for the lock, hash, re-encrypt, bump version
    Open --> Closed: POST /close
    Open --> Closed: idle 3 min<br/>unlocked, no ~$ sidecar, unchanged
    Closed --> Reconciled: startup sweep
    Reconciled --> [*]: identical to the vault → deleted silently
    Reconciled --> Awaiting: differs → reported, never deleted
```

The idle rule is the interesting one. Closing a reader is not an event any application can observe, so
three signals stand in for it, all three held for three minutes: the file is not locked, Word has left
no `~$` sidecar beside it, and the bytes have not changed since they were last stored. **The sidecar is
what makes this safe with Word**, Word does not hold the document itself exclusively between saves,
so a lock check alone would declare an open document idle and delete the file out from under it.

A hard kill cannot run the shutdown path, which is why the startup sweep exists. Anything hashing
identical to the vault is deleted silently; anything that differs is reported and never deleted on
sight, because a crash must not discard an afternoon's drafting.

---

## The CLI

```bash
dotnet run --project src/Avocado.Cli -- create <folder>
dotnet run --project src/Avocado.Cli -- info <folder>
dotnet run --project src/Avocado.Cli -- unlock <folder>            # asks for the recovery code
dotnet run --project src/Avocado.Cli -- backup <folder>
dotnet run --project src/Avocado.Cli -- verify-recovery <folder>
```

It exists so a vault can be created, inspected and backed up from a script or a support session, and
so the vault library is exercised by something that is not the application.

---

## Permissions

The service needs **no elevation, no admin rights and no installed service**. It reads and writes:

- the vault folder, wherever the user pointed it;
- the machine-local working directory (`%LOCALAPPDATA%`, `~/Library/Application Support`, `~/.config`);
- on macOS and Linux, a `0600` device-key file **outside** the vault folder, since a key stored beside
  the thing it unlocks is not a second factor.

It binds one **loopback** port. It opens no listening socket on any other interface and makes no
outbound connection whatsoever, the one external request in the product, the *annuaire des
entreprises* lookup, is made by the renderer, not by the service.
