import { useEffect, useState } from 'react'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Dialog, DialogActions, Field } from './components/ui/dialog.js'
import { Input } from './components/ui/input.js'
import { Select } from './components/ui/select.js'
import type { ContactSummary } from './types.js'

/**
 * A dossier is opened for someone, so this creates the client too when there isn't one yet : asking
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
    <Dialog title="Nouveau dossier" onClose={onCancel}>
      <Field label="Intitulé du dossier">
        <Input inputSize="lg" autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      </Field>

      <Field label="Client">
        <div className="grid gap-1.5">
          {contacts.length > 0 && (
            <Select
              className="h-8"
              value={clientId}
              onChange={(event) => setClientId(event.target.value)}
            >
              <option value="">Nouveau tiers</option>
              {contacts.map((contact) => (
                <option key={contact.id} value={contact.id}>
                  {contact.displayName}
                </option>
              ))}
            </Select>
          )}

          {!clientId && (
            <Input
              inputSize="lg"
              value={clientName}
              placeholder="Raison sociale ou nom"
              onChange={(event) => setClientName(event.target.value)}
            />
          )}
        </div>
      </Field>

      <Field label="Taux horaire (€)">
        <Input
          inputSize="lg"
          className="w-28 font-mono tnum"
          value={rate}
          onChange={(event) => setRate(event.target.value)}
        />
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy || !name.trim()} onClick={() => void create()}>
          Créer le dossier
        </Button>
      </DialogActions>
    </Dialog>
  )
}
