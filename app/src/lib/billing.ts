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
