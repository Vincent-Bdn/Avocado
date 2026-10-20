import { useCallback, useEffect, useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Button } from '../components/ui/button.js'
import { PageHeader } from '../components/ui/page-header.js'
import { Panel } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { formatDuration } from '../labels.js'

interface TimeGridRow {
  matterId: string
  reference: string
  name: string
  clientName: string | null
  /** Aligned with `days`, so a cell is a lookup by index. */
  minutes: number[]
  totalMinutes: number
  billableMinutes: number
}

interface TimeGrid {
  from: string
  to: string
  days: string[]
  dayMinutes: number[]
  rows: TimeGridRow[]
  totalMinutes: number
  billableMinutes: number
}

type Span = 'week' | 'month'

/**
 * Le temps saisi, par jour et par dossier.
 *
 * <p>Her own sketch: the days across the top, the dossiers down the side, hours in the cells. Time
 * was readable one dossier at a time and never across the practice, which left the one question the
 * saisie cannot answer about itself unanswered.</p>
 *
 * <p><b>What it is for is the empty cells.</b> Work that is not written down is never billed, so this
 * is not « combien ai-je travaillé » — the facturation screen says that — but « quel après-midi ai-je
 * oublié de noter ». A dossier she was on all week with a blank Wednesday is the thing she is looking
 * for, which is why every column of the window is drawn whether or not anything landed in it.</p>
 */
export function Temps({ onOpenMatter }: { onOpenMatter: (id: string) => void }) {
  const [span, setSpan] = useState<Span>('week')
  const [anchor, setAnchor] = useState(() => mondayOf(new Date()))
  const [grid, setGrid] = useState<TimeGrid | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [from, to] = window_(anchor, span)

  const load = useCallback((start: string, finish: string) => {
    api<TimeGrid>(`/api/time-entries/grid?from=${start}&to=${finish}`)
      .then((fetched) => { setGrid(fetched); setError(null) })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
  }, [])

  useEffect(() => load(from, to), [from, to, load])

  function shift(by: number) {
    setAnchor((current) => {
      const day = new Date(`${current}T00:00:00Z`)

      if (span === 'week') {
        day.setUTCDate(day.getUTCDate() + by * 7)
        return day.toISOString().slice(0, 10)
      }

      day.setUTCMonth(day.getUTCMonth() + by, 1)
      return day.toISOString().slice(0, 10)
    })
  }

  const actions = (
    <>
      <div className="flex overflow-hidden rounded-sm border border-line-strong">
        {(['week', 'month'] as Span[]).map((candidate) => (
          <button
            key={candidate}
            type="button"
            onClick={() => {
              setSpan(candidate)
              // Re-anchored, so switching to « mois » from a week in the middle of it shows that
              // month rather than six weeks starting on a Tuesday.
              setAnchor((current) => candidate === 'week'
                ? mondayOf(new Date(`${current}T00:00:00Z`))
                : `${current.slice(0, 8)}01`)
            }}
            className={cn(
              'px-2.5 py-1 text-[12px] leading-4',
              span === candidate ? 'bg-brand-subtle font-medium text-ink' : 'text-ink-secondary hover:bg-hover',
            )}
          >
            {candidate === 'week' ? 'Semaine' : 'Mois'}
          </button>
        ))}
      </div>

      <Button variant="secondary" size="iconSm" aria-label="Période précédente" onClick={() => shift(-1)}>
        <ChevronLeft size={13} strokeWidth={2} />
      </Button>

      <Button
        variant="secondary"
        size="sm"
        onClick={() => setAnchor(span === 'week' ? mondayOf(new Date()) : `${localToday().slice(0, 8)}01`)}
      >
        Aujourd’hui
      </Button>

      <Button variant="secondary" size="iconSm" aria-label="Période suivante" onClick={() => shift(1)}>
        <ChevronRight size={13} strokeWidth={2} />
      </Button>
    </>
  )

  return (
    <Panel>
      <PageHeader
        title="Temps passé"
        meta={
          <span>
            {spelled(from)} au {spelled(to)}
            {grid !== null && grid.totalMinutes > 0 && (
              <>
                {' · '}
                {formatDuration(grid.totalMinutes)} saisies, dont{' '}
                {formatDuration(grid.billableMinutes)} facturables
              </>
            )}
          </span>
        }
        actions={actions}
      />

      <div className="flex-1 overflow-auto px-5 py-4">
        {error !== null && <p className="m-0 text-danger">{error}</p>}

        {grid !== null && error === null && (
          grid.rows.length === 0 ? (
            <p className="m-0 type-caption text-muted">
              Aucun temps saisi sur cette période.
            </p>
          ) : (
            <Table grid={grid} onOpenMatter={onOpenMatter} />
          )
        )}
      </div>
    </Panel>
  )
}

/**
 * The table itself.
 *
 * <p>The dossier column is sticky and the header row is sticky, because a month is thirty-one columns
 * and a cell in the middle of it means nothing without both of its labels.</p>
 */
