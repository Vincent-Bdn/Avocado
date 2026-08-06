import { useEffect, useRef, useState } from 'react'
import { Building2, Check, Loader2, Search, User } from 'lucide-react'
import { ApiError, post } from '../api.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Field } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { Sheet } from '../components/ui/sheet.js'
import { Textarea } from '../components/ui/textarea.js'
import { cn } from '../lib/utils.js'
import {
  AnnuaireUnreachable,
  annuaireEnabled,
  formatSiren,
  searchCompanies,
  setAnnuaireEnabled,
  type AnnuaireCompany,
} from '../lib/annuaire.js'
import type { ContactType } from '../types.js'

type Lookup =
  | { state: 'idle' }
  | { state: 'loading'; previous: AnnuaireCompany[] }
  | { state: 'results'; results: AnnuaireCompany[] }
  | { state: 'empty' }
  | { state: 'unreachable' }

/**
 * Creating a tiers. A side sheet rather than a dialog, because what is behind stays readable.
 *
 * One field drives the whole form for a personne morale: the raison sociale queries the registry and
 * fills SIREN, forme juridique and adresse. Those three stay disabled until a company is picked, so
 * the form says plainly which parts it is about to fill in for you, and become editable afterwards,
 * because the registry is frequently a few months behind reality.
 */
