import { useState, type ReactNode } from 'react'
import { cn } from '../lib/utils.js'
import { formatEuros, formatEurosRounded } from '../labels.js'

export interface HonoraireMonth {
  /** The first of the month, ISO. */
  month: string
  billableCents: number
  /** Everything invoiced that month, historical included. Real turnover, and it belongs on the chart. */
  invoicedCents: number
  /** The part of it brought over from before Avocado. Drawn apart, and outside the gap. */
  historicalCents: number
  /** `invoiced − historical`: the factures the gap can speak about. */
  currentCents: number
  paidCents: number
  unpaidCents: number
  subcontractedCents: number
  /** `invoiced − subcontracted`, floored at zero. What the month actually brought in. */
  netCents: number
  /** The gap the two bars draw. Not « reste à facturer », which is a dossier's and cumulative. */
  gapCents: number
  /** Boni (positive) or mali: what she billed against what the attached hours were worth. */
  varianceCents: number
}

export interface Honoraires {
  months: HonoraireMonth[]
  billableCents: number
  invoicedCents: number
  /** Factures repris de l’ancien logiciel: on the chart, outside the comparison. */
  historicalCents: number
  currentCents: number
  paidCents: number
  unpaidCents: number
  subcontractedCents: number
  netCents: number
  /** The practice's boni or mali over the window. */
  varianceCents: number
  scaleCents: number
}

/**
 * The four fills, kept together because they are the whole legend.
 *
 * <p><b>Measured, not chosen.</b> « Facturable » and « antérieur » used to be two desaturated pale
 * greens, #DDE8E0 and #C3CFC6, 7.8 apart in OKLab — below the floor at which full-colour vision tells
 * two fills apart, let alone a colourblind reader, and at a 9px legend swatch they were one colour.
 * « Couleurs plus différenciées » was the request and it was right.</p>
 *
 * <p>What replaced them was arrived at by running the palette validator rather than by eye: the first
 * attempt, a warm sand, measured <em>worse</em> than what it replaced. The set below separates by at
 * least 15 on every pair, in normal vision and under all three kinds of colour blindness.</p>
 *
 * <p>Two of the fills are very light and sit under 3:1 against the page, which obliges relief that is
 * not colour: each carries a border, the legend is always present, and the tooltip names and values
 * every segment.</p>
 */
const BILLABLE = 'bg-[#DDE8E0] border border-[#9DBCA8]'
const PAID = 'bg-[#2C4A38]'

/**
 * Hatched as well as tinted, and ochre rather than a third green: in this application ochre already
 * means money that has not arrived — « reste à facturer » is ochre on every screen — so the one
 * actionable segment on the chart wears the colour she already reads that way. It stacks *above*
 * paid rather than below it, so the dark segment keeps a common baseline and stays comparable from
 * one month to the next.
 */
const UNPAID =
  'bg-[repeating-linear-gradient(135deg,#BE8420_0_2px,#FFFFFF_2px_4px)] border-b border-[#2C4A38]'

/**
 * Factures reprises de l’ancien logiciel. A neutral grey, flat and clearly darker than facturable, so
 * it reads as background rather than as this month's activity: it is turnover she wants to see and not
 * something the gap above it is measuring, and a month of migrated history must not look like a month
 * of billing without working.
 */
const HISTORICAL = 'bg-[#A9B0A8] border border-[#8A938B]'

/**
 * « Honoraires facturables et facturés »: one question, am I invoicing what I actually work?
 *
 * <p>Two bars, not a line. The question is not how turnover is trending but whether a given month was
 * billed, so the <em>gap between the bars</em> is the information and it reads without an axis. The
 * current month is in progress, which is why its gap is called out rather than left to look like a
 * miss.</p>
 *
 * <p>It used to live in a 88px-tall card in the right-hand column of the accueil, with an « agrandir »
 * button opening the same chart with an axis. There is one chart now, it is the size of the thing it
 * is for, and the axis is simply always there: « le tableau de factu en plus gros, la page de
 * présentation générale doit s'ouvrir sur ça en premier ».</p>
 */
