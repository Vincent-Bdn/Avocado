import { useCallback, useEffect, useState } from 'react'
import { Check, ExternalLink, RefreshCw, Unplug } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'

interface AgendaState {
  /** An OAuth client exists. Without one, nothing can be connected at all. */
  isConfigured: boolean
  isConnected: boolean
  isEnabled: boolean
  account: string | null
  syncedAt: string | null
  error: string | null
  failureCount: number
  hasStopped: boolean
}

/**
 * Connecter l'agenda Google.
 *
 * <p>One way, and narrow. Avocado creates a calendar of its own, writes the échéances into it and
 * never reads or touches the calendars the cabinet already has. The scope asked for says exactly
 * that, which is what the consent screen will show.</p>
 */
export function Agenda() {
  const [state, setState] = useState<AgendaState | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(() => {
    api<AgendaState>('/api/agenda')
      .then(setState)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [])

  useEffect(reload, [reload])

  async function run(work: () => Promise<unknown>) {
    setBusy(true)
    setError(null)

    try {
      await work()
      reload()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  if (!state) {
    return error ? <p className="m-0 text-danger">{error}</p> : null
  }

  return (
    <div className="grid gap-3">
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Vos échéances sont inscrites dans un agenda que Avocado crée, « Avocado · Échéances ».
        Vos autres agendas ne sont ni lus ni modifiés, et rien ne revient de Google vers Avocado.
      </p>

      {!state.isConfigured && <Credentials onSaved={reload} />}

      {state.isConfigured && !state.isConnected && (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            disabled={busy}
            onClick={() => void run(async () => {
              const { url } = await post<{ url: string }>('/api/agenda/google/authorize', {})
              await window.avocado.openGoogleConsent(url)
            })}
          >
            <ExternalLink size={14} strokeWidth={1.75} />
            Connecter un compte Google
          </Button>

          <span className="text-[11.5px] text-muted">
            Votre navigateur s’ouvre. Revenez ici une fois l’autorisation donnée.
          </span>
        </div>
      )}

      {state.isConnected && (
        <>
          <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[12.5px]">
            <Check size={13} strokeWidth={2.5} className="text-brand-on-subtle" />
            <span>Connecté{state.account && ` à ${state.account}`}</span>

            {state.syncedAt && (
              <span className="font-mono text-[11px] text-muted">
                synchronisé {new Date(state.syncedAt).toLocaleString('fr-FR')}
              </span>
            )}
          </div>

          {state.error && (
            <p
              className={cn(
                'm-0 rounded-sm border px-3 py-2 text-[12px]',
                state.hasStopped
                  ? 'border-[#EBC9C5] bg-[#FDF4F3] text-[#8A211A]'
                  : 'border-[#E8D5AE] bg-[#FDF8ED] text-[#6E4A0E]',
              )}
            >
              {state.error}
              {state.hasStopped
                ? ' Avocado a cessé d’essayer après cinq tentatives.'
                : ` ${state.failureCount} tentative${state.failureCount > 1 ? 's' : ''}.`}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              disabled={busy}
              onClick={() => void run(() => post('/api/agenda/sync', {}))}
            >
              <RefreshCw size={14} strokeWidth={1.75} />
              Synchroniser maintenant
            </Button>

            <Button
              variant="secondary"
              disabled={busy}
              onClick={() => void run(() =>
                api('/api/agenda/enabled', {
                  method: 'PUT',
                  body: JSON.stringify({ enabled: !state.isEnabled }),
                }))}
            >
              {state.isEnabled ? 'Mettre en pause' : 'Reprendre'}
            </Button>

            <Button
              variant="secondary"
              disabled={busy}
              onClick={() => void run(() => post('/api/agenda/google/disconnect', {}))}
            >
              <Unplug size={14} strokeWidth={1.75} />
              Déconnecter
            </Button>
          </div>

          {!state.isEnabled && (
            <p className="m-0 text-[11.5px] text-muted">
              En pause : le compte reste connecté, rien n’est envoyé.
            </p>
          )}

          <p className="m-0 text-[11.5px] leading-4 text-muted">
            Déconnecter laisse dans votre agenda Google ce qui y a été inscrit.
          </p>
        </>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}
    </div>
  )
}

/**
 * L'identifiant OAuth.
 *
 * <p>Avocado n'en fournit pas. Le code est public, donc un secret livré avec serait un secret publié,
 * et surtout l'écran d'autorisation de Google porte le nom de celui qui a créé le projet : un cabinet
 * qui connecte son agenda a le droit de savoir de qui il s'agit.</p>
 */
function Credentials({ onSaved }: { onSaved: () => void }) {
  const [clientId, setClientId] = useState('')
  const [clientSecret, setClientSecret] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save() {
    if (!clientId.trim()) {
      setError('Indiquez l’identifiant client.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await api('/api/agenda/credentials', {
        method: 'PUT',
        body: JSON.stringify({ clientId: clientId.trim(), clientSecret: clientSecret.trim() }),
      })

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-2 rounded-sm border border-line-subtle bg-sunken p-3">
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px]">
        Il faut un identifiant OAuth, créé une fois dans Google Cloud, type « Application de
        bureau », avec l’API Google Agenda activée. Avocado n’en fournit pas : son code est public,
        et l’écran d’autorisation porte le nom du titulaire du projet.
      </p>

      <div className="flex flex-wrap items-end gap-2">
        <label className="grid gap-1">
          <span className="text-[12px] font-medium">Identifiant client</span>
          <Input
            className="w-[320px] font-mono text-[11.5px]"
            value={clientId}
            placeholder="…apps.googleusercontent.com"
            onChange={(event) => { setClientId(event.target.value); setError(null) }}
          />
        </label>

        <label className="grid gap-1">
          <span className="text-[12px] font-medium">Secret client</span>
          <Input
            className="w-[200px] font-mono text-[11.5px]"
            type="password"
            value={clientSecret}
            onChange={(event) => setClientSecret(event.target.value)}
          />
        </label>

        <Button disabled={busy} onClick={() => void save()}>Enregistrer</Button>
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </div>
  )
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)