function Table({ grid, onOpenMatter }: {
  grid: TimeGrid
  onOpenMatter: (id: string) => void
}) {
  const today = localToday()

  return (
    <table className="w-full border-separate border-spacing-0 text-[12px]">
      <thead>
        <tr>
          <th className="sticky left-0 top-0 z-20 min-w-[200px] border-b border-line bg-panel px-2 py-1.5 text-left type-caption text-muted">
            Dossier
          </th>

          {grid.days.map((day) => (
            <th
              key={day}
              className={cn(
                'sticky top-0 z-10 w-[58px] border-b border-line bg-panel px-1 py-1.5 text-center type-caption',
                weekend(day) ? 'text-muted/60' : 'text-muted',
                day === today && 'text-ink',
              )}
            >
              <div className={cn(day === today && 'font-medium')}>{weekday(day)}</div>
              <div className="font-mono tnum">{day.slice(8, 10)}/{day.slice(5, 7)}</div>
            </th>
          ))}

          <th className="sticky right-0 top-0 z-20 w-[74px] border-b border-line bg-panel px-2 py-1.5 text-right type-caption text-muted">
            Total
          </th>
        </tr>
      </thead>

      <tbody>
        {grid.rows.map((row) => (
          <tr key={row.matterId} className="group">
            <td className="sticky left-0 z-10 border-b border-line-subtle bg-panel px-2 py-1 group-hover:bg-hover">
              <button
                type="button"
                onClick={() => onOpenMatter(row.matterId)}
                className="grid w-full min-w-0 text-left"
              >
                <span className="truncate leading-4">{row.name}</span>
                <span className="truncate text-[10.5px] leading-[14px] text-muted">
                  {row.reference}
                  {row.clientName && ` · ${row.clientName}`}
                  {row.billableMinutes < row.totalMinutes &&
                    ` · ${formatDuration(row.billableMinutes)} facturables`}
                </span>
              </button>
            </td>

            {row.minutes.map((minutes, index) => (
              <td
                key={grid.days[index]}
                className={cn(
                  'border-b border-line-subtle px-1 py-1 text-center font-mono text-[11.5px] tnum group-hover:bg-hover',
                  weekend(grid.days[index]!) && 'bg-[#FAFBF9]',
                )}
              >
                {/* A blank cell, not a zero. Thirty-one zeroes across a row is a wall of noise, and
                    what she is looking for is the hole. */}
                {minutes === 0 ? '' : formatDuration(minutes)}
              </td>
            ))}

            <td className="sticky right-0 z-10 border-b border-line-subtle bg-panel px-2 py-1 text-right font-mono text-[11.5px] font-medium tnum group-hover:bg-hover">
              {formatDuration(row.totalMinutes)}
            </td>
          </tr>
        ))}
      </tbody>

      <tfoot>
        <tr>
          <td className="sticky left-0 z-10 bg-panel px-2 py-1.5 type-caption text-muted">
            {grid.rows.length} dossier{grid.rows.length > 1 ? 's' : ''}
          </td>

          {grid.dayMinutes.map((minutes, index) => (
            <td
              key={grid.days[index]}
              className="px-1 py-1.5 text-center font-mono text-[11.5px] font-medium tnum"
            >
              {minutes === 0 ? '' : formatDuration(minutes)}
            </td>
          ))}

          <td className="sticky right-0 z-10 bg-panel px-2 py-1.5 text-right font-mono text-[12px] font-semibold tnum">
            {formatDuration(grid.totalMinutes)}
          </td>
        </tr>
      </tfoot>
    </table>
  )
}

/**
 * Today, in **local** time.
 *
 * <p>Not `toISOString()`, which is UTC. In France that is an hour or two ahead of midnight, so
 * between local midnight and 01:00 — or 02:00 in summer — it answers yesterday, and the column
 * marked « today » would be the wrong one on exactly the nights somebody is working late and
 * recording time.</p>
 */
function localToday(): string {
  const now = new Date()

  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

/** The Monday of the week containing this date. Monday-based, matching French convention. */
function mondayOf(date: Date): string {
  const day = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  day.setUTCDate(day.getUTCDate() - ((day.getUTCDay() + 6) % 7))

  return day.toISOString().slice(0, 10)
}

/** The window the anchor and the span describe, as two inclusive ISO dates. */
function window_(anchor: string, span: Span): [string, string] {
  if (span === 'week') {
    const end = new Date(`${anchor}T00:00:00Z`)
    end.setUTCDate(end.getUTCDate() + 6)

    return [anchor, end.toISOString().slice(0, 10)]
  }

  const year = Number(anchor.slice(0, 4))
  const month = Number(anchor.slice(5, 7))
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate()

  return [`${anchor.slice(0, 8)}01`, `${anchor.slice(0, 8)}${String(last).padStart(2, '0')}`]
}

const weekdays = ['dim', 'lun', 'mar', 'mer', 'jeu', 'ven', 'sam']

/** « lun ». Built from the parts, like every other date label in the application. */
function weekday(iso: string): string {
  return weekdays[new Date(`${iso}T00:00:00Z`).getUTCDay()] ?? ''
}

function weekend(iso: string): boolean {
  const day = new Date(`${iso}T00:00:00Z`).getUTCDay()

  return day === 0 || day === 6
}

const months = [
  'janvier', 'février', 'mars', 'avril', 'mai', 'juin',
  'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre',
]

function spelled(iso: string): string {
  const day = Number(iso.slice(8, 10))

  return `${day === 1 ? '1er' : day} ${months[Number(iso.slice(5, 7)) - 1]}`
}
