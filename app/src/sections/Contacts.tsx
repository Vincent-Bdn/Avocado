import { useCallback, useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { activityLabels, formatDate } from '../labels.js'
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
      <aside className="secondary-panel">
        <header className="panel-header">
          <span>Tiers · {items.length}</span>
          <button type="button" className="icon-button" title="Nouveau tiers" onClick={() => setCreating(true)}>
            <Plus size={14} strokeWidth={2} />
          </button>
        </header>

        <div className="filters">
          <input
            className="panel-search"
            value={search}
            placeholder="Nom, raison sociale…"
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>

        <div className="matter-list">
          {error && <p className="danger">{error}</p>}

          {items.length === 0 && <p className="muted empty-list">Aucun tiers.</p>}

          {items.map((contact) => (
            <button
              key={contact.id}
              type="button"
              className={`matter-row ${contact.id === selected ? 'matter-row-selected' : ''}`}
              onClick={() => onSelect(contact.id)}
            >
              <span className="matter-name">{contact.displayName}</span>
              <span className="matter-meta">
                {contact.type === 'Organisation' ? 'Personne morale' : 'Personne physique'}
              </span>
            </button>
          ))}
        </div>
      </aside>

      {selected ? (
        <ContactView contactId={selected} onOpenMatter={onOpenMatter} />
      ) : (
        <div className="content">
          <div className="empty centred">
            <h3>Votre premier tiers</h3>
            <p className="muted">
              Clients, parties adverses, confrères, experts : tous ceux avec qui le cabinet traite.
            </p>
            <button type="button" onClick={() => setCreating(true)}>
              Ajouter un tiers
            </button>
          </div>
        </div>
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

  if (!contact) return <div className="content" />

  const clientRoles = contact.roles.filter((role) => role.isClient)
  const otherRoles = contact.roles.filter((role) => !role.isClient)

  return (
    <div className="content">
      <header className="matter-header">
        <div className="line1">
          <span className={`avatar avatar-large ${contact.type === 'Individual' ? 'avatar-round' : ''}`}>
            {initials(contact.displayName)}
          </span>
          <h2>{contact.displayName}</h2>
          <span className="badge badge-closed">
            {contact.type === 'Organisation' ? 'Personne morale' : 'Personne physique'}
          </span>
        </div>

        <div className="line2">
          {contact.siren && <span className="mono">SIREN {contact.siren}</span>}
          {contact.legalForm && <><span className="divider" />{contact.legalForm}</>}
          <span className="divider" />
          <span>
            {contact.matterCount} dossier{contact.matterCount > 1 ? 's' : ''}
          </span>
          <span className="divider" />
          <span>
            {contact.clientSince
              ? `client depuis ${new Date(contact.clientSince).toLocaleDateString('fr-FR', { month: '2-digit', year: 'numeric' })}`
              : 'jamais client'}
          </span>
        </div>
      </header>

      <div className="contact-body">
        <div className="contact-main">
          <section>
            {/* The grouping is the point: only client relations feed billing. */}
            <div className="tier-caption mono client-caption">
              Relations client · {clientRoles.length} — facturables
            </div>

            {clientRoles.length === 0 && (
              <p className="muted micro">Aucune relation client, rien à facturer.</p>
            )}

            {clientRoles.map((role) => (
              <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} client />
            ))}

            {otherRoles.length > 0 && (
              <>
                <div className="tier-caption mono">
                  Autres rôles · {otherRoles.length} — non facturables
                </div>
                {otherRoles.map((role) => (
                  <RoleRow key={role.matterId} role={role} onOpen={onOpenMatter} />
                ))}
              </>
            )}

            <p className="muted micro role-note">
              Le rôle est du texte libre propre à chaque dossier : le même tiers peut être client ici
              et fournisseur mis en cause là. Seules les relations marquées « client » alimentent la
              facturation, c’est le seul rôle que l’application interprète.
            </p>
          </section>

          <section>
            <h3 className="section-head">
              Derniers échanges <span className="muted micro">tous dossiers confondus</span>
            </h3>

            {contact.recentExchanges.length === 0 && <p className="muted micro">Aucun échange.</p>}

            {contact.recentExchanges.map((exchange) => (
              <button
                key={exchange.activityId}
                type="button"
                className="exchange-row"
                onClick={() => onOpenMatter(exchange.matterId)}
              >
                <span className="mono row-date">{formatDate(exchange.occurredAt)}</span>
                <span className="row-main">
                  <span>
                    <strong>{activityLabels[exchange.type]}</strong>
                    {exchange.summary && ` — ${exchange.summary}`}
                  </span>
                </span>
                <span className="mono micro muted">{exchange.matterReference}</span>
              </button>
            ))}
          </section>
        </div>

        <aside className="context">
          <section>
            <h3>Coordonnées</h3>
            <dl className="pairs">
              {contact.phone && (<><dt>Téléphone</dt><dd className="mono">{contact.phone}</dd></>)}
              {contact.email && (<><dt>Courriel</dt><dd>{contact.email}</dd></>)}
              {contact.address && (<><dt>Adresse</dt><dd>{contact.address}</dd></>)}
              {contact.siren && (<><dt>SIREN</dt><dd className="mono">{contact.siren}</dd></>)}
            </dl>
            {!contact.phone && !contact.email && !contact.address && (
              <p className="muted micro">Aucune coordonnée enregistrée.</p>
            )}
          </section>

          {contact.notes && (
            <section>
              <h3>Notes</h3>
              <p className="micro">{contact.notes}</p>
            </section>
          )}
        </aside>
      </div>
    </div>
  )
}

function RoleRow({ role, onOpen, client }: {
  role: ContactRole
  onOpen: (id: string) => void
  client?: boolean
}) {
  return (
    <button
      type="button"
      className={`role-row ${client ? 'role-client' : ''}`}
      onClick={() => onOpen(role.matterId)}
    >
      <span className="row-main">
        <span className="role-matter">{role.matterName}</span>
        {/* Roles are long, and shortening them automatically destroys their meaning. */}
        <span className="muted micro" title={role.role ?? undefined}>{role.role}</span>
      </span>

      <span className={`badge ${role.matterIsOpen ? 'badge-open' : 'badge-closed'}`}>
        {role.matterIsOpen ? 'En cours' : 'Clôturé'}
      </span>
      <span className="mono micro muted">{role.matterReference}</span>
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
    <div className="scrim">
      <div className="dialog">
        <h2>Nouveau tiers</h2>

        <div className="kind-toggle">
          <button
            type="button"
            className={`segment ${type === 'Organisation' ? 'segment-active' : ''}`}
            onClick={() => setType('Organisation')}
          >
            Personne morale
          </button>
          <button
            type="button"
            className={`segment ${type === 'Individual' ? 'segment-active' : ''}`}
            onClick={() => setType('Individual')}
          >
            Personne physique
          </button>
        </div>

        <label>
          {type === 'Organisation' ? 'Raison sociale' : 'Nom'}
          <input autoFocus value={name} onChange={(event) => setName(event.target.value)} />
        </label>

        <label>
          Courriel
          <input value={email} onChange={(event) => setEmail(event.target.value)} />
        </label>

        <label>
          Téléphone
          <input value={phone} onChange={(event) => setPhone(event.target.value)} />
        </label>

        {error && <p className="danger">{error}</p>}

        <div className="dialog-actions">
          <button type="button" className="secondary-button" onClick={onCancel}>
            Annuler
          </button>
          <button type="button" disabled={!name.trim()} onClick={() => void create()}>
            Créer le tiers
          </button>
        </div>
      </div>
    </div>
  )
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('')
}
