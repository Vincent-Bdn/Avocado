import { useCallback, useEffect, useState } from 'react'
import { Check, Plus } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Journal } from './Journal.js'
import { Billing } from './tabs/Billing.js'
import { Deadlines } from './tabs/Deadlines.js'
import { Documents } from './tabs/Documents.js'
import { TimeEntries } from './tabs/TimeEntries.js'
import { formatDuration, formatEuros } from './labels.js'
import type { DeadlineUrgency, MatterDetail } from './types.js'

type Tab = 'journal' | 'documents' | 'deadlines' | 'time' | 'billing'

/** The fiche dossier: header, tabs, and the context panel holding échéances, à facturer and parties. */
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

  if (error) return <div className="content"><p className="danger">{error}</p></div>
  if (!matter) return <div className="content" />

  const client = matter.parties.find((party) => party.isClient)

  /**
   * Closing or reopening keeps the dossier on screen. It changes which list the dossier belongs to,
   * so the secondary panel has to be refreshed, but navigating away from what she was reading would
   * be the application deciding for her.
   */
  async function toggleClosed() {
    if (!matter) return

    await post(`/api/matters/${matterId}/${matter.isOpen ? 'close' : 'reopen'}`, {})
    refreshAll()
  }

  return (
    <div className="content">
      <header className="matter-header">
        <div className="line1">
          <span className="mono reference">{matter.reference}</span>
          <h2>{matter.name}</h2>
          <span className={`badge ${matter.isOpen ? 'badge-open' : 'badge-closed'}`}>
            {matter.isOpen ? (
              <span className="bullet-filled" />
            ) : (
              <Check size={11} strokeWidth={2.5} />
            )}
            {matter.isOpen ? 'En cours' : 'Clôturé'}
          </span>
        </div>

        <div className="line2">
          {client && <span>{client.displayName}</span>}
          {/* No dash placeholder when there is no RG: the segment is omitted entirely. */}
          {matter.courtCaseNumber && (
            <>
              <span className="divider" />
              <span className="mono">RG {matter.courtCaseNumber}</span>
            </>
          )}
          <span className="divider" />
          <span className="mono">
            ouvert le {new Date(matter.openedOn).toLocaleDateString('fr-FR')}
            {matter.closedOn && ` · clôturé le ${new Date(matter.closedOn).toLocaleDateString('fr-FR')}`}
            {' · '}
            {formatEuros(matter.hourlyRateCents)}/h
          </span>
        </div>

        <div className="matter-actions">
          <button
            type="button"
            className={matter.isOpen ? 'secondary-button' : ''}
            onClick={() => void toggleClosed()}
          >
            {matter.isOpen ? 'Clôturer' : 'Rouvrir le dossier'}
          </button>
        </div>
      </header>

      <nav className="tabs">
        {([
          ['journal', 'Journal', matter.counts.activities],
          ['documents', 'Documents', matter.counts.documents],
          ['deadlines', 'Échéances', matter.counts.openDeadlines],
          ['time', 'Temps passé', matter.counts.timeEntries],
          ['billing', 'Facturation', null],
        ] as [Tab, string, number | null][]).map(([id, title, count]) => (
          <button
            key={id}
            type="button"
            className={`tab ${tab === id ? 'tab-active' : ''}`}
            onClick={() => setTab(id)}
          >
            {title}
            {count !== null && <span className="count mono">{count}</span>}
          </button>
        ))}
      </nav>

      <div className="matter-body">
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
    </div>
  )
}

/** 208px: échéances, à facturer, parties. Three blocks separated by rules. */
function ContextPanel({ matter }: { matter: MatterDetail }) {
  const rate = matter.hourlyRateCents

  return (
    <aside className="context">
      <section>
        <h3>Échéances</h3>

        {matter.deadlines.length === 0 && <p className="muted micro">Aucune échéance.</p>}

        {matter.deadlines.map((deadline) => (
          <div key={deadline.id} className={`deadline urgency-${deadline.urgency.toLowerCase()}`}>
            <div className="deadline-label">{deadline.label}</div>
            <div className="mono micro deadline-when">
              <UrgencyBullet urgency={deadline.urgency} />
              {distance(deadline.date, deadline.time)}
            </div>
          </div>
        ))}
      </section>

      <section>
        <h3>À facturer</h3>
        <div className="context-amount mono">{formatEuros(matter.billing.leftToBillCents)}</div>

        <div className="mono micro muted">
          {formatDuration(matter.billing.billableMinutes)} facturables · {formatEuros(rate)}/h
        </div>

        {matter.billing.ledgerCents !== 0 && (
          <div className="mono micro muted">
            {matter.billing.ledgerCents > 0 ? '− ' : '+ '}
            {formatEuros(Math.abs(matter.billing.ledgerCents))}{' '}
            {matter.billing.ledgerCents > 0 ? 'déjà reçu' : 'avancé'}
          </div>
        )}

        {matter.billing.invoicedCents > 0 && (
          <div className="mono micro muted">
            − {formatEuros(matter.billing.invoicedCents)} déjà facturé
          </div>
        )}
      </section>

      <section className="context-parties">
        <h3>Parties</h3>

        {matter.parties.map((party) => (
          <div key={party.id} className="party">
            <span
              className={`avatar ${party.contactType === 'Individual' ? 'avatar-round' : ''} ${
                party.isClient ? 'avatar-client' : ''
              }`}
            >
              {initials(party.displayName)}
            </span>

            <span className="party-text">
              <span className="party-name">{party.displayName}</span>
              {/* Free text and often long: truncated, with the full wording in the title. */}
              <span
                className={`party-role ${party.isClient ? 'party-client' : ''}`}
                title={party.role ?? undefined}
              >
                {party.role}
              </span>
            </span>
          </div>
        ))}

        <button type="button" className="chip chip-dashed add-party" disabled title="À venir">
          <Plus size={11} strokeWidth={2} />
          Ajouter une partie
        </button>
      </section>
    </aside>
  )
}

/** Four tiers, four shapes: a black and white printout stays readable. */
function UrgencyBullet({ urgency }: { urgency: DeadlineUrgency }) {
  return <span className={`tier tier-${urgency.toLowerCase()}`} aria-hidden="true" />
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
