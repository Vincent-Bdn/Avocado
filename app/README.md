# Avocado, application shell

Electron + React + TypeScript. The shell holds **no business logic**: it opens a window, starts and
stops the backend, and hands the renderer one thing, where the API is and this launch's token.
Everything else goes over the same HTTP API a hosted Avocado would expose, which is what keeps this
folder replaceable in a weekend.

```
electron/
  main.ts      window, CSP, lifecycle
  backend.ts   spawns Avocado.Server, reads the AVOCADO_READY handshake off stdout
  preload.ts   the entire renderer surface: one function
src/
  api.ts       fetch wrapper, bearer token, ProblemDetails -> French message
  App.tsx      connection proof
  tokens.css   design tokens, copied from ds/
```

## Running it

```bash
cd app
npm install
npm run electron:dev        # vite build, then electron .
```

The backend is resolved from `src/Avocado.Server/bin/Debug/net10.0/`, so build it first:

```bash
dotnet build src/Avocado.Server
```

By default the vault is `~/Documents/Avocado`. Point somewhere else with `AVOCADO_VAULT`.

## If it fails with `Cannot read properties of undefined (reading 'whenReady')`

**`ELECTRON_RUN_AS_NODE=1` is set in your environment.** VS Code sets it for the processes it spawns,
so any terminal inside VS Code inherits it. It makes Electron run as plain Node, where
`require('electron')` returns the *path to the binary* rather than the API, hence the undefined
`app`. Unset it, or launch from a terminal outside the editor:

```bash
env -u ELECTRON_RUN_AS_NODE npm run electron:dev
```

## Design notes worth keeping

- **The main process is CommonJS.** Electron's ESM entry support still trips over Node's CJS interop
  for the `electron` module itself; `vite-plugin-electron` emits CJS by default and that is fine.
  The preload is separately emitted as `preload.cjs` because a sandboxed preload cannot be an ES
  module.
- **The backend is found relative to `__dirname`, never `process.cwd()`.** A shell that only works
  when started from the right folder fails on someone else's machine.
- **Debug is preferred over Release** when resolving that binary, because `dotnet build` without a
  configuration produces Debug and a stale Release build would otherwise silently shadow it.
- **`contextIsolation`, `sandbox`, no `nodeIntegration`.** The renderer gets one function. Anything
  that genuinely needs the OS, printing the recovery sheet, listing removable drives, gets its own
  named IPC channel, reviewed on its own merits.
- **CSP allows exactly one external origin**: `recherche-entreprises.api.gouv.fr`, for the company
  autofill, which the renderer calls directly. Nothing else leaves the machine.
