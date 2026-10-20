import { useCallback, useEffect, useState } from 'react'
import { Check, Merge, Star } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Journal } from './Journal.js'
import { MatterForm } from './MatterForm.js'
import { MergeMatter } from './MergeMatter.js'
import { Billing } from './tabs/Billing.js'
import { Deadlines } from './tabs/Deadlines.js'
import { DossierFiles } from './tabs/DossierFiles.js'
import { Overview } from './tabs/Overview.js'
import { Parties } from './tabs/Parties.js'
import { TimeEntries } from './tabs/TimeEntries.js'
import { Avatar } from './components/ui/avatar.js'
import { Badge, NumberPill } from './components/ui/badge.js'
import { Button } from './components/ui/button.js'
import { Panel } from './components/ui/panel.js'
import { BillingStrip } from './lib/billing.js'
import { cn } from './lib/utils.js'
import { TierBullet, distance, tierBorder } from './lib/urgency.js'
import { formatEuros } from './labels.js'
import type { MatterDetail } from './types.js'

type Tab = 'overview' | 'parties' | 'journal' | 'documents' | 'deadlines' | 'time' | 'billing'

/** The fiche dossier: header 52, tab bar 32 sticky, body, and the 208px context panel. */
export function MatterView({ matterId, onChanged, onOpenMatter }: {
  matterId: string
  onChanged: () => void
  /** Where to go once this dossier has been merged away: the one it became. */
  onOpenMatter?: (id: string) => void
}) {
  const [matter, setMatter] = useState<MatterDetail | null>(null)
  /**
   * Aperçu, not the journal.
   *
   * <p>The journal is forty rows of what happened, newest first, and it is the right screen for
   * working and the wrong one for arriving: it opened before she had been told which dossier she was
   * looking at.</p>
   */
  const [tab, setTab] = useState<Tab>('overview')
  const [editing, setEditing] = useState(false)
  const [merging, setMerging] = useState(false)
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
  async function toggleFavourite() {
    if (!matter) return

    try {
      await api(`/api/matters/${matterId}/favourite`, {
        method: 'PUT',
        body: JSON.stringify({ isFavourite: !matter.isFavourite }),
      })

      refreshAll()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  async function toggleClosed() {
    if (!matter) return

    try {
      await post(`/api/matters/${matterId}/${matter.isOpen ? 'close' : 'reopen'}`, {})
      refreshAll()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  const tabs: [Tab, string, number | null][] = [
    ['overview', 'Aperçu', null],
    ['parties', 'Parties', matter.parties.length],
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
          <h2 className="type-title-lg m-0 truncate">{matter.name}</h2>

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
          {client && (
            <>
              <Avatar name={client.displayName} type={client.contactType} client size={16} />
              <span className="truncate">{client.displayName}</span>
            </>
          )}

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

        <div className="absolute top-3 right-4 flex gap-2">
          {/* One click, its own endpoint: pinning must not be a read-modify-write of the whole
              dossier, or a star could fail because the n° RG is somewhere else. */}
          <Button
            variant="secondary"
            size="icon"
            aria-pressed={matter.isFavourite}
            title={matter.isFavourite ? 'Retirer des favoris' : 'Mettre en favori'}
            onClick={() => void toggleFavourite()}
            className={matter.isFavourite ? 'border-accent text-accent' : undefined}
          >
            <Star
              size={14}
              strokeWidth={2}
              fill={matter.isFavourite ? 'currentColor' : 'none'}
            />
          </Button>

          {/* Two dossiers that are the same affair: « peut-on merger sur FINANCIERE CN (NIETO) ? »
              Beside Modifier rather than in the facturation tab, because it is a correction of the
              dossier itself. Definitive, so everything it does is counted before she confirms.
              Left of Modifier, and never to its right: the rightmost buttons are the ones a hand
              reaches for without reading, and this one deletes a dossier. */}
          <Button
            variant="secondary"
            size="icon"
            title="Fusionner ce dossier dans un autre"
            onClick={() => setMerging(true)}
          >
            <Merge size={14} strokeWidth={2} />
          </Button>

          <Button variant="secondary" onClick={() => setEditing(true)}>Modifier</Button>

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
              <NumberPill tight className="bg-sunken text-ink-secondary">{count}</NumberPill>
            )}
          </button>
        ))}
      </nav>

      {/* The context panel is the same three blocks as the aperçu, so it steps aside there rather
          than showing every figure twice on one screen. */}
      <div
        className={cn(
          'grid flex-1 overflow-hidden',
          tab === 'overview' || tab === 'parties'
            ? 'grid-cols-[minmax(0,1fr)]'
            : 'grid-cols-[minmax(0,1fr)_208px]',
        )}
      >
        {tab === 'overview' && (
          <Overview
            matter={matter}
            onOpen={setTab}
            onEdit={() => setEditing(true)}
            onChanged={refreshAll}
          />
        )}
        {tab === 'parties' && (
          <Parties matter={matter} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'journal' && (
          <Journal matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'documents' && (
          <DossierFiles
            matterId={matterId}
            folder={matter.documentsFolder}
            isOpen={matter.isOpen}
            onEdit={() => setEditing(true)}
            onChanged={refreshAll}
          />
        )}
        {tab === 'deadlines' && (
          <Deadlines matterId={matterId} isOpen={matter.isOpen} onChanged={refreshAll} />
        )}
        {tab === 'time' && (
          <TimeEntries
            matterId={matterId}
            hourlyRateCents={matter.hourlyRateCents}
            isOpen={matter.isOpen}
            onChanged={refreshAll}
          />
        )}
        {tab === 'billing' && (
          <Billing
            matterId={matterId}
            folder={matter.documentsFolder}
            isOpen={matter.isOpen}
            onChanged={refreshAll}
          />
        )}

        {/* The aperçu shows all of this with room to breathe, and the onglet Parties owns the parties,
            so the panel would be the same figures twice on both. */}
        {tab !== 'overview' && tab !== 'parties' && (
          <ContextPanel matter={matter} />
        )}
      </div>

      {editing && (
        <MatterForm
          matter={matter}
          onCancel={() => setEditing(false)}
          onSaved={() => { setEditing(false); refreshAll() }}
        />
      )}

      {merging && (
        <MergeMatter
          matterId={matterId}
          matterName={matter.name}
          onCancel={() => setMerging(false)}
          onMerged={(targetId) => {
            setMerging(false)
            // This dossier no longer exists, so staying on it would read a 404. The window follows
            // the history to where it went.
            onChanged()
            onOpenMatter?.(targetId)
          }}
        />
      )}
    </Panel>
  )
}

const Divider = () => <span className="h-2.5 w-px shrink-0 bg-line" />

/**
 * 208px: échéances and à facturer. Two blocks separated by a rule.
 *
 * <p>Parties used to be the third, and it was cut: they have had an onglet of their own since real
 * lawyers said the dossier was hard to work with, and repeating them in a 208px column meant the same
 * names twice on screen, truncated in one of the two places. A column this narrow earns its keep by
 * showing what is not already elsewhere.</p>
 */
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
              'mb-1.5 rounded-sm border border-line-subtle border-l-[3px] px-2 py-1.5',
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

      <BillingStrip summary={matter.billing} hourlyRateCents={matter.hourlyRateCents} />

    </aside>
  )
}

const ContextTitle = ({ className, children }: { className?: string; children: string }) => (
  <h3 className={cn('type-group m-0 mb-1.5 font-normal text-muted', className)}>{children}</h3>
)