export function HonorairesChart({ data, picked, onPick, currentMonth }: {
  data: Honoraires
  /** The month whose dossiers are open below, so the column can say so. */
  picked?: number | null
  /** Clicking a column asks which dossiers it is made of. */
  onPick?: (index: number) => void
  /**
   * The first of the month being lived in, ISO, or absent when the period cannot contain it.
   *
   * <p>The chart used to take its last bucket for the current month, which was true while the window
   * was always twelve months ending on today. With the period a parameter it is false for every past
   * one: « exercice précédent » highlighted its final column and the tooltip announced « mois en
   * cours » about août of last year.</p>
   */
  currentMonth?: string
}) {
  const steps = [4, 3, 2, 1, 0]
  const height = 188

  return (
    <div className="overflow-hidden rounded-md border border-line-subtle bg-panel">
      <div className="flex flex-wrap items-center gap-3 border-b border-[#F1F3EE] px-3.5 py-2">
        <Legend swatch={BILLABLE}>Facturable</Legend>
        <Legend swatch={PAID}>Facturé, encaissé</Legend>
        <Legend swatch={UNPAID}>Facturé, non payé</Legend>
        {data.historicalCents > 0 && <Legend swatch={HISTORICAL}>Antérieur à Avocado</Legend>}

        <span className="flex-1" />

        {onPick && (
          <span className="type-caption text-muted">
            Cliquez un mois pour voir les dossiers
          </span>
        )}
      </div>

      <div className="flex gap-2.5 px-3.5 pt-4 pb-1">
        {/* The axis is its own column so the gridlines can span the plot exactly. */}
        <div className="relative shrink-0 basis-[52px]" style={{ height }}>
          {steps.map((step) => (
            <span
              key={step}
              style={{ top: `${((4 - step) / 4) * height - 7}px` }}
              className="absolute right-0 font-mono text-[10px] leading-[14px] text-muted tnum"
            >
              {formatEurosRounded((data.scaleCents / 4) * step)}
            </span>
          ))}
        </div>

        <div className="relative min-w-0 flex-1">
          <div className="pointer-events-none absolute inset-x-0 top-0" style={{ height }}>
            {steps.map((step) => (
              <span
                key={step}
                style={{ top: `${((4 - step) / 4) * height}px` }}
                className={cn(
                  'absolute inset-x-0 border-t',
                  step === 4 ? 'border-line-subtle' : step === 0 ? 'border-line' : 'border-[#F1F3EE]',
                )}
              />
            ))}
          </div>

          <Plot
            data={data}
            height={height}
            picked={picked}
            onPick={onPick}
            currentMonth={currentMonth}
          />
        </div>
      </div>
    </div>
  )
}

/**
 * The bars.
 *
 * <p>Hover answers a question that was asked, so the tooltip appears then and not before. The current
 * month keeps a resting highlight because it is the one being lived in; a picked month keeps a
 * stronger one because its dossiers are listed below and the two have to be visibly connected.</p>
 */
