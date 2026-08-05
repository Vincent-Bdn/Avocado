import { contextBridge, ipcRenderer } from 'electron'

/**
 * The entire surface the renderer gets. One function, returning the backend's address and this
 * launch's token.
 *
 * Keeping it this small is the point: with `contextIsolation` and `sandbox` on, a bug in the UI — or
 * in anything it renders — cannot reach the filesystem, spawn a process, or read the vault. Anything
 * that genuinely needs the OS (printing the recovery sheet, listing removable drives) gets its own
 * named channel here, reviewed on its own merits, rather than a general-purpose bridge.
 */
contextBridge.exposeInMainWorld('avocado', {
  connection: () =>
    ipcRenderer.invoke('avocado:connection') as Promise<{
      url: string
      token: string
      vaultId: string
    }>,
})
