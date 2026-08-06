import { useCallback, useEffect, useState } from 'react'
import { Check, Plus } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Journal } from './Journal.js'
import { Billing } from './tabs/Billing.js'
import { Deadlines } from './tabs/Deadlines.js'
import { Documents } from './tabs/Documents.js'
import { TimeEntries } from './tabs/TimeEntries.js'
import { Badge } from './components/ui/badge.js'
import { Button } from './components/ui/button.js'
import { Panel } from './components/ui/panel.js'
import { cn } from './lib/utils.js'
import { formatDuration, formatEuros } from './labels.js'
import type { DeadlineUrgency, MatterDetail } from './types.js'

type Tab = 'journal' | 'documents' | 'deadlines' | 'time' | 'billing'

/** The fiche dossier: header 52, tab bar 32 sticky, body, and the 208px context panel. */
export function MatterView({ matterId, onChanged }: { matterId: string; onChanged: () => void }) {
  const [matter, setMatter] = useState<MatterDetail | null>(null)
  const [tab, setTab] = useState<Tab>('journal')
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<MatterDetail>(`/api/matters/${matterId}`)
      .then(setMatter)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  const refreshAll = useCallback(() => {
    reload()
    onChanged()
  }, [reload, onChanged])

  if (error) return <Panel><p className="p-4 text-danger">{error}</p></Panel>
  if (!matter) return <Panel />

  const client = matter.parties.find((party) => party.isClient)

  /**
   * Closing or reopening keeps the dossier on screen. It changes which list the dossier belongs to,
   * so the secondary panel is refreshed, but navigating away from what she was reading would be the
   * application deciding for her.
   */
  async function toggleClosed() {
    if (!matter) return

    await post(`/api/matters/${matterId}/${matter.isOpen ? 'close' : 'reopen'}`, {})
    refreshAll()
  }

  const tabs: [Tab, string, number | null][] = [
    ['journal', 'Journal', matter.counts.activities],
    ['documents', 'Documents', matter.counts.documents],
    ['deadlines', 'Échéances', matter.counts.openDeadlines],
    ['time', 'Temps passé', matter.counts.timeEntries],
    ['billing', 'Facturation', null],
  ]

  return (
    <Panel>
      <header className="relative shrink-0 border-b border-line-subtle px-4 py-2">
        <div className="flex items-baseline gap-2.5">
          <span className="font-mono text-[12px] text-muted tnum">{matter.reference}</span>
          <h2 className="m-0 truncate text-[20px] leading-[26px] font-semibold tracking-[-0.015em]">
            {matter.name}
          </h2>

          {/* Colour is never the only signal: a filled bullet or a check glyph doubles it. */}
          <Badge tone={matter.isOpen ? 'brand' : 'neutral'}>
            {matter.isOpen ? (
              <span className="h-1.5 w-1.5 rounded-full bg-current" />
            ) : (
              <Check size={11} strokeWidth={2.5} />
            )}
            {matter.isOpen ? 'En cours' : 'Clôturé'}
          </Badge>
        </div>

        <div className="mt-0.5 flex items-center gap-2 text-[11px] text-ink-secondary">
          {client && <span className="truncate">{client.displayName}</span>}

          {/* No dash placeholder when there is no RG: the segment is omitted entirely. */}
          {matter.courtCaseNumber && (
            <>
              <Divider />
              <span className="font-mono tnum">RG {matter.courtCaseNumber}</span>
            </>
          )}

          <Divider />
          <span className="font-mono tnum whitespace-nowrap">
            ouvert le {new Date(matter.openedOn).toLocaleDateString('fr-FR')}
            {matter.closedOn && ` · clôturé le ${new Date(matter.closedOn).toLocaleDateString('fr-FR')}`}
            {' · '}
            {formatEuros(matter.hourlyRateCents)}/h
          </span>
        </div>

        <div className="absolute top-3 right-4">
          <Button
            variant={matter.isOpen ? 'secondary' : 'primary'}
            onClick={() => void toggleClosed()}
          >
            {matter.isOpen ? 'Clôturer' : 'Rouvrir le dossier'}
          </Button>
        </div>
      </header>

      <nav className="flex h-8 shrink-0 items-stretch gap-0.5 border-b border-line px-2.5">
        {tabs.map(([id, title, count]) => (
          <button
            key={id}
            type="button"
            onClick={() => setTab(id)}
            className={cn(
              'flex items-center gap-1.5 px-2.5 text-[12px] transition-colors',
              // Underline and weight together, never colour alone.
              tab === id
                ? 'font-medium text-ink shadow-[inset_0_-2px_0_var(--brand)]'
                : 'text-ink-secondary hover:text-ink',
            )}
          >
            {title}
            {count !== null && (
              <span className="rounded-sm bg-sunken px-1 font-mono text-[10px] tnum">{count}</span>
            )}
          </button>
        ))}
      </nav>

      <div className="grid flex-1 grid-cols-[minmax(0,1fr)_208px] overflow-hidden">
        {tab === 'journal' && (
          <Journal matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'documents' && (
          <Documents matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'deadlines' && (
          <Deadlines matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'time' && (
          <TimeEntries matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'billing' && (
          <Billing matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}

        <ContextPanel matter={matter} />
      </div>
    </Panel>
  )
}

const Divider = () => <span className="h-2.5 w-px shrink-0 bg-line" />

/** 208px: échéances, à facturer, parties. Three blocks separated by rules. */
function ContextPanel({ matter }: { matter: MatterDetail }) {
  return (
    <aside className="grid content-start gap-3 overflow-y-auto border-l border-line-subtle p-2.5">
      <section className="border-b border-line-subtle pb-2.5">
        <ContextTitle>Échéances</ContextTitle>

        {matter.deadlines.length === 0 && (
          <p className="m-0 text-[11px] text-muted">Aucune échéance.</p>
        )}

        {matter.deadlines.map((deadline) => (
          <div
            key={deadline.id}
            className={cn(
              'mb-1.5 rounded-md border border-line-subtle border-l-[3px] px-2 py-1.5',
              tierBorder[deadline.urgency],
            )}
          >
            <div className="text-[11.5px] leading-[15px]">{deadline.label}</div>
            <div className="mt-0.5 flex items-center gap-1.5 font-mono text-[10px] text-muted tnum">
              <TierBullet urgency={deadline.urgency} />
              {distance(deadline.date, deadline.time)}
            </div>
          </div>
        ))}
      </section>

      <section className="border-b border-line-subtle pb-2.5">
        <ContextTitle>À facturer</ContextTitle>

        <div className="font-mono text-[19px] leading-6 font-semibold tnum">
          {formatEuros(matter.billing.leftToBillCents)}
        </div>

        <div className="font-mono text-[10px] text-muted tnum">
          {formatDuration(matter.billing.billableMinutes)} facturables ·{' '}
          {formatEuros(matter.hourlyRateCents)}/h
        </div>

        {matter.billing.ledgerCents !== 0 && (
          <div className="font-mono text-[10px] text-muted tnum">
            {matter.billing.ledgerCents > 0 ? '− ' : '+ '}
            {formatEuros(Math.abs(matter.billing.ledgerCents))}{' '}
            {matter.billing.ledgerCents > 0 ? 'déjà reçu' : 'avancé'}
          </div>
        )}

        {matter.billing.invoicedCents > 0 && (
          <div className="font-mono text-[10px] text-muted tnum">
            − {formatEuros(matter.billing.invoicedCents)} déjà facturé
          </div>
        )}
      </section>

      <section>
        <ContextTitle>Parties</ContextTitle>

        {matter.parties.map((party) => (
          <div key={party.id} className="mb-1.5 flex items-center gap-2">
            {/* Round = personne physique, rounded square = personne morale. */}
            <span
              className={cn(
                'grid h-5 w-5 shrink-0 place-items-center text-[9px] font-medium',
                party.contactType === 'Individual' ? 'rounded-full' : 'rounded-md',
                party.isClient ? 'bg-brand text-on-brand' : 'bg-sunken text-ink-secondary',
              )}
            >
              {initials(party.displayName)}
            </span>

            <span className="grid min-w-0">
              <span className="truncate text-[11.5px]">{party.displayName}</span>
              {/* Free text and often long: truncated, with the full wording in the title. */}
              <span
                className={cn(
                  'truncate text-[10.5px]',
                  party.isClient ? 'text-brand-on-subtle' : 'text-muted',
                )}
                title={party.role ?? undefined}
              >
                {party.role}
              </span>
            </span>
          </div>
        ))}

        <button
          type="button"
          disabled
          title="À venir"
          className="mt-1.5 flex h-5 items-center gap-1 rounded-sm border border-dashed border-line-strong px-2 text-[11px] text-disabled"
        >
          <Plus size={11} strokeWidth={2} />
          Ajouter une partie
        </button>
      </section>
    </aside>
  )
}

const ContextTitle = ({ children }: { children: string }) => (
  <h3 className="m-0 mb-1.5 font-mono text-[10px] font-normal tracking-[0.05em] uppercase text-muted">
    {children}
  </h3>
)

const tierBorder: Record<DeadlineUrgency, string> = {
  Overdue: 'border-l-danger',
  Today: 'border-l-accent',
  ThisWeek: 'border-l-info',
  Later: 'border-l-line',
}

/** Four tiers, four shapes, so a black and white printout stays readable. */
function TierBullet({ urgency }: { urgency: DeadlineUrgency }) {
  const shape: Record<DeadlineUrgency, string> = {
    Overdue: 'bg-danger rotate-45',
    Today: 'bg-accent rounded-full',
    ThisWeek: 'rounded-full border-[1.5px] border-info',
    Later: 'rounded-full bg-[#c0c6bb]',
  }

  return <span aria-hidden="true" className={cn('h-[7px] w-[7px] shrink-0', shape[urgency])} />
}

/** « 11/03 · dépassée de 3 j », « aujourd'hui · 17:00 », « 19/03 · dans 4 j ». */
function distance(date: string, time: string | null): string {
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

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('')
}
