import { useEffect, useState } from 'react'
import { ApiError, api, post } from './api.js'
import type { ContactSummary } from './types.js'

/**
 * A dossier is opened for someone, so this creates the client too when there isn't one yet — asking
 * her to go and make a tiers first, then come back, is the kind of two-step the incumbents are full of.
 */
export function NewMatter({ onCreated, onCancel }: {
  onCreated: (matterId: string) => void
  onCancel: () => void
}) {
  const [contacts, setContacts] = useState<ContactSummary[]>([])
  const [clientId, setClientId] = useState<string>('')
  const [clientName, setClientName] = useState('')
  const [name, setName] = useState('')
  const [rate, setRate] = useState('280')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

  async function create() {
    setBusy(true)
    setError(null)

    try {
      let contactId = clientId

      if (!contactId) {
        if (!clientName.trim()) {
          setError('Indiquez le client, ou choisissez-en un dans la liste.')
          return
        }

        const created = await post<{ id: string }>('/api/contacts', {
          type: 'Organisation',
          legalName: clientName.trim(),
        })

        contactId = created.id
      }

      const matter = await post<{ id: string }>('/api/matters', {
        name: name.trim(),
        clientContactId: contactId,
        hourlyRateCents: Math.round(Number(rate) * 100),
      })

      onCreated(matter.id)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="scrim">
      <div className="dialog">
        <h2>Nouveau dossier</h2>

        <label>
          Intitulé du dossier
          <input value={name} onChange={(event) => setName(event.target.value)} autoFocus />
        </label>

        <label>
          Client
          {contacts.length > 0 && (
            <select value={clientId} onChange={(event) => setClientId(event.target.value)}>
              <option value="">Nouveau tiers</option>
              {contacts.map((contact) => (
                <option key={contact.id} value={contact.id}>
                  {contact.displayName}
                </option>
              ))}
            </select>
          )}
          {!clientId && (
            <input
              value={clientName}
              onChange={(event) => setClientName(event.target.value)}
              placeholder="Raison sociale ou nom"
            />
          )}
        </label>

        <label>
          Taux horaire (€)
          <input value={rate} onChange={(event) => setRate(event.target.value)} className="mono" />
        </label>

        {error && <p className="danger">{error}</p>}

        <div className="dialog-actions">
          <button type="button" className="secondary-button" onClick={onCancel}>
            Annuler
          </button>
          <button type="button" disabled={busy || !name.trim()} onClick={() => void create()}>
            Créer le dossier
          </button>
        </div>
      </div>
    </div>
  )
}
