import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Clock, Gavel, Lock, Mail, MailOpen, Paperclip, Pencil, Phone, StickyNote, Trash2, Users, X,
} from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Button, Kbd } from './components/ui/button.js'
import { Chip, ChipSpan } from './components/ui/chip.js'
import { EmptyState } from './components/ui/empty-state.js'
import { Input } from './components/ui/input.js'
import { Textarea } from './components/ui/textarea.js'
import { cn } from './lib/utils.js'
import { formatSize } from './lib/urgency.js'
import { activityLabels, composerTypes, formatDate, formatDuration, formatTime, weekLabel } from './labels.js'
import type { ActivityListItem, ActivityListPage, ActivityType, ContactSummary } from './types.js'

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
                <div className="type-group pt-[11px] pb-[5px] text-muted">
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
          className="absolute right-4 bottom-4 flex items-center gap-3 rounded-md border border-line border-l-[3px] border-l-info bg-raised px-3 py-2 text-[12px] shadow-e1"
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
    <div className="m-4 flex items-start gap-3 rounded-md border border-line-subtle bg-[#F8F9F6] px-3.5 py-3">
      <Lock size={15} strokeWidth={1.75} className="mt-0.5 shrink-0 text-muted" />
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

/**
 * Three tints, and only three: brand for a call, info for anything that travelled electronically or
 * face to face, neutral for the rest. Fill and border are the *tint* of the family, never the full
 * colour, so sixteen entries in a row read as a list rather than as a chart.
 */
const brandTint = 'bg-[#E7EEE8] border-[#BFD3C5] text-[#2C4A38]'
const infoTint = 'bg-[#E6EEF6] border-[#C7DAEB] text-[#2B5578]'
const neutralTint = 'bg-[#E9ECE4] border-[#D2D7CB] text-[#4A524B]'

