import { CalendarClock, FileText, MessageSquare, Timer } from 'lucide-react'
import { Avatar } from '../components/ui/avatar.js'
import { BillingFigures, billingTone, readBilling } from '../lib/billing.js'
import { TierBullet, distance, tierBorder } from '../lib/urgency.js'
import { formatDuration, formatEuros } from '../labels.js'
import { cn } from '../lib/utils.js'
import type { MatterDetail } from '../types.js'
import type { ReactNode } from 'react'

/**
 * Aperçu: the dossier at rest.
 *
 * <p>Opening a dossier used to land on the journal, which is the densest screen there is: forty rows
 * of what happened, newest first, before she has been told which dossier she is even looking at. It
 * is the right screen for working and the wrong one for arriving.</p>
 *
 * <p>So this is what the 208px context panel showed, given room to breathe, plus the things that had
 * nowhere to go: the description, the juridiction and the RG, the dates, the rate. The panel is
 * hidden here rather than repeated, since the same figures twice on one screen is the sort of thing
 * that starts a doubt about which one is right.</p>
 *
 * <p>Nothing new is fetched. Every figure below was already on the fiche.</p>
 */
export function Overview({ matter, onOpen }: {
  matter: MatterDetail
  onOpen: (tab: 'journal' | 'documents' | 'deadlines' | 'time' | 'billing') => void
}) {
  const client = matter.parties.find((party) => party.isClient)
  const others = matter.parties.filter((party) => !party.isClient)
  const reading = readBilling(matter.billing)

  return (
    <div className="grid content-start gap-4 overflow-y-auto px-5 pt-4 pb-6">
      {/* The four numbers, each a way into the tab that holds them. */}
      <div className="grid grid-cols-2 gap-2 md:grid-cols-4">
        <Tile
          icon={<MessageSquare size={14} strokeWidth={1.75} />}
          value={matter.counts.activities}
          label="au journal"
          onClick={() => onOpen('journal')}
        />
        <Tile
          icon={<FileText size={14} strokeWidth={1.75} />}
          value={matter.counts.documents}
          label="documents"
          onClick={() => onOpen('documents')}
        />
        <Tile
          icon={<CalendarClock size={14} strokeWidth={1.75} />}
          value={matter.counts.openDeadlines}
          label="échéances"
          onClick={() => onOpen('deadlines')}
        />
        <Tile
          icon={<Timer size={14} strokeWidth={1.75} />}
          value={formatDuration(matter.billing.billableMinutes)}
          label="à facturer"
          onClick={() => onOpen('time')}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <div className="grid content-start gap-4">
          <Card title="Le dossier">
            {matter.description
              ? (
                <p className="m-0 max-w-[64ch] text-[12.5px] leading-[20px] text-ink-secondary">
                  {matter.description}
                </p>
              )
              : <Absent>Aucune description.</Absent>}

            <dl className="m-0 mt-1 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5">
              <Entry term="Référence" value={matter.reference} mono />
              <Entry term="Nature" value={matter.classification} />
              <Entry term="Juridiction" value={matter.court} />
              <Entry term="N° RG" value={matter.courtCaseNumber} mono />
              <Entry term="Ouvert le" value={date(matter.openedOn)} mono />
              {matter.closedOn && <Entry term="Clôturé le" value={date(matter.closedOn)} mono />}
              <Entry term="Taux horaire" value={`${formatEuros(matter.hourlyRateCents)} / heure`} mono />
            </dl>
          </Card>

          <Card title={`Parties · ${matter.parties.length}`}>
            {matter.parties.length === 0 && <Absent>Aucune partie enregistrée.</Absent>}

            {/* The client first and set apart: on a dossier repris de Gestisoft there are sometimes
                three of them, a famille and its two SCI, and running them in with the adverse party
                would lose the one distinction that matters. */}
            {client && <Party party={client} />}
            {matter.parties.filter((party) => party.isClient && party !== client).map((party) => (
              <Party key={party.id} party={party} />
            ))}

            {others.length > 0 && (
              <div className="mt-1 grid gap-2 border-t border-line-subtle pt-2.5">
                {others.map((party) => <Party key={party.id} party={party} />)}
              </div>
            )}
          </Card>
        </div>

        <div className="grid content-start gap-4">
          <Card title="Échéances">
            {matter.deadlines.length === 0 && <Absent>Aucune échéance à venir.</Absent>}

            <div className="grid gap-1.5">
              {matter.deadlines.map((deadline) => (
                <div
                  key={deadline.id}
                  className={cn(
                    'rounded-sm border border-line-subtle border-l-[3px] px-2.5 py-2',
                    tierBorder[deadline.urgency],
                  )}
                >
                  <div className="text-[12.5px] leading-[18px]">{deadline.label}</div>
                  <div className="mt-0.5 flex items-center gap-1.5 font-mono text-[10.5px] text-muted tnum">
                    <TierBullet urgency={deadline.urgency} />
                    {distance(deadline.date, deadline.time)}
                  </div>
                </div>
              ))}
            </div>
          </Card>

          <Card title="Facturation">
            <div className={cn('rounded-sm border px-3 py-2.5', billingTone[reading.tone])}>
              <BillingFigures summary={matter.billing} />

              {reading.settled
                ? (
                  <div className="mt-1.5 font-mono text-[11px] tnum opacity-80">
                    tout le temps saisi est facturé
                  </div>
                )
                : (
                  <div className="mt-1.5 font-mono text-[11px] tnum">
                    {formatDuration(matter.billing.billableMinutes)} facturables ·{' '}
                    {formatEuros(matter.hourlyRateCents)}/h
                  </div>
                )}
            </div>

            <dl className="m-0 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5">
              {matter.billing.invoicedCents > 0 && (
                <Entry term="Déjà facturé" value={formatEuros(matter.billing.invoicedCents)} mono />
              )}

              {matter.billing.ledgerCents !== 0 && (
                <Entry
                  term={matter.billing.ledgerCents > 0 ? 'Déjà reçu' : 'Avancé pour le client'}
                  value={formatEuros(Math.abs(matter.billing.ledgerCents))}
                  mono
                />
              )}

              {matter.billing.subcontractedCents > 0 && (
                <Entry
                  term="Sous-traitance"
                  value={formatEuros(matter.billing.subcontractedCents)}
                  mono
                />
              )}

              {matter.billing.varianceCents !== 0 && (
                <Entry
                  term={matter.billing.varianceCents > 0 ? 'Boni' : 'Mali'}
                  value={formatEuros(Math.abs(matter.billing.varianceCents))}
                  mono
                />
              )}
            </dl>
          </Card>
        </div>
      </div>
    </div>
  )
}

