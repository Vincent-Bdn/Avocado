import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post, type VaultStatus } from './api.js'
import { AppShell } from './AppShell.js'
import { Button } from './components/ui/button.js'
import { Input } from './components/ui/input.js'
import { Wizard } from './wizard/Wizard.js'

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
    return <Centred><p className="m-0 text-muted">Ouverture du coffre…</p></Centred>
  }

  if (screen.kind === 'failed') {
    return <Centred><p className="m-0 text-danger">{screen.message}</p></Centred>
  }

  const { status } = screen

  if (status.state === 'Unlocked') {
    return <AppShell />
  }

  // Full-screen, no rail: the wizard is not a card in the application shell.
  if (status.state === 'Absent') {
    return <Wizard status={status} onReady={() => void refresh()} />
  }

  return (
    <Centred>
      <Unlock status={status} onUnlocked={() => void refresh()} />
    </Centred>
  )
}

/** The pre-shell screens have no rail and no panels: one centred block on --surface-app. */
const Centred = ({ children }: { children: React.ReactNode }) => (
  <main className="grid h-full place-items-center bg-app p-6">{children}</main>
)

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
    <section className="grid w-[520px] max-w-full gap-3 rounded-2xl border border-line bg-panel p-6 shadow-e2">
      <h1 className="m-0 text-[22px] leading-7 font-semibold tracking-[-0.015em]">
        Coffre verrouillé
      </h1>

      <p className="m-0 text-[13px]">{status.lockReason}</p>

      <p className="m-0 text-[12px] leading-[18px] text-muted">
        Saisissez votre clé de récupération : les neuf groupes de six, tels qu’ils figurent sur votre
        fiche. Les tirets et les majuscules n’ont pas d’importance.
      </p>

      <Input
        inputSize="lg"
        autoFocus
        value={code}
        placeholder="87CQ1X-382EVN-…"
        className="font-mono tracking-[0.04em]"
        onChange={(event) => setCode(event.target.value)}
      />

      {error && <p className="m-0 text-danger">{error}</p>}

      <Button size="lg" className="justify-self-start" disabled={busy || !code} onClick={() => void unlock()}>
        {busy ? 'Vérification…' : 'Déverrouiller'}
      </Button>
    </section>
  )
}
