import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Clock,
  Gavel,
  Mail,
  MailOpen,
  Paperclip,
  Pencil,
  Phone,
  StickyNote,
  Trash2,
  Users,
  X,
} from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { activityLabels, composerTypes, formatDate, formatDuration, formatTime, weekLabel } from './labels.js'
import type { ActivityListItem, ActivityListPage, ActivityType } from './types.js'

/** 8 seconds of undo instead of a confirmation dialog, per the design. */
const UNDO_MS = 8000

/**
 * « Le suivi »: the reverse-chronological log, with the composer pinned above it.
 *
 * Adding an entry is the fastest interaction in the application and the one the product exists for.
 * The duration chip is part of it, not a separate form: logging a call and its billable time in one
 * keystroke is what stops a solo practice under-recording its work.
 */
export function Journal({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [page, setPage] = useState<ActivityListPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<ActivityListItem | null>(null)
  const undoTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const reload = useCallback(() => {
    api<ActivityListPage>(`/api/matters/${matterId}/activities`)
      .then(setPage)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  /**
   * Deleting asks nothing. The row leaves at once and the request is held for eight seconds, so undo
   * restores the original entry rather than recreating a different one and losing its attachments.
   */
  function remove(entry: ActivityListItem) {
    setPendingDelete(entry)

    undoTimer.current = setTimeout(() => {
      void api(`/api/activities/${entry.id}`, { method: 'DELETE' }).then(() => {
        setPendingDelete(null)
        reload()
        onChanged()
      })
    }, UNDO_MS)
  }

  function undo() {
    if (undoTimer.current) clearTimeout(undoTimer.current)
    setPendingDelete(null)
  }

  const visible = page?.items.filter((entry) => entry.id !== pendingDelete?.id) ?? []

  return (
    <div className="journal">
      {isOpen ? (
        <Composer matterId={matterId} onAdded={() => { reload(); onChanged() }} />
      ) : (
        <ClosedNotice />
      )}

      {error && <p className="danger">{error}</p>}

      <div className="timeline">
        {visible.length === 0 && (
          <div className="empty">
            <h3>Le journal est vide</h3>
            <p className="muted">
              Notez le prochain appel dès que vous raccrochez : deux lignes suffisent.
            </p>
          </div>
        )}

        {visible.map((entry, index) => {
          const previous = visible[index - 1]
          const separator = weekLabel(entry.occurredAt)
          const showSeparator = !previous || weekLabel(previous.occurredAt) !== separator

          return (
            <div key={entry.id}>
              {showSeparator && <div className="week">{separator}</div>}

              {editing === entry.id ? (
                <EditEntry
                  entry={entry}
                  onCancel={() => setEditing(null)}
                  onSaved={() => { setEditing(null); reload(); onChanged() }}
                />
              ) : (
                <Entry
                  entry={entry}
                  matterId={matterId}
                  canEdit={isOpen}
                  onEdit={() => setEditing(entry.id)}
                  onDelete={() => remove(entry)}
                  onAttached={() => { reload(); onChanged() }}
                />
              )}
            </div>
          )
        })}
      </div>

      {pendingDelete && (
        <div className="toast" role="status">
          <span>Entrée supprimée</span>
          <button type="button" className="link-button" onClick={undo}>
            Annuler
          </button>
        </div>
      )}
    </div>
  )
}

/** Frame 1d: the composer is replaced, not disabled, and the copy explains rather than forbids. */
function ClosedNotice() {
  return (
    <div className="frozen">
      <Clock size={15} strokeWidth={1.75} />
      <div>
        <p><strong>Ce dossier est clôturé, le journal est donc figé.</strong></p>
        <p className="muted">
          Les entrées, les documents et les heures saisies restent consultables et cherchables. Rien
          n’a été supprimé. Pour reprendre l’écriture, rouvrez le dossier : la date de clôture est
          effacée, le dossier repasse « en cours » et le journal note la réouverture.
        </p>
      </div>
    </div>
  )
}

const typeIcons: Record<ActivityType, typeof Phone> = {
  Call: Phone,
  IncomingEmail: MailOpen,
  OutgoingEmail: Mail,
  IncomingLetter: MailOpen,
  OutgoingLetter: Mail,
  Meeting: Users,
  Note: StickyNote,
  Hearing: Gavel,
  Other: StickyNote,
}

function Entry({ entry, matterId, canEdit, onEdit, onDelete, onAttached }: {
  entry: ActivityListItem
  matterId: string
  canEdit: boolean
  onEdit: () => void
  onDelete: () => void
  onAttached: () => void
}) {
  const Icon = typeIcons[entry.type]
  const file = useRef<HTMLInputElement>(null)

  async function attach(files: FileList) {
    const form = new FormData()
    for (const item of files) form.append('files', item)
    form.append('activityId', entry.id)

    await api(`/api/matters/${matterId}/documents`, { method: 'POST', body: form })
    onAttached()
  }

  return (
    <article className="entry">
      <div className="gutter mono">
        <div>{formatDate(entry.occurredAt)}</div>
        <div className="disabled">{formatTime(entry.occurredAt)}</div>
      </div>

      <div className={`dot dot-${toneOf(entry.type)}`}>
        <Icon size={11} strokeWidth={2} />
      </div>

      <div className="body">
        <div className="line1">
          <strong>{activityLabels[entry.type]}</strong>
          {entry.contactName && <span className="entry-contact">{entry.contactName}</span>}
          {entry.durationMinutes && (
            <span className="chip-time mono">
              <Clock size={10} strokeWidth={2} />
              {formatDuration(entry.durationMinutes)}
            </span>
          )}
          {entry.trackingNumber && <span className="muted mono micro">{entry.trackingNumber}</span>}
        </div>

        {entry.subject && <div className="subject">{entry.subject}</div>}
        {entry.body && <p>{entry.body}</p>}

        {entry.attachments.length > 0 && (
          <div className="attachments">
            {entry.attachments.map((attachment) => (
              <span key={attachment.id} className="attachment mono">
                {attachment.name} · {formatSize(attachment.sizeBytes)}
                {attachment.exhibitNumber !== null && ` · pièce n° ${attachment.exhibitNumber}`}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Revealed on hover in a fourth column, so the row height never changes. */}
      {canEdit && (
        <div className="entry-actions">
          <button type="button" title="Modifier" onClick={onEdit}>
            <Pencil size={13} strokeWidth={1.75} />
          </button>
          <button type="button" title="Joindre un fichier" onClick={() => file.current?.click()}>
            <Paperclip size={13} strokeWidth={1.75} />
          </button>
          <button type="button" className="danger-action" title="Supprimer" onClick={onDelete}>
            <Trash2 size={13} strokeWidth={1.75} />
          </button>

          <input
            ref={file}
            type="file"
            multiple
            hidden
            onChange={(event) => event.target.files && void attach(event.target.files)}
          />
        </div>
      )}
    </article>
  )
}

function EditEntry({ entry, onCancel, onSaved }: {
  entry: ActivityListItem
  onCancel: () => void
  onSaved: () => void
}) {
  const [type, setType] = useState<ActivityType>(entry.type)
  const [subject, setSubject] = useState(entry.subject ?? '')
  const [body, setBody] = useState(entry.body ?? '')
  const [busy, setBusy] = useState(false)

  async function save() {
    setBusy(true)

    try {
      await api(`/api/activities/${entry.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          type,
          occurredAt: entry.occurredAt,
          contactId: entry.contactId,
          subject,
          body: body || null,
          trackingNumber: entry.trackingNumber,
        }),
      })

      onSaved()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="entry-edit">
      <div className="types">
        {composerTypes.map((candidate) => (
          <button
            key={candidate}
            type="button"
            className={`chip ${candidate === type ? 'chip-active' : ''}`}
            onClick={() => setType(candidate)}
          >
            {activityLabels[candidate]}
          </button>
        ))}
      </div>

      <input value={subject} onChange={(event) => setSubject(event.target.value)} />
      <textarea rows={3} value={body} onChange={(event) => setBody(event.target.value)} />

      <div className="composer-actions">
        <span className="grow" />
        <button type="button" className="secondary-button" onClick={onCancel}>
          Annuler
        </button>
        <button type="button" disabled={busy || !subject.trim()} onClick={() => void save()}>
          Enregistrer
        </button>
      </div>
    </div>
  )
}

/** Colour family of the type dot: brand for calls, info for mail and meetings, neutral otherwise. */
function toneOf(type: ActivityType): 'brand' | 'info' | 'neutral' {
  if (type === 'Call') return 'brand'
  if (type === 'IncomingEmail' || type === 'OutgoingEmail' || type === 'Meeting') return 'info'
  return 'neutral'
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} o`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} Ko`

  return `${(bytes / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}

function Composer({ matterId, onAdded }: { matterId: string; onAdded: () => void }) {
  const [type, setType] = useState<ActivityType>('Call')
  const [text, setText] = useState('')
  const [hours, setHours] = useState('')
  const [minutes, setMinutes] = useState('')
  const [timeAttached, setTimeAttached] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const textarea = useRef<HTMLTextAreaElement>(null)

  // ⌘J / Ctrl+J from anywhere in the dossier lands here.
  useEffect(() => {
    const focus = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'j') {
        event.preventDefault()
        textarea.current?.focus()
      }
    }

    window.addEventListener('keydown', focus)
    return () => window.removeEventListener('keydown', focus)
  }, [])

  const durationMinutes = timeAttached
    ? Number(hours || 0) * 60 + Number(minutes || 0)
    : 0

  async function add() {
    if (!text.trim()) return

    setBusy(true)
    setError(null)

    try {
      const [subject, ...rest] = text.split('\n')

      await post(`/api/matters/${matterId}/activities`, {
        type,
        subject,
        body: rest.join('\n') || null,
        durationMinutes: durationMinutes > 0 ? durationMinutes : null,
      })

      setText('')
      setHours('')
      setMinutes('')
      setTimeAttached(false)
      onAdded()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="composer">
      <div className="types">
        {composerTypes.map((candidate) => (
          <button
            key={candidate}
            type="button"
            className={`chip ${candidate === type ? 'chip-active' : ''}`}
            onClick={() => setType(candidate)}
          >
            {activityLabels[candidate]}
          </button>
        ))}
      </div>

      <textarea
        ref={textarea}
        value={text}
        onChange={(event) => setText(event.target.value)}
        placeholder="Noter un appel, un courrier, un rendez-vous…"
        rows={2}
        onKeyDown={(event) => {
          if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
            event.preventDefault()
            void add()
          }
        }}
      />

      <div className="composer-actions">
        {!timeAttached ? (
          <button type="button" className="chip chip-dashed" onClick={() => setTimeAttached(true)}>
            ＋ temps passé
          </button>
        ) : (
          <span className="chip-time duration-entry">
            <Clock size={11} strokeWidth={2} />
            <input
              className="mono unit"
              inputMode="numeric"
              placeholder="0"
              value={hours}
              onChange={(event) => setHours(event.target.value.replace(/\D/g, ''))}
              aria-label="Heures"
            />
            h
            <input
              className="mono unit"
              inputMode="numeric"
              placeholder="00"
              value={minutes}
              onChange={(event) => setMinutes(event.target.value.replace(/\D/g, ''))}
              aria-label="Minutes"
            />
            · facturable
            <button type="button" className="unchip" onClick={() => setTimeAttached(false)} aria-label="Retirer">
              <X size={11} strokeWidth={2.5} />
            </button>
          </span>
        )}

        <span className="grow" />
        {error && <span className="danger">{error}</span>}
        <span className="muted mono kbd-hint">⌘⏎</span>
        <button type="button" disabled={busy || !text.trim()} onClick={() => void add()}>
          Ajouter
        </button>
      </div>
    </div>
  )
}
