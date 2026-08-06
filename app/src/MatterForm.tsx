import { useEffect, useState } from 'react'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Dialog, DialogActions, Field } from './components/ui/dialog.js'
import { Input } from './components/ui/input.js'
import { Select } from './components/ui/select.js'
import { Textarea } from './components/ui/textarea.js'
import { centsToAmount, parseAmountToCents } from './lib/amount.js'
import type { ContactSummary, MatterDetail } from './types.js'

/**
 * Creating and correcting a dossier are the same form. Creating also creates the client when there
 * isn't one yet: asking her to go and make a tiers first, then come back, is the kind of two-step the
 * incumbents are full of.
 */
export function MatterForm({ matter, onSaved, onCancel }: {
  matter?: MatterDetail
  onSaved: (matterId: string) => void
  onCancel: () => void
}) {
  const existingClient = matter?.parties.find((party) => party.isClient)

  const [contacts, setContacts] = useState<ContactSummary[]>([])
  const [clientId, setClientId] = useState(existingClient?.contactId ?? '')
  const [clientName, setClientName] = useState('')
  const [name, setName] = useState(matter?.name ?? '')
  const [reference, setReference] = useState(matter?.reference ?? '')
  const [courtCaseNumber, setCourtCaseNumber] = useState(matter?.courtCaseNumber ?? '')
  const [description, setDescription] = useState(matter?.description ?? '')
  const [rate, setRate] = useState(matter ? centsToAmount(matter.hourlyRateCents) : '280,00')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

  async function save() {
    const rateCents = parseAmountToCents(rate)

    if (!name.trim()) {
      setError('Donnez un intitulé au dossier.')
      return
    }

    if (rateCents === null) {
      setError('Indiquez un taux horaire positif, par exemple 280,00.')
      return
    }

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

      const body = {
        name: name.trim(),
        clientContactId: contactId,
        reference: reference.trim() || null,
        description: description.trim() || null,
        openedOn: matter?.openedOn.slice(0, 10) ?? null,
        hourlyRateCents: rateCents,
        courtCaseNumber: courtCaseNumber.trim() || null,
      }

      if (matter) {
        await api(`/api/matters/${matter.id}`, { method: 'PUT', body: JSON.stringify(body) })
        onSaved(matter.id)
      } else {
        const created = await post<{ id: string }>('/api/matters', body)
        onSaved(created.id)
      }
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title={matter ? 'Modifier le dossier' : 'Nouveau dossier'} width={480} onClose={onCancel}>
      <Field label="Intitulé du dossier">
        <Input
          inputSize="lg"
          autoFocus
          value={name}
          onChange={(event) => { setName(event.target.value); setError(null) }}
        />
      </Field>

      <Field label="Client">
        <div className="grid gap-1.5">
          {contacts.length > 0 && (
            <Select className="h-8" value={clientId} onChange={(event) => setClientId(event.target.value)}>
              <option value="">Nouveau tiers</option>
              {contacts.map((contact) => (
                <option key={contact.id} value={contact.id}>{contact.displayName}</option>
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

      <div className="grid grid-cols-2 gap-3">
        <Field label="Référence">
          <Input
            inputSize="lg"
            className="font-mono tnum"
            value={reference}
            placeholder="attribuée automatiquement"
            onChange={(event) => setReference(event.target.value)}
          />
        </Field>

        <Field label="N° RG">
          <Input
            inputSize="lg"
            className="font-mono tnum"
            value={courtCaseNumber}
            placeholder="24/01187"
            onChange={(event) => setCourtCaseNumber(event.target.value)}
          />
        </Field>
      </div>

      <Field label="Taux horaire (€)">
        <Input
          inputSize="lg"
          className="w-28 font-mono tnum"
          value={rate}
          onChange={(event) => { setRate(event.target.value); setError(null) }}
        />
      </Field>

      <Field label="Description">
        <Textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} />
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy} onClick={() => void save()}>
          {matter ? 'Enregistrer' : 'Créer le dossier'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
