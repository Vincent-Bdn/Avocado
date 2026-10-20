import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { ChevronDown, Loader2, X } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { formatEuros, formatEurosRounded } from '../labels.js'
import { HonorairesChart, type Honoraires } from './Honoraires.js'

interface Period {
  label: string
  from: string
  to: string
}

interface HonoraireMatter {
  matterId: string
  reference: string
  name: string
  clientName: string | null
  invoicedCents: number
  paidCents: number
  unpaidCents: number
  billableCents: number
  varianceCents: number
  invoiceCount: number
}

interface HonorairesView {
  from: string
  to: string
  label: string
  honoraires: Honoraires
  matters: HonoraireMatter[]
}

/**
 * La facturation. The first thing the accueil shows, and the biggest thing on it.
 *
 * <p>It was an 88px chart in the right-hand column under the échéances, which is where it sat for the
 * wrong reason: the échéances are urgent and the facturation is important, and urgency had won the
 * layout. « Le tableau de factu en plus gros et couleurs plus différenciées, la page de présentation
 * générale doit s'ouvrir sur ça en premier. »</p>
 *
 * <p><b>Three things asked for separately turn out to be one screen.</b> A period to choose;
 * facturé, encaissé and travaillé over that period; and the dossiers behind a column, to know whom to
 * chase. They share one endpoint, because clicking septembre on the chart <em>is</em> asking for the
 * period « septembre » — so the list under the chart is computed exactly as the column was, which two
 * endpoints would have let drift.</p>
 */
