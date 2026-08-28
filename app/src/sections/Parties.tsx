import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions, Field } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { Select } from '../components/ui/select.js'
import { NewContact } from './NewContact.js'
import type { ContactSummary, MatterDetail } from '../types.js'

/**
 * Attaching a tiers to a dossier, and writing what it is there for.
 *
 * <p>Lifted out of the fiche's context panel when the apercu grew its own parties list. Two copies of
 * a form that writes the same row would drift, and the apercu cannot import from MatterView because
 * MatterView imports the apercu.</p>
 */

/** Writing the role. Inline, because it is one line of free text and a dialog would be theatre. */
export function PartyRole({ party, onCancel, onSaved }: {
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
export function AddParty({ matterId, existing, onCancel, onAdded }: {
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
