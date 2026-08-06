import { useEffect, useRef, useState } from 'react'
import { ApiError, api, post } from './api.js'
import { activityLabels, composerTypes, formatDate, formatDuration, formatTime, weekLabel } from './labels.js'
import type { ActivityListPage, ActivityType } from './types.js'

/**
 * « Le suivi » — the reverse-chronological log, with the composer pinned above it.
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

  const reload = () => {
    api<ActivityListPage>(`/api/matters/${matterId}/activities`)
      .then(setPage)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }

  useEffect(reload, [matterId])

  return (
    <div className="journal">
      {isOpen ? (
        <Composer
          matterId={matterId}
          onAdded={() => {
            reload()
            onChanged()
          }}
        />
      ) : (
        <div className="frozen">
          <p><strong>Ce dossier est clôturé, le journal est donc figé.</strong></p>
          <p className="muted">
            Les entrées, les documents et le temps saisi restent consultables et cherchables. Rien n’a
            été supprimé. Pour reprendre l’écriture, rouvrez le dossier.
          </p>
        </div>
      )}

      {error && <p className="danger">{error}</p>}

      <div className="timeline">
        {page?.items.length === 0 && (
          <div className="empty">
            <h3>Le journal est vide</h3>
            <p className="muted">
              Notez le prochain appel dès que vous raccrochez : deux lignes suffisent.
            </p>
          </div>
        )}

        {page?.items.map((entry, index) => {
          const previous = page.items[index - 1]
          const separator = weekLabel(entry.occurredAt)
          const showSeparator = !previous || weekLabel(previous.occurredAt) !== separator

          return (
            <div key={entry.id}>
              {showSeparator && <div className="week">{separator}</div>}

              <article className="entry">
                <div className="gutter mono">
                  <div>{formatDate(entry.occurredAt)}</div>
                  <div className="disabled">{formatTime(entry.occurredAt)}</div>
                </div>

                <div className={`dot dot-${toneOf(entry.type)}`} aria-hidden="true" />

                <div className="body">
                  <div className="line1">
                    <strong>{activityLabels[entry.type]}</strong>
                    {entry.contactName && <span className="secondary">{entry.contactName}</span>}
                    {entry.durationMinutes && (
                      <span className="chip-time mono">{formatDuration(entry.durationMinutes)}</span>
                    )}
                    {entry.trackingNumber && (
                      <span className="muted mono">{entry.trackingNumber}</span>
                    )}
                  </div>

                  {entry.subject && <div className="subject">{entry.subject}</div>}
                  {entry.body && <p className="secondary">{entry.body}</p>}
                </div>
              </article>
            </div>
          )
        })}
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

function Composer({ matterId, onAdded }: { matterId: string; onAdded: () => void }) {
  const [type, setType] = useState<ActivityType>('Call')
  const [text, setText] = useState('')
  const [minutes, setMinutes] = useState<number | null>(null)
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
        durationMinutes: minutes,
      })

      setText('')
      setMinutes(null)
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
        {minutes === null ? (
          <button type="button" className="chip chip-dashed" onClick={() => setMinutes(15)}>
            ＋ temps passé
          </button>
        ) : (
          <span className="chip chip-time">
            <input
              type="number"
              min={1}
              value={minutes}
              onChange={(event) => setMinutes(Number(event.target.value))}
              className="mono minutes"
            />
            min · facturable
            <button type="button" className="unchip" onClick={() => setMinutes(null)} aria-label="Retirer">
              ×
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