export function Facturation({ onOpenMatter }: { onOpenMatter: (id: string) => void }) {
  const [periods, setPeriods] = useState<Period[]>([])
  const [chosen, setChosen] = useState<Period | null>(null)
  const [custom, setCustom] = useState(false)

  const [view, setView] = useState<HonorairesView | null>(null)
  const [error, setError] = useState<string | null>(null)

  /**
   * The clicked column, fetched on its own.
   *
   * <p>Separate state, not a narrowing of <c>view</c>: re-fetching the period as a single month
   * collapses <c>months</c> to one entry, and the index that was clicked then points past the end of
   * it. The panel would unmount itself the moment it had the data it asked for.</p>
   */
  const [picked, setPicked] = useState<number | null>(null)
  const [drill, setDrill] = useState<HonorairesView | null>(null)

  useEffect(() => {
    api<Period[]>('/api/honoraires/periods')
      .then((offered) => {
        setPeriods(offered)
        // « 12 derniers mois » is the resting view: it answers « ai-je facturé ce que j'ai
        // travaillé » without her having chosen anything.
        setChosen(offered.find((period) => period.label.startsWith('12')) ?? offered[0] ?? null)
      })
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [])

  useEffect(() => {
    if (chosen === null) return

    setPicked(null)
    setDrill(null)

    api<HonorairesView>(`/api/honoraires?from=${chosen.from}&to=${chosen.to}`)
      .then((fetched) => { setView(fetched); setError(null) })
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [chosen])

  const pick = useCallback((index: number) => {
    const month = view?.honoraires.months[index]
    if (month === undefined) return

    if (picked === index) {
      setPicked(null)
      setDrill(null)
      return
    }

    setPicked(index)
    setDrill(null)

    api<HonorairesView>(`/api/honoraires?from=${month.month}&to=${endOfMonth(month.month)}`)
      .then(setDrill)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [view, picked])

  if (error !== null) {
    return (
      <section className="rounded-md border border-line-subtle bg-panel px-3.5 py-3">
        <p className="m-0 text-[12.5px] text-danger">{error}</p>
      </section>
    )
  }

  // A reserved block rather than a spinner: the chart is the first thing on the page and the rest of
  // the accueil must not jump down when it arrives.
  if (view === null) {
    return <section className="h-[380px] rounded-md border border-line-subtle bg-panel" />
  }

  const { honoraires } = view

  return (
    <section className="grid gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="type-title m-0">Facturation</h2>
        <span className="type-caption text-muted">{view.label}</span>
        <span className="flex-1" />

        {periods.map((period) => (
          <Chip
            key={period.label}
            active={!custom && chosen?.label === period.label}
            onClick={() => { setCustom(false); setChosen(period) }}
          >
            {period.label}
          </Chip>
        ))}

        <Chip active={custom} onClick={() => setCustom((open) => !open)}>
          Dates
          <ChevronDown size={11} strokeWidth={2} />
        </Chip>
      </div>

      {/* « Sur la période du 1er janvier au 12 mars j'ai facturé xx. » The two dates, said as she
          says them, with the buckets clamped so a period starting on the 12th counts from the 12th. */}
      {custom && (
        <CustomPeriod
          from={view.from.slice(0, 10)}
          to={view.to.slice(0, 10)}
          onChoose={(from, to) => setChosen({ label: 'Dates', from, to })}
        />
      )}

      {/* Her three figures, in her order, at the size of the question. Travaillé is the time recorded
          in the period at the dossier's rate: what the work was worth, billed or not, which is the
          only reading that makes the gap on the chart below mean anything. */}
      <div className="grid grid-cols-3 overflow-hidden rounded-md border border-line-subtle bg-panel">
        <Headline label="Facturé" cents={honoraires.invoicedCents} tone="text-ink">
          {honoraires.unpaidCents > 0 && (
            <>
              dont <strong className="font-medium">{formatEurosRounded(honoraires.unpaidCents)}</strong>{' '}
              non payés
            </>
          )}
        </Headline>

        <Headline label="Encaissé" cents={honoraires.paidCents} tone="text-brand-on-subtle">
          {honoraires.subcontractedCents > 0 &&
            `${formatEurosRounded(honoraires.subcontractedCents)} rétrocédés`}
        </Headline>

        <Headline label="Travaillé" cents={honoraires.billableCents} tone="text-ink" last>
          {honoraires.varianceCents !== 0 && (
            <span className={honoraires.varianceCents > 0 ? 'text-success' : 'text-warning'}>
              {honoraires.varianceCents > 0 ? 'boni + ' : 'mali − '}
              {formatEurosRounded(Math.abs(honoraires.varianceCents))}
            </span>
          )}
        </Headline>
      </div>

      <p className="m-0 type-caption text-muted">
        Tout est hors taxes : le temps passé, le facturé, l’encaissé. Avocado n’émet aucune facture et
        ne calcule aucune TVA.
      </p>

      <HonorairesChart data={honoraires} picked={picked} onPick={pick} />

      {picked !== null && (
        <MonthDossiers
          view={drill}
          onOpenMatter={onOpenMatter}
          onClose={() => { setPicked(null); setDrill(null) }}
        />
      )}
    </section>
  )
}

/**
 * The dossiers behind one column.
 *
 * <p>Ordered by what is owed, because the question is « permet de savoir qui relancer » and not
 * « qui a le plus facturé ». A dossier worked and not billed is in the list too: it is the other half
 * of what the chart draws, and the one she would never think to go looking for.</p>
 */
function MonthDossiers({ view, onOpenMatter, onClose }: {
  view: HonorairesView | null
  onOpenMatter: (id: string) => void
  onClose: () => void
}) {
  if (view === null) {
    return (
      <div className="flex items-center gap-2 rounded-md border border-[#9DBCA8] bg-panel px-3.5 py-3 text-[12px] text-muted">
        <Loader2 size={13} className="animate-spin" />
        Lecture du mois…
      </div>
    )
  }

  const owed = view.matters.reduce((total, matter) => total + matter.unpaidCents, 0)

  return (
    <div className="overflow-hidden rounded-md border border-[#9DBCA8] bg-panel">
      <div className="flex items-center gap-2 border-b border-line-subtle bg-[#EDF1EC] px-3.5 py-2">
        <span className="text-[12.5px] font-medium">{view.label}</span>

        <span className="type-caption text-muted">
          {view.matters.length} dossier{view.matters.length > 1 ? 's' : ''}
          {owed > 0 && ` · ${formatEurosRounded(owed)} à relancer`}
        </span>

        <span className="flex-1" />

        <Button variant="secondary" size="iconSm" aria-label="Fermer" onClick={onClose}>
          <X size={12} strokeWidth={2} />
        </Button>
      </div>

      <div className="grid grid-cols-[minmax(0,1fr)_96px_96px_96px] gap-2 border-b border-line-subtle px-3.5 py-1.5 type-caption text-muted">
        <span>Dossier</span>
        <span className="text-right">Facturé</span>
        <span className="text-right">Non payé</span>
        <span className="text-right">Travaillé</span>
      </div>

      <div className="max-h-[260px] overflow-y-auto">
        {view.matters.map((matter) => (
          <button
            key={matter.matterId}
            type="button"
            onClick={() => onOpenMatter(matter.matterId)}
            className="grid w-full grid-cols-[minmax(0,1fr)_96px_96px_96px] items-center gap-2 border-b border-line-subtle px-3.5 py-1.5 text-left last:border-b-0 hover:bg-hover"
          >
            <span className="grid min-w-0">
              <span className="truncate text-[12px] leading-4">{matter.name}</span>
              <span className="truncate text-[10.5px] leading-[14px] text-muted">
                {matter.reference}
                {matter.clientName && ` · ${matter.clientName}`}
              </span>
            </span>

            {/* An em dash, not « 0,00 € »: a dossier worked and not billed has no facturé, and a zero
                in a money column invites the reading that something was billed for nothing. */}
            <span className="text-right font-mono text-[11.5px] tnum">
              {matter.invoicedCents === 0 ? '—' : formatEuros(matter.invoicedCents)}
            </span>

            <span
              className={cn(
                'text-right font-mono text-[11.5px] tnum',
                matter.unpaidCents > 0 ? 'font-medium text-warning' : 'text-muted',
              )}
            >
              {matter.unpaidCents === 0 ? '—' : formatEuros(matter.unpaidCents)}
            </span>

            <span className="text-right font-mono text-[11.5px] text-ink-secondary tnum">
              {matter.billableCents === 0 ? '—' : formatEuros(matter.billableCents)}
            </span>
          </button>
        ))}

        {view.matters.length === 0 && (
          <p className="m-0 px-3.5 py-3 type-caption text-muted">
            Aucune facture et aucun temps saisi sur ce mois.
          </p>
        )}
      </div>
    </div>
  )
}

/** Two dates. Applied on « Voir », not on each keystroke: a half-typed year is not a period. */
function CustomPeriod({ from, to, onChoose }: {
  from: string
  to: string
  onChoose: (from: string, to: string) => void
}) {
  const [start, setStart] = useState(from)
  const [finish, setFinish] = useState(to)

  const wrong = finish < start

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border border-line-subtle bg-sunken px-3 py-2.5">
      <label className="grid gap-1 text-[11.5px]">
        Du
        <Input inputSize="sm" type="date" value={start} onChange={(event) => setStart(event.target.value)} />
      </label>

      <label className="grid gap-1 text-[11.5px]">
        au
        <Input inputSize="sm" type="date" value={finish} onChange={(event) => setFinish(event.target.value)} />
      </label>

      <Button size="sm" disabled={wrong} onClick={() => onChoose(start, finish)}>Voir</Button>

      {wrong && <span className="text-[11.5px] text-danger">La fin est avant le début.</span>}
    </div>
  )
}

const Chip = ({ active, onClick, children }: {
  active?: boolean
  onClick: () => void
  children: ReactNode
}) => (
  <button
    type="button"
    onClick={onClick}
    className={cn(
      'flex items-center gap-1 rounded-sm border px-2 py-[3px] text-[11.5px] leading-4',
      active
        ? 'border-[#9DBCA8] bg-[#EDF1EC] font-medium text-ink'
        : 'border-line-subtle text-ink-secondary hover:bg-hover',
    )}
  >
    {children}
  </button>
)

function Headline({ label, cents, tone, last, children }: {
  label: string
  cents: number
  tone: string
  last?: boolean
  children?: ReactNode
}) {
  return (
    <div className={cn('px-3.5 py-2.5', !last && 'border-r border-line-subtle')}>
      <div className="text-[11.5px] leading-4 text-muted">{label}</div>

      <div
        className={cn(
          'font-mono text-[26px] leading-8 font-semibold tracking-[-0.02em] tnum',
          cents === 0 ? 'text-ink-secondary' : tone,
        )}
      >
        {formatEuros(cents)}
      </div>

      {/* Always rendered, so the three columns keep the same height whether or not there is a
          secondary line under the figure. */}
      <div className="min-h-[15px] font-mono text-[10.5px] leading-[15px] text-muted tnum">
        {children}
      </div>
    </div>
  )
}

/** The last day of the month an ISO first-of-month names. */
function endOfMonth(iso: string): string {
  const [year, month] = iso.slice(0, 10).split('-').map(Number)
  const last = new Date(Date.UTC(year!, month!, 0)).getUTCDate()

  return `${iso.slice(0, 8)}${String(last).padStart(2, '0')}`
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)
