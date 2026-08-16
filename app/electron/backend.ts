import { spawn, type ChildProcess } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import { createInterface } from 'node:readline'
import { existsSync } from 'node:fs'
import path from 'node:path'

/** What the backend prints on stdout once it is genuinely listening. */
export interface BackendHandshake {
  url: string
  token: string
  /** Absent, Locked or Unlocked. The renderer decides between wizard and application from this. */
  vaultState: string
  /** Where dossiers are opened, the folder the user chose. */
  workingDirectory: string
  /** Where single documents are checked out. Separate from the above, and where a document opens from. */
  documentDirectory: string
}

const READY_PREFIX = 'AVOCADO_READY '
const STARTUP_TIMEOUT_MS = 30_000

/**
 * Owns the ASP.NET Core process.
 *
 * The shell holds no business logic, its whole job is to start this, learn where it landed, and
 * point a window at it. Everything else goes through the same HTTP API a hosted Avocado would expose,
 * which is what keeps this file replaceable in a weekend.
 */
export class Backend {
  private process: ChildProcess | null = null

  /**
   * Generated per launch and handed to the backend in its environment. Binding to 127.0.0.1 is not a
   * security boundary: any process on the machine can reach the port, and so can any page the user
   * has open in a browser. This token is the actual control, and it never touches disk.
   */
  private readonly token = randomBytes(32).toString('base64')

  async start(vaultDirectory: string): Promise<BackendHandshake> {
    const executable = resolveExecutable()

    this.process = spawn(executable, [], {
      env: {
        ...process.env,
        AVOCADO_VAULT: vaultDirectory,
        // Machine-local, outside the coffre: it holds plaintext while a document is open, and the
        // coffre is the thing that gets backed up and may one day be remote.
        AVOCADO_API_TOKEN: this.token,
        // Port 0, the OS picks a free one and the handshake reports it back. A fixed port would
        // collide with whatever else the machine is running.
        AVOCADO_PORT: '0',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    })

    return await this.awaitHandshake()
  }

  private awaitHandshake(): Promise<BackendHandshake> {
    const child = this.process
    const stdout = child?.stdout
    if (!child || !stdout) {
      return Promise.reject(new Error('Le service Avocado n’a pas démarré.'))
    }

    return new Promise((resolve, reject) => {
      const stderr: string[] = []
      child.stderr?.on('data', (chunk: Buffer) => stderr.push(chunk.toString()))

      const timer = setTimeout(() => {
        reject(new Error(`Le service Avocado n’a pas répondu en ${STARTUP_TIMEOUT_MS / 1000} s.`))
      }, STARTUP_TIMEOUT_MS)

      // Kept open for the life of the process. Closing it once the handshake arrived silently threw
      // away every backend log line from then on, which made anything that happens after startup,
      // like the working folder being swept a second later, impossible to see from here.
      const lines = createInterface({ input: stdout })
      let ready = false

      lines.on('line', (line) => {
        // Logging goes to stdout too, so match the marker rather than parsing the first line.
        if (!line.startsWith(READY_PREFIX)) {
          console.log('[backend]', line)
          return
        }

        if (ready) {
          return
        }

        ready = true
        clearTimeout(timer)
        resolve(JSON.parse(line.slice(READY_PREFIX.length)) as BackendHandshake)
      })

      child.on('exit', (code) => {
        clearTimeout(timer)
        // The backend exits non-zero when the vault cannot be unlocked on this machine, which is the
        // recovery-key path, surface its own words rather than a generic failure.
        reject(new Error(stderr.join('').trim() || `Le service Avocado s’est arrêté (code ${code}).`))
      })
    })
  }

  stop(): void {
    this.process?.kill()
    this.process = null
  }
}

/**
 * The published single-file binary when packaged, the development build otherwise. Self-contained
 * either way: a lawyer installing Avocado is never told to install a .NET runtime first.
 */
function resolveExecutable(): string {
  const name = process.platform === 'win32' ? 'Avocado.Server.exe' : 'Avocado.Server'

  // Anchored to this file, never to process.cwd(): the working directory depends on how the app was
  // launched, and a shell that only works when started from the right folder is a shell that fails
  // on someone else's machine.
  const here = __dirname

  // Packaged first; then Debug before Release, because `dotnet build` without a configuration
  // produces Debug and a stale Release binary would otherwise silently shadow the build you just made.
  const candidates = [
    path.join(process.resourcesPath ?? '', 'backend', name),
    path.resolve(here, '..', '..', 'src', 'Avocado.Server', 'bin', 'Debug', 'net10.0', name),
    path.resolve(here, '..', '..', 'src', 'Avocado.Server', 'bin', 'Release', 'net10.0', name),
  ]

  const found = candidates.find((candidate) => existsSync(candidate))
  if (!found) {
    throw new Error(
      `Service Avocado introuvable. Emplacements essayés :\n${candidates.join('\n')}`,
    )
  }

  return found
}