/** JJ/MM/AAAA, written out rather than asked of a locale that may not be installed. */
function date(iso: string) {
  const parts = iso.slice(0, 10).split('-')

  return parts.length === 3 ? `${parts[2]}/${parts[1]}/${parts[0]}` : iso
}

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="grid content-start gap-2 rounded-md border border-line-subtle bg-panel px-3.5 py-3">
      <h3 className="m-0 font-mono text-[10px] tracking-[0.05em] uppercase text-muted">{title}</h3>
      {children}
    </section>
  )
}

const Absent = ({ children }: { children: ReactNode }) => (
  <p className="m-0 text-[12px] text-disabled italic">{children}</p>
)

/** Omitted entirely when empty: a column of dashes says nothing and takes the same room. */
function Entry({ term, value, mono }: { term: string; value: string | null; mono?: boolean }) {
  if (value === null || value === '') {
    return null
  }

  return (
    <>
      <dt className="text-[11.5px] text-muted">{term}</dt>
      <dd className={cn('m-0 text-[12.5px] text-ink', mono && 'font-mono tnum')}>{value}</dd>
    </>
  )
}

function Party({ party }: { party: MatterDetail['parties'][number] }) {
  return (
    <div className="flex items-center gap-2.5">
      <Avatar name={party.displayName} type={party.contactType} client={party.isClient} />

      <span className="grid min-w-0">
        <span className="truncate text-[12.5px]">{party.displayName}</span>
        <span
          className={cn(
            'truncate text-[11px]',
            party.role
              ? party.isClient ? 'text-brand-on-subtle' : 'text-muted'
              : 'text-disabled italic',
          )}
        >
          {party.role ?? 'rôle non précisé'}
        </span>
      </span>
    </div>
  )
}

function Tile({ icon, value, label, onClick }: {
  icon: ReactNode
  value: string | number
  label: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="grid gap-0.5 rounded-md border border-line-subtle bg-panel px-3 py-2.5 text-left hover:border-line-strong hover:bg-hover"
    >
      <span className="flex items-center gap-1.5 text-muted">{icon}</span>
      <span className="font-mono text-[17px] leading-6 text-ink tnum">{value}</span>
      <span className="text-[11.5px] text-muted">{label}</span>
    </button>
  )
}
