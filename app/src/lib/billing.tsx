import { formatDuration, formatEuros } from '../labels.js'
import { cn } from './utils.js'
import type { BillingSummary } from '../types.js'

/**
 * The fiche's context panel and the Facturation tab must never disagree, so the reading of a
 * BillingSummary lives here rather than being decided twice.
 *
 * Two states, not three. While there is something to bill the question is « combien », and the
 * answer is ochre because it is money not yet asked for. Once there is nothing left the figure that
 * matters is what the dossier earned against what its hours were worth.
 *
 * It used to switch on « no hours left and something invoiced », because the old « reste à facturer »
 * went negative whenever a provision outlived the time it stood against, and a negative headline had
 * to be hidden. Provisions are cash now and the figure cannot go below zero, so the switch is simply
 * whether anything is left.
 */
export interface BillingReading {
  settled: boolean
  /** The one large figure to show. */
  headline: number
  caption: string
  /** Ochre while there is work to bill, brand once there is not, danger on a mali. */
  tone: 'accent' | 'brand' | 'danger'
}

export function readBilling(summary: BillingSummary): BillingReading {
  if (summary.leftToBillCents > 0) {
    return {
      settled: false,
      headline: summary.leftToBillCents,
      caption: summary.fixedFeeCents === null ? 'Reste à facturer' : 'Reste du forfait',
      tone: 'accent',
    }
  }

  if (summary.varianceCents < 0) {
    return { settled: true, headline: summary.varianceCents, caption: 'Mali', tone: 'danger' }
  }

  return {
    settled: true,
    headline: summary.invoicedCents,
    caption: summary.varianceCents > 0 ? 'Facturé, dont boni' : 'Facturé',
    tone: 'brand',
  }
}

/** The card treatments, kept together so the two screens cannot drift apart. */
export const billingTone: Record<BillingReading['tone'], string> = {
  accent: 'border-[#E8D5AE] bg-[#FDF8ED] text-[#6E4A0E]',
  brand: 'border-[#BFD3C5] bg-[#F4F8F5] text-brand-on-subtle',
  danger: 'border-[#EBC9C5] bg-[#FDF4F3] text-[#8A211A]',
}

/**
 * The dossier's billing in the fiche's right-hand column, and the same block in the Documentation
 * screen's simulator. Compact: one headline and three mono lines, no card chrome.
 */
export function BillingStrip({ summary, hourlyRateCents }: {
  summary: BillingSummary
  hourlyRateCents: number
}) {
  const reading = readBilling(summary)

  return (
    <section className={cn('rounded-sm border px-2.5 py-2', billingTone[reading.tone])}>
      <BillingFigures summary={summary} compact />

      {reading.settled ? (
        <div className="mt-1 font-mono text-[10px] tnum opacity-80">
          il n’y a plus rien à facturer
        </div>
      ) : (
        <>
          <div className="mt-1 font-mono text-[10px] tnum">
            {summary.fixedFeeCents !== null
              ? `forfait de ${formatEuros(summary.fixedFeeCents)}`
              : `${formatDuration(summary.unbilledMinutes)} à ${formatEuros(hourlyRateCents)}/h`}
          </div>

          {summary.invoicedCents > 0 && (
            <div className="font-mono text-[10px] tnum opacity-80">
              {formatEuros(summary.invoicedCents)} déjà facturé
            </div>
          )}

          {summary.owedCents > 0 && (
            <div className="font-mono text-[10px] tnum opacity-80">
              {formatEuros(summary.owedCents)} restant dû
            </div>
          )}
        </>
      )}

      {summary.varianceCents !== 0 && (
        <div className="mt-1 font-mono text-[10px] tnum opacity-80">
          {summary.varianceCents > 0 ? 'boni' : 'mali'}{' '}
          {formatEuros(Math.abs(summary.varianceCents))}
        </div>
      )}
    </section>
  )
}

/**
 * What a dossier has earned, asked for and received. The Facturation tab's card, and the one the
 * Documentation screen's simulator shows, which is why it lives here rather than in the tab.
 *
 * <p>One card, not two. It used to switch between « combien reste-t-il » and « qu'a rapporté ce
 * dossier », because the old « reste à facturer » went negative whenever a provision outlived the
 * time it stood against, and a negative headline had to be hidden. Provisions are cash now and the
 * figure cannot go below zero, so there is one card and it says what is true.</p>
 *
 * <p>Nothing here is a compound subtraction except the forfait, where the subtraction actually ties.
 * Three figures that each answer a question, then the adjustments, each on its own line.</p>
 */
