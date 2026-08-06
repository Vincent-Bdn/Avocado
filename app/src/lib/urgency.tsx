import { cn } from './utils.js'
import { urgencyLabels } from '../labels.js'
import type { DeadlineUrgency } from '../types.js'

export const tierBorder: Record<DeadlineUrgency, string> = {
  Overdue: 'border-l-danger',
  Today: 'border-l-accent',
  ThisWeek: 'border-l-info',
  Later: 'border-l-line',
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
    <div className="flex items-center gap-1.5 pt-3.5 pb-1 font-mono text-[10px] tracking-[0.05em] uppercase text-muted">
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
