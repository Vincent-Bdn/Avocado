import { useEffect, useState, type ReactNode } from 'react'
import { Check, ChevronDown, RefreshCw, X } from 'lucide-react'
import { createPortal } from 'react-dom'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Input } from './components/ui/input.js'
import { PageHeader } from './components/ui/page-header.js'
import { Panel } from './components/ui/panel.js'
import { cn } from './lib/utils.js'
import { centsToAmount, parseAmountToCents } from './lib/amount.js'
import type { PracticeSettings } from './types.js'
import { RecoveryKeyCard } from './wizard/StepRecovery.js'
import { RecoverySheet } from './wizard/RecoverySheet.js'
import { SecureKeyOptions, isSecured, nothingSecured, type SecuredBy } from './wizard/SecureKeyOptions.js'

interface RecoveryKeyState {
  code: string | null
  fingerprint: string | null
  createdAt: string | null
}

/** Two of nine, drawn once per visit. Enough to prove the sheet was fetched, short enough not to
 *  feel like an exam. */
function pickTwo(): [number, number] {
  const first = Math.floor(Math.random() * 9)
  let second = Math.floor(Math.random() * 8)
  if (second >= first) second += 1

  return first < second ? [first, second] : [second, first]
}

/**
 * Réglages. Full-width accordion sections separated by rules, so this page can keep growing as more
 * settings arrive without turning into a wall of half-empty cards.
 */
export function Settings() {
  const [key, setKey] = useState<RecoveryKeyState | null>(null)
  const [open, setOpen] = useState<string | null>('rate')
  const [error, setError] = useState<string | null>(null)

  const reload = () => {
    api<RecoveryKeyState>('/api/vault/recovery-key')
      .then(setKey)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }

  useEffect(reload, [])

  const toggle = (id: string) => setOpen((current) => (current === id ? null : id))

  return (
    <Panel>
      <PageHeader title="Réglages" meta={<span>Clé de récupération et contrôle du coffre</span>} />

      <div className="flex-1 overflow-y-auto">
        {error && <p className="px-4 py-3 text-danger">{error}</p>}

        <Section
          id="rate"
          title="Taux horaire du cabinet"
          summary="Point de départ des nouveaux dossiers"
          open={open === 'rate'}
          onToggle={toggle}
        >
          <HourlyRate />
        </Section>

        {key && !key.code && (
          <Section
            id="renew"
            title="Clé de récupération"
            summary="Non consultable sur ce coffre"
            open={open === 'renew'}
            onToggle={toggle}
          >
            <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
              Ce coffre a été créé avant que la clé ne soit conservée : elle ne peut donc plus être
              affichée ni contrôlée. Éditez-en une nouvelle pour retrouver ces deux possibilités. La
              fiche imprimée que vous détenez reste valable jusque-là.
            </p>

            <Regenerate onDone={reload} />
          </Section>
        )}

        {key?.code && (
          <>
            <Section
              id="check"
              title="Contrôle de la clé"
              summary="Vérifier que votre fiche est bien la bonne"
              open={open === 'check'}
              onToggle={toggle}
            >
              <QuarterlyCheck />
            </Section>

            <Section
              id="renew"
              title="Renouveler la clé"
              summary={key.fingerprint ? `Empreinte ${key.fingerprint}` : undefined}
              open={open === 'renew'}
              onToggle={toggle}
            >
              <p className="m-0 text-[12.5px] leading-[19px] text-muted">
                Clé actuelle : empreinte{' '}
                <span className="font-mono text-ink">{key.fingerprint}</span>
                {key.createdAt && `, créée le ${new Date(key.createdAt).toLocaleDateString('fr-FR')}`}.
              </p>

              <Regenerate onDone={reload} />
            </Section>
          </>
        )}
      </div>
    </Panel>
  )
}

/**
 * The rate a new dossier starts from, and only that. It is copied onto the dossier at creation, so
 * changing it here prices tomorrow's work and leaves every hour already recorded exactly as it was.
 */
