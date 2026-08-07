import { useState, type ReactNode } from 'react'
import { Maximize2 } from 'lucide-react'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'
import { formatEuros, formatEurosRounded } from '../labels.js'

export interface HonoraireMonth {
  /** The first of the month, ISO. */
  month: string
  billableCents: number
  invoicedCents: number
  paidCents: number
  unpaidCents: number
  leftToBillCents: number
}

export interface Honoraires {
  months: HonoraireMonth[]
  billableCents: number
  invoicedCents: number
  paidCents: number
  unpaidCents: number
  scaleCents: number
}

/**
 * The three fills, kept together because they are the whole legend.
 *
 * Unpaid is hatched as well as tinted, and stacks *above* paid rather than below it, so the dark
 * segment keeps a common baseline and stays comparable from one month to the next. Colour is never
 * the only signal.
 */
const BILLABLE = 'bg-[#DDE8E0] border border-[#9DBCA8]'
const PAID = 'bg-[#2C4A38]'
const UNPAID =
  'bg-[repeating-linear-gradient(135deg,#8FB79E_0_2px,#FFFFFF_2px_4px)] border-b border-[#2C4A38]'

/**
 * « Honoraires facturables et facturés » — twelve months of one question: am I invoicing what I
 * actually work?
 *
 * Two bars, not a line. The question is not how turnover is trending but whether a given month was
 * billed, so the *gap between the bars* is the information and it reads without an axis. The current
 * month is in progress, which is why its gap is called out rather than left to look like a miss.
 */
export function HonorairesCard({ data }: { data: Honoraires }) {
  const [expanded, setExpanded] = useState(false)

  return (
    <section>
      <h3 className="type-title m-0 flex items-center gap-2 pb-2">
        Honoraires facturables et facturés
        <span className="flex-1" />
        <Button
          variant="secondary"
          size="iconSm"
          title="Agrandir le graphique"
          onClick={() => setExpanded(true)}
        >
          <Maximize2 size={12} strokeWidth={2} />
        </Button>
      </h3>

      <div className="overflow-hidden rounded-md border border-line-subtle bg-panel">
        <div className="flex flex-wrap items-center gap-2.5 border-b border-[#F1F3EE] px-3 pt-2.5 pb-[7px]">
          <span className="font-mono text-[10px] text-muted">12 derniers mois</span>
          <span className="flex-1" />
          <Legend swatch={BILLABLE}>Facturable</Legend>
          <Legend swatch={PAID}>Facturé, payé</Legend>
          <Legend swatch={cn(UNPAID, 'border border-[#2C4A38]')}>Facturé, non payé</Legend>
        </div>

        <Plot data={data} height={88} barWidth={9} tooltipWidth={186} />

        <div className="flex items-center gap-2.5 border-t border-line-subtle bg-[#F8F9F6] px-3 py-[7px]">
          <span className="min-w-0 flex-1 text-[11px] leading-[15px] text-ink-secondary">
            Facturable <Strong>{formatEurosRounded(data.billableCents)}</Strong> · facturé{' '}
            <Strong>{formatEurosRounded(data.invoicedCents)}</Strong>
          </span>

          {data.unpaidCents > 0 && (
            <span className="shrink-0 font-mono text-[10.5px] text-warning tnum">
              {formatEurosRounded(data.unpaidCents)} non payés
            </span>
          )}
        </div>
      </div>

      {expanded && <HonorairesDialog data={data} onClose={() => setExpanded(false)} />}
    </section>
  )
}

/**
 * The plot itself, shared by the card and the dialog. Only the measurements differ, which is the
 * point: two implementations of the same bars would drift the first time one of them was corrected.
 */
