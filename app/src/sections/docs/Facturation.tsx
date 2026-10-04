import { useEffect, useRef, useState, type ReactNode } from 'react'
import { ApiError, post } from '../../api.js'
import { Input } from '../../components/ui/input.js'
import { centsToAmount } from '../../lib/amount.js'
import { PageHeader } from '../../components/ui/page-header.js'
import { Panel } from '../../components/ui/panel.js'
import { BillingCard, BillingStrip } from '../../lib/billing.js'
import { formatDuration, formatEuros } from '../../labels.js'
import { cn } from '../../lib/utils.js'
import type { BillingSummary } from '../../types.js'

/**
 * Ce qu'Avocado calcule en facturation, et pourquoi.
 *
 * <p><b>Le simulateur n'est pas une maquette.</b> Il envoie son scénario au serveur, qui construit
 * les lignes qu'un dossier contiendrait et les passe à la fonction qui calcule la vraie fiche. Les
 * trois écrans montrés sont les composants eux-mêmes, pas des copies. Une page d'explication qui
 * refait le calcul de son côté finit par expliquer autre chose que ce que fait le logiciel, et
 * « reste à facturer » a déjà été écrit trois fois à trois endroits qui n'étaient pas d'accord.</p>
 */

/** What the simulator posts. Mirrors the server's BillingScenario, field for field. */
interface Scenario {
  hourlyRateCents: number
  fixedFeeCents: number | null
  unbilledMinutes: number
  billedMinutes: number
  invoicedCents: number | null
  isPaid: boolean
  abandonedMinutes: number
  provisionCents: number
  disbursementCents: number
  subcontractedCents: number
}

const base: Scenario = {
  hourlyRateCents: 24_000,
  fixedFeeCents: null,
  unbilledMinutes: 210,
  billedMinutes: 720,
  invoicedCents: 240_000,
  isPaid: true,
  abandonedMinutes: 0,
  provisionCents: 0,
  disbursementCents: 0,
  subcontractedCents: 0,
}

/**
 * Les cas qu'on nous a posés, en un clic. Chacun est une question qu'une avocate a réellement
 * envoyée, ce qui est la seule raison d'en faire un bouton.
 */
const presets: { label: string; detail: string; scenario: Scenario }[] = [
  {
    label: 'À l’heure',
    detail: '12 h facturées 2 400 €, 3 h 30 en attente',
    scenario: base,
  },
  {
    label: 'Un mali consenti',
    detail: '30 h qui valent 7 200 €, facturées 6 000 €',
    scenario: { ...base, billedMinutes: 1_800, invoicedCents: 600_000, unbilledMinutes: 0 },
  },
  {
    label: 'Au forfait',
    detail: '3 000 € convenus, 13 h déjà travaillées',
    scenario: {
      ...base,
      fixedFeeCents: 300_000,
      billedMinutes: 480,
      invoicedCents: 100_000,
      isPaid: false,
      unbilledMinutes: 300,
    },
  },
  {
    label: 'Une provision',
    detail: '1 500 € reçus avant toute facture',
    scenario: {
      ...base,
      billedMinutes: 0,
      invoicedCents: null,
      unbilledMinutes: 600,
      provisionCents: 150_000,
    },
  },
  {
    label: 'Du temps soldé',
    detail: '5 h qui ne seront jamais facturées',
    scenario: { ...base, abandonedMinutes: 300, unbilledMinutes: 120 },
  },
]

