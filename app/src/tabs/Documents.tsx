import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Check, Download, FilePlus2, FileText, Folder, FolderInput, Paperclip, Pencil,
  SquareArrowOutUpRight, Trash2, Undo2, X,
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
import { DossierFolder } from './DossierFolder.js'

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
   * While anything is open in Word the list polls, because the reintegration happens in the backend
   * on its own schedule and the row's « modifié » line is the only sign she has that a save landed.
   */
  useEffect(() => {
    if (workspace.open.length === 0) return

    const timer = setInterval(() => { reload(); void readWorkspace() }, 3000)
    return () => clearInterval(timer)
  }, [workspace.open.length, reload, readWorkspace])

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

  /**
   * After the background sweep wrote something: the list is stale and reloads, but whatever she is
   * typing stays open.
   *
   * <p>Sharing one refresh between the two closed the rename form every five seconds while a dossier
   * was open, which made renaming a document from Avocado essentially impossible: the row vanished
   * mid-edit, and what looked like a refresh that ate the change was the form being unmounted before
   * it could be submitted.</p>
   */
  const refreshQuietly = useCallback(() => {
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
  async function open(item: DocumentItem) {
    setError(null)

    try {
      const { path, readOnly } = await post<{ path: string; readOnly: boolean }>(
        `/api/documents/${item.id}/open`,
        {},
      )

      const failure = await window.avocado.openWorkingCopy(path)

      if (failure) {
        toasts.failed(
          `Impossible d’ouvrir « ${item.fileName} »`,
          'Aucune application n’est associée à ce type de fichier sur cet ordinateur.',
        )
      } else if (readOnly) {
        toasts.succeeded(
          'Ouvert en lecture seule',
          'Ce dossier est clôturé : vos modifications ne seront pas reprises dans le coffre. ' +
          'Enregistrez ailleurs si vous voulez repartir de ce document.',
        )
      }

      await readWorkspace()
    } catch (failure) {
      toasts.failed('Impossible d’ouvrir le document', messageOf(failure))
    }
  }

  /** Puts the last save away and removes the working copy. */
  async function close(id: string) {
    setError(null)

    try {
      await post(`/api/documents/${id}/close`, {})
      refresh()
      await readWorkspace()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

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
  const plain = page?.items.filter((item) => item.exhibitNumber === null) ?? []

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
    checkedOut: workspace.open.some((entry) => entry.documentId === item.id),
    onOpen: () => void open(item),
    onClose: () => void close(item.id),
  })

  return (
    <TabPanel className="relative">
      {toasts.view}

      {/* Above the drop zone on purpose: opening the whole dossier is the gesture that replaces
          uploading files one at a time, so it should be met first. */}
      {isOpen && <DossierFolder matterId={matterId} onChanged={refreshQuietly} />}

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
          Tout ce qui arrive au dossier se range ici, chiffré. Les pièces sont des documents qui
          portent un numéro et un libellé écrit pour le juge.
        </EmptyState>
      )}

      {exhibits.length > 0 && (
        <div>
          <Caption>Pièces · {exhibits.length}</Caption>
          {exhibits.map((item) => <DocumentRow key={item.id} {...rowProps(item)} />)}
        </div>
      )}

      {/*
        A folder exists exactly as long as a document names it, which is what stops an empty hierarchy
        accumulating around three files, and « Sans dossier » is always last rather than hidden: a file
        you have not filed is still a file you have.

        Nesting is shown rather than spelled out. Opening a dossier as a real folder means these paths
        now come from directories someone made in Explorer, so « Tototo/Tata/Tutu » printed in full on
        every heading reads as three unrelated groups. Sorting puts a parent before its children, so
        indenting by depth and naming only the last segment renders the tree they actually built.
      */}
      {plain.length > 0 && (
        <div>
          <Caption>Documents · {plain.length}</Caption>

          {groupByFolder(plain).map(([folder, items]) => {
            const segments = folder?.split('/') ?? []
            const indent = segments.length > 0 ? (segments.length - 1) * 14 : 0

            return (
              <div key={folder ?? ''}>
                {(folder !== null || groupByFolder(plain).length > 1) && (
                  <div
                    style={{ paddingLeft: indent }}
                    className="flex items-center gap-1.5 pt-3 pb-1 text-[11.5px] font-medium text-ink-secondary"
                  >
                    <Folder size={12} strokeWidth={2} className="text-muted" />
                    {segments.length > 0 ? segments[segments.length - 1] : 'Sans dossier'}
                    <span className="font-mono text-[10.5px] text-muted tnum">{items.length}</span>
                  </div>
                )}

                <div style={{ paddingLeft: indent }}>
                  {items.map((item) => <DocumentRow key={item.id} {...rowProps(item)} />)}
                </div>
              </div>
            )
          })}
        </div>
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

/** Filed folders first and alphabetical, then everything not yet filed. */
function groupByFolder(items: DocumentItem[]): [string | null, DocumentItem[]][] {
  const groups = new Map<string | null, DocumentItem[]>()

  for (const item of items) {
    const key = item.folder ?? null
    groups.set(key, [...(groups.get(key) ?? []), item])
  }

  return [...groups.entries()].sort(([left], [right]) => {
    if (left === right) return 0
    if (left === null) return 1
    if (right === null) return -1

    return left.localeCompare(right, 'fr')
  })
}

function DocumentRow({
  item, isOpen, editing, nextNumber, folders, checkedOut,
  onEdit, onCancel, onLabel, onWithdraw, onDelete, onFile, onOpen, onClose,
}: {
  item: DocumentItem
  isOpen: boolean
  editing: boolean
  nextNumber: number
  folders: string[]
  checkedOut: boolean
  onEdit: () => void
  onCancel: () => void
  onLabel: (label: string) => void
  onWithdraw: () => void
  onDelete: () => void
  onFile: (folder: string | null) => void
  onOpen: () => void
  onClose: () => void
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
    <Row className="group cursor-default" onDoubleClick={onOpen}>
      {isExhibit ? (
        // The number pill: brand-tinted, so a pièce is identifiable before reading anything.
        <NumberPill
          bordered
          className="min-w-[26px] shrink-0 border-[#BFD3C5] bg-brand-subtle font-medium text-brand-on-subtle"
        >
          {item.exhibitNumber}
        </NumberPill>
      ) : (
        <FileText size={14} strokeWidth={1.75} className="w-[26px] shrink-0 text-disabled" />
      )}

      <RowMain>
        {/* A mono title is the tell that no libellé has been written yet. */}
        <span className={item.exhibitLabel ? '' : 'font-mono'}>
          {item.exhibitLabel ?? item.fileName}
        </span>
        {item.exhibitLabel && <Micro className="font-mono">{item.fileName}</Micro>}
      </RowMain>

      {checkedOut && (
        <span className="flex shrink-0 items-center gap-1 rounded-[3px] bg-brand-subtle px-1.5 py-1 text-[10.5px] leading-3 text-brand-on-subtle">
          <SquareArrowOutUpRight size={10} strokeWidth={2} />
          ouvert
        </span>
      )}

      {item.version > 1 && (
        <Micro className="font-mono tnum" title={`Modifié le ${new Date(item.updatedAt).toLocaleString('fr-FR')}`}>
          v{item.version}
        </Micro>
      )}

      <Micro>{item.type}</Micro>
      <Micro className="font-mono tnum">{formatSize(item.sizeBytes)}</Micro>

      <span
        className={cn(
          'flex gap-0.5 transition-opacity focus-within:opacity-100 group-hover:opacity-100',
          checkedOut ? 'opacity-100' : 'opacity-0',
        )}
      >
        {checkedOut ? (
          <RowAction label="Terminer la modification et remettre au coffre" onClick={onClose}>
            <Check size={13} strokeWidth={2} />
          </RowAction>
        ) : (
          <RowAction
            label={isOpen ? 'Ouvrir et modifier' : 'Ouvrir en lecture seule'}
            onClick={onOpen}
          >
            <SquareArrowOutUpRight size={13} strokeWidth={1.75} />
          </RowAction>
        )}

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
