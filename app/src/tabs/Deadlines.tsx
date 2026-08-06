import { useCallback, useEffect, useState } from 'react'
import { Check, Pencil, Trash2, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { Select } from '../components/ui/select.js'
import { cn } from '../lib/utils.js'
import { tierBorder } from '../lib/urgency.js'
import { urgencyLabels } from '../labels.js'
import { InlineForm, Micro, Row, RowAction, RowDate, RowMain, TabPanel } from './shared.js'
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
    <TabPanel>
      {isOpen && (
        <InlineForm>
          <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

          <Select value={type} onChange={(event) => setType(event.target.value as DeadlineType)}>
            {Object.entries(typeLabels).map(([value, text]) => (
              <option key={value} value={value}>{text}</option>
            ))}
          </Select>

          <Input
            className="flex-1 basis-[200px]"
            value={label}
            placeholder="Conclusions à déposer, audience de mise en état…"
            onChange={(event) => setLabel(event.target.value)}
          />

          <Button disabled={!date || !label.trim()} onClick={() => void add()}>Ajouter</Button>
        </InlineForm>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      {items.length === 0 && (
        <EmptyState title="Aucune échéance">
          Une audience, un délai de procédure, un rendez-vous : ce qui a une date et ne doit pas être
          manqué.
        </EmptyState>
      )}

      <div className="grid">
        {items.map((item) =>
          editing === item.id ? (
            <EditRow
              key={item.id}
              item={item}
              onCancel={() => setEditing(null)}
              onSave={(changes) => void save(item, changes)}
            />
          ) : (
            <Row
              key={item.id}
              className={cn('border-l-[3px]', tierBorder[item.urgency], item.isDone && 'text-muted')}
            >
              <RowDate>
                {new Date(item.date).toLocaleDateString('fr-FR')}
                {item.time && ` · ${item.time.slice(0, 5)}`}
              </RowDate>

              <RowMain>
                <span>{item.label}</span>
                <Micro>{typeLabels[item.type]}</Micro>
              </RowMain>

              <Micro>{item.isDone ? 'Faite' : urgencyLabels[item.urgency]}</Micro>

              {isOpen && (
                <span className="flex gap-0.5">
                  <RowAction
                    label={item.isDone ? 'Rouvrir' : 'Marquer comme faite'}
                    onClick={() => void save(item, { isDone: !item.isDone })}
                  >
                    <Check size={13} strokeWidth={2} />
                  </RowAction>
                  <RowAction label="Modifier" onClick={() => setEditing(item.id)}>
                    <Pencil size={13} strokeWidth={1.75} />
                  </RowAction>
                  <RowAction label="Supprimer" danger onClick={() => void remove(item)}>
                    <Trash2 size={13} strokeWidth={1.75} />
                  </RowAction>
                </span>
              )}
            </Row>
          ),
        )}
      </div>
    </TabPanel>
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
    <InlineForm editing>
      <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

      <Select value={type} onChange={(event) => setType(event.target.value as DeadlineType)}>
        {Object.entries(typeLabels).map(([value, text]) => (
          <option key={value} value={value}>{text}</option>
        ))}
      </Select>

      <Input
        className="flex-1 basis-[200px]"
        value={label}
        onChange={(event) => setLabel(event.target.value)}
      />

      <Button disabled={!label.trim()} onClick={() => onSave({ date, label, type })}>Enregistrer</Button>
      <Button variant="secondary" size="icon" onClick={onCancel} aria-label="Annuler">
        <X size={13} strokeWidth={2} />
      </Button>
    </InlineForm>
  )
}
