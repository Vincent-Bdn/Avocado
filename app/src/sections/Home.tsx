import { useEffect, useState } from 'react'
import { ApiError, api } from '../api.js'
import { activityLabels, formatDuration, formatEuros, formatRelative, urgencyLabels } from '../labels.js'
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

  if (error) return <div className="content"><p className="danger">{error}</p></div>
  if (!data) return <div className="content" />

  const today = new Date(data.today)
  const shown = data.unbilled.matters.slice(0, 4)
  const rest = data.unbilled.matters.slice(4)

  return (
    <div className="content">
      <header className="matter-header">
        <div className="line1">
          <h2>{capitalise(today.toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' }))}</h2>
        </div>
        <div className="line2">
          {data.withinSevenDaysCount} échéance{data.withinSevenDaysCount > 1 ? 's' : ''} dans les 7 jours ·{' '}
          {data.openMatterCount} dossier{data.openMatterCount > 1 ? 's' : ''} en cours
        </div>
      </header>

      <div className="home">
        <section className="home-deadlines">
          <h3 className="section-head">Échéances des 30 prochains jours</h3>

          {data.deadlines.length === 0 && (
            <p className="muted micro">Rien à surveiller dans les trente prochains jours.</p>
          )}

          {tiers.map((tier) => {
            const group = data.deadlines.filter((deadline) => deadline.urgency === tier)
            if (group.length === 0) return null

            return (
              <div key={tier}>
                <div className="tier-caption mono">
                  <span className={`tier tier-${tier.toLowerCase()}`} />
                  {urgencyLabels[tier]} · {group.length}
                </div>

                {group.map((deadline) => (
                  <button
                    key={deadline.id}
                    type="button"
                    className={`home-deadline urgency-${tier.toLowerCase()}`}
                    onClick={() => onOpenMatter(deadline.matterId)}
                  >
                    <span className="home-deadline-text">
                      <span className="deadline-label">{deadline.label}</span>
                      {/* Never the label alone: a deadline without its dossier is unusable. */}
                      <span className="muted micro">
                        {deadline.matterReference} · {deadline.matterName}
                        {deadline.clientName && ` — ${deadline.clientName}`}
                      </span>
                    </span>

                    <span className="mono micro nowrap">{distance(deadline.date, deadline.time)}</span>
                  </button>
                ))}
              </div>
            )
          })}

          {data.nextDeadlineBeyondHorizon && (
            <p className="muted micro">
              Aucune autre échéance avant le{' '}
              {new Date(data.nextDeadlineBeyondHorizon).toLocaleDateString('fr-FR')}.
            </p>
          )}
        </section>

        <div className="home-right">
          {/* The only large number in the application: money earned and not yet asked for. */}
          <section className="unbilled">
            <h3 className="section-head">Temps saisi non facturé</h3>

            <div className="unbilled-amount mono">{formatEuros(data.unbilled.totalCents)}</div>

            <div className="mono micro">
              {formatDuration(data.unbilled.totalBillableMinutes)} facturables sur{' '}
              {data.unbilled.matterCount} dossier{data.unbilled.matterCount > 1 ? 's' : ''}
            </div>

            {data.unbilled.agedOverSixtyDaysCents > 0 && (
              <div className="mono micro aged">
                dont {formatEuros(data.unbilled.agedOverSixtyDaysCents)} de plus de 60 jours
              </div>
            )}

            <div className="unbilled-rows">
              {shown.map((matter) => (
                <button
                  key={matter.matterId}
                  type="button"
                  className="unbilled-row"
                  onClick={() => onOpenMatter(matter.matterId)}
                >
                  <span className="row-main">{matter.matterName}</span>
                  <span className="mono">{formatDuration(matter.billableMinutes)}</span>
                  <span className="mono row-amount">{formatEuros(matter.leftToBillCents)}</span>
                </button>
              ))}

              {rest.length > 0 && (
                <div className="unbilled-row muted">
                  <span className="row-main">{rest.length} autres dossiers</span>
                  <span className="mono">
                    {formatDuration(rest.reduce((total, m) => total + m.billableMinutes, 0))}
                  </span>
                  <span className="mono row-amount">
                    {formatEuros(rest.reduce((total, m) => total + m.leftToBillCents, 0))}
                  </span>
                </div>
              )}
            </div>
          </section>

          <section>
            <h3 className="section-head">Dossiers récemment touchés</h3>

            <div className="recent">
              {data.recentMatters.map((matter, index) => (
                <button
                  key={matter.id}
                  type="button"
                  className={`recent-row ${index === 0 ? 'recent-first' : ''}`}
                  onClick={() => onOpenMatter(matter.id)}
                >
                  <span className="row-main">
                    <span className="recent-name">{matter.name}</span>
                    {/* The dossier name alone does not tell her where she was. */}
                    <span className="muted micro">
                      {matter.lastActivityType && `${activityLabels[matter.lastActivityType]} — `}
                      {matter.lastActivitySummary}
                    </span>
                  </span>

                  <span className="mono micro nowrap">{formatRelative(matter.lastActivityAt)}</span>
                </button>
              ))}

              {data.recentMatters.length === 0 && (
                <p className="muted micro">Aucun dossier ouvert pour l’instant.</p>
              )}
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

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
