/** Mirrors the server DTOs. Enums travel as their names; French labels live in `labels.ts`. */

export type ContactType = 'Individual' | 'Organisation'

export type ActivityType =
  | 'Call'
  | 'IncomingEmail'
  | 'OutgoingEmail'
  | 'IncomingLetter'
  | 'OutgoingLetter'
  | 'Meeting'
  | 'Note'
  | 'Hearing'
  | 'Other'

export type DeadlineUrgency = 'Overdue' | 'Today' | 'ThisWeek' | 'Later'

export interface ContactSummary {
  id: string
  type: ContactType
  displayName: string
  email: string | null
  phone: string | null
}

export interface MatterListItem {
  id: string
  reference: string
  name: string
  clientName: string | null
  courtCaseNumber: string | null
  classification: string | null
  isOpen: boolean
  isFavourite: boolean
  nextDeadlineDate: string | null
  nextDeadlineTime: string | null
  nextDeadlineUrgency: DeadlineUrgency | null
  lastActivityAt: string | null
}

export interface MatterListPage {
  items: MatterListItem[]
  total: number
}

export interface MatterParty {
  id: string
  contactId: string
  contactType: ContactType
  displayName: string
  isClient: boolean
  role: string | null
}

/**
 * What a dossier earned, asked for and received.
 *
 * Three figures and two adjustments, in that order, because that is the order she reads them in.
 */
export interface BillingSummary {
  /** Every billable hour at its rate, billed or not. What boni and mali are measured against. */
  workedCents: number
  /** Billable time no facture has taken yet. */
  unbilledTimeCents: number
  unbilledMinutes: number
  /** Factures HT. An abandon is not one. */
  invoicedCents: number
  /** Factures payées plus provisions reçues. */
  collectedCents: number
  /** `invoicedCents − collectedCents`. Negative means the client has paid in advance. */
  owedCents: number
  /** Débours avancés pour le client, still to be recharged. Its own figure, never mixed in. */
  disbursementsCents: number
  /** Positive = boni, negative = mali. */
  varianceCents: number
  /** Rétrocessions et sous-traitance. Never part of `leftToBillCents`. */
  subcontractedCents: number
  /** `invoicedCents − subcontractedCents`: what the dossier actually brought the cabinet. */
  netCents: number
  /** At the hour: the time no facture took. At a forfait: `forfait − facturé`, never below zero. */
  leftToBillCents: number
  /** Set when the dossier is billed at an agreed price, which then decides what is left. */
  fixedFeeCents: number | null
}

export interface MatterDetail {
  id: string
  reference: string
  name: string
  description: string | null
  openedOn: string
  closedOn: string | null
  hourlyRateCents: number
  /** Le forfait convenu, en centimes. Null quand le dossier est facturé à l'heure. */
  fixedFeeCents: number | null
  courtCaseNumber: string | null
  classification: string | null
  court: string | null
  /** Where her documents live, on her own disk. Null until she points at a folder. */
  documentsFolder: string | null
  isOpen: boolean
  isFavourite: boolean
  parties: MatterParty[]
  deadlines: {
    id: string
    date: string
    time: string | null
    label: string
    urgency: DeadlineUrgency
  }[]
  /** The five last modified in her folder. The Documents tab shows the rest. */
  documents: {
    name: string
    relativePath: string
    exhibitNumber: number | null
    modifiedAt: string
  }[]
  counts: { activities: number; documents: number; openDeadlines: number; timeEntries: number }
  billing: BillingSummary
}

export interface PracticeSettings {
  hourlyRateCents: number
  /** Hers. What tells a courriel reçu from a courriel envoyé. */
  emailAddresses: string[]
  vaultDirectory: string
  /** « MM-DD ». The first day of the exercice comptable, against which the CA is counted. */
  exerciceStart: string
}

export interface ActivityListItem {
  id: string
  type: ActivityType
  occurredAt: string
  contactId: string | null
  contactName: string | null
  subject: string | null
  body: string | null
  trackingNumber: string | null
  durationMinutes: number | null
  attachments: { id: string; name: string; sizeBytes: number; exhibitNumber: number | null }[]
}

export interface ActivityListPage {
  items: ActivityListItem[]
  total: number
}