export function FacturationDoc() {
  const [scenario, setScenario] = useState<Scenario>(base)
  const [summary, setSummary] = useState<BillingSummary | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let current = true

    post<BillingSummary>('/api/billing/simulate', scenario)
      .then((answer) => { if (current) { setSummary(answer); setError(null) } })
      .catch((failure: unknown) => {
        if (current) setError(failure instanceof ApiError ? failure.message : String(failure))
      })

    return () => { current = false }
  }, [scenario])

  const set = (patch: Partial<Scenario>) => setScenario((current) => ({ ...current, ...patch }))

  return (
    <Panel>
      <PageHeader
        title="Facturation"
        meta={<span>Ce qu’Avocado calcule, et pourquoi. Tout est hors taxes.</span>}
      />

      <div className="flex-1 overflow-y-auto">
        <Rules />

        <section className="border-t border-line-strong">
          <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
            <h2 className="m-0 type-label text-ink-secondary">Essayer</h2>
            <p className="m-0 text-[11.5px] leading-4 text-muted">
              Changez les chiffres, les écrans se recalculent. Ce sont les vrais.
            </p>
          </header>

          <div className="px-4 py-3.5">
            <div className="flex flex-wrap gap-1.5">
              {presets.map((preset) => (
                <button
                  key={preset.label}
                  type="button"
                  title={preset.detail}
                  onClick={() => setScenario(preset.scenario)}
                  className="rounded-[5px] border border-line px-2.5 py-1 text-left text-[12px] leading-4 hover:bg-hover"
                >
                  {preset.label}
                  <span className="block font-mono text-[10.5px] text-muted">{preset.detail}</span>
                </button>
              ))}
            </div>

            <div className="mt-3.5 grid gap-5 min-[1100px]:grid-cols-[300px_minmax(0,1fr)]">
              <Controls scenario={scenario} set={set} />

              <div className="grid content-start gap-4">
                {error && <p className="m-0 text-danger">{error}</p>}
                {summary && <Screens summary={summary} scenario={scenario} />}
              </div>
            </div>
          </div>
        </section>
      </div>
    </Panel>
  )
}

/* -------------------------------------------------------------------------- les règles */

function Rules() {
  return (
    <section>
      <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
        <h2 className="m-0 type-label text-ink-secondary">Les règles</h2>
        <p className="m-0 text-[11.5px] leading-4 text-muted">Quatre, et rien d’autre</p>
      </header>

      <div className="grid gap-3.5 px-4 py-3.5">
        <Rule title="Une heure vaut durée × taux">
          Le taux du dossier, ou celui de la ligne quand vous en avez fixé un autre. C’est le seul
          calcul qu’Avocado fasse sur votre temps.
        </Rule>

        <Rule title="Une facture emporte ses lignes">
          Vous cochez les lignes de temps, Avocado propose leur valeur, vous changez le montant si
          vous voulez. Les lignes cochées quittent le reste à facturer. L’écart entre le montant et
          la valeur des heures est un <Term>boni</Term> au-dessus, un <Term>mali</Term> en dessous.
        </Rule>

        <Rule title="Un forfait remplace les heures">
          Quand un prix est convenu d’avance, le reste à facturer devient « forfait moins déjà
          facturé ». Les heures continuent d’être saisies, elles ne décident plus rien et servent
          à mesurer le boni ou le mali.
        </Rule>

        <Rule title="Les mouvements ne sont pas de la facturation">
          Une provision est de l’argent reçu : elle compte dans l’encaissé, jamais dans le reste à
          facturer. Un débours est une avance faite pour le client : sa propre ligne, à lui
          refacturer. Une rétrocession est ce que le dossier a coûté au cabinet, sans effet sur ce
          que le client doit.
        </Rule>

        <p className="m-0 text-[12px] leading-[18px] text-muted">
          Avocado n’émet aucune facture et ne calcule aucune TVA. Il note ce qui est parti, pour que
          ce qui reste soit connu.
        </p>
      </div>
    </section>
  )
}

const Rule = ({ title, children }: { title: string; children: ReactNode }) => (
  <div>
    <h3 className="m-0 text-[12.5px] leading-[18px] font-semibold">{title}</h3>
    <p className="m-0 mt-0.5 max-w-[68ch] text-[12.5px] leading-[19px] text-ink-secondary">
      {children}
    </p>
  </div>
)

const Term = ({ children }: { children: ReactNode }) => (
  <strong className="font-semibold text-ink">{children}</strong>
)

/* -------------------------------------------------------------------------- les réglages */

