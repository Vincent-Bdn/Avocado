import { cn } from './utils.js'
import { urgencyLabels } from '../labels.js'
import type { DeadlineUrgency } from '../types.js'

export const tierBorder: Record<DeadlineUrgency, string> = {
  Overdue: 'border-l-danger',
  Today: 'border-l-accent',
  ThisWeek: 'border-l-info',
  Later: 'border-l-line',
}

/**
 * The full row treatment for an échéance: 1px border, radius 4, a tinted fill and a 3px left border
 * in the tier colour. The four palettes are the design system's literals, which is why they are hex
 * rather than tokens: the tints are lighter than --status-*-bg and were picked so that four coloured
 * rows can sit under one another without the column turning into a rainbow.
 */
export const tierRow: Record<DeadlineUrgency, string> = {
  Overdue: 'border-[#EBC9C5] border-l-[#A32A22] bg-[#FDF4F3] text-[#8A211A]',
  Today: 'border-[#E8D5AE] border-l-[#8A5A10] bg-[#FDF8ED] text-[#6E4A0E]',
  ThisWeek: 'border-[#C7DAEB] border-l-[#2B5578] bg-[#F4F8FC] text-[#234B6B]',
  Later: 'border-[#E5E8E0] border-l-[#D2D7CB] bg-[#F8F9F6] text-ink',
}

/** Four tiers, four shapes, so a black and white printout stays readable. */
export function TierBullet({ urgency, className }: { urgency: DeadlineUrgency; className?: string }) {
  const shape: Record<DeadlineUrgency, string> = {
    Overdue: 'bg-danger rotate-45',
    Today: 'bg-accent rounded-full',
    ThisWeek: 'rounded-full border-[1.5px] border-info',
    Later: 'rounded-full bg-[#c0c6bb]',
  }

  return <span aria-hidden="true" className={cn('h-[7px] w-[7px] shrink-0', shape[urgency], className)} />
}

/** The uppercase mono caption that opens a tier group: shape, wording, count. */
export function TierCaption({ urgency, count }: { urgency: DeadlineUrgency; count: number }) {
  return (
    <div className="type-group flex items-center gap-1.5 pt-3.5 pb-1 text-muted">
      <TierBullet urgency={urgency} />
      {urgencyLabels[urgency]} · {count}
    </div>
  )
}

/** « 11/03 · dépassée de 3 j », « aujourd'hui · 17:00 », « 19/03 · dans 4 j ». */
export function distance(date: string, time: string | null): string {
  const day = new Date(`${date}T00:00:00`)
  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const days = Math.round((day.getTime() - today.getTime()) / 86_400_000)
  const shown = day.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' })

  if (days === 0) return time ? `aujourd’hui · ${time.slice(0, 5)}` : 'aujourd’hui'
  if (days < 0) return `${shown} · dépassée de ${-days} j`
  if (days < 31) return `${shown} · dans ${days} j`

  return `${shown} · dans ${Math.round(days / 30)} mois`
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('')
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} o`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} Ko`

  return `${(bytes / 1024 / 1024).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}
