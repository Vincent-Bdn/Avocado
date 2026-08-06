import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from '../api.js'
import { formatDuration, formatEuros } from '../labels.js'

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

/**
 * Temps passé. No chronometer: most lawyers write the time down at the end, and a running timer that
 * a crash could lose is a worse promise than none at all.
 */
export function TimeEntries({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [page, setPage] = useState<TimeEntryPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [duration, setDuration] = useState('')
  const [task, setTask] = useState('')
  const [billable, setBillable] = useState(true)

  const reload = useCallback(() => {
    api<TimeEntryPage>(`/api/matters/${matterId}/time-entries`)
      .then(setPage)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  async function add() {
    const minutes = parseDuration(duration)
    if (minutes === null) {
      setError('Durée non comprise. Essayez « 1h30 », « 90 » ou « 1,5 ».')
      return
    }

    setError(null)

    try {
      await post(`/api/matters/${matterId}/time-entries`, {
        date,
        task,
        durationMinutes: minutes,
        isBillable: billable,
      })

      setDuration('')
      setTask('')
      reload()
      onChanged()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  return (
    <div className="tab-panel">
      {isOpen && (
        <div className="inline-form">
          <input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

          <input
            className="narrow"
            value={duration}
            placeholder="1h30"
            aria-label="Durée"
            onChange={(event) => setDuration(event.target.value)}
          />

          <input
            className="flex"
            value={task}
            placeholder="Rédaction des conclusions, appel du confrère…"
            onChange={(event) => setTask(event.target.value)}
          />

          <label className="confirm">
            <input
              type="checkbox"
              checked={billable}
              onChange={(event) => setBillable(event.target.checked)}
            />
            Facturable
          </label>

          <button type="button" disabled={!duration || !task.trim()} onClick={() => void add()}>
            Ajouter
          </button>
        </div>
      )}

      {error && <p className="danger">{error}</p>}

      {page && (
        <div className="totals mono">
          <span>Aujourd’hui {formatDuration(page.totals.todayMinutes)}</span>
          <span>Cette semaine {formatDuration(page.totals.weekMinutes)}</span>
          <span>Total {formatDuration(page.totals.matterMinutes)}</span>
          <span className="grow" />
          <span>
            {formatDuration(page.totals.billableMinutes)} facturables ·{' '}
            <strong>{formatEuros(page.totals.billableAmountCents)}</strong>
          </span>
        </div>
      )}

      {page?.items.length === 0 && (
        <div className="empty">
          <h3>Aucun temps saisi</h3>
          <p className="muted">
            Ce que vous ne notez pas maintenant ne se facturera jamais. Le plus simple reste de
            l’attacher à l’entrée de journal, au moment où vous la notez.
          </p>
        </div>
      )}

      <div className="rows">
        {page?.items.map((entry) => (
          <div
            key={entry.id}
            className={`time-row ${entry.isRateOverridden ? 'row-override' : ''} ${entry.isBillable ? '' : 'row-muted'}`}
          >
            <span className="mono row-date">
              {new Date(entry.date).toLocaleDateString('fr-FR')}
              {entry.startedAt && ` · ${entry.startedAt.slice(0, 5)}`}
            </span>

            <span className="row-main">
              <span>{entry.task}</span>
              {entry.fromActivityId && <span className="muted micro">depuis le journal</span>}
            </span>

            <span className="mono">{formatDuration(entry.durationMinutes)}</span>

            <span className="mono muted">
              {entry.isBillable ? `${formatEuros(entry.appliedRateCents)}/h` : 'non facturable'}
            </span>

            <span className="mono row-amount">
              {entry.isBillable ? formatEuros(entry.amountCents) : '—'}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

/** Accepts « 1h30 », « 90 » and « 1,5 ». Formatting is a UI concern, so it stays here. */
export function parseDuration(input: string): number | null {
  const value = input.trim().toLowerCase().replace(',', '.')
  if (!value) return null

  const hoursAndMinutes = /^(\d+)\s*h\s*(\d+)?$/.exec(value)
  if (hoursAndMinutes) {
    return Number(hoursAndMinutes[1]) * 60 + Number(hoursAndMinutes[2] ?? 0)
  }

  const decimalHours = /^(\d+\.\d+)\s*h?$/.exec(value)
  if (decimalHours) {
    return Math.round(Number(decimalHours[1]) * 60)
  }

  const minutes = /^(\d+)$/.exec(value)
  return minutes ? Number(minutes[1]) : null
}
