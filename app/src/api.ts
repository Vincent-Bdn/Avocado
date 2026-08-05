/** Injected by the preload. The only thing the renderer receives from the main process. */
declare global {
  interface Window {
    avocado: {
      connection: () => Promise<{ url: string; token: string; vaultId: string }>
    }
  }
}

export interface HealthResponse {
  vaultId: string
  folder: string
  unlockPaths: { kind: string; label: string }[]
  hasRecoveryKey: boolean
}

/**
 * Thrown for a non-2xx response, carrying the backend's own French message where it sent one.
 * The API answers with ProblemDetails, so `detail` and validation errors are worth surfacing —
 * « Ce tiers intervient dans 3 dossiers » beats « Erreur 409 ».
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
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
    throw new ApiError(response.status, await describe(response))
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

async function describe(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as ProblemDetails
    const validation = Object.values(problem.errors ?? {}).flat()

    return validation[0] ?? problem.detail ?? problem.title ?? `Erreur ${response.status}`
  } catch {
    return `Erreur ${response.status}`
  }
}