function Plot({ data, height, picked, onPick, currentMonth }: {
  data: Honoraires
  height: number
  picked?: number | null
  onPick?: (index: number) => void
  currentMonth?: string
}) {
  const [hovered, setHovered] = useState<number | null>(null)

  // Found rather than assumed to be last. −1 for a period that has already closed, which is what
  // leaves every column in its resting state instead of calling one of them « en cours ».
  const current = currentMonth === undefined
    ? -1
    : data.months.findIndex((month) => month.month.slice(0, 7) === currentMonth.slice(0, 7))

  const highlighted = hovered ?? picked ?? (current === -1 ? null : current)

  const percent = (cents: number) =>
    data.scaleCents === 0 ? 0 : Math.min(100, (cents / data.scaleCents) * 100)

  /** A segment's share of its own column, which is a different scale from the axis. */
  const share = (cents: number, of: number) => (of === 0 ? 0 : (cents / of) * 100)

  return (
    <div className="relative">
      {hovered !== null && data.months[hovered] && (
        <Tooltip
          month={data.months[hovered]}
          index={hovered}
          count={data.months.length}
          isCurrent={hovered === current}
        />
      )}

      <div className="flex items-end gap-1">
        {data.months.map((month, index) => {
          const net = percent(month.netCents)

          // The confrères are paid out of what came in, so what is left is settled money first, and
          // what came over from the old software is set aside before either. Every split is a cap and
          // never a pro rata: a proportion invented here would look like a figure.
          const historical = Math.min(month.historicalCents, month.netCents)
          const rest = month.netCents - historical
          const paid = Math.min(month.paidCents, rest)
          const unpaid = rest - paid

          return (
            <button
              key={month.month}
              type="button"
              // Never disabled: a disabled button fires no mouse events, so the tooltip would
              // disappear wherever the chart is shown without a drill-down.
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
              onClick={() => onPick?.(index)}
              className={cn(
                'flex min-w-0 flex-1 flex-col items-center gap-1.5 rounded-[3px] pt-0.5',
                onPick && 'cursor-pointer',
                index === picked
                  ? 'bg-[#EDF1EC] ring-1 ring-[#9DBCA8]'
                  : index === highlighted && 'bg-[#F8F9F6]',
              )}
            >
              <div style={{ height }} className="flex w-full items-end justify-center gap-[3px]">
                <div
                  style={{ height: `${percent(month.billableCents)}%` }}
                  className={cn('w-[42%] max-w-[17px] rounded-t-[2px]', BILLABLE)}
                />

                {/* One column, filled from the bottom: paid, unpaid hatched above it, and whatever
                    was repris above that. Paid keeps the baseline so the dark segment stays
                    comparable from one month to the next. */}
                <div
                  style={{ height: `${net}%` }}
                  className="flex w-[42%] max-w-[17px] flex-col justify-end overflow-hidden rounded-t-[2px]"
                >
                  {historical > 0 && <div style={{ height: `${share(historical, month.netCents)}%` }} className={HISTORICAL} />}
                  {unpaid > 0 && <div style={{ height: `${share(unpaid, month.netCents)}%` }} className={UNPAID} />}
                  <div style={{ height: `${share(paid, month.netCents)}%` }} className={PAID} />
                </div>
              </div>

              <span
                className={cn(
                  'text-[10.5px] leading-[14px]',
                  index === current || index === picked ? 'font-medium text-ink' : 'text-muted',
                )}
              >
                {shortMonth(month.month)}
              </span>
            </button>
          )
        })}
      </div>
    </div>
  )
}

/**
 * Figures, label left and value right in tabular mono so they compare vertically.
 *
 * It follows the hovered month and clamps against the edges, flush left at the start, flush right at
 * the end, centred in between, rather than overflowing the card it sits in.
 */
