import { useEffect, useState } from 'react'
import { ApiError, api, type HealthResponse } from './api.js'

type State =
  | { status: 'connecting' }
  | { status: 'ready'; health: HealthResponse }
  | { status: 'failed'; message: string }

/**
 * A connection proof, not the application. It exists so the handshake — spawn the backend, read its
 * address and token off stdout, call it with that token — is verifiable before any screen is built
 * on top of it.
 */
export function App() {
  const [state, setState] = useState<State>({ status: 'connecting' })

  useEffect(() => {
    let cancelled = false

    api<HealthResponse>('/health')
      .then((health) => !cancelled && setState({ status: 'ready', health }))
      .catch((error: unknown) =>
        !cancelled &&
        setState({
          status: 'failed',
          message: error instanceof ApiError ? error.message : String(error),
        }),
      )

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <main className="shell">
      <h1>Avocado</h1>

      {state.status === 'connecting' && <p className="muted">Ouverture du coffre…</p>}

      {state.status === 'failed' && <p className="danger">{state.message}</p>}

      {state.status === 'ready' && (
        <dl>
          <dt>Coffre</dt>
          <dd className="mono">{state.health.vaultId}</dd>
          <dt>Dossier</dt>
          <dd className="mono">{state.health.folder}</dd>
          <dt>Déverrouillage</dt>
          <dd>{state.health.unlockPaths.map((path) => path.label).join(' · ')}</dd>
          <dt>Clé de récupération</dt>
          <dd>{state.health.hasRecoveryKey ? 'enregistrée' : 'absente'}</dd>
        </dl>
      )}
    </main>
  )
}
