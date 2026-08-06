import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post, type VaultStatus } from './api.js'
import { Setup } from './Setup.js'

type Screen =
  | { kind: 'loading' }
  | { kind: 'status'; status: VaultStatus }
  | { kind: 'failed'; message: string }

export function App() {
  const [screen, setScreen] = useState<Screen>({ kind: 'loading' })

  const refresh = useCallback(async () => {
    try {
      setScreen({ kind: 'status', status: await api<VaultStatus>('/api/vault/status') })
    } catch (failure) {
      setScreen({
        kind: 'failed',
        message: failure instanceof ApiError ? failure.message : String(failure),
      })
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  if (screen.kind === 'loading') {
    return <main className="shell"><p className="muted">Ouverture du coffre…</p></main>
  }

  if (screen.kind === 'failed') {
    return <main className="shell"><p className="danger">{screen.message}</p></main>
  }

  const { status } = screen

  return (
    <main className="shell">
      {status.state === 'Absent' && <Setup status={status} onReady={() => void refresh()} />}
      {status.state === 'Locked' && <Unlock status={status} onUnlocked={() => void refresh()} />}
      {status.state === 'Unlocked' && <Ready status={status} />}
    </main>
  )
}

/**
 * A vault that exists but will not open here: a folder restored onto a new machine, or a different
 * Windows account. The recovery key is the only way through, which is why the setup wizard refuses
 * to let anyone past it without saving one.
 */
function Unlock({ status, onUnlocked }: { status: VaultStatus; onUnlocked: () => void }) {
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function unlock() {
    setBusy(true)
    setError(null)

    try {
      await post('/api/vault/unlock', { recoveryCode: code })
      onUnlocked()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="pane">
      <h1>Coffre verrouillé</h1>
      <p>{status.lockReason}</p>
      <p className="muted">
        Saisissez votre clé de récupération : les neuf groupes de six, tels qu’ils figurent sur votre
        fiche. Les tirets et les majuscules n’ont pas d’importance.
      </p>

      <input
        value={code}
        onChange={(e) => setCode(e.target.value)}
        className="mono"
        placeholder="87CQ1X-382EVN-…"
        autoFocus
      />

      {error && <p className="danger">{error}</p>}

      <button type="button" disabled={busy || !code} onClick={() => void unlock()}>
        {busy ? 'Vérification…' : 'Déverrouiller'}
      </button>
    </section>
  )
}

/** Connection proof, until the real shell is built on the design system. */
function Ready({ status }: { status: VaultStatus }) {
  return (
    <section className="pane">
      <h1>Avocado</h1>
      <dl>
        <dt>Coffre</dt>
        <dd className="mono">{status.vaultId}</dd>
        <dt>Dossier</dt>
        <dd className="mono">{status.directory}</dd>
        <dt>Clé de récupération</dt>
        <dd>{status.hasRecoveryKey ? 'enregistrée' : 'absente'}</dd>
      </dl>
    </section>
  )
}
