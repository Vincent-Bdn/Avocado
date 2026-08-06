import { app, BrowserWindow, dialog, ipcMain, Menu, screen, session } from 'electron'
import path from 'node:path'
import { Backend, type BackendHandshake } from './backend.js'

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
      // Node, no remote module, and its own context — the preload exposes exactly one function.
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  // No FOUC on a local application; wait until there is something worth showing.
  window.once('ready-to-show', () => window.show())

  await window.loadFile(path.join(directory, '..', 'dist', 'index.html'))
}

/**
 * Nothing loads from the network. Everything the app renders is bundled, which is also what makes the
 * "no telemetry, no CDN" claim checkable rather than promised.
 *
 * The one deliberate exception is the French company registry, and only when the user leaves that
 * lookup enabled — it is an open, unauthenticated API and the renderer calls it directly.
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

    handshake = await backend.start(vaultDirectory)

    // Resolved before the window exists, so the renderer never has to ask twice or poll.
    ipcMain.handle('avocado:connection', () => handshake)

    // The wizard needs a real folder picker. Typing a path is not something to ask of someone who
    // has never seen a file dialog fail.
    ipcMain.handle('avocado:chooseFolder', async (_event, startIn?: string) => {
      const result = await dialog.showOpenDialog({
        title: 'Emplacement du coffre',
        defaultPath: startIn,
        properties: ['openDirectory', 'createDirectory'],
        buttonLabel: 'Choisir ce dossier',
      })

      return result.canceled ? null : (result.filePaths[0] ?? null)
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
