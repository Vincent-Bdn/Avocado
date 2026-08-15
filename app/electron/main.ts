import { app, BrowserWindow, dialog, ipcMain, Menu, screen, session, shell } from 'electron'
import { writeFile } from 'node:fs/promises'
import path from 'node:path'
import { Backend, type BackendHandshake } from './backend.js'
import { listRemovableDrives, saveRecoveryKey } from './drives.js'

// The main process is emitted as CommonJS: Electron's ESM entry support still trips over Node's
// CJS interop for the `electron` module itself, and this is not the place to be adventurous.
const directory = __dirname
const backend = new Backend()

let handshake: BackendHandshake | null = null

/**
 * Below this the four-band shell stops working: rail 48 + secondary 232 + content 480 + gutters.
 * Narrower is not a layout to design, it is a window nobody can work in.
 */
const MIN_WIDTH = 1024
const MIN_HEIGHT = 700

async function createWindow(): Promise<void> {
  // 1440x900 is what the screens were designed against, but plenty of laptops are shorter than that
  // and an oversized window opens with its content off the bottom of the display.
  const { width: availableWidth, height: availableHeight } = screen.getPrimaryDisplay().workAreaSize

  const window = new BrowserWindow({
    width: Math.min(1440, availableWidth),
    height: Math.min(900, availableHeight),
    minWidth: MIN_WIDTH,
    minHeight: MIN_HEIGHT,
    show: false,
    // --surface-app in the light theme, so the frame painted before the renderer arrives already
    // matches what replaces it.
    backgroundColor: '#F3F5F0',
    title: 'Avocado',
    // Copied from public/ by Vite, so this one path works in development and when packaged.
    icon: path.join(directory, '..', 'dist', 'icon.png'),
    webPreferences: {
      preload: path.join(directory, 'preload.cjs'),
      // The renderer is untrusted by construction: it renders content that came off disk. It gets no
      // Node, no remote module, and its own context, the preload exposes exactly one function.
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  // No FOUC on a local application; wait until there is something worth showing.
  window.once('ready-to-show', () => window.show())

  // The renderer's console reaches the shell's stdout. Without this, diagnosing a layout or fetch
  // problem means opening devtools by hand on a machine you may not be sitting at.
  // Electron changed this signature: older builds pass (event, level, message), newer ones a single
  // details object. Accept both rather than silently logging `undefined`.
  window.webContents.on('console-message', (...args: unknown[]) => {
    const [first, third] = args
    const message =
      typeof third === 'string'
        ? third
        : (first as { message?: string } | undefined)?.message

    console.log(`[renderer] ${message ?? ''}`)
  })

  await window.loadFile(path.join(directory, '..', 'dist', 'index.html'))
}

/**
 * Nothing loads from the network. Everything the app renders is bundled, which is also what makes the
 * "no telemetry, no CDN" claim checkable rather than promised.
 *
 * The one deliberate exception is the French company registry, and only when the user leaves that
 * lookup enabled, it is an open, unauthenticated API and the renderer calls it directly.
 */
function applyContentSecurityPolicy(): void {
  session.defaultSession.webRequest.onHeadersReceived((details, callback) => {
    callback({
      responseHeaders: {
        ...details.responseHeaders,
        'Content-Security-Policy': [
          [
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self'",
            `connect-src 'self' ${handshake?.url ?? ''} https://recherche-entreprises.api.gouv.fr`,
            "object-src 'none'",
            "frame-src 'none'",
          ].join('; '),
        ],
      },
    })
  })
}

app.whenReady().then(async () => {
  try {
    // Electron's default menu is English and offers File/Edit/View/Window/Help, none of which this
    // application has. No design frame shows a menu bar; navigation is the rail and the ⌘K palette.
    // Chromium still handles the clipboard accelerators in text fields without it.
    Menu.setApplicationMenu(null)

    const vaultDirectory =
      process.env.AVOCADO_VAULT ?? path.join(app.getPath('documents'), 'Avocado')

    // Working copies never go in the coffre, and never anywhere that synchronises. On Windows that
    // rules out `userData` as well: Electron points it at Roaming, which a domain profile copies
    // between machines. LOCALAPPDATA is the machine-local half of the same idea. On macOS and Linux
    // `userData` already is local (~/Library/Application Support, ~/.config).
    const localState =
      process.platform === 'win32' && process.env.LOCALAPPDATA
        ? path.join(process.env.LOCALAPPDATA, 'Avocado')
        : app.getPath('userData')

    const workingDirectory = path.join(localState, 'working')

    handshake = await backend.start(vaultDirectory, workingDirectory)

    // Resolved before the window exists, so the renderer never has to ask twice or poll.
    ipcMain.handle('avocado:connection', () => handshake)

    // The wizard needs a real folder picker. Typing a path is not something to ask of someone who
    // has never seen a file dialog fail.
    ipcMain.handle('avocado:chooseFolder', async (_event, startIn?: string, title?: string) => {
      const result = await dialog.showOpenDialog({
        title: title ?? 'Emplacement du coffre',
        defaultPath: startIn,
        properties: ['openDirectory', 'createDirectory'],
        buttonLabel: 'Choisir ce dossier',
      })

      return result.canceled ? null : (result.filePaths[0] ?? null)
    })

    // The recovery step will not let the user continue until one of these has actually happened.
    ipcMain.handle('avocado:removableDrives', () => listRemovableDrives())

    /**
     * Opens a working copy with whatever the OS uses for that file type. The guard is the point: the
     * renderer can only ever ask for a path inside the coffre's `.travail` folder, so a bug in the UI
     * cannot turn this into « open any file on this machine ».
     */
    ipcMain.handle('avocado:openWorkingCopy', async (_event, target: string) => {
      const working = workingDirectory
      const resolved = path.resolve(target)

      if (!resolved.startsWith(path.resolve(working) + path.sep)) {
        throw new Error('Chemin hors du dossier de travail.')
      }

      // Returns '' on success and a message on failure, which is the opposite of every other
      // Electron API, so it is normalised here rather than at every call site.
      const failure = await shell.openPath(resolved)
      return failure || null
    })

    ipcMain.handle('avocado:saveAs', async (_event, fileName: string, base64: string) => {
      const chosen = await dialog.showSaveDialog({
        title: 'Enregistrer',
        defaultPath: path.join(app.getPath('documents'), fileName),
        buttonLabel: 'Enregistrer',
      })

      if (chosen.canceled || !chosen.filePath) {
        return null
      }

      await writeFile(chosen.filePath, Buffer.from(base64, 'base64'))
      return chosen.filePath
    })

    ipcMain.handle('avocado:saveRecoveryKey', (_event, drivePath: string, contents: string) =>
      saveRecoveryKey(drivePath, contents))

    // Electron's print path has no preview, so on a machine without a printer it lands in a
    // "Microsoft Print to PDF" dialog that says as much. Producing the PDF directly is the honest
    // second option, and for most people it is the one they actually wanted.
    ipcMain.handle('avocado:exportRecoverySheet', async (event) => {
      const target = await dialog.showSaveDialog({
        title: 'Enregistrer la fiche',
        defaultPath: path.join(app.getPath('documents'), 'avocado-cle-de-recuperation.pdf'),
        filters: [{ name: 'PDF', extensions: ['pdf'] }],
        buttonLabel: 'Enregistrer',
      })

      if (target.canceled || !target.filePath) {
        return null
      }

      const contents = event.sender
      const pdf = await contents.printToPDF({
        pageSize: 'A4',
        printBackground: false,
        margins: { top: 0.6, bottom: 0.6, left: 0.6, right: 0.6 },
      })

      await writeFile(target.filePath, pdf)
      return target.filePath
    })

    applyContentSecurityPolicy()
    await createWindow()
  } catch (error) {
    // Also to stdout: an error box cannot be copied, pasted, or read from a log file.
    console.error('[avocado] démarrage impossible:', error)
    dialog.showErrorBox(
      'Avocado n’a pas pu démarrer',
      error instanceof Error ? error.message : String(error),
    )
    app.quit()
  }
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    void createWindow()
  }
})

// The backend holds the data encryption key in memory; it must not outlive the window that justified
// unlocking it.
app.on('before-quit', () => backend.stop())
