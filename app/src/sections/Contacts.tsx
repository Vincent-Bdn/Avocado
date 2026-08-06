import { useCallback, useEffect, useState } from 'react'
import { Info, Plus, Users } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Avatar } from '../components/ui/avatar.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { MetaDivider, PageHeader } from '../components/ui/page-header.js'
import { Panel, PanelHeader } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { activityLabels, formatDate } from '../labels.js'
import { Micro } from '../tabs/shared.js'
import type { ActivityType, ContactSummary, ContactType } from '../types.js'

import { NewContact as NewContactSheet } from './NewContact.js'

export { NewContact } from './NewContact.js'

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
  attachedPeople: ContactAttachment[]
  attachedTo: ContactAttachment | null
}

interface ContactAttachment {
  id: string
  type: ContactType
  displayName: string
  function: string | null
  email: string | null
  phone: string | null
}

/** Tiers: the address book, and one contact's roles across the practice. */
export function Contacts({ selected, onSelect, onOpenMatter, onNewContact }: {
  selected: string | null
  onSelect: (id: string | null) => void
  onOpenMatter: (id: string) => void
  onNewContact: () => void
}) {
  const [reloadToken, setReloadToken] = useState(0)
  const [items, setItems] = useState<ContactSummary[]>([])
  const [search, setSearch] = useState('')
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
          <Button variant="ghost" size="iconSm" title="Nouveau tiers" onClick={onNewContact}>
            <Plus size={14} strokeWidth={2} />
          </Button>
        </PanelHeader>

        <div className="shrink-0 border-b border-line-subtle px-1.5 py-1">
          <Input
            inputSize="sm"
            className="w-full"
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
                'grid h-9 w-full content-center rounded-sm px-2 py-1 text-left transition-colors',
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
        <ContactView
          key={`${selected}-${reloadToken}`}
          contactId={selected}
          onOpenMatter={onOpenMatter}
          onOpenContact={onSelect}
          onChanged={() => { setReloadToken((token) => token + 1); reload() }}
        />
      ) : (
        <Panel className="items-center justify-center">
          <EmptyState
            icon={<Users size={18} strokeWidth={1.8} />}
            title="Votre premier tiers"
            className="max-w-[460px]"
            actions={<Button onClick={onNewContact}>Ajouter un tiers</Button>}
          >
            Clients, parties adverses, confrères, experts : tous ceux avec qui le cabinet traite.
          </EmptyState>
        </Panel>
      )}
    </>
  )
}