function Plot({ data, height, barWidth, tooltipWidth, labels = 'initial' }: {
  data: Honoraires
  height: number
  barWidth: number
  tooltipWidth: number
  labels?: 'initial' | 'full'
}) {
  const [hovered, setHovered] = useState<number | null>(null)
  const current = data.months.length - 1

  // The current month keeps the highlight at rest, because it is the one being lived in. The tooltip
  // is a different thing: it answers a question that was asked, so it appears on hover and not before.
  const highlighted = hovered ?? current

  const percent = (cents: number) =>
    data.scaleCents === 0 ? 0 : Math.min(100, (cents / data.scaleCents) * 100)

  return (
    <div className={cn('relative', labels === 'full' ? 'py-0' : 'px-3 pt-2.5 pb-1.5')}>
      {hovered !== null && data.months[hovered] && (
        <Tooltip
          month={data.months[hovered]}
          index={hovered}
          count={data.months.length}
          width={tooltipWidth}
          large={labels === 'full'}
        />
      )}

      <div className={cn('flex items-end', labels === 'full' ? 'gap-1' : 'gap-0.5')}>
        {data.months.map((month, index) => {
          const invoiced = percent(month.invoicedCents)

          return (
            <div
              key={month.month}
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
              className={cn(
                'flex min-w-0 flex-1 flex-col items-center rounded-[3px]',
                labels === 'full' ? 'gap-1.5' : 'gap-1 pt-0.5',
                index === highlighted && 'bg-[#F8F9F6]',
              )}
            >
              <div
                style={{ height }}
                className={cn(
                  'flex w-full items-end justify-center',
                  labels === 'full' ? 'gap-[3px]' : 'gap-0.5',
                )}
              >
                <div
                  style={{ width: barWidth, height: `${percent(month.billableCents)}%` }}
                  className={cn('rounded-t-[2px]', BILLABLE)}
                />

                {/* One column, filled from the bottom: paid, then unpaid hatched above it. */}
                <div
                  style={{ width: barWidth, height: `${invoiced}%` }}
                  className="flex flex-col justify-end overflow-hidden rounded-t-[2px]"
                >
                  {month.unpaidCents > 0 && (
                    <div
                      style={{
                        height: `${month.invoicedCents === 0 ? 0 : (month.unpaidCents / month.invoicedCents) * 100}%`,
                      }}
                      className={UNPAID}
                    />
                  )}
                  <div
                    style={{
                      height: `${month.invoicedCents === 0 ? 0 : (month.paidCents / month.invoicedCents) * 100}%`,
                    }}
                    className={PAID}
                  />
                </div>
              </div>

              <span
                className={cn(
                  labels === 'full'
                    ? 'text-[10.5px] leading-[14px]'
                    : 'font-mono text-[9px] leading-3',
                  index === current ? 'font-medium text-ink' : 'text-muted',
                )}
              >
                {labels === 'full' ? shortMonth(month.month) : initial(month.month)}
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

/**
 * Five figures, label left and value right in tabular mono so they compare vertically.
 *
 * It follows the hovered month and clamps against the edges — flush left at the start, flush right at
 * the end, centred in between — rather than overflowing the card it sits in.
 */
function Tooltip({ month, index, count, width, large }: {
  month: HonoraireMonth
  index: number
  count: number
  width: number
  large?: boolean
}) {
  const isCurrent = index === count - 1
  const anchor = ((index + 0.5) / count) * 100
  const shift = index === 0 ? '0%' : index === count - 1 ? '-100%' : '-50%'

  return (
    <div
      style={{ left: `${anchor}%`, transform: `translateX(${shift})`, width }}
      className={cn(
        'pointer-events-none absolute z-20 overflow-hidden rounded-md border border-line bg-panel shadow-e2',
        large ? '-top-1.5' : 'top-0',
      )}
    >
      <div
        className={cn(
          'border-b border-line-subtle bg-[#F8F9F6] font-semibold',
          large ? 'px-3 py-1.5 text-[12px] leading-4' : 'px-2.5 py-1 text-[11px] leading-[15px]',
        )}
      >
        {longMonth(month.month)}
        {isCurrent && ' · mois en cours'}
      </div>

      <div className={cn('grid', large ? 'gap-1 px-3 py-2' : 'gap-[3px] px-2.5 py-1.5')}>
        <Line large={large} swatch={BILLABLE} label="Facturable" value={month.billableCents} />
        <Line large={large} swatch={PAID} label="Facturé" value={month.invoicedCents} />
        <Line large={large} indented label="· payé" value={month.paidCents} tone="text-brand-on-subtle" />
        <Line large={large} indented label="· non payé" value={month.unpaidCents} tone="text-warning" />

        <div
          className={cn(
            'mt-0.5 flex items-center justify-between gap-2 border-t border-line-subtle',
            large ? 'pt-[5px]' : 'pt-1',
          )}
        >
          <span className={cn('font-medium', large ? 'text-[11.5px]' : 'text-[10.5px]')}>
            Reste à facturer
          </span>
          <span
            className={cn(
              'font-mono font-semibold tnum',
              large ? 'text-[11.5px]' : 'text-[10.5px]',
            )}
          >
            {formatEuros(month.leftToBillCents)}
          </span>
        </div>
      </div>
    </div>
  )
}

function Line({ swatch, label, value, tone, indented, large }: {
  swatch?: string
  label: string
  value: number
  tone?: string
  indented?: boolean
  large?: boolean
}) {
  return (
    <div
      className={cn(
        'flex items-center justify-between gap-2',
        large ? 'text-[11.5px] leading-4' : 'text-[10.5px] leading-[14px]',
        indented && (large ? 'pl-[15px]' : 'pl-[13px]'),
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

/** The same chart with room to read it: a real axis, full month names, and the four totals. */
function HonorairesDialog({ data, onClose }: { data: Honoraires; onClose: () => void }) {
  const steps = [4, 3, 2, 1, 0]
  const first = data.months[0]
  const last = data.months[data.months.length - 1]

  return (
    <div
      onClick={onClose}
      className="fixed inset-0 z-50 grid place-items-center bg-[var(--surface-scrim)] p-6"
    >
      <div
        onClick={(event) => event.stopPropagation()}
        className="flex max-h-full w-[880px] max-w-full flex-col overflow-hidden rounded-xl bg-panel shadow-e3"
      >
        <header className="flex items-start gap-3 border-b border-line-subtle px-[18px] pt-3.5 pb-3">
          <div className="min-w-0 flex-1">
            <div className="type-title">Honoraires facturables et facturés</div>
            <div className="mt-px text-[11.5px] leading-4 text-ink-secondary">
              {first && last && `${capitalise(longMonth(first.month))} à ${longMonth(last.month)}`} ·
              le temps saisi valorisé au taux du dossier, face aux factures réellement émises
            </div>
          </div>

          <Button variant="secondary" size="iconSm" aria-label="Fermer" onClick={onClose}>
            <span className="text-[13px] leading-none">✕</span>
          </Button>
        </header>

        <div className="flex flex-wrap items-center gap-3.5 border-b border-[#F1F3EE] px-[18px] py-2.5">
          <Legend swatch={BILLABLE} large>Facturable — temps saisi × taux horaire</Legend>
          <Legend swatch={PAID} large>Facturé et payé</Legend>
          <Legend swatch={cn(UNPAID, 'border border-[#2C4A38]')} large>Facturé, non encore payé</Legend>
        </div>

        <div className="flex gap-2.5 overflow-y-auto px-[18px] pt-[18px] pb-2.5">
          {/* The axis is its own column so the gridlines can span the plot exactly. */}
          <div className="relative h-[260px] shrink-0 basis-[52px]">
            {steps.map((step) => (
              <span
                key={step}
                style={{ top: `${((4 - step) / 4) * 260 - 7}px` }}
                className="absolute right-0 font-mono text-[10px] leading-[14px] text-muted tnum"
              >
                {formatEurosRounded((data.scaleCents / 4) * step)}
              </span>
            ))}
          </div>

          <div className="relative min-w-0 flex-1">
            <div className="pointer-events-none absolute inset-x-0 top-0 h-[260px]">
              {steps.map((step) => (
                <span
                  key={step}
                  style={{ top: `${((4 - step) / 4) * 260}px` }}
                  className={cn(
                    'absolute inset-x-0 border-t',
                    step === 4 ? 'border-line-subtle' : step === 0 ? 'border-line' : 'border-[#F1F3EE]',
                  )}
                />
              ))}
            </div>

            <Plot data={data} height={260} barWidth={17} tooltipWidth={236} labels="full" />
          </div>
        </div>

        <p className="m-0 px-[18px] pt-1 pb-3.5 text-[11px] leading-4 text-muted">
          {last && `Mois en cours : ${longMonth(last.month)}`}
        </p>

        <div className="grid shrink-0 grid-cols-4 border-t border-line-subtle bg-[#F8F9F6]">
          <Total label="Facturable" value={data.billableCents} />
          <Total label="Facturé" value={data.invoicedCents} />
          <Total label="Encaissé" value={data.paidCents} tone="text-brand-on-subtle" />
          <Total label="En attente de règlement" value={data.unpaidCents} tone="text-warning" last />
        </div>
      </div>
    </div>
  )
}

const Total = ({ label, value, tone, last }: {
  label: string
  value: number
  tone?: string
  last?: boolean
}) => (
  <div className={cn('px-[18px] py-2.5', !last && 'border-r border-line-subtle')}>
    <div className="text-[11px] leading-[15px] text-muted">{label}</div>
    <div className={cn('font-mono text-[15px] leading-[21px] font-semibold tnum', tone)}>
      {formatEurosRounded(value)}
    </div>
  </div>
)

const Legend = ({ swatch, large, children }: {
  swatch: string
  large?: boolean
  children: ReactNode
}) => (
  <span
    className={cn(
      'inline-flex shrink-0 items-center gap-1.5 text-ink-secondary',
      large ? 'text-[11.5px] leading-4' : 'text-[10.5px] leading-[14px]',
    )}
  >
    <span className={cn('shrink-0 rounded-[2px]', large ? 'h-[11px] w-[11px]' : 'h-[9px] w-[9px]', swatch)} />
    {children}
  </span>
)

const Strong = ({ children }: { children: ReactNode }) => (
  <strong className="font-medium tnum">{children}</strong>
)

const monthNames = [
  'janvier', 'février', 'mars', 'avril', 'mai', 'juin',
  'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre',
]

/** « mars 2026 ». Built from the parts rather than toLocaleDateString: the app runs with
 *  InvariantGlobalization on the backend, and month names are content, not formatting. */
function longMonth(iso: string): string {
  const [year, month] = iso.split('-')
  return `${monthNames[Number(month) - 1]} ${year}`
}

/** « avr. 25 ». Twelve of these have to fit across the dialog, so the year is two digits. */
function shortMonth(iso: string): string {
  const [year, month] = iso.split('-')
  const name = monthNames[Number(month) - 1] ?? ''
  const short = name.length <= 4 ? name : `${name.slice(0, name === 'juillet' ? 4 : 3)}.`

  return `${short} ${year?.slice(2)}`
}

function initial(iso: string): string {
  return (monthNames[Number(iso.split('-')[1]) - 1] ?? '').charAt(0).toUpperCase()
}

const capitalise = (value: string) => value.charAt(0).toUpperCase() + value.slice(1)
