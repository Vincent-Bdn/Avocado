import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, ChevronRight, Folder, FolderOpen, Paperclip, Search, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { useToasts } from '../components/ui/toast.js'
import { FileGlyph } from './Documents.js'
import { TabPanel } from './shared.js'
import { formatSize } from '../lib/urgency.js'
import { cn } from '../lib/utils.js'

interface FolderEntry {
  name: string
  relativePath: string
  isFolder: boolean
  sizeBytes: number
  modifiedAt: string
  isMail: boolean
  /** Set when Avocado produced this file as a pièce. */
  exhibitNumber: number | null
}

interface FolderListing {
  path: string | null
  missing: boolean
  files: number
  bytes: number
  entries: FolderEntry[]
}

/**
 * The dossier's folder, as it is on disk.
 *
 * <p><b>Avocado stopped owning these files.</b> Ten lawyers said the same thing: they already have a
 * place for their documents, arranged the way they want it, and an application that took copies into
 * an encrypted store they had to check files out of was work rather than help. So this lists her
 * folder, and Explorer is where the work happens.</p>
 *
 * <p>Nothing is cached. She renames things, drops mail in from Outlook and reorganises on a Friday
 * afternoon, and none of that should need Avocado to notice: the list is read when it is looked at,
 * and again whenever she comes back to the window.</p>
 */
