/** Injected by the preload. The only things the renderer receives from the main process. */
declare global {
  interface Window {
    avocado: {
      connection: () => Promise<{ url: string; token: string; vaultState: string }>
      chooseFolder: (startIn?: string) => Promise<string | null>
      removableDrives: () => Promise<{ path: string; label: string; freeBytes: number }[]>
      saveRecoveryKey: (drivePath: string, contents: string) => Promise<string>
    }
  }
}

export type VaultState = 'Absent' | 'Locked' | 'Unlocked'

export interface VaultStatus {
  state: VaultState
  directory: string
  lockReason: string | null
  vaultId: string | null
  hasRecoveryKey: boolean
  suggestedDirectory: string
}

export interface VaultCreated {
  vaultId: string
  directory: string
  /** Shown once. Never fetched again, never stored. */
  recoveryCode: string
}

/**
 * Thrown for a non-2xx response, carrying the backend's own French message where it sent one.
 * The API answers with ProblemDetails, so `detail` and validation errors are worth surfacing —
 * « Ce dossier est inclus dans un dossier synchronisé » beats « Erreur 400 ».
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    /** Stable identifier from the backend, e.g. `synced-folder`. Branch on this, never on the text. */
    readonly code?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

let connection: { url: string; token: string } | null = null

async function connect(): Promise<{ url: string; token: string }> {
  connection ??= await window.avocado.connection()
  return connection
}

/**
 * Every call carries this launch's bearer token. Without it the backend answers 401 — which is what
 * stops another local process, or a page open in a browser, from reading the vault over loopback.
 *
 * While the vault is shut, everything except `/api/vault` and `/health` answers 503; the renderer
 * reads the state from `/api/vault/status` rather than inferring it from a failure.
 */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const { url, token } = await connect()

  const response = await fetch(`${url}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (!response.ok) {
    const problem = await read(response)
    throw new ApiError(response.status, describe(problem, response.status), problem.code)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export const post = <T>(path: string, body: unknown): Promise<T> =>
  api<T>(path, { method: 'POST', body: JSON.stringify(body) })

async function read(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return {}
  }
}

function describe(problem: ProblemDetails, status: number): string {
  const validation = Object.values(problem.errors ?? {}).flat()

  return validation[0] ?? problem.detail ?? problem.title ?? `Erreur ${status}`
}
