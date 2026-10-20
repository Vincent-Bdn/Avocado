import { useEffect, useState } from 'react'
import { FolderOpen, Star } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Dialog, DialogActions, Field } from './components/ui/dialog.js'
import { Input } from './components/ui/input.js'
import { Select } from './components/ui/select.js'
import { Textarea } from './components/ui/textarea.js'
import { NewContact } from './sections/NewContact.js'
import { cn } from './lib/utils.js'
import { centsToAmount, parseAmountToCents } from './lib/amount.js'
import type { ContactSummary, MatterDetail, PracticeSettings } from './types.js'

/** The one word the application interprets. Everything else is free text the practice can invent. */
const LITIGATION = 'Contentieux'

/**
 * Les trois natures, et il n'y en a pas d'autres.
 *
 * <p>This was free text with autocompletion that learned the practice's own vocabulary, so that
 * « Arbitrage » typed once was offered for ever without waiting for a release. The lawyer who asked
 * for that asked for the opposite afterwards, having used it: three answers, chosen from a list,
 * because a field that accepts anything is a field where the same dossier is Conseil on Monday and
 * conseil on Thursday, and neither groups with the other.</p>
 *
 * <p>Anything a vault already holds is still offered, so a dossier recorded as « Arbitrage » keeps
 * its nature and can be reopened without the form quietly rewriting it. See
 * <c>otherClassifications</c>.</p>
 */
const CLASSIFICATIONS = [LITIGATION, 'Conseil', 'Autre']

/**
 * Creating and correcting a dossier are the same form. Creating also creates the client when there
 * isn't one yet, through the same sheet as the fiche tiers: sending her off to make a tiers first and
 * come back is the kind of two-step the incumbents are full of, and a cut-down inline field would be
 * a second, worse way of doing the same thing.
 */
