import { useCallback, useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { urgencyLabels } from '../labels.js'
import type { DeadlineUrgency } from '../types.js'

type DeadlineType = 'Hearing' | 'ProceduralDeadline' | 'Appointment' | 'Other'

interface DeadlineItem {
  id: string
  date: string
  time: string | null
  type: DeadlineType
  label: string
  remindDaysBefore: number
  isDone: boolean
  urgency: DeadlineUrgency
}

const typeLabels: Record<DeadlineType, string> = {
  Hearing: 'Audience',
  ProceduralDeadline: 'Délai de procédure',
  Appointment: 'Rendez-vous',
  Other: 'Autre',
}

/** Audiences and délais. Closing a dossier hides these rather than deleting them. */
export function Deadlines({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [items, setItems] = useState<DeadlineItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [date, setDate] = useState('')
  const [label, setLabel] = useState('')
  const [type, setType] = useState<DeadlineType>('ProceduralDeadline')

  const reload = useCallback(() => {
    api<DeadlineItem[]>(`/api/matters/${matterId}/deadlines?includeDone=true`)
      .then(setItems)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  async function add() {
    setError(null)

    try {
      await post(`/api/matters/${matterId}/deadlines`, { date, label, type })
      setLabel('')
      setDate('')
      reload()
      onChanged()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  async function markDone(item: DeadlineItem) {
    await api(`/api/deadlines/${item.id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...item, isDone: !item.isDone }),
    })

    reload()
    onChanged()
  }

  return (
    <div className="tab-panel">
      {isOpen && (
        <div className="inline-form">
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            aria-label="Date"
          />

          <select value={type} onChange={(event) => setType(event.target.value as DeadlineType)}>
            {Object.entries(typeLabels).map(([value, text]) => (
              <option key={value} value={value}>{text}</option>
            ))}
          </select>

          <input
            className="flex"
            value={label}
            placeholder="Conclusions à déposer, audience de mise en état…"
            onChange={(event) => setLabel(event.target.value)}
          />

          <button type="button" disabled={!date || !label.trim()} onClick={() => void add()}>
            Ajouter
          </button>
        </div>
      )}

      {error && <p className="danger">{error}</p>}

      {items.length === 0 && (
        <div className="empty">
          <h3>Aucune échéance</h3>
          <p className="muted">
            Une audience, un délai de procédure, un rendez-vous : ce qui a une date et ne doit pas
            être manqué.
          </p>
        </div>
      )}

      <div className="rows">
        {items.map((item) => (
          <div key={item.id} className={`deadline-row urgency-${item.urgency.toLowerCase()} ${item.isDone ? 'row-done' : ''}`}>
            <span className="mono row-date">
              {new Date(item.date).toLocaleDateString('fr-FR')}
              {item.time && ` · ${item.time.slice(0, 5)}`}
            </span>

            <span className="row-main">
              <span>{item.label}</span>
              <span className="muted micro">{typeLabels[item.type]}</span>
            </span>

            <span className="muted micro">{item.isDone ? 'Faite' : urgencyLabels[item.urgency]}</span>

            {isOpen && (
              <button
                type="button"
                className="ghost-button"
                title={item.isDone ? 'Rouvrir' : 'Marquer comme faite'}
                onClick={() => void markDone(item)}
              >
                <Check size={13} strokeWidth={2} />
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