function Controls({ scenario, set }: {
  scenario: Scenario
  set: (patch: Partial<Scenario>) => void
}) {
  return (
    <div className="grid content-start gap-2.5 rounded-md border border-line-subtle bg-sunken p-3">
      <Money
        label="Taux horaire"
        cents={scenario.hourlyRateCents}
        onChange={(cents) => set({ hourlyRateCents: cents ?? 0 })}
      />

      <Hours
        label="Heures en attente"
        hint="saisies, rattachées à aucune facture"
        minutes={scenario.unbilledMinutes}
        onChange={(minutes) => set({ unbilledMinutes: minutes })}
      />

      <div className="border-t border-line-subtle pt-2.5">
        <Hours
          label="Heures d’une facture"
          hint="les lignes qu’elle a emportées"
          minutes={scenario.billedMinutes}
          onChange={(minutes) => set({ billedMinutes: minutes })}
        />

        {scenario.billedMinutes > 0 && (
          <div className="mt-2.5 grid gap-2.5">
            <Money
              label="Montant facturé"
              hint={`à la valeur des heures : ${formatEuros(
                scenario.hourlyRateCents * scenario.billedMinutes / 60,
              )}`}
              cents={scenario.invoicedCents}
              onChange={(cents) => set({ invoicedCents: cents })}
            />

            <label className="flex items-center gap-2 text-[12px]">
              <input
                type="checkbox"
                checked={scenario.isPaid}
                onChange={(event) => set({ isPaid: event.target.checked })}
              />
              Payée
            </label>
          </div>
        )}
      </div>

      <div className="grid gap-2.5 border-t border-line-subtle pt-2.5">
        <Money
          label="Forfait convenu"
          hint="vide : le dossier est à l’heure"
          cents={scenario.fixedFeeCents}
          onChange={(cents) => set({ fixedFeeCents: cents })}
        />

        <Hours
          label="Heures soldées en mali"
          hint="que vous ne facturerez pas"
          minutes={scenario.abandonedMinutes}
          onChange={(minutes) => set({ abandonedMinutes: minutes })}
        />
      </div>

      <div className="grid gap-2.5 border-t border-line-subtle pt-2.5">
        <Money
          label="Provision reçue"
          cents={scenario.provisionCents}
          onChange={(cents) => set({ provisionCents: cents ?? 0 })}
        />

        <Money
          label="Débours avancé"
          cents={scenario.disbursementCents}
          onChange={(cents) => set({ disbursementCents: cents ?? 0 })}
        />

        <Money
          label="Rétrocession"
          cents={scenario.subcontractedCents}
          onChange={(cents) => set({ subcontractedCents: cents ?? 0 })}
        />
      </div>
    </div>
  )
}

/**
 * Un montant, et une durée en heures.
 *
 * <p>La saisie est tolérante : vide vaut « rien », la virgule et le point marchent tous les deux, et
 * les espaces sont ignorés. C'est une page qui s'essaie, pas un formulaire qui enregistre, alors
 * rien n'est refusé.</p>
 *
 * <p>Le champ ne se recale sur la valeur que lorsqu'elle a changé ailleurs, un préréglage par
 * exemple. Se recaler sur celle qu'il vient lui-même de produire mangeait la virgule de « 2, »
 * avant qu'on ait pu taper les décimales.</p>
 */
function useTolerantField<T>(value: T, format: (value: T) => string, parse: (text: string) => T) {
  const [text, setText] = useState(() => format(value))
  const emitted = useRef(value)

  useEffect(() => {
    if (value !== emitted.current) {
      emitted.current = value
      setText(format(value))
    }
  }, [value, format])

  return {
    text,
    change(next: string) {
      setText(next)
      const parsed = parse(next)
      emitted.current = parsed

      return parsed
    },
  }
}

/** Les chiffres, la virgule et le point. Tout le reste est du décor et ne compte pas. */
const digits = (text: string) => text.replace(/[^\d.,-]/g, '').replace(',', '.')

const formatCents = (cents: number | null) => (cents === null ? '' : centsToAmount(cents))

