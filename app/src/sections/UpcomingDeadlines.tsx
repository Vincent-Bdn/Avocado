import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { PageHeader } from '../components/ui/page-header.js'
import { Panel } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { TierCaption, tierBorder } from '../lib/urgency.js'
import { RowAction, TabPanel } from '../tabs/shared.js'
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
    <Panel>
      <PageHeader
        title="Échéances"
        meta={
          <span>
            {items.length} échéance{items.length > 1 ? 's' : ''} à surveiller, tous dossiers confondus
          </span>
        }
      />

      <TabPanel className="flex-1">
        {error && <p className="m-0 text-danger">{error}</p>}

        {items.length === 0 && (
          <EmptyState title="Rien à surveiller">
            Les audiences et les délais que vous notez dans un dossier apparaissent ici, du plus
            urgent au plus lointain.
          </EmptyState>
        )}

        {tiers.map((tier) => {
          const group = items.filter((item) => item.urgency === tier)
          if (group.length === 0) return null

          return (
            <div key={tier}>
              <TierCaption urgency={tier} count={group.length} />

              {group.map((deadline) => (
                <div
                  key={deadline.id}
                  className={cn(
                    'group flex items-center gap-2.5 border-t border-line-subtle border-l-[3px] px-2 py-1.5',
                    tierBorder[tier],
                  )}
                >
                  <button
                    type="button"
                    onClick={() => onOpenMatter(deadline.matterId)}
                    className="grid min-w-0 flex-1 text-left"
                  >
                    <span className="truncate text-[12.5px]">{deadline.label}</span>
                    <span className="truncate text-[11px] text-muted">
                      {deadline.matterReference} · {deadline.matterName}
                    </span>
                  </button>

                  <span className="shrink-0 font-mono text-[11px] whitespace-nowrap text-ink-secondary tnum">
                    {new Date(deadline.date).toLocaleDateString('fr-FR')}
                    {deadline.time && ` · ${deadline.time.slice(0, 5)}`}
                  </span>

                  {/* The action appears on hover so the list stays a list, not a wall of buttons. */}
                  <span className="opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                    <RowAction label="Marquer comme faite" onClick={() => void markDone(deadline)}>
                      <Check size={13} strokeWidth={2} />
                    </RowAction>
                  </span>
                </div>
              ))}
            </div>
          )
        })}
      </TabPanel>
    </Panel>
  )
}
