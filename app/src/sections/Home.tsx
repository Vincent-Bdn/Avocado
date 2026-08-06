import { useEffect, useState } from 'react'
import { PageHeader } from '../components/ui/page-header.js'
import { Panel } from '../components/ui/panel.js'
import { ApiError, api } from '../api.js'
import { cn } from '../lib/utils.js'
import { TierCaption, distance, tierBorder } from '../lib/urgency.js'
import { activityLabels, formatDuration, formatEuros, formatRelative } from '../labels.js'
import type { ActivityType, DeadlineUrgency } from '../types.js'

interface DashboardSummary {
  today: string
  openMatterCount: number
  contactCount: number
  withinSevenDaysCount: number
  deadlines: {
    id: string
    matterId: string
    matterReference: string
    matterName: string
    clientName: string | null
    label: string
    date: string
    time: string | null
    urgency: DeadlineUrgency
  }[]
  nextDeadlineBeyondHorizon: string | null
  unbilled: {
    totalCents: number
    totalBillableMinutes: number
    matterCount: number
    agedOverSixtyDaysCents: number
    matters: { matterId: string; matterName: string; billableMinutes: number; leftToBillCents: number }[]
  }
  recentMatters: {
    id: string
    reference: string
    name: string
    clientName: string | null
    lastActivityType: ActivityType | null
    lastActivitySummary: string | null
    lastActivityAt: string | null
  }[]
}

const tiers: DeadlineUrgency[] = ['Overdue', 'Today', 'ThisWeek', 'Later']

/**
 * What she sees on opening the application: what falls due, what has been earned and not billed, and
 * where she left off. Three things, and no vanity charts.
 */
