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

export interface BillingSummary {
  /** Billable time not yet attached to a facture. */
  billableTimeCents: number
  billableMinutes: number
  ledgerCents: number
  invoicedCents: number
  /** The part of `invoicedCents` recorded by hand rather than built from selected hours. */
  manualInvoicedCents: number
  /** Positive = boni, negative = mali. */
  varianceCents: number
  /** Rétrocessions et sous-traitance. Never part of `leftToBillCents`. */
  subcontractedCents: number
  /** `invoicedCents − subcontractedCents`: what the dossier actually brought the cabinet. */
  netCents: number
  leftToBillCents: number
}

export interface MatterDetail {
  id: string
  reference: string
  name: string
  description: string | null
  openedOn: string
  closedOn: string | null
  hourlyRateCents: number
  courtCaseNumber: string | null
  classification: string | null
  court: string | null
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
  counts: { activities: number; documents: number; openDeadlines: number; timeEntries: number }
  billing: BillingSummary
}

export interface PracticeSettings {
  hourlyRateCents: number
  vaultDirectory: string
  workingDirectory: string
  workingDirectoryIsFixed: boolean
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