function parseCents(text: string): number | null {
  if (text.trim() === '') return null

  const value = Number(digits(text))

  return Number.isFinite(value) && value >= 0 ? Math.round(value * 100) : null
}

function Money({ label, hint, cents, onChange }: {
  label: string
  hint?: string
  cents: number | null
  onChange: (cents: number | null) => void
}) {
  const field = useTolerantField(cents, formatCents, parseCents)

  return (
    <Field label={label} hint={hint}>
      <Input
        className="w-full font-mono tnum"
        inputMode="decimal"
        value={field.text}
        placeholder="aucun"
        onChange={(event) => onChange(field.change(event.target.value))}
      />
    </Field>
  )
}

const formatHours = (minutes: number) => String(minutes / 60).replace('.', ',')

function parseHours(text: string): number {
  const value = Number(digits(text))

  return Number.isFinite(value) && value >= 0 ? Math.round(value * 60) : 0
}

function Hours({ label, hint, minutes, onChange }: {
  label: string
  hint?: string
  minutes: number
  onChange: (minutes: number) => void
}) {
  const field = useTolerantField(minutes, formatHours, parseHours)

  return (
    <Field label={label} hint={hint}>
      <Input
        className="w-full font-mono tnum"
        inputMode="decimal"
        value={field.text}
        placeholder="0"
        onChange={(event) => onChange(field.change(event.target.value))}
      />
    </Field>
  )
}

