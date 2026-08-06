import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { urgencyLabels } from '../labels.js'
import type { DeadlineUrgency } from '../types.js'

interface MatterDeadline {
  id: string
  matterId: string
  matterReference: string
  matterName: string
  date: string
  time: string | null
  type: string
  label: string
  isDone: boolean
  urgency: DeadlineUrgency
}

const tiers: DeadlineUrgency[] = ['Overdue', 'Today', 'ThisWeek', 'Later']

/** Every open échéance across the practice, grouped by tier. A triage screen, so scan order matters. */
export function UpcomingDeadlines({ onOpenMatter }: { onOpenMatter: (id: string) => void }) {
  const [items, setItems] = useState<MatterDeadline[]>([])
  const [error, setError] = useState<string | null>(null)

  const reload = () => {
    api<MatterDeadline[]>('/api/deadlines')
      .then(setItems)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }

  useEffect(reload, [])

  async function markDone(deadline: MatterDeadline) {
    await api(`/api/deadlines/${deadline.id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...deadline, isDone: true }),
    })

    reload()
  }

  return (
    <div className="content">
      <header className="matter-header">
        <div className="line1">
          <h2>Échéances</h2>
        </div>
        <div className="line2">
          {items.length} échéance{items.length > 1 ? 's' : ''} à surveiller, tous dossiers confondus
        </div>
      </header>

      <div className="tab-panel">
        {error && <p className="danger">{error}</p>}

        {items.length === 0 && (
          <div className="empty">
            <h3>Rien à surveiller</h3>
            <p className="muted">
              Les audiences et les délais que vous notez dans un dossier apparaissent ici, du plus
              urgent au plus lointain.
            </p>
          </div>
        )}

        {tiers.map((tier) => {
          const group = items.filter((item) => item.urgency === tier)
          if (group.length === 0) return null

          return (
            <div key={tier}>
              <div className="tier-caption mono">
                <span className={`tier tier-${tier.toLowerCase()}`} />
                {urgencyLabels[tier]} · {group.length}
              </div>

              {group.map((deadline) => (
                <div key={deadline.id} className={`home-deadline urgency-${tier.toLowerCase()}`}>
                  <button
                    type="button"
                    className="home-deadline-text as-link"
                    onClick={() => onOpenMatter(deadline.matterId)}
                  >
                    <span className="deadline-label">{deadline.label}</span>
                    <span className="muted micro">
                      {deadline.matterReference} · {deadline.matterName}
                    </span>
                  </button>

                  <span className="mono micro nowrap">
                    {new Date(deadline.date).toLocaleDateString('fr-FR')}
                    {deadline.time && ` · ${deadline.time.slice(0, 5)}`}
                  </span>

                  <button
                    type="button"
                    className="ghost-button"
                    title="Marquer comme faite"
                    onClick={() => void markDone(deadline)}
                  >
                    <Check size={13} strokeWidth={2} />
                  </button>
                </div>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}
