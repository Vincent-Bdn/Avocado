import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import {
  ChevronRight, Download, File, FileArchive, FilePlus2, FileSpreadsheet, FileText, FileType, Folder,
  FolderInput, FolderOpen, Image as ImageIcon, Mail, Paperclip, Pencil, Search,
  Trash2, Undo2, X,
} from 'lucide-react'
import { ApiError, api, download, post } from '../api.js'
import { NumberPill } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Dialog, DialogActions, Field } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { Select } from '../components/ui/select.js'
import { useToasts } from '../components/ui/toast.js'
import { cn } from '../lib/utils.js'
import { formatSize } from '../lib/urgency.js'
import { Caption, Micro, Row, RowAction, RowMain, TabPanel } from './shared.js'

interface DocumentItem {
  id: string
  exhibitNumber: number | null
  exhibitLabel: string | null
  fileName: string
  folder: string | null
  type: string | null
  sizeBytes: number
  mimeType: string | null
  documentDate: string | null
  addedAt: string
  updatedAt: string
  version: number
  originActivityId: string | null
}

interface WorkspaceState {
  open: { documentId: string; path: string }[]
  abandoned: { documentId: string; fileName: string; modifiedUtc: string }[]
}

interface TemplateItem {
  id: string
  name: string
  kind: string | null
}