export function BillingCard({ summary, footnote }: {
  summary: BillingSummary
  /** « Dernière facture le … ». The tab knows it; the simulator has no dossier and omits it. */
  footnote?: string
}) {
  const reading = readBilling(summary)
  const forfait = summary.fixedFeeCents

  return (
    <section className={cn('rounded-md border px-4 py-3.5', billingTone[reading.tone])}>
      <BillingFigures summary={summary} />

      <div className="mt-2.5 grid gap-0.5 rounded-sm bg-panel px-2.5 py-2 font-mono text-[12px] text-ink tnum">
        <Line label="Travaillé" value={formatEuros(summary.workedCents)} />

        {forfait === null ? (
          <>
            <Line label="Facturé" value={formatEuros(summary.invoicedCents)} />
            <Line label="Encaissé" value={formatEuros(summary.collectedCents)} />
          </>
        ) : (
          <>
            <Line label="Encaissé" value={formatEuros(summary.collectedCents)} />
            <Line label="Forfait convenu" value={formatEuros(forfait)} />
            <Line label="− Facturé" value={formatEuros(summary.invoicedCents)} />
            <div className="mt-1 flex justify-between border-t border-line-subtle pt-1 font-semibold">
              <span>= Reste du forfait</span>
              <span>{formatEuros(summary.leftToBillCents)}</span>
            </div>
          </>
        )}
      </div>

      <div className="mt-2 grid gap-1.5">
        {summary.varianceCents !== 0 && reading.caption !== 'Mali' && (
          <Aside
            tone={summary.varianceCents > 0 ? 'good' : 'bad'}
            label={summary.varianceCents > 0 ? 'Boni' : 'Mali'}
            value={`${summary.varianceCents > 0 ? '+ ' : '− '}${formatEuros(Math.abs(summary.varianceCents))}`}
            detail="Écart entre ce qui a été facturé et ce que valaient les heures rattachées."
          />
        )}

        {summary.owedCents !== 0 && (
          <Aside
            label={summary.owedCents > 0 ? 'Reste dû par le client' : 'Payé d’avance'}
            value={formatEuros(Math.abs(summary.owedCents))}
            detail={summary.owedCents > 0
              ? 'Factures émises, pas encore encaissées.'
              : 'Provisions reçues au-delà de ce qui a été facturé.'}
          />
        )}

        {summary.disbursementsCents !== 0 && (
          <Aside
            label="Débours à refacturer"
            value={formatEuros(summary.disbursementsCents)}
            detail="Avancés pour le compte du client."
          />
        )}
      </div>

      <p className="m-0 mt-2 text-[11px] leading-4">
        {summary.unbilledMinutes === 0
          ? 'Plus aucune heure en attente.'
          : `${formatDuration(summary.unbilledMinutes)} non facturées.`}
        {footnote && ` ${footnote}`}
      </p>
    </section>
  )
}

const Line = ({ label, value }: { label: string; value: string }) => (
  <div className="flex justify-between">
    <span>{label}</span>
    <span>{value}</span>
  </div>
)

/**
 * One adjustment, with what it means next to it. Boni, reste dû and débours used to be stacked into
 * a single figure, which is how « reste à facturer » became a number nobody could take apart.
 */
const Aside = ({ label, value, detail, tone }: {
  label: string
  value: string
  detail: string
  tone?: 'good' | 'bad'
}) => (
  <div className="flex items-baseline gap-2 text-[11.5px] leading-4">
    <span className="type-group opacity-80">{label}</span>
    <span
      className={cn(
        'font-mono text-[13px] font-semibold tnum',
        tone === 'good' && 'text-success',
        tone === 'bad' && 'text-warning',
      )}
    >
      {value}
    </span>
    <span className="min-w-0 flex-1 opacity-80">{detail}</span>
  </div>
)

/**
 * The two figures a dossier is judged on, side by side and at the same size.
 *
 * One is what is still to be asked for; the other is what stays with the cabinet once the confrères
 * are paid. Neither is a footnote to the other, and they used to be: the net appeared in 15px under
 * a 28px headline on one screen and not at all on the other, which is how a figure stops being read.
 *
 * The second column appears only when there is sous-traitance. Restating a number that equals the
 * first one teaches people to stop looking at the second.
 */
export function BillingFigures({ summary, compact }: {
  summary: BillingSummary
  compact?: boolean
}) {
  const reading = readBilling(summary)
  const subcontracted = summary.subcontractedCents > 0

  const primary = (
    <Figure
      compact={compact}
      label={reading.caption}
      value={reading.headline < 0 && reading.settled ? -reading.headline : reading.headline}
      sign={reading.headline < 0 && reading.settled ? '− ' : ''}
    />
  )

  if (!subcontracted) return primary

  return (
    <div
      className={cn(
        compact
          ? 'grid gap-1.5 divide-y divide-current/20'
          : 'grid grid-cols-2 gap-4 divide-x divide-current/20',
      )}
    >
      {primary}

      <Figure
        compact={compact}
        className={compact ? 'pt-1.5' : 'pl-4'}
        label="Net de sous-traitance"
        value={summary.netCents}
        detail={`${formatEuros(summary.subcontractedCents)} rétrocédés`}
      />
    </div>
  )
}

function Figure({ label, value, detail, sign, compact, className }: {
  label: string
  value: number
  detail?: string
  sign?: string
  compact?: boolean
  className?: string
}) {
  return (
    <div className={className}>
      <div className={cn('font-medium', compact ? 'type-group opacity-80' : 'text-[12px]')}>
        {label}
      </div>

      <div
        className={cn(
          'font-mono font-semibold tracking-[-0.02em] tnum',
          compact ? 'text-[17px] leading-[22px]' : 'text-[26px] leading-8',
        )}
      >
        {sign}
        {formatEuros(value)}
      </div>

      {detail && (
        <div className={cn('font-mono tnum opacity-80', compact ? 'text-[10px]' : 'text-[11px]')}>
          {detail}
        </div>
      )}
    </div>
  )
}
