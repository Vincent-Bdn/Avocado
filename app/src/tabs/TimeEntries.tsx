import { useCallback, useEffect, useState } from 'react'
import { Pencil, Trash2, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAction, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'

interface TimeEntryItem {
  id: string
  date: string
  startedAt: string | null
  task: string
  durationMinutes: number
  isBillable: boolean
  appliedRateCents: number
  isRateOverridden: boolean
  amountCents: number
  fromActivityId: string | null
}

interface TimeEntryPage {
  items: TimeEntryItem[]
  totals: {
    todayMinutes: number
    weekMinutes: number
    matterMinutes: number
    billableMinutes: number
    nonBillableMinutes: number
    billableAmountCents: number
  }
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

/**
 * Temps passé. No chronometer: most lawyers write the time down at the end, and a running timer that
 * a crash could lose is a worse promise than none at all. Every line can be corrected, because a
 * duration typed in a hurry is exactly the kind of thing that needs correcting.
 */
export function TimeEntries({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [page, setPage] = useState<TimeEntryPage | null>(null)
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<TimeEntryPage>(`/api/matters/${matterId}/time-entries`)
      .then(setPage)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(reload, [reload])

  const refresh = () => {
    setEditing(null)
    reload()
    onChanged()
  }

  async function remove(entry: TimeEntryItem) {
    setError(null)

    try {
      await api(`/api/time-entries/${entry.id}`, { method: 'DELETE' })
      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  return (
    <TabPanel>
      {isOpen && <EntryForm matterId={matterId} onSaved={refresh} />}

      {error && <p className="m-0 text-danger">{error}</p>}

      {page && (
        <div className="flex flex-wrap items-center gap-3.5 rounded-md bg-sunken px-2.5 py-2 font-mono text-[11.5px] tnum">
          <span>Aujourd’hui {formatDuration(page.totals.todayMinutes)}</span>
          <span>Cette semaine {formatDuration(page.totals.weekMinutes)}</span>
          <span>Total {formatDuration(page.totals.matterMinutes)}</span>
          <span className="flex-1" />
          <span>
            {formatDuration(page.totals.billableMinutes)} facturables ·{' '}
            <strong className="font-semibold">{formatEuros(page.totals.billableAmountCents)}</strong>
          </span>
        </div>
      )}

      {page?.items.length === 0 && (
        <EmptyState title="Aucun temps saisi">
          Ce que vous ne notez pas maintenant ne se facturera jamais. Le plus simple reste de
          l’attacher à l’entrée de journal, au moment où vous la notez.
        </EmptyState>
      )}

      <div className="grid">
        {page?.items.map((entry) =>
          editing === entry.id ? (
            <EntryForm
              key={entry.id}
              matterId={matterId}
              entry={entry}
              onSaved={refresh}
              onCancel={() => setEditing(null)}
            />
          ) : (
            <Row
              key={entry.id}
              className={cn(
                'group',
                // A half-rate agreed in February must still be visible in June.
                entry.isRateOverridden && 'bg-accent-subtle text-warning',
                !entry.isBillable && 'text-muted',
              )}
            >
              <RowDate>
                {new Date(entry.date).toLocaleDateString('fr-FR')}
                {entry.startedAt && ` · ${entry.startedAt.slice(0, 5)}`}
              </RowDate>

              <RowMain>
                <span>{entry.task}</span>
                {entry.fromActivityId && <Micro>depuis le journal</Micro>}
              </RowMain>

              <span className="font-mono tnum">{formatDuration(entry.durationMinutes)}</span>

              <span className="font-mono text-muted tnum">
                {entry.isBillable ? `${formatEuros(entry.appliedRateCents)}/h` : 'non facturable'}
              </span>

              <RowAmount className={entry.isRateOverridden ? 'font-medium' : ''}>
                {entry.isBillable ? formatEuros(entry.amountCents) : ''}
              </RowAmount>

              {isOpen && (
                <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                  <RowAction label="Modifier" onClick={() => setEditing(entry.id)}>
                    <Pencil size={13} strokeWidth={1.75} />
                  </RowAction>
                  <RowAction label="Supprimer" danger onClick={() => void remove(entry)}>
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

/** One form for both adding and correcting, so the two can never drift apart. */
function EntryForm({ matterId, entry, onSaved, onCancel }: {
  matterId: string
  entry?: TimeEntryItem
  onSaved: () => void
  onCancel?: () => void
}) {
  const [date, setDate] = useState(entry?.date.slice(0, 10) ?? new Date().toISOString().slice(0, 10))
  const [hours, setHours] = useState(entry ? String(Math.floor(entry.durationMinutes / 60)) : '')
  const [minutes, setMinutes] = useState(entry ? String(entry.durationMinutes % 60) : '')
  const [task, setTask] = useState(entry?.task ?? '')
  const [billable, setBillable] = useState(entry?.isBillable ?? true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const durationMinutes = Number(hours || 0) * 60 + Number(minutes || 0)

  async function save() {
    if (durationMinutes <= 0) {
      setError('Indiquez une durée, par exemple 1 h 30.')
      return
    }

    if (!task.trim()) {
      setError('Décrivez ce qui a été fait.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = {
        date,
        startedAt: entry?.startedAt ?? null,
        task: task.trim(),
        durationMinutes,
        isBillable: billable,
      }

      if (entry) {
        await api(`/api/time-entries/${entry.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/time-entries`, body)
        setHours('')
        setMinutes('')
        setTask('')
      }

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(entry)}>
        <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

        {/* Two fields rather than parsed prose: « 30 minutes » is a sentence, and guessing at
            sentences is how a duration silently lands wrong. */}
        <span className="flex items-center gap-1">
          <Input
            className="w-11 text-center font-mono tnum"
            inputMode="numeric"
            placeholder="0"
            value={hours}
            aria-label="Heures"
            onChange={(event) => { setHours(event.target.value.replace(/\D/g, '')); setError(null) }}
          />
          <span className="text-muted">h</span>
          <Input
            className="w-11 text-center font-mono tnum"
            inputMode="numeric"
            placeholder="00"
            value={minutes}
            aria-label="Minutes"
            onChange={(event) => { setMinutes(event.target.value.replace(/\D/g, '')); setError(null) }}
          />
          <span className="text-muted">min</span>
        </span>

        <Input
          className="flex-1 basis-[200px]"
          value={task}
          placeholder="Rédaction des conclusions, appel du confrère…"
          onChange={(event) => { setTask(event.target.value); setError(null) }}
        />

        <label className="flex items-center gap-2 text-[13px]">
          <input
            type="checkbox"
            checked={billable}
            onChange={(event) => setBillable(event.target.checked)}
          />
          Facturable
        </label>

        <Button disabled={busy} onClick={() => void save()}>
          {entry ? 'Enregistrer' : 'Ajouter'}
        </Button>

        {onCancel && (
          <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
            <X size={13} strokeWidth={2} />
          </Button>
        )}
      </InlineForm>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </div>
  )
}