export function NewContact({ onCreated, onCancel }: {
  onCreated: (id: string) => void
  onCancel: () => void
}) {
  const [type, setType] = useState<ContactType>('Organisation')

  // Personne morale.
  const [legalName, setLegalName] = useState('')
  const [siren, setSiren] = useState('')
  const [legalForm, setLegalForm] = useState('')
  const [picked, setPicked] = useState<AnnuaireCompany | null>(null)

  // Personne physique.
  const [civility, setCivility] = useState('')
  const [lastName, setLastName] = useState('')
  const [firstName, setFirstName] = useState('')

  // Both.
  const [address, setAddress] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [notes, setNotes] = useState('')

  const [lookupOn, setLookupOn] = useState(annuaireEnabled)
  const [lookup, setLookup] = useState<Lookup>({ state: 'idle' })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const term = legalName.trim()
  const morale = type === 'Organisation'
  const searchable = morale && lookupOn && !picked && term.length >= 3

  /**
   * Debounced by 250ms and cancelled on every keystroke. The previous list is kept while a new query
   * is in flight and only dimmed: clearing it makes the panel flicker on every letter, and the answer
   * you were about to click disappears under the cursor.
   */
  useEffect(() => {
    if (!searchable) {
      setLookup({ state: 'idle' })
      return
    }

    const controller = new AbortController()

    const timer = setTimeout(() => {
      setLookup((current) => ({
        state: 'loading',
        previous: current.state === 'results' ? current.results : [],
      }))

      searchCompanies(term, controller.signal)
        .then((results) =>
          setLookup(results.length ? { state: 'results', results } : { state: 'empty' }),
        )
        .catch((failure: unknown) => {
          if (controller.signal.aborted) return
          setLookup(failure instanceof AnnuaireUnreachable ? { state: 'unreachable' } : { state: 'empty' })
        })
    }, 250)

    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [term, searchable])

  function choose(company: AnnuaireCompany) {
    setPicked(company)
    setLegalName(company.name)
    setSiren(formatSiren(company.siren))
    setLegalForm(company.legalForm ?? '')
    setAddress(
      [company.address, [company.postalCode, company.commune].filter(Boolean).join(' ')]
        .filter(Boolean)
        .join('\n'),
    )
    setLookup({ state: 'idle' })
  }

  const named = morale ? legalName.trim() : lastName.trim()

  async function create() {
    setBusy(true)
    setError(null)

    try {
      const created = await post<{ id: string }>('/api/contacts', {
        type,
        civility: civility || null,
        lastName: morale ? null : lastName.trim(),
        firstName: morale ? null : firstName.trim() || null,
        legalName: morale ? legalName.trim() : null,
        siren: morale ? siren.replace(/\s/g, '') || null : null,
        legalForm: morale ? legalForm || null : null,
        address: address || null,
        email: email || null,
        phone: phone || null,
        notes: notes || null,
      })

      onCreated(created.id)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Sheet
      title="Nouveau tiers"
      onClose={onCancel}
      footer={
        <div className="grid gap-2.5">
          {/* Say what leaves the machine, on the screen where it leaves it. */}
          <p className="m-0 type-caption text-muted">
            {lookupOn
              ? 'L’annuaire ne reçoit que ce que vous tapez dans le champ « Raison sociale ». Rien d’autre ne quitte ce poste.'
              : 'Aucune requête n’est envoyée. Tous les champs sont à saisir à la main.'}
          </p>

          <div className="flex items-center gap-2">
            <Switch
              checked={lookupOn}
              label="Interroger l’annuaire des entreprises"
              onChange={(next) => {
                setLookupOn(next)
                setAnnuaireEnabled(next)
                setLookup({ state: 'idle' })
              }}
            />

            <span className="flex-1" />

            <Button variant="secondary" onClick={onCancel}>Annuler</Button>
            <Button disabled={busy || !named} onClick={() => void create()}>Créer le tiers</Button>
          </div>
        </div>
      }
    >
      <Field label="Nature">
        <div className="flex gap-2">
          <NatureOption
            active={morale}
            icon={<Building2 size={14} strokeWidth={2} />}
            label="Personne morale"
            hint="société, association"
            onClick={() => setType('Organisation')}
          />
          <NatureOption
            active={!morale}
            icon={<User size={14} strokeWidth={2} />}
            label="Personne physique"
            hint="particulier, confrère"
            onClick={() => setType('Individual')}
          />
        </div>
      </Field>

      {morale ? (
        <>
          <div className="grid gap-1">
            <label className="grid gap-1">
              <span className="type-label text-ink-secondary">Raison sociale</span>

              <span className="relative flex items-center">
                {lookupOn && (
                  <Search
                    size={14}
                    strokeWidth={1.75}
                    className="pointer-events-none absolute left-2 text-muted"
                  />
                )}

                <Input
                  inputSize="lg"
                  autoFocus
                  value={legalName}
                  placeholder={lookupOn ? 'Raison sociale ou SIREN' : 'Raison sociale'}
                  className={cn('w-full', lookupOn && 'pl-7')}
                  onChange={(event) => {
                    setLegalName(event.target.value)
                    setPicked(null)
                  }}
                />

                {lookup.state === 'loading' && (
                  <Loader2 size={14} strokeWidth={2} className="absolute right-2 animate-spin text-muted" />
                )}
              </span>
            </label>

            {lookupOn && !picked && term.length < 3 && (
              <p className="m-0 type-caption text-muted">
                Trois caractères suffisent. Rien n’est envoyé avant la troisième lettre.
              </p>
            )}

            {picked && (
              <p className="m-0 flex items-center gap-1.5 type-caption text-success">
                <Check size={12} strokeWidth={2.5} />
                Renseigné depuis l’annuaire. Les champs restent modifiables.
              </p>
            )}

            <Results lookup={lookup} term={term} onChoose={choose} onFree={() => setPicked(null)} />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <Field label="SIREN">
              <Input
                inputSize="lg"
                className="font-mono tnum"
                value={siren}
                disabled={lookupOn && !picked}
                onChange={(event) => setSiren(event.target.value)}
              />
            </Field>

            <Field label="Forme juridique">
              <Input
                inputSize="lg"
                value={legalForm}
                disabled={lookupOn && !picked}
                onChange={(event) => setLegalForm(event.target.value)}
              />
            </Field>
          </div>
        </>
      ) : (
        <>
          <div className="grid grid-cols-[92px_minmax(0,1fr)] gap-3">
            <Field label="Civilité">
              <Input
                inputSize="lg"
                value={civility}
                placeholder="Mme"
                onChange={(event) => setCivility(event.target.value)}
              />
            </Field>

            <Field label="Nom">
              <Input
                inputSize="lg"
                autoFocus
                value={lastName}
                onChange={(event) => setLastName(event.target.value)}
              />
            </Field>
          </div>

          <Field label="Prénom">
            <Input inputSize="lg" value={firstName} onChange={(event) => setFirstName(event.target.value)} />
          </Field>
        </>
      )}

      <Field label="Adresse">
        <Textarea
          rows={2}
          value={address}
          disabled={morale && lookupOn && !picked}
          onChange={(event) => setAddress(event.target.value)}
        />
      </Field>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Téléphone">
          <Input
            inputSize="lg"
            className="font-mono tnum"
            value={phone}
            onChange={(event) => setPhone(event.target.value)}
          />
        </Field>

        <Field label="Courriel">
          <Input inputSize="lg" value={email} onChange={(event) => setEmail(event.target.value)} />
        </Field>
      </div>

      <Field label="Notes">
        <Textarea rows={3} value={notes} onChange={(event) => setNotes(event.target.value)} />
      </Field>

      {error && <p className="m-0 text-danger">{error}</p>}
    </Sheet>
  )
}

/** The four states of the lookup, each of which has to leave the form usable. */
function Results({ lookup, term, onChoose, onFree }: {
  lookup: Lookup
  term: string
  onChoose: (company: AnnuaireCompany) => void
  onFree: () => void
}) {
  if (lookup.state === 'idle') return null

  if (lookup.state === 'unreachable') {
    return (
      <div className="rounded-md border border-[#E8D5AE] border-l-[3px] border-l-[#8A5A10] bg-[#FDF8ED] px-3 py-2.5 text-[#6E4A0E]">
        <div className="text-[12px] font-semibold">Annuaire injoignable, saisie manuelle possible</div>
        <p className="m-0 mt-0.5 text-[11.5px] leading-[17px]">
          Vous êtes hors ligne ou le service ne répond pas. Les champs restent modifiables ; vous
          pourrez vérifier plus tard depuis la fiche.
        </p>
      </div>
    )
  }

  if (lookup.state === 'empty') {
    return (
      <div className="rounded-md border border-line bg-raised p-1 shadow-e2">
        <p className="m-0 px-2 py-2 text-[11.5px] leading-[17px] text-muted">
          Aucune entreprise ne correspond. Vérifiez l’orthographe, ou saisissez le SIREN directement.
        </p>
        <button
          type="button"
          onClick={onFree}
          className="flex h-[26px] w-full items-center gap-2 rounded-[3px] border-t border-line-subtle px-2 text-left text-[12px] hover:bg-hover"
        >
          Créer « {term} » comme tiers libre
          <span className="type-kbd ml-auto text-muted">⌘⏎</span>
        </button>
      </div>
    )
  }

  const results = lookup.state === 'loading' ? lookup.previous : lookup.results
  if (results.length === 0) return null

  return (
    <div
      className={cn(
        'rounded-md border border-line bg-raised p-1 shadow-e2',
        lookup.state === 'loading' && 'opacity-60',
      )}
    >
      <div className="type-group px-2 pt-1 pb-1 text-muted">Annuaire des entreprises · INSEE</div>

      {results.map((company) => (
        <button
          key={company.siren}
          type="button"
          onClick={() => onChoose(company)}
          className="grid w-full gap-px rounded-[3px] px-2 py-1.5 text-left hover:bg-brand-subtle"
        >
          <span className="flex items-center gap-2">
            <span className="truncate text-[12px] font-medium">{company.name}</span>
            {company.ceased && <Badge tone="accent">cessée</Badge>}
          </span>

          <span className="truncate font-mono text-[10.5px] text-muted">
            {formatSiren(company.siren)}
            {company.legalForm && ` · ${company.legalForm}`}
            {company.commune && ` · ${company.commune}`}
            {company.postalCode && ` (${company.postalCode})`}
          </span>
        </button>
      ))}
    </div>
  )
}

function NatureOption({ active, icon, label, hint, onClick }: {
  active: boolean
  icon: React.ReactNode
  label: string
  hint: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        'grid flex-1 gap-0.5 rounded-sm border px-3 py-2 text-left',
        active ? 'border-brand bg-brand-subtle text-brand-on-subtle' : 'border-line-strong hover:bg-hover',
      )}
    >
      <span className="flex items-center gap-1.5 text-[12.5px] font-medium">
        {icon}
        {label}
      </span>
      <span className="text-[11px] text-muted">{hint}</span>
    </button>
  )
}

/**
 * A switch, not a checkbox: this setting applies the moment it is flipped. The design system is
 * explicit that the two are never interchangeable.
 */
function Switch({ checked, label, onChange }: {
  checked: boolean
  label: string
  onChange: (next: boolean) => void
}) {
  const id = useRef(`switch-${Math.random().toString(36).slice(2)}`).current

  return (
    <span className="flex items-center gap-2">
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative h-[15px] w-[26px] shrink-0 rounded-full transition-colors',
          checked ? 'bg-brand' : 'bg-[#C6CCC2]',
        )}
      >
        <span
          className={cn(
            'absolute top-0.5 h-[11px] w-[11px] rounded-full bg-white transition-[left]',
            checked ? 'left-[13px]' : 'left-0.5',
          )}
        />
      </button>

      <label htmlFor={id} className="text-[11.5px] text-ink-secondary">{label}</label>
    </span>
  )
}