export function DossierFiles({ matterId, folder, isOpen, onChanged }: {
  matterId: string
  folder: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [listing, setListing] = useState<FolderListing | null>(null)
  const [at, setAt] = useState('')
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)
  const toasts = useToasts()

  const reload = useCallback(() => {
    api<FolderListing>(`/api/matters/${matterId}/folder?path=${encodeURIComponent(at)}`)
      .then(setListing)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
  }, [matterId, at])

  useEffect(reload, [reload])

  // She will go and do the work in Explorer and come back. What she comes back to has to be what she
  // left there, not what it was when the tab opened.
  useEffect(() => {
    const refresh = () => reload()

    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [reload])

  useEffect(() => { setAt(''); setQuery('') }, [folder])

  async function reveal(entry: FolderEntry) {
    if (entry.isFolder) {
      setAt(entry.relativePath)
      return
    }

    await window.avocado.revealFile(`${folder}\\${entry.relativePath.replace(/\//g, '\\')}`)
  }

  async function verser(entry: FolderEntry) {
    setError(null)

    try {
      const { number } = await post<{ number: number; name: string }>(
        `/api/matters/${matterId}/exhibits/verser`,
        { relativePath: entry.relativePath, label: null },
      )

      toasts.succeeded(
        `Versé comme pièce ${number}`,
        'Une copie a été écrite dans le sous-dossier « Pièces ». L’original n’a pas bougé.',
      )

      reload()
      onChanged()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  const crumbs = at === '' ? [] : at.split('/')

  const shown = query.trim() === ''
    ? listing?.entries ?? []
    : (listing?.entries ?? []).filter((entry) =>
      fold(entry.name).includes(fold(query)))

  return (
    <TabPanel>
      {toasts.view}

      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={() => void window.avocado.revealFolder(folder)}>
          <FolderOpen size={14} strokeWidth={1.75} />
          Ouvrir le dossier
        </Button>

        <span
          title={folder}
          className="min-w-0 flex-1 truncate font-mono text-[11px] text-muted"
        >
          {folder}
        </span>
      </div>

      {listing?.missing && (
        <div className="flex items-start gap-2 rounded-sm border border-[#E8D5AE] bg-warning-bg px-2.5 py-2 text-warning">
          <AlertCircle size={14} strokeWidth={2} className="mt-px shrink-0" />
          <div className="min-w-0">
            <div className="text-[12.5px] font-medium">Ce dossier est introuvable</div>
            <p className="m-0 mt-0.5 text-[11.5px] leading-[17px]">
              Il a peut-être été renommé, déplacé, ou il est sur un disque qui n’est pas branché.
              Rien n’est perdu : indiquez son nouvel emplacement dans « Modifier ».
            </p>
          </div>
        </div>
      )}

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {listing && !listing.missing && (
        <>
          <div className="flex flex-wrap items-center gap-1.5">
            {/* A breadcrumb rather than a tree: the folder is hers and may be nested any way she
                likes, and walking into one is the gesture she already knows from Explorer. */}
            <button
              type="button"
              onClick={() => setAt('')}
              className={cn(
                'text-[11.5px] leading-4',
                at === '' ? 'font-medium text-ink' : 'text-brand-on-subtle hover:underline',
              )}
            >
              {folder.split(/[/\\]/).filter(Boolean).pop()}
            </button>

            {crumbs.map((crumb, index) => (
              <span key={crumb + index} className="flex items-center gap-1.5">
                <ChevronRight size={12} strokeWidth={2} className="text-muted" />
                <button
                  type="button"
                  onClick={() => setAt(crumbs.slice(0, index + 1).join('/'))}
                  className={cn(
                    'text-[11.5px] leading-4',
                    index === crumbs.length - 1
                      ? 'font-medium text-ink'
                      : 'text-brand-on-subtle hover:underline',
                  )}
                >
                  {crumb}
                </button>
              </span>
            ))}

            <span className="flex-1" />

            <span className="flex items-center gap-1.5">
              <Search size={13} strokeWidth={1.75} className="shrink-0 text-muted" />
              <Input
                inputSize="sm"
                className="w-[180px]"
                value={query}
                placeholder="Filtrer ici…"
                onChange={(event) => setQuery(event.target.value)}
              />
              {query !== '' && (
                <button
                  type="button"
                  aria-label="Effacer"
                  onClick={() => setQuery('')}
                  className="grid h-6 w-6 shrink-0 place-items-center rounded-[3px] text-ink-secondary hover:bg-hover"
                >
                  <X size={13} strokeWidth={2} />
                </button>
              )}
            </span>
          </div>

          <div className="border-t border-line-subtle">
            {shown.length === 0 && (
              <p className="m-0 py-3 text-[12px] text-muted">
                {query.trim() === '' ? 'Ce sous-dossier est vide.' : 'Rien de ce nom ici.'}
              </p>
            )}

            {shown.map((entry) => (
              <div
                key={entry.relativePath}
                className="group flex items-center gap-2.5 border-b border-[#F1F3EE] px-1"
              >
                <button
                  type="button"
                  onClick={() => void reveal(entry)}
                  title={entry.isFolder ? 'Ouvrir ce sous-dossier' : 'Montrer ce fichier dans l’explorateur'}
                  className="flex min-w-0 flex-1 items-center gap-2.5 py-2 text-left"
                >
                  {entry.isFolder ? (
                    <Folder size={14} strokeWidth={1.75} className="w-[26px] shrink-0 text-ink-secondary" />
                  ) : entry.exhibitNumber === null ? (
                    <FileGlyph fileName={entry.name} className="w-[26px]" />
                  ) : (
                    <span className="grid h-[18px] w-[26px] shrink-0 place-items-center rounded-[3px] border border-[#BFD3C5] bg-brand-subtle font-mono text-[9.5px] leading-none font-medium text-brand-on-subtle">
                      {entry.exhibitNumber}
                    </span>
                  )}

                  <span className={cn('min-w-0 flex-1 truncate text-[12px]', entry.isFolder && 'font-medium')}>
                    {entry.name}
                  </span>
                </button>

                <span className="shrink-0 font-mono text-[10.5px] text-muted tnum">
                  {formatSize(entry.sizeBytes)}
                </span>

                <span className="w-[104px] shrink-0 text-right font-mono text-[10.5px] text-muted tnum">
                  {when(entry.modifiedAt)}
                </span>

                {/* Versement writes a numbered copy into « Pièces ». Not offered on a pièce, which is
                    already one, nor on a folder, nor on a dossier clôturé. */}
                <span className="w-6 shrink-0">
                  {isOpen && !entry.isFolder && entry.exhibitNumber === null && (
                    <button
                      type="button"
                      title="Verser comme pièce : une copie numérotée dans « Pièces »"
                      onClick={() => void verser(entry)}
                      className="grid h-6 w-6 place-items-center rounded-[3px] text-ink-secondary opacity-0 hover:bg-hover focus-visible:opacity-100 group-hover:opacity-100"
                    >
                      <Paperclip size={13} strokeWidth={1.75} />
                    </button>
                  )}
                </span>
              </div>
            ))}
          </div>

          <p className="m-0 text-[11px] leading-4 text-muted">
            {listing.files.toLocaleString('fr-FR')} fichiers dans ce dossier,{' '}
            {(listing.bytes / 1e9).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go. Avocado
            les lit et ne les déplace pas. Il ne les sauvegarde pas encore : continuez à sauvegarder
            ce répertoire comme vous le faisiez.
          </p>
        </>
      )}
    </TabPanel>
  )
}

const fold = (value: string) =>
  value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** « hier 17:12 » while it is fresh, JJ/MM/AAAA once it is not. */
function when(iso: string) {
  const at = new Date(iso)
  const midnight = new Date()
  midnight.setHours(0, 0, 0, 0)

  const days = Math.floor((midnight.getTime() - at.getTime()) / 86_400_000) + 1
  const time = at.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })

  if (days <= 0) return `aujourd’hui ${time}`
  if (days === 1) return `hier ${time}`

  return at.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
}
