import { useCallback, useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions, Field } from '../components/ui/dialog.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { MetaDivider, PageHeader } from '../components/ui/page-header.js'
import { Panel, PanelHeader } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { initials } from '../lib/urgency.js'
import { activityLabels, formatDate } from '../labels.js'
import { Micro } from '../tabs/shared.js'
import type { ActivityType, ContactSummary, ContactType } from '../types.js'

interface ContactRole {
  matterId: string
  matterReference: string
  matterName: string
  matterIsOpen: boolean
  isClient: boolean
  role: string | null
}

interface ContactDetail {
  id: string
  type: ContactType
  displayName: string
  civility: string | null
  lastName: string | null
  firstName: string | null
  legalName: string | null
  siren: string | null
  legalForm: string | null
  email: string | null
  phone: string | null
  address: string | null
  notes: string | null
  matterCount: number
  clientMatterCount: number
  clientSince: string | null
  roles: ContactRole[]
  recentExchanges: {
    activityId: string
    matterId: string
    matterReference: string
    type: ActivityType
    occurredAt: string
    summary: string | null
  }[]
}

/** Tiers: the address book, and one contact's roles across the practice. */
export function Contacts({ selected, onSelect, onOpenMatter }: {
  selected: string | null
  onSelect: (id: string | null) => void
  onOpenMatter: (id: string) => void
}) {
  const [items, setItems] = useState<ContactSummary[]>([])
  const [search, setSearch] = useState('')
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<ContactSummary[]>(`/api/contacts?search=${encodeURIComponent(search)}`)
      .then((found) => {
        setItems(found)
        if (!selected && found[0]) onSelect(found[0].id)
      })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [search, selected, onSelect])

  useEffect(reload, [reload])

  return (
    <>
      <Panel>
        <PanelHeader>
          <span>Tiers · {items.length}</span>
          <Button variant="ghost" size="iconSm" title="Nouveau tiers" onClick={() => setCreating(true)}>
            <Plus size={14} strokeWidth={2} />
          </Button>
        </PanelHeader>

        <div className="shrink-0 border-b border-line-subtle px-1.5 py-1">
          <Input
            className="h-6 w-full text-[11.5px]"
            value={search}
            placeholder="Nom, raison sociale…"
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>

        <div className="flex-1 overflow-y-auto p-1">
          {error && <p className="p-2 text-danger">{error}</p>}

          {items.length === 0 && <p className="px-2 py-3 text-muted">Aucun tiers.</p>}

          {items.map((contact) => (
            <button
              key={contact.id}
              type="button"
              onClick={() => onSelect(contact.id)}
              className={cn(
                'grid h-9 w-full content-center rounded-md px-2 py-1 text-left transition-colors',
                contact.id === selected
                  ? 'bg-brand-subtle shadow-[inset_2px_0_0_var(--brand)]'
                  : 'hover:bg-hover',
              )}
            >
              <span className="truncate text-[12px] leading-4">{contact.displayName}</span>
              <span className="truncate font-mono text-[10px] leading-[13px] text-muted">
                {contact.type === 'Organisation' ? 'Personne morale' : 'Personne physique'}
              </span>
            </button>
          ))}
        </div>
      </Panel>

      {selected ? (
        <ContactView contactId={selected} onOpenMatter={onOpenMatter} />
      ) : (
        <Panel className="items-center justify-center">
          <EmptyState
            title="Votre premier tiers"
            className="max-w-[460px]"
            actions={<Button onClick={() => setCreating(true)}>Ajouter un tiers</Button>}
          >
            Clients, parties adverses, confrères, experts : tous ceux avec qui le cabinet traite.
          </EmptyState>
        </Panel>
      )}

      {creating && (
        <NewContact
          onCancel={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false)
            onSelect(id)
            reload()
          }}
        />
      )}
    </>
  )
}