function HourlyRate() {
  const [rate, setRate] = useState('')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<PracticeSettings>('/api/settings')
      .then((settings) => setRate(centsToAmount(settings.hourlyRateCents)))
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [])

  async function save() {
    const cents = parseAmountToCents(rate)

    if (cents === null) {
      setError('Indiquez un taux horaire positif, par exemple 240,00.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await api('/api/settings', { method: 'PUT', body: JSON.stringify({ hourlyRateCents: cents }) })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Ce taux ne sert qu’à pré-remplir un nouveau dossier. Chaque dossier garde ensuite le sien, et
        chaque ligne de temps peut porter un taux dérogatoire : le modifier ici ne retarife jamais du
        travail déjà saisi.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <Input
          inputSize="lg"
          className="w-28 font-mono tnum"
          invalid={Boolean(error)}
          value={rate}
          aria-label="Taux horaire"
          onChange={(event) => { setRate(event.target.value); setError(null) }}
        />
        <span className="text-[12.5px] text-muted">€ HT / heure</span>

        <Button disabled={busy} onClick={() => void save()}>Enregistrer</Button>

        {saved && (
          <span className="flex items-center gap-1.5 text-[12.5px] text-success">
            <Check size={13} strokeWidth={2.5} />
            Enregistré
          </span>
        )}
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </>
  )
}

/** Full-width row, separated by rules rather than boxed: this list is meant to grow. */
function Section({ id, title, summary, open, onToggle, children }: {
  id: string
  title: string
  summary?: string
  open: boolean
  onToggle: (id: string) => void
  children: ReactNode
}) {
  return (
    <section className="border-b border-line-subtle">
      <button
        type="button"
        onClick={() => onToggle(id)}
        className="flex w-full items-center gap-2 px-4 py-3 text-left hover:bg-hover"
      >
        <ChevronDown
          size={14}
          strokeWidth={2}
          className={cn(
            'shrink-0 text-ink-secondary transition-transform',
            !open && '-rotate-90',
          )}
        />

        <span className="text-[13px] font-medium">{title}</span>

        {summary && (
          <span className="truncate font-mono text-[11px] text-muted">{summary}</span>
        )}
      </button>

      {open && <div className="grid gap-3 px-4 pt-0 pb-4 pl-10">{children}</div>}
    </section>
  )
}

/** « Retrouvez votre fiche, et recopiez deux groupes. » */
function QuarterlyCheck() {
  const [indices, setIndices] = useState<[number, number]>(pickTwo)
  const [values, setValues] = useState<Record<number, string>>({})
  const [result, setResult] = useState<Record<number, boolean> | null>(null)
  const [busy, setBusy] = useState(false)

  async function verify() {
    setBusy(true)

    try {
      const response = await post<{ passed: boolean; correct: Record<number, boolean> }>(
        '/api/vault/recovery-key/check',
        { groups: Object.fromEntries(indices.map((index) => [index, values[index] ?? ''])) },
      )

      setResult(response.correct)
    } finally {
      setBusy(false)
    }
  }

  const passed = result !== null && indices.every((index) => result[index])

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px]">
        Retrouvez votre fiche, et recopiez deux groupes. Un dispositif de secours jamais testé est un
        dispositif qui ne marche pas.
      </p>

      <div className="flex flex-wrap gap-4">
        {indices.map((index) => (
          <label key={index} className="grid gap-1 text-[11px] text-muted">
            Groupe n° {index + 1}
            <span className="flex items-center gap-1.5">
              <Input
                inputSize="lg"
                maxLength={8}
                value={values[index] ?? ''}
                placeholder="······"
                className="w-[120px] text-center font-mono tracking-[0.12em] uppercase"
                onChange={(event) => {
                  setValues({ ...values, [index]: event.target.value })
                  setResult(null)
                }}
              />

              {/* The glyph, not just a colour: this is the one screen where being sure matters. */}
              {result?.[index] === true && <Check size={14} strokeWidth={2.5} className="text-success" />}
              {result?.[index] === false && <X size={14} strokeWidth={2.5} className="text-danger" />}
            </span>
          </label>
        ))}
      </div>

      {passed && (
        <p className="m-0 flex items-center gap-1.5 text-[12.5px] text-success">
          <Check size={13} strokeWidth={2.5} />
          Votre fiche est la bonne. Rangez-la où vous l’avez prise.
        </p>
      )}

      {result !== null && !passed && (
        <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
          Un groupe ne correspond pas. Vérifiez la ligne, ou éditez une nouvelle clé si la fiche est
          introuvable.
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        <Button
          disabled={busy || indices.some((index) => !values[index])}
          onClick={() => void verify()}
        >
          Vérifier
        </Button>

        <Button
          variant="secondary"
          onClick={() => {
            setIndices(pickTwo())
            setValues({})
            setResult(null)
          }}
        >
          Deux autres groupes
        </Button>
      </div>
    </>
  )
}

/** The renewal itself: the ochre statement of consequence, then the same securing step as at setup. */
function Regenerate({ onDone }: { onDone: () => void }) {
  const [issued, setIssued] = useState<RecoveryKeyState | null>(null)
  const [secured, setSecured] = useState<SecuredBy>(nothingSecured)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const createdOn = new Date().toLocaleDateString('fr-FR')

  async function regenerate() {
    setBusy(true)
    setError(null)

    try {
      setIssued(await post<RecoveryKeyState>('/api/vault/recovery-key/regenerate', {}))
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  /** Clears the issued key from the screen as well as reloading, or "Terminé" appears to do nothing. */
  function finish() {
    setIssued(null)
    setSecured(nothingSecured)
    onDone()
  }

  if (!issued?.code) {
    return (
      <>
        <div className="grid max-w-[72ch] gap-1 rounded-md border border-accent bg-accent-subtle px-3.5 py-3 text-warning">
          <strong className="text-[12.5px] font-semibold">Ce qui change</strong>
          <p className="m-0 text-[12px] leading-[18px]">
            Les sauvegardes faites <strong className="font-semibold">à partir de maintenant</strong>{' '}
            s’ouvriront avec la nouvelle clé. Les sauvegardes plus anciennes continueront d’exiger
            l’ancienne : gardez la fiche précédente tant que ces sauvegardes comptent.
          </p>
        </div>

        {error && <p className="m-0 text-danger">{error}</p>}

        <div className="flex">
          <Button disabled={busy} onClick={() => void regenerate()}>
            <RefreshCw size={13} strokeWidth={1.75} />
            {busy ? 'Génération…' : 'Éditer une nouvelle clé'}
          </Button>
        </div>
      </>
    )
  }

  return (
    <>
      <RecoveryKeyCard
        recoveryCode={issued.code}
        createdOn={createdOn}
        onCopied={() => setSecured((current) => ({ ...current, copied: true }))}
      />

      <div className="text-[12.5px] font-medium">Mettez cette nouvelle clé à l’abri :</div>

      <SecureKeyOptions
        recoveryCode={issued.code}
        fingerprint={issued.fingerprint ?? ''}
        createdOn={createdOn}
        secured={secured}
        onSecured={setSecured}
      />

      <div className="flex">
        <Button disabled={!isSecured(secured)} onClick={finish}>Terminé</Button>
      </div>

      {createPortal(
        <RecoverySheet
          recoveryCode={issued.code}
          fingerprint={issued.fingerprint ?? ''}
          createdOn={createdOn}
        />,
        document.body,
      )}
    </>
  )
}
