import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'

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
  const [hours, setHours] = useState('')
  const [minutes, setMinutes] = useState('')
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

  const durationMinutes = Number(hours || 0) * 60 + Number(minutes || 0)

  async function add() {
    setError(null)

    try {
      await post(`/api/matters/${matterId}/time-entries`, {
        date,
        task,
        durationMinutes,
        isBillable: billable,
      })

      setHours('')
      setMinutes('')
      setTask('')
      reload()
      onChanged()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  return (
    <TabPanel>
      {isOpen && (
        <InlineForm>
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
              onChange={(event) => setHours(event.target.value.replace(/\D/g, ''))}
            />
            <span className="text-muted">h</span>
            <Input
              className="w-11 text-center font-mono tnum"
              inputMode="numeric"
              placeholder="00"
              value={minutes}
              aria-label="Minutes"
              onChange={(event) => setMinutes(event.target.value.replace(/\D/g, ''))}
            />
            <span className="text-muted">min</span>
          </span>

          <Input
            className="flex-1 basis-[200px]"
            value={task}
            placeholder="Rédaction des conclusions, appel du confrère…"
            onChange={(event) => setTask(event.target.value)}
          />

          <label className="flex items-center gap-2 text-[13px]">
            <input
              type="checkbox"
              checked={billable}
              onChange={(event) => setBillable(event.target.checked)}
            />
            Facturable
          </label>

          <Button disabled={durationMinutes <= 0 || !task.trim()} onClick={() => void add()}>
            Ajouter
          </Button>
        </InlineForm>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      {page && (
        <div className="flex flex-wrap items-center gap-3.5 rounded-lg bg-sunken px-2.5 py-2 font-mono text-[11.5px] tnum">
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
        {page?.items.map((entry) => (
          <Row
            key={entry.id}
            className={cn(
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
          </Row>
        ))}
      </div>
    </TabPanel>
  )
}