function ContactView({ contactId, onOpenMatter }: {
  contactId: string
  onOpenMatter: (id: string) => void
}) {
  const [contact, setContact] = useState<ContactDetail | null>(null)

  useEffect(() => {
    api<ContactDetail>(`/api/contacts/${contactId}`).then(setContact).catch(() => setContact(null))
  }, [contactId])

  if (!contact) return <Panel />

  const clientRoles = contact.roles.filter((role) => role.isClient)
  const otherRoles = contact.roles.filter((role) => !role.isClient)

  return (
    <Panel>
      <PageHeader
        title={
          <>
            <span
              className={cn(
                'grid h-7 w-7 shrink-0 place-items-center bg-sunken text-[11px] font-medium text-ink-secondary',
                contact.type === 'Individual' ? 'rounded-full' : 'rounded-lg',
              )}
            >
              {initials(contact.displayName)}
            </span>

            <h2 className="m-0 truncate text-[20px] leading-[26px] font-semibold tracking-[-0.015em]">
              {contact.displayName}
            </h2>

            <Badge>{contact.type === 'Organisation' ? 'Personne morale' : 'Personne physique'}</Badge>
          </>
        }
        meta={
          <>
            {contact.siren && <span className="font-mono tnum">SIREN {contact.siren}</span>}
            {contact.legalForm && (<><MetaDivider /><span>{contact.legalForm}</span></>)}
            <MetaDivider />
            <span>{contact.matterCount} dossier{contact.matterCount > 1 ? 's' : ''}</span>
            <MetaDivider />
            <span>
              {contact.clientSince
                ? `client depuis ${new Date(contact.clientSince).toLocaleDateString('fr-FR', { month: '2-digit', year: 'numeric' })}`
                : 'jamais client'}
            </span>
          </>
        }
      />

      <div className="grid flex-1 grid-cols-[minmax(0,1fr)_208px] overflow-hidden">
        <div className="grid content-start gap-4 overflow-y-auto px-4 pt-3 pb-5">
          <section>
            {/* The grouping is the point: only client relations feed billing. */}
            <RoleCaption>Relations client · {clientRoles.length} · facturables</RoleCaption>

            {clientRoles.length === 0 && (
              <Micro>Aucune relation client, rien à facturer.</Micro>
            )}

            {clientRoles.map((role) => (
              <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} client />
            ))}

            {otherRoles.length > 0 && (
              <>
                <RoleCaption>Autres rôles · {otherRoles.length} · non facturables</RoleCaption>
                {otherRoles.map((role) => (
                  <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} />
                ))}
              </>
            )}

            <p className="mt-2.5 mb-0 max-w-[64ch] text-[11px] leading-4 text-muted">
              Le rôle est du texte libre propre à chaque dossier : le même tiers peut être client ici
              et fournisseur mis en cause là. Seules les relations marquées « client » alimentent la
              facturation, c’est le seul rôle que l’application interprète.
            </p>
          </section>

          <section>
            <h3 className="m-0 flex items-baseline gap-1.5 pb-1 text-[12px] font-semibold">
              Derniers échanges <Micro>tous dossiers confondus</Micro>
            </h3>

            {contact.recentExchanges.length === 0 && <Micro>Aucun échange.</Micro>}

            {contact.recentExchanges.map((exchange) => (
              <button
                key={exchange.activityId}
                type="button"
                onClick={() => onOpenMatter(exchange.matterId)}
                className="flex w-full min-h-[34px] items-center gap-2.5 border-t border-line-subtle px-2 py-1.5 text-left text-[12px] hover:bg-hover"
              >
                <span className="w-[104px] shrink-0 font-mono text-[11.5px] text-ink-secondary tnum">
                  {formatDate(exchange.occurredAt)}
                </span>

                <span className="min-w-0 flex-1 truncate">
                  <strong className="font-medium">{activityLabels[exchange.type]}</strong>
                  {exchange.summary && ` · ${exchange.summary}`}
                </span>

                <span className="shrink-0 font-mono text-[11px] text-muted">
                  {exchange.matterReference}
                </span>
              </button>
            ))}
          </section>
        </div>

        <aside className="grid content-start gap-3 overflow-y-auto border-l border-line-subtle p-2.5">
          <section>
            <ContextTitle>Coordonnées</ContextTitle>

            <dl className="m-0 grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1 text-[11.5px]">
              {contact.phone && (<><Term>Téléphone</Term><dd className="m-0 font-mono tnum">{contact.phone}</dd></>)}
              {contact.email && (<><Term>Courriel</Term><dd className="m-0 truncate">{contact.email}</dd></>)}
              {contact.address && (<><Term>Adresse</Term><dd className="m-0">{contact.address}</dd></>)}
              {contact.siren && (<><Term>SIREN</Term><dd className="m-0 font-mono tnum">{contact.siren}</dd></>)}
            </dl>

            {!contact.phone && !contact.email && !contact.address && (
              <Micro>Aucune coordonnée enregistrée.</Micro>
            )}
          </section>

          {contact.notes && (
            <section>
              <ContextTitle>Notes</ContextTitle>
              <p className="m-0 text-[11.5px] leading-4">{contact.notes}</p>
            </section>
          )}
        </aside>
      </div>
    </Panel>
  )
}

