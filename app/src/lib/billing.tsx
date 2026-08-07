import { formatEuros } from '../labels.js'
import { cn } from './utils.js'
import type { BillingSummary } from '../types.js'

/**
 * The fiche's context panel and the Facturation tab must never disagree, so the reading of a
 * BillingSummary lives here rather than being decided twice.
 *
 * Two states, not one. While there are hours to bill the question is « combien », and the answer is
 * ochre because it is money not yet asked for. Once everything is billed that question has no
 * meaning, « reste à facturer : −150 € » is a provision showing through, and the figure that
 * matters becomes the boni or the mali: where the practice made money and where it gave some away.
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
  const settled = summary.billableMinutes === 0 && summary.invoicedCents > 0

  if (!settled) {
    return {
      settled: false,
      headline: summary.leftToBillCents,
      caption: 'Reste à facturer',
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
