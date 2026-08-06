import { useCallback, useEffect, useRef, useState } from 'react'
import { FileText, Paperclip } from 'lucide-react'
import { ApiError, api } from '../api.js'

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
  const [promoting, setPromoting] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const input = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    api<DocumentPage>(`/api/matters/${matterId}/documents`)
      .then(setPage)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  async function upload(files: FileList) {
    setBusy(true)
    setError(null)

    try {
      const form = new FormData()
      for (const file of files) {
        form.append('files', file)
      }

      // A drop always creates plain documents. Numbering evidence is a legal act, never a side
      // effect of dragging a file.
      await api(`/api/matters/${matterId}/documents`, { method: 'POST', body: form })
      reload()
      onChanged()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  async function promote(id: string) {
    try {
      await api(`/api/documents/${id}/exhibit`, {
        method: 'PUT',
        body: JSON.stringify({ exhibitLabel: label }),
      })

      setPromoting(null)
      setLabel('')
      reload()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  const exhibits = page?.items.filter((item) => item.exhibitNumber !== null) ?? []
  const plain = page?.items.filter((item) => item.exhibitNumber === null) ?? []

  return (
    <div className="tab-panel">
      {isOpen && (
        <div
          className="dropzone"
          onDragOver={(event) => event.preventDefault()}
          onDrop={(event) => {
            event.preventDefault()
            if (event.dataTransfer.files.length) void upload(event.dataTransfer.files)
          }}
        >
          <Paperclip size={18} strokeWidth={1.75} />
          <div>
            <strong>{busy ? 'Chiffrement en cours…' : 'Glisser des fichiers ici'}</strong>
            <span className="muted micro">
              ou parcourir · PDF, DOCX, EML, JPG, XLSX · 50 Mo par fichier. Ils arrivent comme
              documents ; vous leur donnerez un n° de pièce si besoin.
            </span>
          </div>

          <button type="button" className="secondary-button" onClick={() => input.current?.click()}>
            Parcourir…
          </button>

          <input
            ref={input}
            type="file"
            multiple
            hidden
            onChange={(event) => event.target.files && void upload(event.target.files)}
          />
        </div>
      )}

      {error && <p className="danger">{error}</p>}

      {page?.total === 0 && (
        <div className="empty">
          <h3>Aucun document</h3>
          <p className="muted">
            Tout ce qui arrive au dossier se range ici, chiffré. Les pièces sont des documents qui
            portent un numéro et un libellé écrit pour le juge.
          </p>
        </div>
      )}

      {exhibits.length > 0 && (
        <>
          <div className="rows-caption mono">Pièces · {exhibits.length}</div>
          <div className="rows">
            {exhibits.map((item) => (
              <Row key={item.id} item={item} />
            ))}
          </div>
        </>
      )}

      {plain.length > 0 && (
        <>
          <div className="rows-caption mono">Documents · {plain.length}</div>
          <div className="rows">
            {plain.map((item) => (
              <div key={item.id}>
                <Row item={item} />

                {isOpen && promoting !== item.id && (
                  <button
                    type="button"
                    className="link-button"
                    onClick={() => {
                      setPromoting(item.id)
                      setLabel('')
                    }}
                  >
                    Verser comme pièce n° {page?.nextExhibitNumber}
                  </button>
                )}

                {promoting === item.id && (
                  <div className="promote">
                    <input
                      className="flex"
                      autoFocus
                      value={label}
                      placeholder="Bail commercial du local sis 14 rue Duquesne, du 1er mars 2019"
                      onChange={(event) => setLabel(event.target.value)}
                    />
                    <span className="muted micro">écrit pour le juge, pas le nom du fichier</span>
                    <button type="button" disabled={!label.trim()} onClick={() => void promote(item.id)}>
                      Verser comme pièce n° {page?.nextExhibitNumber}
                    </button>
                    <button type="button" className="secondary-button" onClick={() => setPromoting(null)}>
                      Annuler
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      {page && page.freeExhibitNumbers.length > 0 && (
        <p className="muted micro">
          Numéros libres : {page.freeExhibitNumbers.join(', ')}. Ils restent libres volontairement, ces
          numéros pouvant être cités dans des conclusions déjà déposées.
        </p>
      )}
    </div>
  )
}

function Row({ item }: { item: DocumentItem }) {
  return (
    <div className="document-row">
      {item.exhibitNumber !== null ? (
        <span className="exhibit-pill mono">{item.exhibitNumber}</span>
      ) : (
        <FileText size={14} strokeWidth={1.75} className="file-glyph" />
      )}

      <span className="row-main">
        <span className={item.exhibitLabel ? '' : 'mono'}>{item.exhibitLabel ?? item.fileName}</span>
        {item.exhibitLabel && <span className="muted micro mono">{item.fileName}</span>}
      </span>

      <span className="muted micro">{item.type}</span>
      <span className="mono muted micro">{formatSize(item.sizeBytes)}</span>
    </div>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} o`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} Ko`

  return `${(bytes / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}
