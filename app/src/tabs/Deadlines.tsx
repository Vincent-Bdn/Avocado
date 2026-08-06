import { useCallback, useEffect, useState } from 'react'
import { Check, Pencil, Trash2, X } from 'lucide-react'
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
  const [editing, setEditing] = useState<string | null>(null)
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

  async function save(item: DeadlineItem, changes: Partial<DeadlineItem>) {
    await api(`/api/deadlines/${item.id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...item, ...changes }),
    })

    setEditing(null)
    reload()
    onChanged()
  }

  async function remove(item: DeadlineItem) {
    await api(`/api/deadlines/${item.id}`, { method: 'DELETE' })
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
        {items.map((item) =>
          editing === item.id ? (
            <EditRow
              key={item.id}
              item={item}
              onCancel={() => setEditing(null)}
              onSave={(changes) => void save(item, changes)}
            />
          ) : (
            <div
              key={item.id}
              className={`deadline-row urgency-${item.urgency.toLowerCase()} ${item.isDone ? 'row-done' : ''}`}
            >
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
                <span className="row-actions">
                  <button
                    type="button"
                    title={item.isDone ? 'Rouvrir' : 'Marquer comme faite'}
                    onClick={() => void save(item, { isDone: !item.isDone })}
                  >
                    <Check size={13} strokeWidth={2} />
                  </button>
                  <button type="button" title="Modifier" onClick={() => setEditing(item.id)}>
                    <Pencil size={13} strokeWidth={1.75} />
                  </button>
                  <button
                    type="button"
                    className="danger-action"
                    title="Supprimer"
                    onClick={() => void remove(item)}
                  >
                    <Trash2 size={13} strokeWidth={1.75} />
                  </button>
                </span>
              )}
            </div>
          ),
        )}
      </div>
    </div>
  )
}

function EditRow({ item, onCancel, onSave }: {
  item: DeadlineItem
  onCancel: () => void
  onSave: (changes: Partial<DeadlineItem>) => void
}) {
  const [date, setDate] = useState(item.date)
  const [label, setLabel] = useState(item.label)
  const [type, setType] = useState<DeadlineType>(item.type)

  return (
    <div className="inline-form editing">
      <input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

      <select value={type} onChange={(event) => setType(event.target.value as DeadlineType)}>
        {Object.entries(typeLabels).map(([value, text]) => (
          <option key={value} value={value}>{text}</option>
        ))}
      </select>

      <input className="flex" value={label} onChange={(event) => setLabel(event.target.value)} />

      <button type="button" disabled={!label.trim()} onClick={() => onSave({ date, label, type })}>
        Enregistrer
      </button>
      <button type="button" className="secondary-button" onClick={onCancel}>
        <X size={13} strokeWidth={2} />
      </button>
    </div>
  )
}
