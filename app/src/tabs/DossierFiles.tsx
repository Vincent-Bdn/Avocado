import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, ChevronRight, Folder, FolderOpen, Loader2, Search, Stamp, X } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { useToasts } from '../components/ui/toast.js'
import { FileGlyph } from './FileGlyph.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { TabPanel } from './shared.js'
import { VerserPiecesPanel, type Picked } from './VerserPieces.js'
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
 * <p><b>A tree that opens in place, not a walk into folders.</b> This was briefly a breadcrumb: click
 * a folder, the list is replaced by its contents, click back up. Lawyers preferred the tree on sight
 * and the reason holds up. A dossier's shape is « Pièces, Correspondance, Procédure », and what she
 * is doing is comparing two of them: a tree lets her open both and keep her place, where walking in
 * loses everything else on screen. The listing is still fetched one level at a time, when a folder is
 * opened, so a dossier of 4 559 documents costs nothing until she asks for a part of it.</p>
 *
 * <p>Nothing is cached across visits. She renames things, drops mail in from Outlook and reorganises
 * on a Friday afternoon, and none of that should need Avocado to notice: the folders she has open are
 * re-read whenever she comes back to the window.</p>
 */
export function DossierFiles({ matterId, folder, isOpen, onEdit, onChanged }: {
  matterId: string
  /** Null until she has pointed this dossier at one, which is the state every dossier starts in. */
  folder: string | null
  isOpen: boolean
  onEdit: () => void
  onChanged: () => void
}) {
  const [listing, setListing] = useState<FolderListing | null>(null)
  /** Relative paths of the folders she has opened. The root is always open and is never in here. */
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  /** What each opened folder holds. Absent means « not fetched yet ». */
  const [children, setChildren] = useState<Record<string, FolderEntry[]>>({})
  const [loading, setLoading] = useState<Set<string>>(new Set())
  const [query, setQuery] = useState('')
  const [found, setFound] = useState<FolderEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  /** Files ticked for versement, by relative path, in the order she ticked them. */
  const [picked, setPicked] = useState<Picked[]>([])
  const [versing, setVersing] = useState(false)
  const toasts = useToasts()

  const reload = useCallback(() => {
    if (folder === null) return

    api<FolderListing>(`/api/matters/${matterId}/folder`)
      .then(setListing)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))

    // And every folder she had open, so that coming back from Explorer shows what she did there
    // rather than what was there when she left.
    setChildren((held) => {
      for (const path of Object.keys(held)) {
        void api<FolderListing>(`/api/matters/${matterId}/folder?path=${encodeURIComponent(path)}`)
          .then((fresh) => setChildren((current) => ({ ...current, [path]: fresh.entries })))
          .catch(() => undefined)
      }

      return held
    })
  }, [matterId, folder])

  useEffect(reload, [reload])

  // She will go and do the work in Explorer and come back. What she comes back to has to be what she
  // left there, not what it was when the tab opened.
  useEffect(() => {
    const refresh = () => reload()

    window.addEventListener('focus', refresh)
    return () => window.removeEventListener('focus', refresh)
  }, [reload])

  useEffect(() => {
    setExpanded(new Set())
    setChildren({})
    setQuery('')
    setPicked([])
  }, [folder])

  /**
   * Searched by the backend, across the whole dossier.
   *
   * <p>The tree only holds the folders she has opened, so filtering what is on screen would answer
   * « rien de ce nom ici » about a file that is there, three folders down. A search that does not
   * find things does not merely fail, it misleads.</p>
   */
  useEffect(() => {
    const term = query.trim()

    if (folder === null || term === '') {
      setFound(null)
      return
    }

    const controller = new AbortController()

    const timer = setTimeout(() => {
      void api<FolderListing>(
        `/api/matters/${matterId}/folder?q=${encodeURIComponent(term)}`,
        { signal: controller.signal },
      )
        .then((results) => setFound(results.entries))
        .catch(() => undefined)
    }, 200)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [matterId, folder, query])

  /** Opens a folder in place, fetching what is in it the first time only. */
  function toggle(entry: FolderEntry) {
    const path = entry.relativePath

    setExpanded((open) => {
      const next = new Set(open)

      if (next.has(path)) {
        next.delete(path)
      } else {
        next.add(path)
      }

      return next
    })

    if (children[path] !== undefined) return

    setLoading((busy) => new Set(busy).add(path))

    void api<FolderListing>(`/api/matters/${matterId}/folder?path=${encodeURIComponent(path)}`)
      .then((inside) => setChildren((held) => ({ ...held, [path]: inside.entries })))
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
      .finally(() => setLoading((busy) => {
        const next = new Set(busy)
        next.delete(path)
        return next
      }))
  }

  async function open(entry: FolderEntry) {
    if (entry.isFolder) {
      toggle(entry)
      return
    }

    await window.avocado.revealFile(`${folder}\\${entry.relativePath.replace(/\//g, '\\')}`)
  }

  function pick(entry: FolderEntry) {
    setPicked((chosen) => chosen.some((file) => file.relativePath === entry.relativePath)
      ? chosen.filter((file) => file.relativePath !== entry.relativePath)
      // Appended, not sorted: the order she ticks them in is the order they are numbered, and a
      // bordereau's order is an argument rather than a listing.
      : [...chosen, { name: entry.name, relativePath: entry.relativePath }])
  }

  // The state every dossier starts in, and the only thing to do about it.
  if (folder === null) {
    return (
      <TabPanel>
        <EmptyState icon={<FolderOpen size={18} strokeWidth={1.8} />} title="Où sont les documents de ce dossier ?">
          Vos documents restent chez vous, dans le répertoire où vous travaillez déjà : sur le bureau,
          un disque réseau, où vous voulez. Indiquez-le et Avocado le lira, sans rien déplacer ni
          copier.
        </EmptyState>

        <div>
          <Button onClick={onEdit}>
            <FolderOpen size={14} strokeWidth={1.75} />
            Indiquer le répertoire
          </Button>
        </div>
      </TabPanel>
    )
  }

  /** One row, and its children underneath when it is open. Depth only indents. */
  function rows(entries: FolderEntry[], depth: number, withPath = false): React.ReactNode[] {
    return entries.flatMap((entry) => {
      const isExpanded = expanded.has(entry.relativePath)
      const isLoading = loading.has(entry.relativePath)
      const inside = children[entry.relativePath]
      const chosen = picked.some((file) => file.relativePath === entry.relativePath)

      const row = (
        <div
          key={entry.relativePath}
          className="group flex items-center gap-2.5 border-b border-[#F1F3EE] px-1"
        >
          <button
            type="button"
            onClick={() => void open(entry)}
            title={entry.isFolder
              ? isExpanded ? 'Refermer ce sous-dossier' : 'Ouvrir ce sous-dossier'
              : 'Montrer ce fichier dans l’explorateur'}
            className="flex min-w-0 flex-1 items-center gap-1.5 py-2 text-left"
            style={{ paddingLeft: `${depth * 18}px` }}
          >
            {/* The chevron is the affordance: it turns, and the row below appears under it rather
                than replacing everything. Files get the same width so the names line up. */}
            <span className="grid h-3.5 w-3.5 shrink-0 place-items-center">
              {entry.isFolder && (isLoading
                ? <Loader2 size={11} className="animate-spin text-muted" />
                : (
                  <ChevronRight
                    size={12}
                    strokeWidth={2.25}
                    className={cn('text-ink-secondary transition-transform', isExpanded && 'rotate-90')}
                  />
                ))}
            </span>

            {entry.isFolder ? (
              <Folder
                size={14}
                strokeWidth={1.75}
                className={cn('w-[22px] shrink-0', isExpanded ? 'text-brand-on-subtle' : 'text-ink-secondary')}
              />
            ) : entry.exhibitNumber === null ? (
              <FileGlyph fileName={entry.name} className="w-[22px]" />
            ) : (
              <span className="grid h-[18px] w-[22px] shrink-0 place-items-center rounded-[3px] border border-[#BFD3C5] bg-brand-subtle font-mono text-[9.5px] leading-none font-medium text-brand-on-subtle">
                {entry.exhibitNumber}
              </span>
            )}

            <span className="grid min-w-0 flex-1">
              <span className={cn('truncate text-[12px]', entry.isFolder && 'font-medium')}>
                {entry.name}
              </span>
              {/* A search result is only useful if she can see where it is: the same file name
                  appears in Pièces and in Correspondance and they are not the same file. */}
              {withPath && entry.relativePath.includes('/') && (
                <span className="truncate text-[10.5px] leading-[14px] text-muted">
                  {entry.relativePath.slice(0, entry.relativePath.lastIndexOf('/')).replace(/\//g, ' › ')}
                </span>
              )}
            </span>
          </button>

          <span className="shrink-0 font-mono text-[10.5px] text-muted tnum">
            {entry.isFolder ? '' : formatSize(entry.sizeBytes)}
          </span>

          <span className="w-[104px] shrink-0 text-right font-mono text-[10.5px] text-muted tnum">
            {when(entry.modifiedAt)}
          </span>

          {/* Ticked for versement. Not offered on a pièce, which is already one, nor on a folder,
              nor on a dossier clôturé. The box stays visible once ticked, so a selection spread over
              three open sub-folders is still visible as one. */}
          <span className="grid h-6 w-6 shrink-0 place-items-center">
            {isOpen && !entry.isFolder && entry.exhibitNumber === null && (
              <input
                type="checkbox"
                aria-label={`Verser ${entry.name} comme pièce`}
                title="Verser comme pièce"
                checked={chosen}
                onChange={() => pick(entry)}
                className={cn(
                  'cursor-pointer',
                  !chosen && 'opacity-0 group-hover:opacity-100 focus-visible:opacity-100',
                )}
              />
            )}
          </span>
        </div>
      )

      if (!entry.isFolder || !isExpanded) return [row]

      if (inside === undefined) return [row]

      if (inside.length === 0) {
        return [
          row,
          <p
            key={`${entry.relativePath}:empty`}
            className="m-0 border-b border-[#F1F3EE] py-1.5 text-[11.5px] text-muted"
            style={{ paddingLeft: `${(depth + 1) * 18 + 28}px` }}
          >
            Ce sous-dossier est vide.
          </p>,
        ]
      }

      return [row, ...rows(inside, depth + 1)]
    })
  }

  const searching = query.trim() !== ''

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
            <span className="flex-1 text-[11.5px] leading-4 text-muted">
              {searching
                ? `Recherche dans tout le dossier${found === null ? '…' : ` · ${found.length} résultat${found.length > 1 ? 's' : ''}`}`
                : 'Ouvrez un sous-dossier pour voir ce qu’il contient.'}
            </span>

            <span className="flex items-center gap-1.5">
              <Search size={13} strokeWidth={1.75} className="shrink-0 text-muted" />
              <Input
                inputSize="sm"
                className="w-[200px]"
                value={query}
                placeholder="Chercher dans tout le dossier…"
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
            {searching ? (
              found !== null && found.length === 0 ? (
                <p className="m-0 py-3 text-[12px] text-muted">
                  Aucun fichier de ce nom dans ce dossier.
                </p>
              ) : (
                // Flat, with the path, because a result is only useful if she can see where it is.
                rows(found ?? [], 0, true)
              )
            ) : listing.entries.length === 0 ? (
              <p className="m-0 py-3 text-[12px] text-muted">Ce dossier est vide.</p>
            ) : (
              rows(listing.entries, 0)
            )}
          </div>

          {picked.length > 0 && (
            <div className="sticky bottom-0 flex flex-wrap items-center gap-2.5 rounded-md border border-brand bg-brand-subtle px-3 py-2">
              <span className="text-[12.5px] text-brand-on-subtle">
                {picked.length} fichier{picked.length > 1 ? 's' : ''} sélectionné
                {picked.length > 1 ? 's' : ''}
              </span>

              <span className="flex-1" />

              <Button variant="secondary" size="sm" onClick={() => setPicked([])}>
                Tout décocher
              </Button>

              <Button size="sm" onClick={() => setVersing(true)}>
                <Stamp size={13} strokeWidth={1.75} />
                Verser comme pièces
              </Button>
            </div>
          )}

          <p className="m-0 text-[11px] leading-4 text-muted">
            {listing.files.toLocaleString('fr-FR')} fichiers dans ce dossier,{' '}
            {(listing.bytes / 1e9).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go. Avocado
            les lit et ne les déplace pas ; il en copie une version chiffrée dans ses sauvegardes.
          </p>
        </>
      )}

      {versing && (
        <VerserPiecesPanel
          matterId={matterId}
          picked={picked}
          onClose={() => setVersing(false)}
          onDone={(result) => {
            setVersing(false)
            setPicked([])

            const written = result.pieces.filter((piece) => piece.error === null)

            if (result.failed > 0) {
              // Named rather than counted away: she has to know which one is not in the bordereau.
              setError(
                `${result.failed} fichier(s) non versé(s) : `
                + result.pieces.filter((piece) => piece.error !== null)
                  .map((piece) => `${piece.relativePath} (${piece.error})`).join(' · '),
              )
            }

            toasts.succeeded(
              `${written.length} pièce${written.length > 1 ? 's' : ''} versée${written.length > 1 ? 's' : ''}`,
              result.bordereauPath === null
                ? 'Copiées dans « Pièces ». Vos originaux n’ont pas bougé.'
                : 'Le bordereau a été écrit à côté d’elles. Vos originaux n’ont pas bougé.',
            )

            // « Pièces » may have just been created, so the whole tree is re-read.
            reload()
            onChanged()
          }}
        />
      )}
    </TabPanel>
  )
}

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
