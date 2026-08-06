import { useCallback, useEffect, useRef, useState } from 'react'
import { Download, FileText, Paperclip, Pencil, Trash2, Undo2, X } from 'lucide-react'
import { ApiError, api, download } from '../api.js'
import { NumberPill } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { formatSize } from '../lib/urgency.js'
import { Caption, Micro, Row, RowAction, RowMain, TabPanel } from './shared.js'

interface DocumentItem {
  id: string
  exhibitNumber: number | null
  exhibitLabel: string | null
  fileName: string
  type: string | null
  sizeBytes: number
  mimeType: string | null
  documentDate: string | null
  addedAt: string
  originActivityId: string | null
}

interface DocumentPage {
  items: DocumentItem[]
  total: number
  exhibitCount: number
  totalSizeBytes: number
  freeExhibitNumbers: number[]
  nextExhibitNumber: number
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
  const input = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    api<DocumentPage>(`/api/matters/${matterId}/documents`)
      .then(setPage)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(reload, [reload])

  const refresh = () => {
    setEditing(null)
    reload()
    onChanged()
  }

  async function upload(files: FileList) {
    setBusy(true)
    setError(null)

    try {
      const form = new FormData()
      for (const file of files) form.append('files', file)

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
  })

  return (
    <TabPanel>
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

          <div className="grid flex-1 gap-0.5">
            <strong className="text-[12.5px] font-medium">
              {busy
                ? 'Chiffrement en cours…'
                : dragging
                  ? 'Déposer pour classer dans ce dossier'
                  : 'Glisser des fichiers ici'}
            </strong>
            <Micro>
              ou parcourir · PDF, DOCX, EML, JPG, XLSX · 50 Mo par fichier. Ils arrivent comme
              documents ; vous leur donnerez un n° de pièce si besoin.
            </Micro>
          </div>

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

      {plain.length > 0 && (
        <div>
          <Caption>Documents · {plain.length}</Caption>
          {plain.map((item) => <DocumentRow key={item.id} {...rowProps(item)} />)}
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

function DocumentRow({ item, isOpen, editing, nextNumber, onEdit, onCancel, onLabel, onWithdraw, onDelete }: {
  item: DocumentItem
  isOpen: boolean
  editing: boolean
  nextNumber: number
  onEdit: () => void
  onCancel: () => void
  onLabel: (label: string) => void
  onWithdraw: () => void
  onDelete: () => void
}) {
  const [label, setLabel] = useState(item.exhibitLabel ?? '')
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

        <Button disabled={!label.trim()} onClick={() => onLabel(label.trim())}>
          {isExhibit ? 'Enregistrer' : `Verser comme pièce n° ${nextNumber}`}
        </Button>

        <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
          <X size={13} strokeWidth={2} />
        </Button>
      </div>
    )
  }

  return (
    <Row className="group">
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

      <Micro>{item.type}</Micro>
      <Micro className="font-mono tnum">{formatSize(item.sizeBytes)}</Micro>

      <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
        <RowAction
          label="Télécharger"
          onClick={() => void download(`/api/documents/${item.id}/content`, item.fileName)}
        >
          <Download size={13} strokeWidth={1.75} />
        </RowAction>

        {isOpen && (
          <>
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