interface DocumentPage {
  items: DocumentItem[]
  total: number
  exhibitCount: number
  totalSizeBytes: number
  freeExhibitNumbers: number[]
  nextExhibitNumber: number
  folders: string[]
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

/**
 * Any file attached to the dossier. A document becomes a pièce when it is given a number and a
 * libellé written for the judge, so both live in one list and the distinction is legible at a glance.
 */

export function Documents({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [page, setPage] = useState<DocumentPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [workspace, setWorkspace] = useState<WorkspaceState>({ open: [], abandoned: [] })

  const [templates, setTemplates] = useState<TemplateItem[]>([])
  const [generating, setGenerating] = useState(false)
  const [uploadFolder, setUploadFolder] = useState('')
  const [query, setQuery] = useState('')
  const [showExhibits, setShowExhibits] = useState(true)
  const toasts = useToasts()
  const input = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    api<DocumentPage>(`/api/matters/${matterId}/documents`)
      .then(setPage)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(reload, [reload])

  useEffect(() => {
    api<TemplateItem[]>('/api/templates').then(setTemplates).catch(() => setTemplates([]))
  }, [])

  const readWorkspace = useCallback(
    () => api<WorkspaceState>('/api/documents/workspace').then(setWorkspace).catch(() => undefined),
    [],
  )

  useEffect(() => { void readWorkspace() }, [readWorkspace])

  /**
   * Only the leftovers are read now.
   *
   * <p>« open » used to list documents checked out one at a time, and the list polled while any of them
   * was in Word. That path is gone; what remains is « abandoned », the working copies a crash left
   * behind, which is a question about data and has to keep being asked. The dossier folder does its own
   * polling, in DossierFolder.</p>
   */

  /**
   * After something the user did: the edited row has served its purpose and closes.
   *
   * Stable, because DossierFolder holds a refresh in a dependency list driving a five second
   * interval; a new function each render would clear and re-arm it before it ever fired.
   */
  const refresh = useCallback(() => {
    setEditing(null)
    reload()
    onChanged()
  }, [reload, onChanged])

  async function upload(files: FileList) {
    setBusy(true)
    setError(null)

    try {
      const form = new FormData()
      for (const file of files) form.append('files', file)

      // Filed on the way in, from the field beside the drop zone. Uploading and then classifying is
      // two steps for one intention, and the second is the one that gets skipped.
      if (uploadFolder.trim()) form.append('folder', uploadFolder.trim())

      // A drop always creates plain documents. Numbering evidence is a legal act, never a side
      // effect of dragging a file.
      await api(`/api/matters/${matterId}/documents`, { method: 'POST', body: form })
      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  async function file(item: DocumentItem, folder: string | null) {
    setError(null)

    try {
      await api(`/api/documents/${item.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          fileName: item.fileName,
          folder,
          type: item.type,
          documentDate: item.documentDate,
        }),
      })

      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  /** Promoting, relabelling and withdrawing are all the same two endpoints. */
  async function setExhibit(id: string, label: string | null) {
    setError(null)

    try {
      if (label === null) {
        await api(`/api/documents/${id}/exhibit`, { method: 'DELETE' })
      } else {
        await api(`/api/documents/${id}/exhibit`, {
          method: 'PUT',
          body: JSON.stringify({ exhibitLabel: label }),
        })
      }

      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  /**
   * Decrypts into the coffre's working folder and hands the path to the operating system.
   *
   * A closed dossier opens read-only, reading an old lettre de mission to reuse its wording is a
   * normal thing to do, and the toast says so, because a file that looks editable and silently
   * discards the edits would be worse than one that refuses to open.
   */
  async function resolve(id: string, keep: boolean) {
    setError(null)

    try {
      await post(`/api/documents/${id}/resolve?keep=${keep}`, {})
      refresh()
      await readWorkspace()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  async function remove(id: string) {
    setError(null)

    try {
      await api(`/api/documents/${id}`, { method: 'DELETE' })
      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  const exhibits = page?.items.filter((item) => item.exhibitNumber !== null) ?? []

  /**
   * The folders standing open, by path, or null while she has not chosen.
   *
   * <p><b>Null rather than a default</b>, so that the default is derived from whatever dossier is on
   * screen instead of being settled once. This component is not remounted when she moves to another
   * dossier, only handed a new matterId, and its list arrives a moment later: anything computed the
   * instant the id changed would be computed from the previous dossier's folders.</p>
   *
   * <p>The default itself is: shut. The reason this screen was rebuilt is a dossier of 1 878
   * documents across 104 folders seven deep, and drawing all of it was the whole problem. A small one
   * opens flat instead, since 68 of her 101 dossiers have no folders at all and a handful of files,
   * and making her unfold something to see seven documents would be worse than what was here
   * before.</p>
   *
   * <p>Kept for the session, because switching tabs unmounts this. Not kept longer: where she was
   * last month is not where she is now.</p>
   */
  const [chosen, setChosen] = useState<Set<string> | null>(null)

  useEffect(() => setChosen(read(`documents-open:${matterId}`)), [matterId])

  const expanded = useMemo(
    () => chosen
      ?? new Set(page && page.total <= FLAT_ENOUGH ? page.folders.flatMap(ancestors) : []),
    [chosen, page],
  )

  const settle = useCallback((next: Set<string>) => {
    setChosen(next)
    write(`documents-open:${matterId}`, next)
  }, [matterId])

  const toggleFolder = useCallback((path: string) => {
    const next = new Set(expanded)
    if (!next.delete(path)) next.add(path)
    settle(next)
  }, [expanded, settle])

  const tree = useMemo(() => build(page?.items ?? []), [page])

  const found = useMemo(() => {
    const needles = fold(query).split(/\s+/).filter(Boolean)

    if (needles.length === 0) return []

    // Every word has to match, anywhere: « facture couleyre » finds it whichever order she typed and
    // whichever of the name, the libellé or the folder each word came from.
    return (page?.items ?? []).filter((item) => {
      const haystack = fold(
        `${item.fileName} ${item.exhibitLabel ?? ''} ${item.folder ?? ''} ${item.type ?? ''}`,
      )

      return needles.every((needle) => haystack.includes(needle))
    })
  }, [page, query])

  const rowProps = (item: DocumentItem) => ({
    item,
    isOpen,
    editing: editing === item.id,
    nextNumber: page?.nextExhibitNumber ?? 1,
    onEdit: () => setEditing(item.id),
    onCancel: () => setEditing(null),
    onLabel: (label: string) => void setExhibit(item.id, label),
    onWithdraw: () => void setExhibit(item.id, null),
    onDelete: () => void remove(item.id),
    folders: page?.folders ?? [],
    onFile: (folder: string | null) => void file(item, folder),
  })

  return (
    <TabPanel className="relative">
      {toasts.view}

      {isOpen && (
        <div
          onDragOver={(event) => { event.preventDefault(); setDragging(true) }}
          onDragLeave={() => setDragging(false)}
          onDrop={(event) => {
            event.preventDefault()
            setDragging(false)
            if (event.dataTransfer.files.length) void upload(event.dataTransfer.files)
          }}
          className={cn(
            'flex items-center gap-3 rounded-md border-[1.5px] border-dashed px-3.5 py-3.5',
            dragging ? 'border-[var(--focus-ring)] bg-brand-subtle' : 'border-line bg-app',
          )}
        >
          <Paperclip size={18} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />

          <div className="grid min-w-0 flex-1 gap-0.5">
            <strong className="text-[12.5px] font-medium">
              {busy
                ? 'Chiffrement en cours…'
                : dragging
                  ? uploadFolder.trim()
                    ? `Déposer pour classer dans « ${uploadFolder.trim()} »`
                    : 'Déposer pour classer dans ce dossier'
                  : 'Glisser des fichiers ici'}
            </strong>
            <Micro>
              PDF, DOCX, EML, JPG, XLSX · 50 Mo par fichier. Ils arrivent comme documents ; vous leur
              donnerez un n° de pièce si besoin.
            </Micro>
          </div>

          {/* Chosen before the drop, not after it. */}
          <span className="flex shrink-0 items-center gap-1.5">
            <Folder size={13} strokeWidth={1.75} className="text-muted" />
            <Input
              list="upload-folders"
              className="w-[170px]"
              value={uploadFolder}
              placeholder="Classer dans…"
              onChange={(event) => setUploadFolder(event.target.value)}
            />
            <datalist id="upload-folders">
              {(page?.folders ?? []).map((name) => <option key={name} value={name} />)}
            </datalist>
          </span>

          <Button variant="secondary" onClick={() => input.current?.click()}>Parcourir…</Button>

          <input
            ref={input}
            type="file"
            multiple
            hidden
            onChange={(event) => event.target.files && void upload(event.target.files)}
          />
        </div>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      {/*
        Left behind by a crash. Never deleted on sight: the copy on disk may hold an afternoon the
        coffre has never seen, so the choice is hers and the wording says which is which.
      */}
      {workspace.abandoned.map((file) => (
        <div
          key={file.documentId}
          className="flex flex-wrap items-center gap-2 rounded-md border border-[#E8D5AE] border-l-[3px] border-l-[#8A5A10] bg-[#FDF8ED] px-3.5 py-3 text-[#6E4A0E]"
        >
          <div className="min-w-0 flex-1">
            <div className="text-[12.5px] font-semibold">
              « {file.fileName} » est resté ouvert lors du dernier arrêt
            </div>
            <p className="m-0 mt-0.5 text-[11.5px] leading-[17px]">
              Sa dernière modification date du{' '}
              {new Date(file.modifiedUtc).toLocaleString('fr-FR')}. Reprendre le remet dans le coffre ;
              écarter le supprime définitivement.
            </p>
          </div>

          <Button onClick={() => void resolve(file.documentId, true)}>Reprendre les modifications</Button>
          <Button variant="secondary" onClick={() => void resolve(file.documentId, false)}>Écarter</Button>
        </div>
      ))}

      {isOpen && templates.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => setGenerating(true)}>
            <FilePlus2 size={14} strokeWidth={1.75} />
            Générer depuis un modèle
          </Button>
          <Micro>{templates.length} modèle{templates.length > 1 ? 's' : ''} disponible{templates.length > 1 ? 's' : ''}</Micro>
        </div>
      )}

      {generating && (
        <GenerateDialog
          matterId={matterId}
          templates={templates}
          folders={page?.folders ?? []}
          onCancel={() => setGenerating(false)}
          onGenerated={() => { setGenerating(false); refresh() }}
        />
      )}

      {page?.total === 0 && (
        <EmptyState icon={<FileText size={18} strokeWidth={1.8} />} title="Aucun document">
          Ces documents-ci vivent dans le coffre, chiffrés. Indiquez à ce dossier le répertoire où
          vous travaillez et ils cèderont la place à vos propres fichiers.
        </EmptyState>
      )}

      {page && page.total > 0 && (
        <>
          {/* Above everything, because past a few hundred documents the answer to « où est ce
              courrier » is typing three letters, not walking a tree. */}
          <span className="flex items-center gap-1.5">
            <Search size={13} strokeWidth={1.75} className="shrink-0 text-muted" />
            <Input
              inputSize="sm"
              className="flex-1"
              value={query}
              placeholder={`Rechercher parmi ${page.total.toLocaleString('fr-FR')} documents…`}
              onChange={(event) => setQuery(event.target.value)}
            />
            {query !== '' && (
              <RowAction label="Effacer la recherche" onClick={() => setQuery('')}>
                <X size={13} strokeWidth={2} />
              </RowAction>
            )}
          </span>

          {/* A search answers across the whole dossier, so it replaces the tree rather than filtering
              it: a folder half emptied by a filter still reads as a folder, and she would take what
              is left for everything it holds. */}
          {query.trim() !== '' ? (
            <div>
              <Caption>
                {found.length === 0
                  ? 'Aucun résultat'
                  : `${found.length.toLocaleString('fr-FR')} résultat${found.length > 1 ? 's' : ''}`}
              </Caption>

              {found.slice(0, RESULTS).map((item) => (
                <DocumentRow key={item.id} {...rowProps(item)} context={item.folder} />
              ))}

              {found.length > RESULTS && (
                <Micro>
                  Les {RESULTS} premiers sur {found.length.toLocaleString('fr-FR')}. Ajoutez un mot
                  pour resserrer.
                </Micro>
              )}
            </div>
          ) : (
            <>
              {/* Pièces stay pinned above the tree and also appear in the folder they are filed in.
                  Both are true, and a folder that hid its own pièces would report a count nobody
                  could reconcile with the folder on disk. */}
              {exhibits.length > 0 && (
                <Section
                  label="Pièces"
                  count={exhibits.length}
                  open={showExhibits}
                  onToggle={() => setShowExhibits((current) => !current)}
                >
                  {exhibits.map((item) => (
                    <DocumentRow key={item.id} {...rowProps(item)} context={item.folder} />
                  ))}
                </Section>
              )}

              <div className="flex items-baseline gap-2">
                <Caption>Documents · {page.total.toLocaleString('fr-FR')}</Caption>

                {tree.children.length > 0 && (
                  <button
                    type="button"
                    onClick={() => settle(expanded.size > 0 ? new Set() : new Set(paths(tree)))}
                    className="ml-auto text-[11px] text-ink-secondary underline-offset-2 hover:underline"
                  >
                    {expanded.size > 0 ? 'Tout replier' : 'Tout déplier'}
                  </button>
                )}
              </div>

              <Tree
                node={tree}
                depth={0}
                expanded={expanded}
                onToggle={toggleFolder}
                row={(item) => <DocumentRow key={item.id} {...rowProps(item)} />}
              />
            </>
          )}
        </>
      )}

      {page && page.freeExhibitNumbers.length > 0 && (
        <Micro>
          Numéros libres : {page.freeExhibitNumbers.join(', ')}. Ils restent libres volontairement, ces
          numéros pouvant être cités dans des conclusions déjà déposées.
        </Micro>
      )}
    </TabPanel>
  )
}

/**
 * Picking a modèle and where the result should be filed. The generated file lands in the coffre, not
 * in a download: it is a draft she will finish in Word, and the edits go straight back.
 */
function GenerateDialog({ matterId, templates, folders, onCancel, onGenerated }: {
  matterId: string
  templates: TemplateItem[]
  folders: string[]
  onCancel: () => void
  onGenerated: () => void
}) {
  const [templateId, setTemplateId] = useState(templates[0]?.id ?? '')
  const [fileName, setFileName] = useState('')
  const [folder, setFolder] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function generate() {
    setBusy(true)
    setError(null)

    try {
      await post(`/api/matters/${matterId}/documents/from-template/${templateId}`, {
        fileName: fileName.trim() || null,
        folder: folder.trim() || null,
      })

      onGenerated()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title="Générer depuis un modèle" onClose={onCancel}>
      <Field label="Modèle">
        <Select className="h-8" value={templateId} onChange={(event) => setTemplateId(event.target.value)}>
          {templates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.kind ? `${template.kind} · ${template.name}` : template.name}
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Nom du fichier">
        <Input
          inputSize="lg"
          value={fileName}
          placeholder="laissé vide, il est composé du modèle et de la référence"
          onChange={(event) => setFileName(event.target.value)}
        />
      </Field>

      <Field label="Classer dans">
        <Input
          inputSize="lg"
          list="generate-folders"
          value={folder}
          placeholder="Correspondance"
          onChange={(event) => setFolder(event.target.value)}
        />
        <datalist id="generate-folders">
          {folders.map((name) => <option key={name} value={name} />)}
        </datalist>
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy || !templateId} onClick={() => void generate()}>Générer</Button>
      </DialogActions>
    </Dialog>
  )
}

/** A dossier this size or smaller opens with everything already unfolded. */
const FLAT_ENOUGH = 40

/** How many search results to draw before asking for another word. */
const RESULTS = 200

/** One folder, built from the flat path each document carries. */
interface Branch {
  path: string
  name: string
  children: Branch[]
  files: DocumentItem[]
  /** Including everything underneath, which is what says where the mass of a dossier sits. */
  total: number
}

/**
 * The folder tree of a dossier.
 *
 * <p>A document carries its folder as one string, « 00 Missions/02 API-SC/01 Courriers », and no
 * folder exists on its own: it exists exactly as long as a document names it. So the tree is built
 * from the paths every time, which is also what stops an empty hierarchy accumulating around three
 * files. Intermediate folders holding nothing directly still get a node, since something below them
 * does.</p>
 */
function build(items: DocumentItem[]): Branch {
  const root: Branch = { path: '', name: '', children: [], files: [], total: 0 }

  const reach = (path: string) => {
    let node = root
    let walked = ''

    for (const segment of path.split('/')) {
      if (segment === '') continue

      walked = walked === '' ? segment : `${walked}/${segment}`

      let next = node.children.find((child) => child.name === segment)

      if (next === undefined) {
        next = { path: walked, name: segment, children: [], files: [], total: 0 }
        node.children.push(next)
      }

      node = next
    }

    return node
  }

  for (const item of items) reach(item.folder?.trim() ?? '').files.push(item)

  // Numeric collation, because she numbers her drawers: « 2 Actes » belongs before « 10 Pièces » and
  // sorts after it on the strings alone.
  const settle = (node: Branch): number => {
    node.children.sort((left, right) => left.name.localeCompare(right.name, 'fr', { numeric: true }))
    node.files.sort((left, right) =>
      left.fileName.localeCompare(right.fileName, 'fr', { numeric: true }))

    node.total = node.files.length + node.children.reduce((sum, child) => sum + settle(child), 0)
    return node.total
  }

  settle(root)
  return root
}

/** Every folder path in the tree, for « Tout déplier ». */
function paths(node: Branch): string[] {
  return node.children.flatMap((child) => [child.path, ...paths(child)])
}

/** « a/b/c » needs a, a/b and a/b/c open for its files to be on screen. */
function ancestors(path: string): string[] {
  const segments = path.split('/')

  return segments.map((_, index) => segments.slice(0, index + 1).join('/'))
}

/** Accent- and case-blind, so « procedure » finds « Procédure ». */
const fold = (value: string) =>
  value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

/** Session storage is a convenience and never load-bearing, so every access can fail quietly. */
function read(key: string): Set<string> | null {
  try {
    const stored = sessionStorage.getItem(key)
    return stored === null ? null : new Set(JSON.parse(stored) as string[])
  } catch {
    return null
  }
}

function write(key: string, value: Set<string>) {
  try {
    sessionStorage.setItem(key, JSON.stringify([...value]))
  } catch {
    // A private window, or storage turned off. The tree simply forgets between visits.
  }
}

/** A group that opens and shuts, so everything on this screen behaves the same way. */
function Section({ label, count, open, onToggle, children }: {
  label: string
  count: number
  open: boolean
  onToggle: () => void
  children: ReactNode
}) {
  return (
    <div>
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-center gap-1.5 pt-3.5 pb-1 font-mono text-[10px] tracking-[0.05em] uppercase text-muted hover:text-ink-secondary"
      >
        <ChevronRight
          size={11}
          strokeWidth={2.5}
          className={cn('shrink-0 transition-transform', open && 'rotate-90')}
        />
        {label} · <span className="tnum">{count.toLocaleString('fr-FR')}</span>
      </button>

      {open && children}
    </div>
  )
}

/**
 * Subfolders first, then this folder's own files.
 *
 * <p>Which is the answer to a folder holding both: it is how any file manager lists one, and it is
 * why the tree carries the files rather than a second pane. A dossier is full of folders that hold
 * four documents and two subfolders, and a layout that put them in different places would make her
 * look twice for one folder's contents.</p>
 */
function Tree({ node, depth, expanded, onToggle, row }: {
  node: Branch
  depth: number
  expanded: Set<string>
  onToggle: (path: string) => void
  row: (item: DocumentItem) => ReactNode
}) {
  return (
    <>
      {node.children.map((child) => {
        const open = expanded.has(child.path)

        return (
          <div key={child.path}>
            <button
              type="button"
              onClick={() => onToggle(child.path)}
              style={{ paddingLeft: 8 + depth * 15 }}
              className="flex min-h-[30px] w-full items-center gap-1.5 border-t border-line-subtle pr-2 text-left text-[12px] hover:bg-hover"
            >
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={cn('shrink-0 text-muted transition-transform', open && 'rotate-90')}
              />

              {open
                ? <FolderOpen size={14} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />
                : <Folder size={14} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />}

              <span className="min-w-0 flex-1 truncate font-medium">{child.name}</span>

              <span className="shrink-0 font-mono text-[10.5px] text-muted tnum">
                {child.total.toLocaleString('fr-FR')}
              </span>
            </button>

            {open && (
              <Tree
                node={child}
                depth={depth + 1}
                expanded={expanded}
                onToggle={onToggle}
                row={row}
              />
            )}
          </div>
        )
      })}

      <div style={{ paddingLeft: depth * 15 }}>{node.files.map(row)}</div>
    </>
  )
}

/**
 * What kind of file this is, at a glance.
 *
 * <p>Worth more than the mapping costs in a dossier that is half correspondence: 4 713 of the 13 917
 * documents in the real export arrived as courriels, and telling a .msg from the .pdf beside it is most
 * of what a name in a long list has to do. Lawyers reading the first version said every row surfaced
 * the same whatever the file was.</p>
 *
 * <p>Each kind carries a tint as well as a glyph, muted enough that fifty rows do not become a
 * rainbow, and the glyph alone still separates them in monochrome.</p>
 */
const KINDS: { extensions: string[]; icon: typeof Mail; tone: string; label: string }[] = [
  { extensions: ['msg', 'eml'], icon: Mail, tone: 'text-[#2B5578]', label: 'Courriel' },
  {
    extensions: ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'tif', 'tiff', 'heic'],
    icon: ImageIcon,
    tone: 'text-[#7A4E86]',
    label: 'Image',
  },
  { extensions: ['xls', 'xlsx', 'xlsm', 'csv', 'ods'], icon: FileSpreadsheet, tone: 'text-[#2F6B47]', label: 'Tableur' },
  { extensions: ['pdf'], icon: FileType, tone: 'text-[#A32A22]', label: 'PDF' },
  { extensions: ['doc', 'docx', 'odt', 'rtf', 'txt'], icon: FileText, tone: 'text-[#2C4A38]', label: 'Document' },
  { extensions: ['zip', '7z', 'rar'], icon: FileArchive, tone: 'text-[#8A5A10]', label: 'Archive' },
]

export function kindOf(fileName: string) {
  const extension = fileName.slice(fileName.lastIndexOf('.') + 1).toLowerCase()

  return KINDS.find((kind) => kind.extensions.includes(extension))
}

export function FileGlyph({ fileName, size = 14, className }: {
  fileName: string
  size?: number
  className?: string
}) {
  const kind = kindOf(fileName)
  const Glyph = kind?.icon ?? File

  return (
    <Glyph
      size={size}
      strokeWidth={1.75}
      aria-label={kind?.label}
      className={cn('shrink-0', kind?.tone ?? 'text-disabled', className)}
    />
  )
}

function DocumentRow({
  item, isOpen, editing, nextNumber, folders, context,
  onEdit, onCancel, onLabel, onWithdraw, onDelete, onFile,
}: {
  item: DocumentItem
  isOpen: boolean
  editing: boolean
  nextNumber: number
  /** Where it is filed, shown only where the row is out of its folder: a result, a pièce, a récent. */
  context?: string | null
  folders: string[]
  onEdit: () => void
  onCancel: () => void
  onLabel: (label: string) => void
  onWithdraw: () => void
  onDelete: () => void
  onFile: (folder: string | null) => void
}) {
  const [label, setLabel] = useState(item.exhibitLabel ?? '')
  const [folder, setFolder] = useState(item.folder ?? '')
  const isExhibit = item.exhibitNumber !== null

  if (editing) {
    return (
      <div className="my-1.5 flex flex-wrap items-center gap-2 rounded-md border border-[var(--focus-ring)] px-3 py-2.5">
        <Input
          autoFocus
          className="flex-1 basis-[260px]"
          value={label}
          placeholder="Bail commercial du local sis 14 rue Duquesne, du 1er mars 2019"
          onChange={(event) => setLabel(event.target.value)}
        />

        <Micro>écrit pour le juge, pas le nom du fichier</Micro>

        <span className="flex items-center gap-1.5">
          <Folder size={13} strokeWidth={1.75} className="text-muted" />
          <Input
            list="document-folders"
            className="w-[180px]"
            value={folder}
            placeholder="Dossier de classement"
            onChange={(event) => setFolder(event.target.value)}
          />
          <datalist id="document-folders">
            {folders.map((name) => <option key={name} value={name} />)}
          </datalist>
        </span>

        <Button
          disabled={!label.trim()}
          onClick={() => {
            if ((item.folder ?? '') !== folder.trim()) onFile(folder.trim() || null)
            onLabel(label.trim())
          }}
        >
          {isExhibit ? 'Enregistrer' : `Verser comme pièce n° ${nextNumber}`}
        </Button>

        <Button
          variant="secondary"
          onClick={() => onFile(folder.trim() || null)}
          title="Classer sans en faire une pièce"
        >
          Classer seulement
        </Button>

        <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
          <X size={13} strokeWidth={2} />
        </Button>
      </div>
    )
  }

  return (
    // Double-click is the gesture everyone already has for « ouvrir », so it is the one bound here.
    <Row className="group cursor-default">
      {isExhibit ? (
        // The number pill: brand-tinted, so a pièce is identifiable before reading anything.
        <NumberPill
          bordered
          className="min-w-[26px] shrink-0 border-[#BFD3C5] bg-brand-subtle font-medium text-brand-on-subtle"
        >
          {item.exhibitNumber}
        </NumberPill>
      ) : (
        <FileGlyph fileName={item.fileName} className="w-[26px]" />
      )}

      <RowMain>
        {/* A mono title is the tell that no libellé has been written yet. */}
        <span className={item.exhibitLabel ? '' : 'font-mono'}>
          {item.exhibitLabel ?? item.fileName}
        </span>
        {item.exhibitLabel && <Micro className="font-mono">{item.fileName}</Micro>}
        {context && <Micro className="font-mono">{context}</Micro>}
      </RowMain>

      {item.version > 1 && (
        <Micro className="font-mono tnum" title={`Modifié le ${new Date(item.updatedAt).toLocaleString('fr-FR')}`}>
          v{item.version}
        </Micro>
      )}

      <Micro>{item.type}</Micro>
      <Micro className="font-mono tnum">{formatSize(item.sizeBytes)}</Micro>

      <span className="flex gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
        <RowAction
          label="Télécharger une copie"
          onClick={() => void download(`/api/documents/${item.id}/content`, item.fileName)}
        >
          <Download size={13} strokeWidth={1.75} />
        </RowAction>

        {isOpen && (
          <>
            {/* Refiling is one field, so it is offered on its own rather than only inside the
                pièce form: most documents are filed and never become pièces. */}
            <span className="relative">
              <RowAction label="Classer dans un dossier" onClick={() => undefined}>
                <FolderInput size={13} strokeWidth={1.75} />
              </RowAction>
              <select
                aria-label="Classer dans"
                value={item.folder ?? ''}
                onChange={(event) => onFile(event.target.value || null)}
                className="absolute inset-0 cursor-pointer opacity-0"
              >
                <option value="">Sans dossier</option>
                {folders.map((name) => <option key={name} value={name}>{name}</option>)}
              </select>
            </span>

            <RowAction
              label={isExhibit ? 'Modifier le libellé de la pièce' : `Verser comme pièce n° ${nextNumber}`}
              onClick={onEdit}
            >
              <Pencil size={13} strokeWidth={1.75} />
            </RowAction>

            {isExhibit && (
              <RowAction label="Retirer des pièces" onClick={onWithdraw}>
                <Undo2 size={13} strokeWidth={1.75} />
              </RowAction>
            )}

            <RowAction label="Supprimer" danger onClick={onDelete}>
              <Trash2 size={13} strokeWidth={1.75} />
            </RowAction>
          </>
        )}
      </span>
    </Row>
  )
}
