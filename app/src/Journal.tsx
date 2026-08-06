import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Clock, Gavel, Mail, MailOpen, Paperclip, Pencil, Phone, StickyNote, Trash2, Users, X,
} from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Chip, ChipSpan } from './components/ui/chip.js'
import { EmptyState } from './components/ui/empty-state.js'
import { Input } from './components/ui/input.js'
import { Textarea } from './components/ui/textarea.js'
import { cn } from './lib/utils.js'
import { formatSize } from './lib/urgency.js'
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

  const visible = page?.items.filter((entry) => entry.id !== pendingDelete?.id) ?? []

  return (
    <div className="relative flex min-w-0 flex-col overflow-hidden">
      {isOpen ? (
        <Composer matterId={matterId} onAdded={() => { reload(); onChanged() }} />
      ) : (
        <ClosedNotice />
      )}

      {error && <p className="px-4 text-danger">{error}</p>}

      <div className="flex-1 overflow-y-auto px-4 pb-4">
        {visible.length === 0 && (
          <EmptyState title="Le journal est vide" className="mt-4">
            Notez le prochain appel dès que vous raccrochez : deux lignes suffisent.
          </EmptyState>
        )}

        {visible.map((entry, index) => {
          const previous = visible[index - 1]
          const separator = weekLabel(entry.occurredAt)
          const showSeparator = !previous || weekLabel(previous.occurredAt) !== separator

          return (
            <div key={entry.id}>
              {showSeparator && (
                <div className="pt-[11px] pb-[5px] font-mono text-[10px] tracking-[0.05em] uppercase text-muted">
                  {separator}
                </div>
              )}

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
        <div
          role="status"
          className="absolute right-4 bottom-4 flex items-center gap-3 rounded-lg border border-line border-l-[3px] border-l-info bg-raised px-3 py-2 text-[12px] shadow-e1"
        >
          <span>Entrée supprimée</span>
          <button
            type="button"
            className="underline text-ink-secondary"
            onClick={() => {
              if (undoTimer.current) clearTimeout(undoTimer.current)
              setPendingDelete(null)
            }}
          >
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
    <div className="m-4 flex items-start gap-3 rounded-lg border border-line-subtle bg-app px-3.5 py-3">
      <Clock size={15} strokeWidth={1.75} className="mt-0.5 shrink-0 text-muted" />
      <div>
        <p className="m-0 text-[12.5px] leading-[19px]">
          <strong>Ce dossier est clôturé, le journal est donc figé.</strong>
        </p>
        <p className="m-0 mt-1 text-[12.5px] leading-[19px] text-muted">
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

/** Brand for calls, info for mail and meetings, neutral otherwise. Tint fill, never full colour. */
const typeTone: Record<ActivityType, string> = {
  Call: 'bg-brand-subtle border-brand text-brand',
  IncomingEmail: 'bg-sunken border-info text-info',
  OutgoingEmail: 'bg-sunken border-info text-info',
  IncomingLetter: 'bg-sunken border-line text-ink-secondary',
  OutgoingLetter: 'bg-sunken border-line text-ink-secondary',
  Meeting: 'bg-sunken border-info text-info',
  Note: 'bg-sunken border-line text-ink-secondary',
  Hearing: 'bg-sunken border-line text-ink-secondary',
  Other: 'bg-sunken border-line text-ink-secondary',
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
    <article className="group relative grid grid-cols-[58px_20px_minmax(0,1fr)] gap-2 border-t border-line-subtle py-2 hover:bg-app hover:shadow-[inset_2px_0_0_var(--border-default)]">
      <div className="font-mono text-[11px] leading-[15px] tnum">
        <div>{formatDate(entry.occurredAt)}</div>
        <div className="text-disabled">{formatTime(entry.occurredAt)}</div>
      </div>

      <div className={cn('grid h-5 w-5 place-items-center rounded-full border', typeTone[entry.type])}>
        <Icon size={11} strokeWidth={2} />
      </div>

      <div className="min-w-0">
        <div className="flex flex-wrap items-baseline gap-1.5 text-[12.5px] leading-[17px]">
          <strong className="font-medium">{activityLabels[entry.type]}</strong>
          {entry.contactName && (
            <span className="text-[11.5px] text-ink-secondary">{entry.contactName}</span>
          )}
          {entry.durationMinutes && (
            <ChipSpan tone="time" className="font-mono tnum">
              <Clock size={10} strokeWidth={2} />
              {formatDuration(entry.durationMinutes)}
            </ChipSpan>
          )}
          {entry.trackingNumber && (
            <span className="font-mono text-[11px] text-muted">{entry.trackingNumber}</span>
          )}
        </div>

        {entry.subject && <div className="text-[12.5px] leading-[19px]">{entry.subject}</div>}
        {entry.body && (
          <p className="m-0 mt-0.5 text-[12.5px] leading-[19px] text-ink-secondary">{entry.body}</p>
        )}

        {entry.attachments.length > 0 && (
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {entry.attachments.map((attachment) => (
              <span
                key={attachment.id}
                className="rounded-sm border border-line-subtle bg-app px-1.5 py-px font-mono text-[10.5px] text-ink-secondary"
              >
                {attachment.name} · {formatSize(attachment.sizeBytes)}
                {attachment.exhibitNumber !== null && ` · pièce n° ${attachment.exhibitNumber}`}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Always present, only its opacity changes, so the row never reflows on hover. */}
      {canEdit && (
        <div className="absolute top-1.5 right-0 flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
          <EntryAction label="Modifier" onClick={onEdit}>
            <Pencil size={13} strokeWidth={1.75} />
          </EntryAction>
          <EntryAction label="Joindre un fichier" onClick={() => file.current?.click()}>
            <Paperclip size={13} strokeWidth={1.75} />
          </EntryAction>
          <EntryAction label="Supprimer" danger onClick={onDelete}>
            <Trash2 size={13} strokeWidth={1.75} />
          </EntryAction>

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

function EntryAction({ label, danger, onClick, children }: {
  label: string
  danger?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      className={cn(
        'grid h-6 w-6 place-items-center rounded-md border border-line-subtle bg-panel hover:bg-hover',
        danger ? 'text-danger hover:border-danger' : 'text-ink-secondary',
      )}
    >
      {children}
    </button>
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
    <div className="grid gap-2 border-t border-line-subtle py-3">
      <div className="flex flex-wrap gap-[5px]">
        {composerTypes.map((candidate) => (
          <Chip
            key={candidate}
            tone={candidate === type ? 'active' : 'idle'}
            onClick={() => setType(candidate)}
          >
            {activityLabels[candidate]}
          </Chip>
        ))}
      </div>

      <Input value={subject} onChange={(event) => setSubject(event.target.value)} />
      <Textarea rows={3} value={body} onChange={(event) => setBody(event.target.value)} />

      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>Annuler</Button>
        <Button disabled={busy || !subject.trim()} onClick={() => void save()}>Enregistrer</Button>
      </div>
    </div>
  )
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

  const durationMinutes = timeAttached ? Number(hours || 0) * 60 + Number(minutes || 0) : 0

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
    <div className="grid shrink-0 gap-2 border-b border-line-subtle px-4 py-3">
      <div className="flex flex-wrap gap-[5px]">
        {composerTypes.map((candidate) => (
          <Chip
            key={candidate}
            tone={candidate === type ? 'active' : 'idle'}
            onClick={() => setType(candidate)}
          >
            {activityLabels[candidate]}
          </Chip>
        ))}
      </div>

      <Textarea
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

      <div className="flex items-center gap-2">
        {!timeAttached ? (
          <Chip tone="dashed" onClick={() => setTimeAttached(true)}>＋ temps passé</Chip>
        ) : (
          <ChipSpan tone="time">
            <Clock size={11} strokeWidth={2} />
            <UnitInput value={hours} onChange={setHours} label="Heures" placeholder="0" />h
            <UnitInput value={minutes} onChange={setMinutes} label="Minutes" placeholder="00" />
            · facturable
            <button
              type="button"
              aria-label="Retirer"
              className="ml-0.5"
              onClick={() => setTimeAttached(false)}
            >
              <X size={11} strokeWidth={2.5} />
            </button>
          </ChipSpan>
        )}

        <span className="flex-1" />
        {error && <span className="text-danger">{error}</span>}
        <span className="font-mono text-kbd text-muted">⌘⏎</span>
        <Button disabled={busy || !text.trim()} onClick={() => void add()}>Ajouter</Button>
      </div>
    </div>
  )
}

/** Two numeric fields rather than parsed prose: « 30 minutes » is a sentence, and guessing at
 *  sentences is how a duration silently lands wrong. */
function UnitInput({ value, onChange, label, placeholder }: {
  value: string
  onChange: (next: string) => void
  label: string
  placeholder: string
}) {
  return (
    <input
      aria-label={label}
      inputMode="numeric"
      placeholder={placeholder}
      value={value}
      onChange={(event) => onChange(event.target.value.replace(/\D/g, ''))}
      className="h-4 w-[26px] border-0 bg-transparent p-0 text-center font-mono text-[11px] text-inherit focus-visible:outline-none tnum"
    />
  )
}
