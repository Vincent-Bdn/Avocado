import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from './api.js'
import { Journal } from './Journal.js'
import { Billing } from './tabs/Billing.js'
import { Deadlines } from './tabs/Deadlines.js'
import { Documents } from './tabs/Documents.js'
import { TimeEntries } from './tabs/TimeEntries.js'
import { formatEuros, urgencyLabels } from './labels.js'
import type { MatterDetail } from './types.js'

/** The fiche dossier: header, journal, and the context panel's échéances and « à facturer ». */
type Tab = 'journal' | 'documents' | 'deadlines' | 'time' | 'billing'

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

  return (
    <div className="content">
      <header className="matter-header">
        <div className="line1">
          <span className="mono reference">{matter.reference}</span>
          <h2>{matter.name}</h2>
          <span className={`badge ${matter.isOpen ? 'badge-open' : 'badge-closed'}`}>
            {matter.isOpen ? '● En cours' : '✓ Clôturé'}
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
          <span className="mono">{formatEuros(matter.hourlyRateCents)}/h</span>
        </div>

        <div className="matter-actions">
          <button
            type="button"
            className="secondary-button"
            onClick={() => {
              void post(`/api/matters/${matterId}/${matter.isOpen ? 'close' : 'reopen'}`, {}).then(() => {
                reload()
                onChanged()
              })
            }}
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

        <aside className="context">
          <section>
            <h3>Échéances</h3>
            {matter.deadlines.length === 0 && <p className="muted">Aucune échéance.</p>}
            {matter.deadlines.map((deadline) => (
              <div key={deadline.id} className={`deadline urgency-${deadline.urgency.toLowerCase()}`}>
                <div>{deadline.label}</div>
                <div className="mono muted">
                  {new Date(deadline.date).toLocaleDateString('fr-FR')} · {urgencyLabels[deadline.urgency]}
                </div>
              </div>
            ))}
          </section>

          <section>
            <h3>À facturer</h3>
            <div className="amount mono">{formatEuros(matter.billing.leftToBillCents)}</div>
            <div className="mono muted">
              {Math.floor(matter.billing.billableMinutes / 60)} h{' '}
              {String(matter.billing.billableMinutes % 60).padStart(2, '0')} facturables
            </div>
          </section>

          <section>
            <h3>Parties</h3>
            {matter.parties.map((party) => (
              <div key={party.id} className="party">
                <span
                  className={`avatar ${party.contactType === 'Individual' ? 'avatar-round' : ''} ${
                    party.isClient ? 'avatar-client' : ''
                  }`}
                >
                  {party.displayName.slice(0, 2).toUpperCase()}
                </span>
                <span>
                  <div>{party.displayName}</div>
                  <div className="muted" title={party.role ?? undefined}>{party.role}</div>
                </span>
              </div>
            ))}
          </section>
        </aside>
      </div>
    </div>
  )
}