const Field = ({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) => (
  <label className="grid gap-1">
    <span className="text-[12px] leading-4 font-medium">{label}</span>
    {children}
    {hint && <span className="font-mono text-[10.5px] leading-4 text-muted">{hint}</span>}
  </label>
)

/* -------------------------------------------------------------------------- les écrans */

function Screens({ summary, scenario }: { summary: BillingSummary; scenario: Scenario }) {
  return (
    <>
      <Screen where="Onglet Facturation" of="le dossier">
        <BillingCard summary={summary} />
      </Screen>

      <div className="grid gap-4 min-[760px]:grid-cols-[208px_minmax(0,1fr)]">
        <Screen where="Colonne de droite" of="la fiche du dossier">
          <BillingStrip summary={summary} hourlyRateCents={scenario.hourlyRateCents} />
        </Screen>

        <Screen where="Accueil" of="la ligne sous « Reste à facturer »">
          <div className="grid h-7 grid-cols-[minmax(0,1fr)_66px_92px] items-center gap-2 rounded-sm border border-[#F2E7D2] bg-[#FFFDF8] px-3.5">
            <span className="truncate text-[11.5px]">Votre dossier</span>
            <span className="text-right font-mono text-[11px] text-ink-secondary tnum">
              {summary.fixedFeeCents !== null ? 'forfait' : formatDuration(summary.unbilledMinutes)}
            </span>
            <span className="text-right font-mono text-[11.5px] font-medium tnum">
              {formatEuros(summary.leftToBillCents)}
            </span>
          </div>

          <p className="m-0 mt-1.5 text-[11.5px] leading-4 text-muted">
            Le dossier n’y figure que s’il reste quelque chose à facturer.
          </p>
        </Screen>
      </div>

      <Screen where="Honoraires" of="le graphique de l’accueil">
        <div className="flex items-center gap-2.5 rounded-sm border border-line-subtle bg-[#F8F9F6] px-3 py-[7px]">
          <span className="min-w-0 flex-1 text-[11px] leading-[15px] text-ink-secondary">
            Facturable <strong className="font-semibold">{formatEuros(summary.workedCents)}</strong>{' '}
            · {summary.subcontractedCents > 0 ? 'net facturé ' : 'facturé '}
            <strong className="font-semibold">{formatEuros(summary.netCents)}</strong>
          </span>

          {summary.varianceCents !== 0 && (
            <span
              className={cn(
                'shrink-0 font-mono text-[10.5px] tnum',
                summary.varianceCents > 0 ? 'text-success' : 'text-warning',
              )}
            >
              {summary.varianceCents > 0 ? 'boni + ' : 'mali − '}
              {formatEuros(Math.abs(summary.varianceCents))}
            </span>
          )}
        </div>

        <p className="m-0 mt-1.5 text-[11.5px] leading-4 text-muted">
          Le graphique additionne tous les dossiers sur douze mois. Ici, celui-ci seul.
        </p>
      </Screen>

      <Why summary={summary} scenario={scenario} />
    </>
  )
}

const Screen = ({ where, of: context, children }: {
  where: string
  of: string
  children: ReactNode
}) => (
  <div>
    <h3 className="m-0 mb-1.5 flex flex-wrap items-baseline gap-x-1.5 text-[11.5px] leading-4">
      <span className="font-semibold">{where}</span>
      <span className="text-muted">{context}</span>
    </h3>
    {children}
  </div>
)

/* -------------------------------------------------------------------------- pourquoi */

/**
 * Chaque chiffre affiché, dit en une phrase, avec les montants du scénario dedans.
 *
 * <p>Écrit à partir du résultat et non du scénario : si le modèle change, ces phrases changent avec
 * lui plutôt que de rester vraies sur le papier.</p>
 */
function Why({ summary, scenario }: { summary: BillingSummary; scenario: Scenario }) {
  const lines: [string, string][] = []

  lines.push([
    summary.fixedFeeCents === null ? 'Reste à facturer' : 'Reste du forfait',
    summary.fixedFeeCents === null
      ? `${formatEuros(summary.leftToBillCents)} : la valeur des heures qu’aucune facture n’a prises.`
      : `${formatEuros(summary.fixedFeeCents)} convenus moins ${formatEuros(summary.invoicedCents)} déjà facturés, soit ${formatEuros(summary.leftToBillCents)}. Les heures n’entrent pas dans ce calcul.`,
  ])

  lines.push([
    'Travaillé',
    `${formatEuros(summary.workedCents)} : toutes les heures facturables au taux du dossier, facturées ou non.`,
  ])

  if (summary.varianceCents !== 0) {
    const worked = summary.invoicedCents - summary.varianceCents

    lines.push([
      summary.varianceCents > 0 ? 'Boni' : 'Mali',
      `${formatEuros(summary.invoicedCents)} facturés pour des heures qui valaient ${formatEuros(worked)}.`
      + (scenario.abandonedMinutes > 0
        ? ` Les ${formatDuration(scenario.abandonedMinutes)} soldées y sont, pour toute leur valeur.`
        : ''),
    ])
  }

  if (summary.owedCents > 0) {
    lines.push([
      'Reste dû',
      `${formatEuros(summary.owedCents)} : facturés, pas encore encaissés.`,
    ])
  }

  if (summary.owedCents < 0) {
    lines.push([
      'Payé d’avance',
      `${formatEuros(-summary.owedCents)} reçus au-delà de ce qui a été facturé. C’est de la trésorerie, pas du travail facturé : le reste à facturer n’en tient pas compte.`,
    ])
  }

  if (summary.disbursementsCents > 0) {
    lines.push([
      'Débours',
      `${formatEuros(summary.disbursementsCents)} avancés pour le client, à lui réclamer. Sa propre ligne, jamais mélangée aux honoraires.`,
    ])
  }

  if (summary.subcontractedCents > 0) {
    lines.push([
      'Net de sous-traitance',
      `${formatEuros(summary.netCents)} restent au cabinet après ${formatEuros(summary.subcontractedCents)} rétrocédés. Le client doit toujours ${formatEuros(summary.invoicedCents)}.`,
    ])
  }

  return (
    <div className="rounded-md border border-line-subtle px-3.5 py-3">
      <h3 className="m-0 mb-2 text-[11.5px] leading-4 font-semibold">Pourquoi ces chiffres</h3>

      <dl className="m-0 grid gap-1.5">
        {lines.map(([label, text]) => (
          <div key={label} className="flex flex-wrap items-baseline gap-x-2 text-[12px] leading-[18px]">
            <dt className="type-group m-0 text-ink-secondary">{label}</dt>
            <dd className="m-0 min-w-0 flex-1 text-ink-secondary">{text}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
