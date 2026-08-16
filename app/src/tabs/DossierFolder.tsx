import { useCallback, useEffect, useRef, useState } from 'react'
import { AlertTriangle, Check, FolderOpen, Loader2, RefreshCw } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'

/**
 * « Ouvrir le dossier »: every document of the matter, decrypted into a real folder she works in with
 * Explorer and Word, kept in step while she does, and put away when she says so.
 *
 * <p>The screen's job is to make two things impossible to miss: that the folder exists and where it
 * is, and that closing it is a thing she does rather than something that happens to her. Everything
 * else, what a change means, what a rename is, what to do after a crash, was decided in the backend
 * where it could be tested.</p>
 */

interface Change {
  kind: 'Unchanged' | 'Modified' | 'Added' | 'Renamed' | 'Deleted'
  relativePath: string
  previousPath: string | null
  sizeBytes: number
}

interface Checkout {
  matterId: string
  folderPath: string
  openedAt: string
  syncedAt: string | null
  fileCount: number
  awaitingDecision: boolean
  changes: Change[]
}

export function DossierFolder({ matterId, onChanged, onOpenChange }: {
  matterId: string
  onChanged: () => void
  onOpenChange: (open: boolean) => void
}) {
  const [checkout, setCheckout] = useState<Checkout | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)

  // What the list below was last told about. The sweep writes documents into the vault on its own
  // schedule, so this panel is the only thing that knows the list has gone stale; without telling it,
  // the header says « 10 fichiers » over « Aucun document » until something else forces a reload.
  const known = useRef<string | null>(null)

  const reload = useCallback(() => {
    api<Checkout[]>('/api/checkouts')
      .then((open) => {
        const mine = open.find((entry) => entry.matterId === matterId) ?? null
        setCheckout(mine)

        // While the folder exists it is the truth, and the tab below has to stop offering a second
        // way to change the same documents.
        onOpenChange(mine !== null)

        const signature = mine === null ? 'none' : `${mine.fileCount}:${mine.syncedAt ?? ''}`

        if (known.current !== null && known.current !== signature) {
          onChanged()
        }

        known.current = signature
      })
      .catch(() => setCheckout(null))
  }, [matterId, onChanged, onOpenChange])

  useEffect(reload, [reload])

  // The backend writes changes back every five seconds; this is only the screen catching up with it,
  // so nothing here is the thing that makes a save happen.
  useEffect(() => {
    const timer = setInterval(reload, 5_000)
    return () => clearInterval(timer)
  }, [reload])

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      reload()
      onChanged()
    } catch (failure: unknown) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (!checkout) {
    return (
      <div className="flex items-center gap-2 border-b border-line-subtle px-4 py-2">
        <Button
          variant="secondary"
          size="sm"
          disabled={busy}
          onClick={() =>
            run(async () => {
              const opened = await post<{ folderPath: string }>(`/api/matters/${matterId}/checkout`, {})
              await window.avocado.revealFolder(opened.folderPath)
            })
          }
        >
          {busy ? <Loader2 size={13} className="animate-spin" /> : <FolderOpen size={13} strokeWidth={2} />}
          Ouvrir le dossier
        </Button>

        <span className="text-[11.5px] leading-[17px] text-muted">
          Tous les documents sont déchiffrés dans un dossier de cet ordinateur. Vous y travaillez
          normalement, tout est réenregistré au fur et à mesure.
        </span>

        {error && <span className="text-[11.5px] text-danger">{error}</span>}
      </div>
    )
  }

  const notable = checkout.changes

  // Answered before anything else is offered. While this is pending the backend leaves the folder
  // alone, so the question is real rather than asked after the fact.
  if (checkout.awaitingDecision) {
    return (
      <div className="grid gap-2 border-b border-line-subtle bg-warning-bg px-4 py-2.5">
        <div className="flex items-center gap-1.5 text-[12.5px] font-medium text-warning">
          <AlertTriangle size={14} strokeWidth={2} />
          Ce dossier a changé pendant qu’Avocado était fermé
        </div>

        <p className="m-0 max-w-[76ch] text-[11.5px] leading-[17px] text-warning">
          Le dossier était resté ouvert sur cet ordinateur, et son contenu n’est plus celui qu’Avocado
          avait déposé. Rien n’a été enregistré : c’est à vous de dire ce qui fait foi.
        </p>

        <ChangeList changes={notable} />

        {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

        <div className="flex flex-wrap gap-1.5">
          <Button
            size="sm"
            disabled={busy}
            onClick={() => run(() => post(`/api/matters/${matterId}/checkout/resolve?keepFolder=true`, {}))}
          >
            Garder le travail fait dans le dossier
          </Button>

          <Button
            variant="secondary"
            size="sm"
            disabled={busy}
            onClick={() => run(() => post(`/api/matters/${matterId}/checkout/resolve?keepFolder=false`, {}))}
          >
            Revenir à ce qui est dans le coffre
          </Button>
        </div>

        <p className="m-0 max-w-[76ch] text-[11px] leading-[16px] text-warning opacity-90">
          « Revenir au coffre » efface ces modifications et réécrit le dossier tel qu’Avocado l’avait.
          Aucun document ne sera supprimé du coffre dans un cas comme dans l’autre : cela reste
          réservé à « J’ai terminé », où la liste vous est présentée.
        </p>
      </div>
    )
  }

  return (
    <div className="grid gap-2 border-b border-line-subtle bg-brand-subtle/40 px-4 py-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <FolderOpen size={14} strokeWidth={2} className="text-brand" />
        <span className="text-[12.5px] font-medium">Le dossier est ouvert sur cet ordinateur</span>

        <button
          type="button"
          onClick={() => void window.avocado.revealFolder(checkout.folderPath)}
          className="truncate font-mono text-[10.5px] text-muted underline-offset-2 hover:underline"
          title={checkout.folderPath}
        >
          {checkout.folderPath}
        </button>

        <span className="ml-auto flex items-center gap-1.5">
          <Button
            variant="ghost"
            size="sm"
            disabled={busy}
            onClick={() => run(() => post(`/api/matters/${matterId}/checkout/sync`, {}))}
            title="Réenregistrer tout de suite plutôt que d'attendre le prochain passage"
          >
            <RefreshCw size={12} strokeWidth={2} className={busy ? 'animate-spin' : undefined} />
            Réenregistrer
          </Button>

          <Button size="sm" disabled={busy} onClick={() => setConfirming(true)}>
            <Check size={13} strokeWidth={2.5} />
            J’ai terminé
          </Button>
        </span>
      </div>

      <div className="font-mono text-[10.5px] text-muted tnum">
        {checkout.fileCount} fichier{checkout.fileCount > 1 ? 's' : ''}
        {checkout.syncedAt && ` · réenregistré ${relative(checkout.syncedAt)}`}
      </div>

      {/* Said here, next to the folder, rather than as a disabled tooltip on every row. Someone who
          notices the buttons are gone should find the reason in the first place they look. */}
      <p className="m-0 max-w-[76ch] text-[11px] leading-[16px] text-ink-secondary">
        Tant que le dossier est ouvert, tout se fait dans le dossier : renommer, classer dans un
        sous-dossier, ouvrir, ajouter, supprimer. Avocado suit et réenregistre. Les mêmes actions sont
        retirées de la liste ci-dessous, parce qu’elles porteraient sur le coffre pendant que vous
        travaillez sur les fichiers, et c’est le dossier qui l’emporte.
      </p>

      {notable.length > 0 && <ChangeList changes={notable} />}

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {confirming && (
        <div className="grid gap-2 rounded-sm border border-line bg-panel px-2.5 py-2">
          <span className="text-[12px] font-medium">Fermer le dossier</span>

          {notable.some((change) => change.kind === 'Deleted') ? (
            <>
              <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-ink-secondary">
                Ces fichiers ne sont plus dans le dossier. En fermant, les documents correspondants
                seront retirés du coffre. Le reste est déjà enregistré.
              </p>
              <ChangeList changes={notable.filter((change) => change.kind === 'Deleted')} />
            </>
          ) : (
            <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-ink-secondary">
              Tout est enregistré. Le dossier va être refermé et les fichiers déchiffrés effacés de cet
              ordinateur.
            </p>
          )}

          <div className="flex gap-1.5">
            <Button variant="secondary" size="sm" onClick={() => setConfirming(false)}>
              Continuer à travailler
            </Button>
            <Button
              size="sm"
              disabled={busy}
              onClick={() => {
                setConfirming(false)
                void run(() => api(`/api/matters/${matterId}/checkout`, { method: 'DELETE' }))
              }}
            >
              Fermer le dossier
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

/** What moved, in her words rather than in the reconciler's. */
function ChangeList({ changes }: { changes: Change[] }) {
  return (
    <ul className="m-0 grid list-none gap-0.5 p-0">
      {changes.map((change) => (
        <li key={`${change.kind}-${change.relativePath}`} className="flex items-baseline gap-1.5 text-[11px] leading-[16px]">
          <span
            className={cn(
              'shrink-0 rounded-full px-1.5 py-px font-mono text-[9.5px] leading-3',
              tone[change.kind],
            )}
          >
            {label[change.kind]}
          </span>

          <span className="truncate font-mono text-[10.5px]">
            {change.previousPath ? `${change.previousPath} → ${change.relativePath}` : change.relativePath}
          </span>
        </li>
      ))}
    </ul>
  )
}

const label: Record<Change['kind'], string> = {
  Unchanged: 'inchangé',
  Modified: 'modifié',
  Added: 'ajouté',
  Renamed: 'renommé',
  Deleted: 'supprimé',
}

const tone: Record<Change['kind'], string> = {
  Unchanged: 'bg-sunken text-muted',
  Modified: 'bg-info-bg text-info',
  Added: 'bg-success-bg text-success',
  Renamed: 'bg-info-bg text-info',
  Deleted: 'bg-warning-bg text-warning',
}

function relative(iso: string) {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)

  if (seconds < 10) return 'à l’instant'
  if (seconds < 60) return `il y a ${seconds} s`
  return `il y a ${Math.round(seconds / 60)} min`
}