const typeTone: Record<ActivityType, string> = {
  Call: brandTint,
  IncomingEmail: infoTint,
  OutgoingEmail: infoTint,
  IncomingLetter: neutralTint,
  OutgoingLetter: neutralTint,
  Meeting: infoTint,
  Note: neutralTint,
  Hearing: neutralTint,
  Other: neutralTint,
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
    <article className="group relative grid grid-cols-[58px_20px_minmax(0,1fr)] gap-2 border-t border-line-subtle py-2 hover:bg-[#F8F9F6] hover:shadow-[inset_2px_0_0_var(--border-default)]">
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
                className="rounded-[3px] border border-line-subtle bg-app px-1.5 py-px font-mono text-[10.5px] text-ink-secondary"
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
        'grid h-6 w-6 place-items-center rounded-[3px] border border-line-subtle bg-panel hover:bg-hover',
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

/**
 * The inline composer: pinned under the tab bar, never a modal, and the fastest thing in the
 * application. ⌘J from anywhere in the dossier lands in the textarea, ⌘⏎ saves.
 *
 * The timestamp is a real field rather than a caption, because she logs the 11:00 call at 17:00, and
 * the duration is a first-class chip rather than a sub-form, because logging a call and its billable
 * time in one keystroke is the single interaction that stops a solo practice under-recording its work.
 */
function Composer({ matterId, onAdded }: { matterId: string; onAdded: () => void }) {
  const [type, setType] = useState<ActivityType>('Call')
  const [occurredAt, setOccurredAt] = useState(nowLocal)
  const [text, setText] = useState('')
  const [hours, setHours] = useState('')
  const [minutes, setMinutes] = useState('')
  const [timeAttached, setTimeAttached] = useState(false)
  const [billable, setBillable] = useState(true)
  const [contactId, setContactId] = useState('')
  const [contacts, setContacts] = useState<ContactSummary[]>([])
  const [files, setFiles] = useState<File[]>([])
  const [focused, setFocused] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const textarea = useRef<HTMLTextAreaElement>(null)
  const filePicker = useRef<HTMLInputElement>(null)

  useEffect(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

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
  const contact = contacts.find((candidate) => candidate.id === contactId)

  /** Échap abandons the composer rather than saving a half-written entry. */
  function abandon() {
    setText('')
    setFiles([])
    setTimeAttached(false)
    setContactId('')
    textarea.current?.blur()
  }

  async function add() {
    if (!text.trim()) return

    setBusy(true)
    setError(null)

    try {
      const [subject, ...rest] = text.split('\n')

      const created = await post<{ id: string }>(`/api/matters/${matterId}/activities`, {
        type,
        occurredAt: new Date(occurredAt).toISOString(),
        contactId: contactId || null,
        subject,
        body: rest.join('\n') || null,
        durationMinutes: durationMinutes > 0 ? durationMinutes : null,
        durationIsBillable: billable,
      })

      // The files ride along after the entry exists, so a failed upload cannot lose the writing.
      if (files.length > 0) {
        const form = new FormData()
        for (const file of files) form.append('files', file)
        form.append('activityId', created.id)
        await api(`/api/matters/${matterId}/documents`, { method: 'POST', body: form })
      }

      setText('')
      setHours('')
      setMinutes('')
      setTimeAttached(false)
      setContactId('')
      setFiles([])
      setOccurredAt(nowLocal())
      onAdded()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="shrink-0 border-b border-line-subtle px-4 py-3">
      <div
        onFocus={() => setFocused(true)}
        onBlur={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node)) setFocused(false)
        }}
        className={cn(
          'overflow-hidden rounded-md border',
          focused
            ? 'border-[var(--focus-ring)] shadow-[0_0_0_2px_color-mix(in_srgb,var(--focus-ring)_22%,transparent)]'
            : 'border-line-strong',
        )}
      >
        {/* Row 1: the nine types, and when this actually happened. */}
        <div className="flex flex-wrap items-center gap-[5px] border-b border-line-subtle bg-[#F8F9F6] px-2 py-1.5">
          {composerTypes.map((candidate) => (
            <Chip
              key={candidate}
              tone={candidate === type ? 'active' : 'idle'}
              onClick={() => setType(candidate)}
            >
              {activityLabels[candidate]}
            </Chip>
          ))}

          <span className="flex-1" />

          {focused ? (
            <input
              type="datetime-local"
              aria-label="Date et heure"
              value={occurredAt}
              onChange={(event) => setOccurredAt(event.target.value)}
              className="h-5 rounded-[3px] border border-line-strong bg-panel px-1.5 font-mono text-[10.5px] text-ink tnum"
            />
          ) : (
            <span className="font-mono text-[10.5px] whitespace-nowrap text-muted tnum">
              {readableStamp(occurredAt)}
            </span>
          )}
        </div>

        {/* Row 2: the writing itself, borderless inside the composer's own frame. */}
        <Textarea
          ref={textarea}
          value={text}
          onChange={(event) => setText(event.target.value)}
          placeholder="Noter un appel, un courrier, un rendez-vous…"
          rows={2}
          className="w-full rounded-none border-0 bg-panel px-2.5 py-2.5"
          onKeyDown={(event) => {
            if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
              event.preventDefault()
              void add()
            }
            if (event.key === 'Escape') abandon()
          }}
        />

        {/* Row 3: what travels with the entry. */}
        <div className="flex flex-wrap items-center gap-2 border-t border-line-subtle px-2 py-1.5">
          {contact ? (
            <ChipSpan tone="idle">
              {contact.displayName}
              <button type="button" aria-label="Retirer le contact" onClick={() => setContactId('')}>
                <X size={11} strokeWidth={2.5} />
              </button>
            </ChipSpan>
          ) : (
            // The dashed chip is the label; the native select sits invisibly on top of it, which is
            // the smallest honest way to get a real menu without building a combobox for nine names.
            <span className="relative">
              <Chip tone="dashed">＋ contact</Chip>
              <select
                aria-label="Contact"
                value={contactId}
                onChange={(event) => setContactId(event.target.value)}
                className="absolute inset-0 cursor-pointer opacity-0"
              >
                <option value="">Aucun</option>
                {contacts.map((candidate) => (
                  <option key={candidate.id} value={candidate.id}>{candidate.displayName}</option>
                ))}
              </select>
            </span>
          )}

          {files.length > 0 ? (
            <ChipSpan tone="idle" className="font-mono">
              <Paperclip size={11} strokeWidth={2} />
              {files.length} fichier{files.length > 1 ? 's' : ''}
              <button type="button" aria-label="Retirer les fichiers" onClick={() => setFiles([])}>
                <X size={11} strokeWidth={2.5} />
              </button>
            </ChipSpan>
          ) : (
            <Chip tone="dashed" onClick={() => filePicker.current?.click()}>＋ pièce jointe</Chip>
          )}

          <input
            ref={filePicker}
            type="file"
            multiple
            hidden
            onChange={(event) => setFiles([...(event.target.files ?? [])])}
          />

          {!timeAttached ? (
            <Chip tone="dashed" onClick={() => setTimeAttached(true)}>＋ temps passé</Chip>
          ) : (
            <ChipSpan tone="time">
              <Clock size={11} strokeWidth={2} />
              <UnitInput value={hours} onChange={setHours} label="Heures" placeholder="0" />h
              <UnitInput value={minutes} onChange={setMinutes} label="Minutes" placeholder="00" />
              <button
                type="button"
                className="underline decoration-dotted"
                onClick={() => setBillable((current) => !current)}
              >
                {billable ? '· facturable' : '· non facturable'}
              </button>
              <button type="button" aria-label="Retirer" onClick={() => setTimeAttached(false)}>
                <X size={11} strokeWidth={2.5} />
              </button>
            </ChipSpan>
          )}

          <span className="flex-1" />

          {error && <span className="text-danger">{error}</span>}

          <span className="font-mono text-[10.5px] text-muted">
            {text.trim() ? 'Échap annule' : '⌘J pour écrire'}
          </span>

          <Button size="sm" disabled={busy || !text.trim()} onClick={() => void add()}>
            Ajouter
            <Kbd>⌘⏎</Kbd>
          </Button>
        </div>
      </div>
    </div>
  )
}

/** `AAAA-MM-JJTHH:mm` in local time, which is what `<input type="datetime-local">` speaks. */
function nowLocal(): string {
  const now = new Date()
  now.setMinutes(now.getMinutes() - now.getTimezoneOffset())

  return now.toISOString().slice(0, 16)
}

/** « auj. 13/03 · 17:04 » at rest, so the stamp reads as a fact rather than as a field. */
function readableStamp(value: string): string {
  const when = new Date(value)
  const day = when.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' })
  const hour = when.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })
  const today = new Date().toDateString() === when.toDateString()

  return `${today ? 'auj. ' : ''}${day} · ${hour}`
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