export function Home({ onOpenMatter }: { onOpenMatter: (id: string) => void }) {
  const [data, setData] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api<DashboardSummary>('/api/dashboard')
      .then(setData)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [])

  if (error) return <Panel><p className="p-4 text-danger">{error}</p></Panel>
  if (!data) return <Panel />

  const today = new Date(data.today)
  const shown = data.unbilled.matters.slice(0, 4)
  const rest = data.unbilled.matters.slice(4)

  return (
    <Panel>
      <PageHeader
        title={capitalise(
          today.toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' }),
        )}
        meta={
          <span>
            {data.withinSevenDaysCount} échéance{data.withinSevenDaysCount > 1 ? 's' : ''} dans les 7
            jours · {data.openMatterCount} dossier{data.openMatterCount > 1 ? 's' : ''} en cours
          </span>
        }
      />

      <div className="grid flex-1 grid-cols-[minmax(0,1fr)_320px] gap-4 overflow-y-auto px-4 pt-3 pb-5">
        <section className="grid content-start">
          <SectionHead>Échéances des 30 prochains jours</SectionHead>

          {data.deadlines.length === 0 && (
            <p className="m-0 text-[11px] text-muted">Rien à surveiller dans les trente prochains jours.</p>
          )}

          {tiers.map((tier) => {
            const group = data.deadlines.filter((deadline) => deadline.urgency === tier)
            if (group.length === 0) return null

            return (
              <div key={tier}>
                <TierCaption urgency={tier} count={group.length} />

                {group.map((deadline) => (
                  <button
                    key={deadline.id}
                    type="button"
                    onClick={() => onOpenMatter(deadline.matterId)}
                    className={cn(
                      'flex w-full items-center gap-2.5 border-t border-line-subtle border-l-[3px] px-2 py-1.5 text-left hover:bg-hover',
                      tierBorder[tier],
                    )}
                  >
                    <span className="grid min-w-0 flex-1">
                      <span className="truncate text-[12.5px]">{deadline.label}</span>
                      {/* Never the label alone: a deadline without its dossier is unusable. */}
                      <span className="truncate text-[11px] text-muted">
                        {deadline.matterReference} · {deadline.matterName}
                        {deadline.clientName && ` · ${deadline.clientName}`}
                      </span>
                    </span>

                    <span className="shrink-0 font-mono text-[11px] whitespace-nowrap text-ink-secondary tnum">
                      {distance(deadline.date, deadline.time)}
                    </span>
                  </button>
                ))}
              </div>
            )
          })}

          {data.nextDeadlineBeyondHorizon && (
            <p className="mt-3 mb-0 text-[11px] text-muted">
              Aucune autre échéance avant le{' '}
              {new Date(data.nextDeadlineBeyondHorizon).toLocaleDateString('fr-FR')}.
            </p>
          )}
        </section>

        <div className="grid content-start gap-4">
          {/* The only large number in the application: money earned and not yet asked for. */}
          <section className="rounded-lg border border-accent bg-accent-subtle px-3 py-2.5 text-warning">
            <SectionHead className="pt-0">Temps saisi non facturé</SectionHead>

            <div className="font-mono text-[26px] leading-8 font-semibold tracking-[-0.02em] tnum">
              {formatEuros(data.unbilled.totalCents)}
            </div>

            <div className="font-mono text-[11px] tnum">
              {formatDuration(data.unbilled.totalBillableMinutes)} facturables sur{' '}
              {data.unbilled.matterCount} dossier{data.unbilled.matterCount > 1 ? 's' : ''}
            </div>

            {data.unbilled.agedOverSixtyDaysCents > 0 && (
              <div className="font-mono text-[11px] font-medium tnum">
                dont {formatEuros(data.unbilled.agedOverSixtyDaysCents)} de plus de 60 jours
              </div>
            )}

            <div className="mt-2 grid rounded-md bg-panel px-1.5 py-1 text-ink">
              {shown.map((matter) => (
                <button
                  key={matter.matterId}
                  type="button"
                  onClick={() => onOpenMatter(matter.matterId)}
                  className="flex items-center gap-2 rounded-sm px-1 py-1 text-left text-[11.5px] hover:bg-hover"
                >
                  <span className="min-w-0 flex-1 truncate">{matter.matterName}</span>
                  <span className="font-mono text-[11px] text-muted tnum">
                    {formatDuration(matter.billableMinutes)}
                  </span>
                  <span className="w-[76px] text-right font-mono tnum">
                    {formatEuros(matter.leftToBillCents)}
                  </span>
                </button>
              ))}

              {rest.length > 0 && (
                <div className="flex items-center gap-2 px-1 py-1 text-[11.5px] text-muted">
                  <span className="min-w-0 flex-1 truncate">{rest.length} autres dossiers</span>
                  <span className="font-mono text-[11px] tnum">
                    {formatDuration(rest.reduce((total, matter) => total + matter.billableMinutes, 0))}
                  </span>
                  <span className="w-[76px] text-right font-mono tnum">
                    {formatEuros(rest.reduce((total, matter) => total + matter.leftToBillCents, 0))}
                  </span>
                </div>
              )}
            </div>
          </section>

          <section>
            <SectionHead className="pt-0">Dossiers récemment touchés</SectionHead>

            <div className="grid">
              {data.recentMatters.map((matter) => (
                <button
                  key={matter.id}
                  type="button"
                  onClick={() => onOpenMatter(matter.id)}
                  className="flex items-center gap-2.5 border-t border-line-subtle px-1 py-1.5 text-left hover:bg-hover"
                >
                  <span className="grid min-w-0 flex-1">
                    <span className="truncate text-[12px]">{matter.name}</span>
                    {/* The dossier name alone does not tell her where she was. */}
                    <span className="truncate text-[11px] text-muted">
                      {matter.lastActivityType && `${activityLabels[matter.lastActivityType]} · `}
                      {matter.lastActivitySummary}
                    </span>
                  </span>

                  <span className="shrink-0 font-mono text-[11px] whitespace-nowrap text-muted tnum">
                    {formatRelative(matter.lastActivityAt)}
                  </span>
                </button>
              ))}

              {data.recentMatters.length === 0 && (
                <p className="m-0 text-[11px] text-muted">Aucun dossier ouvert pour l’instant.</p>
              )}
            </div>
          </section>
        </div>
      </div>
    </Panel>
  )
}

const SectionHead = ({ className, children }: { className?: string; children: React.ReactNode }) => (
  <h3 className={cn('m-0 pt-0.5 pb-1.5 text-[12px] font-semibold', className)}>{children}</h3>
)

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}
