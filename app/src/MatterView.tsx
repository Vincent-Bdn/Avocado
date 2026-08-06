import { useCallback, useEffect, useState } from 'react'
import { Check, Plus, X } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Journal } from './Journal.js'
import { MatterForm } from './MatterForm.js'
import { Billing } from './tabs/Billing.js'
import { Deadlines } from './tabs/Deadlines.js'
import { Documents } from './tabs/Documents.js'
import { TimeEntries } from './tabs/TimeEntries.js'
import { Avatar } from './components/ui/avatar.js'
import { Badge, NumberPill } from './components/ui/badge.js'
import { Button, Kbd } from './components/ui/button.js'
import { Dialog, DialogActions, Field } from './components/ui/dialog.js'
import { Input } from './components/ui/input.js'
import { Panel } from './components/ui/panel.js'
import { Select } from './components/ui/select.js'
import { NewContact } from './sections/NewContact.js'
import { RowAction } from './tabs/shared.js'
import { cn } from './lib/utils.js'
import { TierBullet, distance, tierBorder } from './lib/urgency.js'
import { formatDuration, formatEuros } from './labels.js'
import type { ContactSummary, MatterDetail } from './types.js'

type Tab = 'journal' | 'documents' | 'deadlines' | 'time' | 'billing'

/** The fiche dossier: header 52, tab bar 32 sticky, body, and the 208px context panel. */
export function MatterView({ matterId, onChanged }: { matterId: string; onChanged: () => void }) {
  const [matter, setMatter] = useState<MatterDetail | null>(null)
  const [tab, setTab] = useState<Tab>('journal')
  const [editing, setEditing] = useState(false)
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

    try {
      await post(`/api/matters/${matterId}/${matter.isOpen ? 'close' : 'reopen'}`, {})
      refreshAll()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
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
          <Button variant="secondary" onClick={() => setEditing(true)}>Modifier</Button>

          <Button
            variant={matter.isOpen ? 'secondary' : 'primary'}
            onClick={() => void toggleClosed()}
          >
            {matter.isOpen ? 'Clôturer' : 'Rouvrir le dossier'}
          </Button>

          {matter.isOpen && (
            <Button onClick={() => { setTab('journal'); focusComposer() }}>
              Entrée
              <Kbd>⌘J</Kbd>
            </Button>
          )}
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

        <ContextPanel matter={matter} onChanged={refreshAll} />
      </div>

      {editing && (
        <MatterForm
          matter={matter}
          onCancel={() => setEditing(false)}
          onSaved={() => { setEditing(false); refreshAll() }}
        />
      )}
    </Panel>
  )
}

const Divider = () => <span className="h-2.5 w-px shrink-0 bg-line" />

/**
 * The header button and ⌘J have to land in the same place. Rather than thread a ref through the tab
 * switch, the button replays the shortcut the composer already listens for.
 */
function focusComposer() {
  requestAnimationFrame(() =>
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j', ctrlKey: true })),
  )
}

/** 208px: échéances, à facturer, parties. Three blocks separated by rules. */
function ContextPanel({ matter, onChanged }: { matter: MatterDetail; onChanged: () => void }) {
  const [addingParty, setAddingParty] = useState(false)
  const [editingParty, setEditingParty] = useState<string | null>(null)

  async function removeParty(id: string) {
    await api(`/api/parties/${id}`, { method: 'DELETE' }).then(onChanged).catch(() => onChanged())
  }

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

      <section className="rounded-sm border border-[#E8D5AE] bg-[#FDF8ED] px-2.5 py-2 text-[#6E4A0E]">
        <ContextTitle className="text-[#6E4A0E]">À facturer</ContextTitle>

        <div className="font-mono text-[19px] leading-6 font-semibold tnum">
          {formatEuros(matter.billing.leftToBillCents)}
        </div>

        <div className="font-mono text-[10px] tnum">
          {formatDuration(matter.billing.billableMinutes)} facturables ·{' '}
          {formatEuros(matter.hourlyRateCents)}/h
        </div>

        {matter.billing.ledgerCents !== 0 && (
          <div className="font-mono text-[10px] tnum">
            {matter.billing.ledgerCents > 0 ? '− ' : '+ '}
            {formatEuros(Math.abs(matter.billing.ledgerCents))}{' '}
            {matter.billing.ledgerCents > 0 ? 'déjà reçu' : 'avancé'}
          </div>
        )}

        {/*
          Already invoiced is the other half of the answer and belongs on the same card, not in a
          footnote: what she is actually watching is the trésorerie, and « reste à facturer » alone
          says nothing about what has already gone out the door.
        */}
        {matter.billing.invoicedCents > 0 && (
          <div className="mt-2 border-t border-[#E8D5AE] pt-1.5">
            <div className="type-group text-[#6E4A0E] opacity-80">Déjà facturé</div>
            <div className="font-mono text-[15px] leading-5 font-semibold tnum">
              {formatEuros(matter.billing.invoicedCents)}
            </div>
            <div className="font-mono text-[10px] tnum opacity-80">
              soit {formatEuros(matter.billing.invoicedCents + matter.billing.leftToBillCents)} au total
              sur ce dossier
            </div>
          </div>
        )}
      </section>

      <section>
        <ContextTitle>Parties</ContextTitle>

        {matter.parties.map((party) =>
          editingParty === party.id ? (
            <PartyRole
              key={party.id}
              party={party}
              onCancel={() => setEditingParty(null)}
              onSaved={() => { setEditingParty(null); onChanged() }}
            />
          ) : (
            <div key={party.id} className="group/party mb-1.5 flex items-center gap-2">
              <Avatar name={party.displayName} type={party.contactType} client={party.isClient} />

              <span className="grid min-w-0 flex-1">
                <span className="truncate text-[11.5px]">{party.displayName}</span>

                {/*
                  The role is free text and often long, so it truncates with the full wording in the
                  title. It is also the only place it can be written, which is why the whole line is
                  a button: « aucun rôle » has to be as clickable as a role that is already there.
                */}
                <button
                  type="button"
                  title={party.role ?? 'Indiquer le rôle de cette partie'}
                  onClick={() => setEditingParty(party.id)}
                  className={cn(
                    'truncate text-left text-[10.5px] hover:underline',
                    party.role
                      ? party.isClient ? 'text-brand-on-subtle' : 'text-muted'
                      : 'text-disabled italic',
                  )}
                >
                  {party.role ?? 'indiquer le rôle…'}
                </button>
              </span>

              {!party.isClient && (
                <RowAction
                  label="Retirer du dossier"
                  danger
                  onClick={() => void removeParty(party.id)}
                >
                  <X size={12} strokeWidth={2} />
                </RowAction>
              )}
            </div>
          ),
        )}

        <button
          type="button"
          onClick={() => setAddingParty(true)}
          className="mt-1.5 flex h-6 items-center gap-1 rounded-[3px] border border-dashed border-line-strong px-2 text-[11px] text-ink-secondary hover:bg-hover"
        >
          <Plus size={11} strokeWidth={2} />
          Ajouter une partie
        </button>
      </section>

      {addingParty && (
        <AddParty
          matterId={matter.id}
          existing={matter.parties.map((party) => party.contactId)}
          onCancel={() => setAddingParty(false)}
          onAdded={() => { setAddingParty(false); onChanged() }}
        />
      )}
    </aside>
  )
}

