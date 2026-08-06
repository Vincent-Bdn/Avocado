import { useEffect, useState, type ReactNode } from 'react'
import { BookOpen, FolderPlus, HardDriveDownload, Search, Timer } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Button, Kbd } from '../components/ui/button.js'
import { PageHeader } from '../components/ui/page-header.js'
import { Panel } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { TierCaption, distance, tierRow } from '../lib/urgency.js'
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
export function Home({ onOpenMatter, onNewMatter, onNewContact, onSearch }: {
  onOpenMatter: (id: string) => void
  onNewMatter: () => void
  onNewContact: () => void
  onSearch: () => void
}) {
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
  const empty = data.openMatterCount === 0 && data.recentMatters.length === 0

  const actions = (
    <>
      <Button variant="secondary" onClick={onSearch}>
        <Search size={16} strokeWidth={1.75} />
        Rechercher
        <Kbd on="secondary">⌘K</Kbd>
      </Button>

      <Button onClick={onNewMatter}>
        Nouveau dossier
        <Kbd>⌘N</Kbd>
      </Button>
    </>
  )

  // First run says what the state of things is, not what today's date is: an empty diary has no
  // « 4 échéances dans les 7 jours » to report.
  if (empty) {
    return (
      <Panel>
        <PageHeader
          title="Bonjour"
          meta={<span>Le coffre est créé et chiffré. Il ne contient encore rien.</span>}
          actions={actions}
        />

        <FirstRun onNewMatter={onNewMatter} onNewContact={onNewContact} />
      </Panel>
    )
  }

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
        actions={actions}
      />

      {/* 1.32fr / 1fr: the deadlines column has to hold a label, its dossier and a distance without
          truncating all three, and the right column is a stack of two summaries. */}
      <div className="grid flex-1 grid-cols-[minmax(0,1.32fr)_minmax(0,1fr)] gap-4 overflow-y-auto px-5 py-4">
        <section className="grid content-start">
          <SectionTitle count={data.deadlines.length}>Échéances des 30 prochains jours</SectionTitle>

          {data.deadlines.length === 0 && (
            <p className="m-0 type-caption text-muted">
              Rien à surveiller dans les trente prochains jours.
            </p>
          )}

          {tiers.map((tier) => {
            const group = data.deadlines.filter((deadline) => deadline.urgency === tier)
            if (group.length === 0) return null

            return (
              <div key={tier}>
                <TierCaption urgency={tier} count={group.length} />

                <div className="grid gap-1.5">
                  {group.map((deadline) => (
                    <button
                      key={deadline.id}
                      type="button"
                      onClick={() => onOpenMatter(deadline.matterId)}
                      className={cn(
                        'flex w-full items-center gap-2.5 rounded-sm border border-l-[3px] px-[9px] py-[7px] text-left',
                        tierRow[tier],
                      )}
                    >
                      <span className="grid min-w-0 flex-1">
                        <span className="truncate text-[12.5px] leading-[17px] font-medium">
                          {deadline.label}
                        </span>
                        {/* Never the label alone: a deadline without its dossier is unusable. */}
                        <span className="truncate text-[11px] leading-[15px] opacity-80">
                          {deadline.matterReference} · {deadline.matterName}
                          {deadline.clientName && ` · ${deadline.clientName}`}
                        </span>
                      </span>

                      <span className="shrink-0 font-mono text-[11px] whitespace-nowrap tnum">
                        {distance(deadline.date, deadline.time)}
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            )
          })}

          {/* A closing line rather than a truncation counter: it answers the question the list raises. */}
          {data.nextDeadlineBeyondHorizon && (
            <p className="mt-3 mb-0 type-caption text-muted">
              Aucune autre échéance avant le{' '}
              {new Date(data.nextDeadlineBeyondHorizon).toLocaleDateString('fr-FR')}.
            </p>
          )}
        </section>

        <div className="grid content-start gap-4">
          <Unbilled data={data} onOpenMatter={onOpenMatter} />

          <section>
            <SectionTitle action="⌘K">Dossiers récemment touchés</SectionTitle>

            <div className="overflow-hidden rounded-md border border-line-subtle">
              {data.recentMatters.map((matter, index) => (
                <button
                  key={matter.id}
                  type="button"
                  onClick={() => onOpenMatter(matter.id)}
                  className={cn(
                    'flex h-[38px] w-full items-center gap-2.5 px-2.5 text-left hover:bg-hover',
                    index > 0 && 'border-t border-line-subtle',
                    index === 0 && 'bg-[#F8F9F6]',
                  )}
                >
                  <span className="grid min-w-0 flex-1">
                    <span
                      className={cn('truncate text-[12.5px] leading-4', index === 0 && 'font-medium')}
                    >
                      {matter.name}
                    </span>
                    {/* The dossier name alone does not tell her where she was. */}
                    <span className="truncate text-[11px] leading-[15px] text-muted">
                      {matter.lastActivityType && `${activityLabels[matter.lastActivityType]} · `}
                      {matter.lastActivitySummary}
                    </span>
                  </span>

                  <span className="shrink-0 font-mono text-[10.5px] whitespace-nowrap text-muted tnum">
                    {formatRelative(matter.lastActivityAt)}
                  </span>
                </button>
              ))}

              {data.recentMatters.length === 0 && (
                <p className="m-0 px-2.5 py-3 type-caption text-muted">
                  Aucun dossier ouvert pour l’instant.
                </p>
              )}
            </div>
          </section>
        </div>
      </div>
    </Panel>
  )
}

/**
 * The only large number in the application: money already earned and not yet asked for, and the most
 * forgotten thing in a solo practice. The per-dossier breakdown sits right there rather than behind a
 * click, and the rows have to foot to the headline.
 */
function Unbilled({ data, onOpenMatter }: {
  data: DashboardSummary
  onOpenMatter: (id: string) => void
}) {
  const shown = data.unbilled.matters.slice(0, 4)
  const rest = data.unbilled.matters.slice(4)

  return (
    <section className="overflow-hidden rounded-md border border-[#E8D5AE] bg-[#FDF8ED]">
      <div className="px-3.5 pt-3 pb-2.5 text-[#6E4A0E]">
        <SectionTitle className="text-[#6E4A0E]">Temps saisi non facturé</SectionTitle>

        <div className="font-mono text-[28px] leading-[34px] font-semibold tracking-[-0.02em] tnum">
          {formatEuros(data.unbilled.totalCents)}
        </div>

        <div className="mt-0.5 flex items-baseline gap-2">
          <span className="font-mono text-[11px] tnum">
            {formatDuration(data.unbilled.totalBillableMinutes)} facturables sur{' '}
            {data.unbilled.matterCount} dossier{data.unbilled.matterCount > 1 ? 's' : ''}
          </span>

          {data.unbilled.agedOverSixtyDaysCents > 0 && (
            <span className="ml-auto text-right font-mono text-[10.5px] font-medium tnum">
              dont {formatEuros(data.unbilled.agedOverSixtyDaysCents)}
              <br />
              de plus de 60 jours
            </span>
          )}
        </div>
      </div>

      <div className="bg-[#FFFDF8]">
        {shown.map((matter) => (
          <button
            key={matter.matterId}
            type="button"
            onClick={() => onOpenMatter(matter.matterId)}
            className="grid h-7 w-full grid-cols-[minmax(0,1fr)_66px_92px] items-center gap-2 border-t border-[#F2E7D2] px-3.5 text-left hover:bg-[#FBF4E6]"
          >
            <span className="truncate text-[11.5px]">{matter.matterName}</span>
            <span className="text-right font-mono text-[11px] text-ink-secondary tnum">
              {formatDuration(matter.billableMinutes)}
            </span>
            <span className="text-right font-mono text-[11.5px] font-medium tnum">
              {formatEuros(matter.leftToBillCents)}
            </span>
          </button>
        ))}

        {rest.length > 0 && (
          <div className="grid h-7 grid-cols-[minmax(0,1fr)_66px_92px] items-center gap-2 border-t border-[#F2E7D2] px-3.5 text-muted">
            <span className="truncate text-[11.5px]">{rest.length} autres dossiers</span>
            <span className="text-right font-mono text-[11px] tnum">
              {formatDuration(rest.reduce((total, matter) => total + matter.billableMinutes, 0))}
            </span>
            <span className="text-right font-mono text-[11.5px] tnum">
              {formatEuros(rest.reduce((total, matter) => total + matter.leftToBillCents, 0))}
            </span>
          </div>
        )}
      </div>
    </section>
  )
}

/**
 * First run. Teach, do not apologise: the centred block says what a dossier is for and offers the two
 * ways in, and the three cards below name the habits that make the application worth having.
 */
function FirstRun({ onNewMatter, onNewContact }: {
  onNewMatter: () => void
  onNewContact: () => void
}) {
  return (
    <div className="flex-1 overflow-y-auto px-5 py-8">
      <div className="mx-auto grid max-w-[620px] justify-items-center gap-3 text-center">
        <span className="grid h-10 w-10 place-items-center rounded-md border border-line-subtle bg-app text-ink-secondary">
          <FolderPlus size={18} strokeWidth={1.8} />
        </span>

        <h2 className="type-title-lg m-0">Votre premier dossier</h2>

        <p className="m-0 max-w-[52ch] text-ink-secondary">
          Un dossier réunit son client, le journal de tout ce qui s’y passe, ses documents et ses
          pièces, ses échéances, et le temps que vous y consacrez. Tout le reste de l’application en
          découle.
        </p>

        <div className="mt-1 flex flex-wrap justify-center gap-2">
          <Button size="lg" onClick={onNewMatter}>
            Créer un dossier
            <Kbd>⌘N</Kbd>
          </Button>

          <Button variant="secondary" size="lg" onClick={onNewContact}>
            Ajouter un tiers d’abord
          </Button>
        </div>
      </div>

      <div className="mx-auto mt-8 grid max-w-[860px] gap-3 sm:grid-cols-3">
        <TeachingCard icon={<BookOpen size={14} strokeWidth={2} />} title="Le journal">
          Deux lignes après chaque appel. C’est le geste qui fait vivre l’application, et il vaut
          mieux qu’il soit noté à chaud.
        </TeachingCard>

        <TeachingCard icon={<Timer size={14} strokeWidth={2} />} title="Le temps passé">
          Attaché à l’entrée de journal, pas saisi à part. Ce que vous ne notez pas maintenant ne se
          facturera jamais.
        </TeachingCard>

        <TeachingCard icon={<HardDriveDownload size={14} strokeWidth={2} />} title="La sauvegarde">
          Branchez votre clé une fois par semaine. Une sauvegarde est un fichier fermé, que la
          synchronisation copie sans risque.
        </TeachingCard>
      </div>
    </div>
  )
}

const TeachingCard = ({ icon, title, children }: {
  icon: ReactNode
  title: string
  children: ReactNode
}) => (
  <article className="grid content-start gap-1.5 rounded-md border border-line-subtle bg-[#F8F9F6] px-3.5 py-3">
    <span className="flex items-center gap-1.5 text-[12px] font-medium">
      <span className="text-brand">{icon}</span>
      {title}
    </span>
    <p className="m-0 text-[11.5px] leading-[17px] text-ink-secondary">{children}</p>
  </article>
)

/** The `title` step, an optional mono count, and an optional right-aligned hint. */
function SectionTitle({ count, action, className, children }: {
  count?: number
  action?: string
  className?: string
  children: ReactNode
}) {
  return (
    <h3 className={cn('type-title m-0 flex items-baseline gap-2 pb-2', className)}>
      {children}
      {count !== undefined && <span className="font-mono text-[11px] text-muted tnum">{count}</span>}
      {action && <span className="ml-auto font-mono text-[10.5px] text-muted">{action}</span>}
    </h3>
  )
}

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}