export function MatterForm({ matter, onSaved, onCancel }: {
  matter?: MatterDetail
  onSaved: (matterId: string) => void
  onCancel: () => void
}) {
  const existingClient = matter?.parties.find((party) => party.isClient)

  const [contacts, setContacts] = useState<ContactSummary[]>([])
  /** Natures this vault holds that are not one of the three. Offered so none is lost on a reopen. */
  const [otherClassifications, setOtherClassifications] = useState<string[]>([])
  const [clientId, setClientId] = useState(existingClient?.contactId ?? '')
  const [creatingClient, setCreatingClient] = useState(false)
  const [name, setName] = useState(matter?.name ?? '')
  const [reference, setReference] = useState(matter?.reference ?? '')
  const [classification, setClassification] = useState(matter?.classification ?? LITIGATION)
  const [court, setCourt] = useState(matter?.court ?? '')
  const [courtCaseNumber, setCourtCaseNumber] = useState(matter?.courtCaseNumber ?? '')
  const [description, setDescription] = useState(matter?.description ?? '')
  const [rate, setRate] = useState(matter ? centsToAmount(matter.hourlyRateCents) : '')
  const [forfait, setForfait] = useState(
    matter?.fixedFeeCents ? centsToAmount(matter.fixedFeeCents) : '',
  )
  const [favourite, setFavourite] = useState(matter?.isFavourite ?? false)

  /**
   * Where her documents for this dossier live.
   *
   * <p><b>Always hers to set, and never constrained by anything Avocado prefers.</b> Ten lawyers
   * organise their files ten ways: one keeps fifty dossiers loose on the Desktop, another splits them
   * between « en cours » and « archivés », others do something else again. None of that is Avocado's
   * business, and a folder that has to sit somewhere in particular is the sort of rule that made the
   * previous version unusable.</p>
   */
  const [folder, setFolder] = useState(matter?.documentsFolder ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const litigation = classification.trim().toLowerCase() === LITIGATION.toLowerCase()

  useEffect(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

  // The three are the list. What this adds is whatever a vault already carries from before the list
  // was closed, « Arbitrage », « Médiation », so reopening such a dossier does not silently reclass
  // it as Contentieux the first time she corrects its name.
  useEffect(() => {
    api<string[]>('/api/matters/classifications')
      .then((used) => setOtherClassifications(
        used.filter((candidate) => !CLASSIFICATIONS.includes(candidate)),
      ))
      .catch(() => setOtherClassifications([]))
  }, [])

  // The practice's rate is the starting point for a new dossier, and only that: it is copied onto
  // the dossier here, so changing it later prices tomorrow's work and leaves yesterday's alone.
  useEffect(() => {
    if (matter) return

    api<PracticeSettings>('/api/settings')
      .then((settings) => setRate(centsToAmount(settings.hourlyRateCents)))
      .catch(() => undefined)
  }, [matter])

  async function save() {
    const rateCents = parseAmountToCents(rate)

    if (!name.trim()) {
      setError('Donnez un intitulé au dossier.')
      return
    }

    if (!clientId) {
      setError('Choisissez le client, ou créez-le.')
      return
    }

    if (rateCents === null) {
      setError('Indiquez un taux horaire positif, par exemple 240,00.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = {
        name: name.trim(),
        clientContactId: clientId,
        reference: reference.trim() || null,
        description: description.trim() || null,
        openedOn: matter?.openedOn.slice(0, 10) ?? null,
        hourlyRateCents: rateCents,
        fixedFeeCents: parseAmountToCents(forfait),
        classification: classification.trim() || null,
        // Both only mean something on a contentieux, and the server refuses them elsewhere.
        court: litigation ? court.trim() || null : null,
        courtCaseNumber: litigation ? courtCaseNumber.trim() || null : null,
        isFavourite: favourite,
      }

      // Its own route: setting it checks the folder is there and that no other dossier already has
      // it, which the matter form has no business deciding.
      const id = matter
        ? (await api(`/api/matters/${matter.id}`, { method: 'PUT', body: JSON.stringify(body) }), matter.id)
        : (await post<{ id: string }>('/api/matters', body)).id

      if ((matter?.documentsFolder ?? '') !== folder.trim()) {
        await api(`/api/matters/${id}/folder`, {
          method: 'PUT',
          body: JSON.stringify({ path: folder.trim() || null }),
        })
      }

      onSaved(id)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (creatingClient) {
    return (
      <NewContact
        onCancel={() => setCreatingClient(false)}
        onCreated={(id) => {
          setCreatingClient(false)
          api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => undefined)
          setClientId(id)
        }}
      />
    )
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
        <div className="flex gap-2">
          <Select
            className="h-8 flex-1"
            value={clientId}
            onChange={(event) => { setClientId(event.target.value); setError(null) }}
          >
            <option value="">Choisir un tiers…</option>
            {contacts.map((contact) => (
              <option key={contact.id} value={contact.id}>{contact.displayName}</option>
            ))}
          </Select>

          <Button variant="secondary" size="lg" onClick={() => setCreatingClient(true)}>
            Nouveau tiers…
          </Button>
        </div>
      </Field>

      <Field label="Nature du dossier">
        <div className="grid gap-1">
          {/*
            Three answers, chosen. It was free text with autocompletion, which meant « Conseil » and
            « conseil » were two natures and the filters on the dossier list could not group them.
            Natures already recorded in this vault are kept at the bottom so none is lost.
          */}
          <Select
            className="h-8"
            value={classification}
            onChange={(event) => setClassification(event.target.value)}
          >
            {CLASSIFICATIONS.map((candidate) => (
              <option key={candidate} value={candidate}>{candidate}</option>
            ))}

            {otherClassifications.length > 0 && (
              <optgroup label="Déjà utilisées dans ce coffre">
                {otherClassifications.map((candidate) => (
                  <option key={candidate} value={candidate}>{candidate}</option>
                ))}
              </optgroup>
            )}
          </Select>

          {litigation && (
            <p className="m-0 type-caption text-muted">
              Un dossier contentieux porte une juridiction et un n° RG.
            </p>
          )}
        </div>
      </Field>

      {/* Only a contentieux reaches a court, so only a contentieux gets the two fields. */}
      {litigation && (
        <div className="grid grid-cols-2 gap-3">
          <Field label="Juridiction">
            <Input
              inputSize="lg"
              value={court}
              placeholder="TC Lyon"
              onChange={(event) => setCourt(event.target.value)}
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
      )}

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

        <Field label="Forfait convenu (€ HT, facultatif)">
          <Input
            value={forfait}
            placeholder="3 000,00"
            className="font-mono tnum"
            onChange={(event) => setForfait(event.target.value)}
          />
        </Field>

        <Field label="Taux horaire (€)">
          <Input
            inputSize="lg"
            className="font-mono tnum"
            value={rate}
            onChange={(event) => { setRate(event.target.value); setError(null) }}
          />
        </Field>
      </div>

      <Field label="Dossier de documents">
        <div className="flex flex-wrap items-center gap-2">
          <Input
            inputSize="lg"
            className="min-w-0 flex-1 font-mono text-[12px]"
            value={folder}
            placeholder="aucun pour l’instant"
            onChange={(event) => { setFolder(event.target.value); setError(null) }}
          />

          <Button
            variant="secondary"
            size="lg"
            onClick={() => {
              void window.avocado
                .chooseFolder(folder || undefined, 'Le dossier où vivent les documents de ce dossier')
                .then((chosen) => { if (chosen) { setFolder(chosen); setError(null) } })
            }}
          >
            <FolderOpen size={14} strokeWidth={1.75} />
            Parcourir…
          </Button>

          {folder !== '' && (
            <Button variant="secondary" size="lg" onClick={() => setFolder('')}>Détacher</Button>
          )}
        </div>

        <p className="m-0 mt-1 text-[11.5px] leading-[17px] text-muted">
          Avocado lit ce répertoire, il ne le déplace pas. Bureau, disque réseau, peu importe.
        </p>
      </Field>

      <Field label="Description">
        <Textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} />
      </Field>

      <button
        type="button"
        aria-pressed={favourite}
        onClick={() => setFavourite((current) => !current)}
        className={cn(
          'flex h-8 items-center gap-2 justify-self-start rounded-sm border px-2.5 text-[12.5px]',
          favourite
            ? 'border-accent bg-accent-subtle text-warning'
            : 'border-line-strong text-ink-secondary hover:bg-hover',
        )}
      >
        <Star size={14} strokeWidth={2} fill={favourite ? 'currentColor' : 'none'} />
        {favourite ? 'En favori, épinglé en haut de la liste' : 'Mettre en favori'}
      </button>

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