function Tooltip({ month, index, count, isCurrent }: {
  month: HonoraireMonth
  index: number
  count: number
  isCurrent: boolean
}) {
  const anchor = ((index + 0.5) / count) * 100
  const shift = index === 0 ? '0%' : index === count - 1 ? '-100%' : '-50%'

  return (
    <div
      style={{ left: `${anchor}%`, transform: `translateX(${shift})`, width: 212 }}
      className="pointer-events-none absolute -top-1.5 z-20 overflow-hidden rounded-md border border-line bg-panel text-left shadow-e2"
    >
      <div className="border-b border-line-subtle bg-[#F8F9F6] px-3 py-1.5 text-[12px] leading-4 font-semibold">
        {longMonth(month.month)}
        {isCurrent && ' · mois en cours'}
      </div>

      <div className="grid gap-1 px-3 py-2">
        <Line swatch={BILLABLE} label="Facturable" value={month.billableCents} />
        <Line swatch={PAID} label="Facturé" value={month.invoicedCents} />
        <Line indented label="· encaissé" value={month.paidCents} tone="text-brand-on-subtle" />
        <Line indented label="· non payé" value={month.unpaidCents} tone="text-warning" />

        {month.historicalCents > 0 && (
          <Line indented label="· antérieur" value={month.historicalCents} tone="text-muted" />
        )}

        {month.subcontractedCents > 0 && (
          <>
            <Line indented label="· sous-traitance" value={-month.subcontractedCents} tone="text-warning" />
            <Line label="Net" value={month.netCents} />
          </>
        )}

        <div className="mt-0.5 flex items-center justify-between gap-2 border-t border-line-subtle pt-[5px]">
          {/* Not « reste à facturer ». That figure is a dossier's, cumulative, and nets off
              provisions; this is one month of recorded time against one month of factures, and it
              goes negative whenever a facture issued in March pays for February. */}
          <span
            title={(month.gapCents >= 0
              ? 'Temps saisi ce mois-là qui n’a pas été facturé le même mois'
              : 'Facturé ce mois-là au-delà du temps saisi le même mois')
              + (month.historicalCents > 0
                ? '. Les factures antérieures à Avocado n’y entrent pas.'
                : '')}
            className="text-[11.5px] font-medium"
          >
            Écart
          </span>
          <span className="font-mono text-[11.5px] font-semibold tnum">
            {formatEuros(month.gapCents)}
          </span>
        </div>

        {/* Under the écart and not added to it. The écart says when she billed, this says at what
            price: work done in février and billed in mars opens a gap nobody decided, where this
            is 7 200 € of hours deliberately billed 6 000 €. */}
        {month.varianceCents !== 0 && (
          <div className="flex items-center justify-between gap-2 text-[11.5px] leading-4">
            <span>{month.varianceCents > 0 ? 'Boni' : 'Mali'}</span>
            <span
              className={cn(
                'font-mono tnum',
                month.varianceCents > 0 ? 'text-success' : 'text-warning',
              )}
            >
              {month.varianceCents > 0 ? '+ ' : '− '}
              {formatEuros(Math.abs(month.varianceCents))}
            </span>
          </div>
        )}
      </div>
    </div>
  )
}

function Line({ swatch, label, value, tone, indented }: {
  swatch?: string
  label: string
  value: number
  tone?: string
  indented?: boolean
}) {
  return (
    <div
      className={cn(
        'flex items-center justify-between gap-2 text-[11.5px] leading-4',
        indented && 'pl-[15px]',
      )}
    >
      <span className={cn('inline-flex items-center gap-1.5', indented ? 'text-muted' : 'text-ink-secondary')}>
        {swatch && <span className={cn('h-2 w-2 shrink-0 rounded-[2px]', swatch)} />}
        {label}
      </span>

      <span className={cn('font-mono tnum', tone ?? 'font-medium')}>{formatEuros(value)}</span>
    </div>
  )
}

export const Legend = ({ swatch, children }: { swatch: string; children: ReactNode }) => (
  <span className="inline-flex shrink-0 items-center gap-1.5 text-[11.5px] leading-4 text-ink-secondary">
    <span className={cn('h-[11px] w-[11px] shrink-0 rounded-[2px]', swatch)} />
    {children}
  </span>
)

const monthNames = [
  'janvier', 'février', 'mars', 'avril', 'mai', 'juin',
  'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre',
]

/** « mars 2026 ». Built from the parts rather than toLocaleDateString: the backend runs with
 *  InvariantGlobalization, and month names are content, not formatting. */
export function longMonth(iso: string): string {
  const [year, month] = iso.split('-')
  return `${monthNames[Number(month) - 1]} ${year}`
}

/** « avr. 25 ». Twelve of these have to fit across the chart, so the year is two digits. */
export function shortMonth(iso: string): string {
  const [year, month] = iso.split('-')
  const name = monthNames[Number(month) - 1] ?? ''
  const short = name.length <= 4 ? name : `${name.slice(0, name === 'juillet' ? 4 : 3)}.`

  return `${short} ${year?.slice(2)}`
}

export const capitalise = (value: string) => value.charAt(0).toUpperCase() + value.slice(1)
