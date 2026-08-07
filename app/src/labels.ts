import type { ActivityType, DeadlineUrgency } from './types.js'

/**
 * The French side of the contract. The API speaks enum keys, so every label the user reads is
 * decided here — one place to correct wording, and a renumbering on the server can never silently
 * relabel history.
 */
export const activityLabels: Record<ActivityType, string> = {
  Call: 'Appel',
  IncomingEmail: 'Mail reçu',
  OutgoingEmail: 'Mail envoyé',
  IncomingLetter: 'Courrier reçu',
  OutgoingLetter: 'Courrier envoyé',
  Meeting: 'RDV',
  Note: 'Note',
  Hearing: 'Audience',
  Other: 'Autre',
}

/** Composer order: the everyday ones first, not alphabetical. */
export const composerTypes: ActivityType[] = [
  'Call',
  'IncomingEmail',
  'OutgoingEmail',
  'IncomingLetter',
  'OutgoingLetter',
  'Meeting',
  'Note',
  'Hearing',
  'Other',
]

export const urgencyLabels: Record<DeadlineUrgency, string> = {
  Overdue: 'Dépassée',
  Today: 'Aujourd’hui',
  ThisWeek: 'Cette semaine',
  Later: 'Plus tard',
}

const euros = new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' })

export const formatEuros = (cents: number): string => euros.format(cents / 100)

const roundEuros = new Intl.NumberFormat('fr-FR', {
  style: 'currency',
  currency: 'EUR',
  maximumFractionDigits: 0,
})

/**
 * « 16 000 € ». For an axis and for headline totals, where the centimes are noise: a gridline reading
 * 16 000,00 € says nothing more than 16 000 € and takes half again the width to say it. Anywhere a
 * figure is a figure rather than a scale, use formatEuros.
 */
export const formatEurosRounded = (cents: number): string => roundEuros.format(cents / 100)

/** « 4 h 20 », « 45 min » — never « 4.33 h ». */
export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60

  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${String(rest).padStart(2, '0')}`
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' })
}

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })
}

/** « il y a 2 h », « hier », « 3 j » : the right-hand column of every recency list. */
export function formatRelative(iso: string | null): string {
  // Nothing rather than a placeholder glyph. An empty cell reads as "rien", a dash reads as a value.
  if (!iso) return ''

  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)

  if (days <= 0) return `il y a ${Math.max(1, Math.floor((Date.now() - new Date(iso).getTime()) / 3_600_000))} h`
  if (days === 1) return 'hier'
  if (days < 31) return `${days} j`

  return new Date(iso).toLocaleDateString('fr-FR', { month: 'short', year: '2-digit' })
}

/** Groups the timeline: « Cette semaine », « Semaine du 2 mars », « Février ». */
export function weekLabel(iso: string): string {
  const date = new Date(iso)
  const days = Math.floor((Date.now() - date.getTime()) / 86_400_000)

  if (days < 7) return 'Cette semaine'
  if (days < 31) return `Semaine du ${date.toLocaleDateString('fr-FR', { day: 'numeric', month: 'long' })}`

  return date.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' })
}