/** Writing the role. Inline, because it is one line of free text and a dialog would be theatre. */
function PartyRole({ party, onCancel, onSaved }: {
  party: MatterDetail['parties'][number]
  onCancel: () => void
  onSaved: () => void
}) {
  const [role, setRole] = useState(party.role ?? '')
  const [busy, setBusy] = useState(false)

  async function save() {
    setBusy(true)

    try {
      await api(`/api/parties/${party.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          contactId: party.contactId,
          isClient: party.isClient,
          role: role.trim() || null,
        }),
      })

      onSaved()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mb-1.5 grid gap-1 rounded-sm border border-[var(--focus-ring)] p-1.5">
      <span className="truncate text-[11px] font-medium">{party.displayName}</span>

      <Input
        autoFocus
        inputSize="sm"
        value={role}
        placeholder="Partie adverse, expert judiciaire…"
        onChange={(event) => setRole(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') void save()
          if (event.key === 'Escape') onCancel()
        }}
      />

      <div className="flex gap-1">
        <Button size="sm" disabled={busy} onClick={() => void save()}>Enregistrer</Button>
        <Button variant="secondary" size="sm" onClick={onCancel}>Annuler</Button>
      </div>
    </div>
  )
}

/**
 * Attaching a tiers to the dossier. The role is free text and the field says so: « Expert judiciaire
 * désigné par ordonnance du 12/01/2026 » is a real role, and a dropdown of six options would send
 * that wording somewhere it cannot be read.
 */
function AddParty({ matterId, existing, onCancel, onAdded }: {
  matterId: string
  existing: string[]
  onCancel: () => void
  onAdded: () => void
}) {
  const [contacts, setContacts] = useState<ContactSummary[]>([])
  const [contactId, setContactId] = useState('')
  const [role, setRole] = useState('')
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

  useEffect(load, [load])

  async function add() {
    if (!contactId) {
      setError('Choisissez un tiers.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await post(`/api/matters/${matterId}/parties`, { contactId, isClient: false, role: role.trim() || null })
      onAdded()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (creating) {
    return (
      <NewContact
        onCancel={() => setCreating(false)}
        onCreated={(id) => { setCreating(false); load(); setContactId(id) }}
      />
    )
  }

  const available = contacts.filter((contact) => !existing.includes(contact.id))

  return (
    <Dialog title="Ajouter une partie" width={480} onClose={onCancel}>
      <Field label="Tiers">
        <div className="flex gap-2">
          <Select
            className="h-8 flex-1"
            value={contactId}
            onChange={(event) => { setContactId(event.target.value); setError(null) }}
          >
            <option value="">Choisir un tiers…</option>
            {available.map((contact) => (
              <option key={contact.id} value={contact.id}>{contact.displayName}</option>
            ))}
          </Select>

          <Button variant="secondary" size="lg" onClick={() => setCreating(true)}>Nouveau tiers…</Button>
        </div>
      </Field>

      <Field label="Rôle dans ce dossier">
        <Input
          inputSize="lg"
          value={role}
          placeholder="Avocat de la partie adverse au barreau de Villefranche"
          onChange={(event) => setRole(event.target.value)}
        />
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy} onClick={() => void add()}>Ajouter</Button>
      </DialogActions>
    </Dialog>
  )
}

const ContextTitle = ({ className, children }: { className?: string; children: string }) => (
  <h3 className={cn('type-group m-0 mb-1.5 font-normal text-muted', className)}>{children}</h3>
)