function ContactView({ contactId, onOpenMatter, onOpenContact, onChanged }: {
  contactId: string
  onOpenMatter: (id: string) => void
  onOpenContact: (id: string) => void
  onChanged: () => void
}) {
  const [contact, setContact] = useState<ContactDetail | null>(null)
  const [editing, setEditing] = useState(false)
  const [attaching, setAttaching] = useState(false)

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
            <Avatar name={contact.displayName} type={contact.type} size={28} />

            <h2 className="type-title-lg m-0 truncate">{contact.displayName}</h2>

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
        actions={<Button variant="secondary" onClick={() => setEditing(true)}>Modifier</Button>}
      />

      <div className="grid flex-1 grid-cols-[minmax(0,1fr)_208px] overflow-hidden">
        <div className="grid content-start gap-4 overflow-y-auto px-4 pt-3 pb-5">
          <section>
            {/* The grouping is the point: only client relations feed billing. */}
            {clientRoles.length === 0 ? (
              <RoleCaption>Aucune relation client, rien à facturer</RoleCaption>
            ) : (
              <RoleCaption client>Relations client · {clientRoles.length} · facturables</RoleCaption>
            )}

            <div className="grid gap-1.5">
              {clientRoles.map((role) => (
                <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} client />
              ))}
            </div>

            {otherRoles.length > 0 && (
              <>
                <RoleCaption>Autres rôles · {otherRoles.length} · non facturables</RoleCaption>
                <div className="grid gap-1.5">
                  {otherRoles.map((role) => (
                    <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} />
                  ))}
                </div>
              </>
            )}

            <div className="mt-3 flex items-start gap-2 rounded-md border border-line-subtle px-3 py-2.5">
              <Info size={14} strokeWidth={2} className="mt-0.5 shrink-0 text-ink-secondary" />
              <p className="m-0 max-w-[64ch] text-[11.5px] leading-[17px] text-ink-secondary">
                Le rôle est du <strong className="font-medium text-ink">texte libre</strong> propre à
                chaque dossier : le même tiers peut être client ici et fournisseur mis en cause là.
                Seules les relations marquées « client » alimentent la facturation, c’est le seul rôle
                que l’application interprète.
              </p>
            </div>
          </section>

          <section>
            <h3 className="type-title m-0 flex items-baseline gap-2 pb-2">
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

          <section>
            <ContextTitle>
              {contact.type === 'Organisation' ? 'Personnes rattachées' : 'Rattachement'}
            </ContextTitle>

            {contact.attachedTo && (
              <AttachedRow attachment={contact.attachedTo} onOpen={() => onOpenContact(contact.attachedTo!.id)} />
            )}

            {contact.attachedPeople.map((person) => (
              <AttachedRow key={person.id} attachment={person} onOpen={() => onOpenContact(person.id)} />
            ))}

            {contact.type === 'Organisation' && (
              <button
                type="button"
                onClick={() => setAttaching(true)}
                className="mt-1.5 flex h-6 items-center gap-1 rounded-[3px] border border-dashed border-line-strong px-2 text-[11px] text-ink-secondary hover:bg-hover"
              >
                <Plus size={11} strokeWidth={2} />
                Rattacher une personne
              </button>
            )}

            {contact.type === 'Individual' && !contact.attachedTo && (
              <Micro>Aucun rattachement.</Micro>
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

      {editing && (
        <NewContactSheet
          contact={contact}
          onCancel={() => setEditing(false)}
          onCreated={() => { setEditing(false); onChanged() }}
        />
      )}

      {attaching && (
        <NewContactSheet
          attachTo={{ id: contact.id, name: contact.displayName }}
          onCancel={() => setAttaching(false)}
          onCreated={() => { setAttaching(false); onChanged() }}
        />
      )}
    </Panel>
  )
}

/** 32px row: avatar, name, function. The gérant is a tiers in his own right, so the row opens him. */
const AttachedRow = ({ attachment, onOpen }: {
  attachment: ContactAttachment
  onOpen: () => void
}) => (
  <button
    type="button"
    onClick={onOpen}
    className="flex h-8 w-full items-center gap-2 rounded-[3px] px-1 text-left hover:bg-hover"
  >
    <Avatar name={attachment.displayName} type={attachment.type} />
    <span className="grid min-w-0">
      <span className="truncate text-[11.5px] leading-4">{attachment.displayName}</span>
      {attachment.function && (
        <span className="truncate text-[10.5px] leading-[13px] text-muted" title={attachment.function}>
          {attachment.function}
        </span>
      )}
    </span>
  </button>
)

const Term = ({ children }: { children: string }) => (
  <dt className="text-muted">{children}</dt>
)

const ContextTitle = ({ children }: { children: string }) => (
  <h3 className="m-0 mb-1.5 font-mono text-[10px] font-normal tracking-[0.05em] uppercase text-muted">
    {children}
  </h3>
)

const RoleCaption = ({ client, children }: { client?: boolean; children: React.ReactNode }) => (
  <div
    className={cn(
      'type-group flex items-center gap-1.5 pt-3.5 pb-1.5 first:pt-0',
      client ? 'text-brand-on-subtle' : 'text-muted',
    )}
  >
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
        'flex w-full items-center gap-2.5 rounded-sm border border-l-[3px] px-2.5 py-[7px] text-left',
        client
          ? 'border-[#BFD3C5] border-l-brand bg-[#F4F8F5]'
          : 'border-line-subtle border-l-line bg-[#F8F9F6]',
      )}
    >
      <span className="grid min-w-0 flex-1">
        <span className="truncate text-[12.5px] leading-[17px] font-medium">{role.matterName}</span>
        {/* Roles are long, and shortening them automatically destroys their meaning. */}
        <span className="truncate text-[11px] leading-[15px] text-muted" title={role.role ?? undefined}>
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