const Term = ({ children }: { children: string }) => (
  <dt className="text-muted">{children}</dt>
)

const ContextTitle = ({ children }: { children: string }) => (
  <h3 className="m-0 mb-1.5 font-mono text-[10px] font-normal tracking-[0.05em] uppercase text-muted">
    {children}
  </h3>
)

const RoleCaption = ({ children }: { children: React.ReactNode }) => (
  <div className="flex items-center gap-1.5 pt-3.5 pb-1 font-mono text-[10px] tracking-[0.05em] uppercase text-muted first:pt-0">
    {children}
  </div>
)

function RoleRow({ role, onOpen, client }: {
  role: ContactRole
  onOpen: (id: string) => void
  client?: boolean
}) {
  return (
    <button
      type="button"
      onClick={() => onOpen(role.matterId)}
      className={cn(
        'flex w-full min-h-[34px] items-center gap-2.5 border-t border-line-subtle px-2 py-1.5 text-left text-[12px] hover:bg-hover',
        client && 'border-l-[3px] border-l-brand',
      )}
    >
      <span className="grid min-w-0 flex-1">
        <span className="truncate">{role.matterName}</span>
        {/* Roles are long, and shortening them automatically destroys their meaning. */}
        <span className="truncate text-[11px] text-muted" title={role.role ?? undefined}>
          {role.role}
        </span>
      </span>

      <Badge tone={role.matterIsOpen ? 'brand' : 'neutral'}>
        {role.matterIsOpen ? 'En cours' : 'Clôturé'}
      </Badge>

      <span className="shrink-0 font-mono text-[11px] text-muted">{role.matterReference}</span>
    </button>
  )
}

function NewContact({ onCreated, onCancel }: {
  onCreated: (id: string) => void
  onCancel: () => void
}) {
  const [type, setType] = useState<ContactType>('Organisation')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function create() {
    try {
      const created = await post<{ id: string }>('/api/contacts', {
        type,
        legalName: type === 'Organisation' ? name : null,
        lastName: type === 'Individual' ? name : null,
        email: email || null,
        phone: phone || null,
      })

      onCreated(created.id)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  return (
    <Dialog title="Nouveau tiers" onClose={onCancel}>
      <div className="flex gap-0.5">
        <Segment active={type === 'Organisation'} onClick={() => setType('Organisation')}>
          Personne morale
        </Segment>
        <Segment active={type === 'Individual'} onClick={() => setType('Individual')}>
          Personne physique
        </Segment>
      </div>

      <Field label={type === 'Organisation' ? 'Raison sociale' : 'Nom'}>
        <Input inputSize="lg" autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      </Field>

      <Field label="Courriel">
        <Input inputSize="lg" value={email} onChange={(event) => setEmail(event.target.value)} />
      </Field>

      <Field label="Téléphone">
        <Input inputSize="lg" value={phone} onChange={(event) => setPhone(event.target.value)} />
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={!name.trim()} onClick={() => void create()}>
          Créer le tiers
        </Button>
      </DialogActions>
    </Dialog>
  )
}

function Segment({ active, onClick, children }: {
  active: boolean
  onClick: () => void
  children: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-6 rounded-sm px-2.5 text-[12px] transition-colors',
        active ? 'bg-brand-subtle text-brand-on-subtle' : 'text-ink-secondary hover:bg-hover',
      )}
    >
      {children}
    </button>
  )
}
