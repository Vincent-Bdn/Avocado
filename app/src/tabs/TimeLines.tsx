import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApiError, api } from '../api.js'
import { cn } from '../lib/utils.js'
import { formatDuration, formatEuros } from '../labels.js'

/** A line of temps passé no facture has taken yet. */
export interface UnbilledEntry {
  id: string
  date: string
  task: string
  durationMinutes: number
  isBillable: boolean
  amountCents: number
  invoiceId: string | null
}

/**
 * The lines a facture can still settle, oldest first.
 *
 * <p>Oldest first, not newest: a facture pays for the work that has been waiting, and the
 * pre-selection below fills up to the amount in that order.</p>
 */
export function useUnbilledLines(matterId: string, when = true) {
  const [lines, setLines] = useState<UnbilledEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    if (!when) return

    api<{ items: UnbilledEntry[] }>(`/api/matters/${matterId}/time-entries`)
      .then((page) => setLines(
        page.items
          .filter((entry) => entry.isBillable && entry.invoiceId === null)
          .sort((left, right) => left.date.localeCompare(right.date)),
      ))
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
  }, [matterId, when])

  useEffect(reload, [reload])

  return { lines, error, reload }
}

/**
 * Which lines to tick before she has looked, for a facture whose amount is already known.
 *
 * <p>Oldest first until the next line would overshoot, so a 600 € facture on a dossier with 2 000 €
 * of waiting hours arrives with roughly the right three ticked rather than all of them. It is a
 * starting point and it is visibly one: the strip states what the selection is worth against what
 * the facture says, so a wrong guess is obvious rather than silent.</p>
 *
 * <p><b>Nothing, when the amount is unknown.</b> It used to fall back to ticking everything, which
 * is the right default for « facturer du temps passé » — that screen exists to bill the hours, and
 * it sets its own selection. Here it was the wrong one twice over: a PDF whose HT could not be read
 * would silently propose to settle every unbilled hour on the dossier, which is the bug this whole
 * picker was added to fix, pointing the other way.</p>
 */
export function preselect(lines: UnbilledEntry[], amountCents: number | null): Set<string> {
  if (amountCents === null || amountCents <= 0) {
    return new Set()
  }

  const chosen = new Set<string>()
  let running = 0

  for (const line of lines) {
    if (running + line.amountCents > amountCents) continue

    chosen.add(line.id)
    running += line.amountCents
  }

  // A facture smaller than every single line still has to start somewhere, and an empty picker reads
  // as « there is nothing to settle », which is a different and wrong statement.
  if (chosen.size === 0 && lines.length > 0) {
    chosen.add(lines[0]!.id)
  }

  return chosen
}

/**
 * The line picker, shared by the three ways a facture comes into existence: établie depuis le temps
 * passé, déposée en PDF, saisie à la main.
 *
 * <p>It used to exist only inside « Facturer du temps passé ». The other two created a facture that
 * consumed nothing, so the hours went on waiting and the facture reported no boni and no mali — which
 * is how a dossier ends up soldé en mali for hours that had in fact been billed.</p>
 */
export function LinePicker({ lines, selected, onChange, height = 'max-h-[240px]' }: {
  lines: UnbilledEntry[]
  selected: Set<string>
  onChange: (next: Set<string>) => void
  height?: string
}) {
  const toggle = (id: string) => {
    const next = new Set(selected)
    next.has(id) ? next.delete(id) : next.add(id)
    onChange(next)
  }

  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-2 type-caption text-muted">
        <span>
          {selected.size} ligne{selected.size > 1 ? 's' : ''} sur {lines.length}
        </span>
        <span className="flex-1" />
        <button
          type="button"
          className="text-brand-on-subtle underline-offset-2 hover:underline"
          onClick={() => onChange(new Set(lines.map((line) => line.id)))}
        >
          Tout
        </button>
        <button
          type="button"
          className="text-brand-on-subtle underline-offset-2 hover:underline"
          onClick={() => onChange(new Set())}
        >
          Rien
        </button>
      </div>

      <div className={cn('overflow-y-auto rounded-sm border border-line-subtle', height)}>
        {lines.map((entry) => (
          <label
            key={entry.id}
            className="flex cursor-pointer items-center gap-2.5 border-b border-line-subtle px-2.5 py-1.5 text-[12px] last:border-b-0 hover:bg-hover"
          >
            <input type="checkbox" checked={selected.has(entry.id)} onChange={() => toggle(entry.id)} />

            <span className="w-[74px] shrink-0 font-mono text-[11.5px] text-ink-secondary tnum">
              {new Date(entry.date).toLocaleDateString('fr-FR')}
            </span>

            <span className="min-w-0 flex-1 truncate">{entry.task}</span>

            <span className="shrink-0 font-mono text-[11.5px] text-muted tnum">
              {formatDuration(entry.durationMinutes)}
            </span>

            <span className="w-[84px] shrink-0 text-right font-mono tnum">
              {formatEuros(entry.amountCents)}
            </span>
          </label>
        ))}
      </div>
    </div>
  )
}

/** What the selection is worth, beside the forfait when there is one. */
export function SelectionStrip({ chosen, forfaitLeftCents }: {
  chosen: UnbilledEntry[]
  forfaitLeftCents?: number | null
}) {
  const { minutes, cents } = useMemo(() => ({
    minutes: chosen.reduce((total, entry) => total + entry.durationMinutes, 0),
    cents: chosen.reduce((total, entry) => total + entry.amountCents, 0),
  }), [chosen])

  return (
    <div className="flex flex-wrap items-baseline gap-3 rounded-sm bg-sunken px-2.5 py-2 font-mono text-[11.5px] tnum">
      <span>
        <span className="text-muted">Sélection</span> {chosen.length} ligne
        {chosen.length > 1 ? 's' : ''}
      </span>
      <span aria-hidden="true" className="h-3 w-px bg-line" />
      <span>
        <span className="text-muted">Durée</span> {formatDuration(minutes)}
      </span>
      <span className="flex-1" />

      {/* One group, so the two figures being compared wrap together rather than landing on
          different lines with the question between them. */}
      <span className="flex items-baseline gap-3">
        {forfaitLeftCents != null && (
          <>
            <span>
              <span className="text-muted">Reste du forfait</span>{' '}
              <strong className="font-semibold">{formatEuros(forfaitLeftCents)}</strong>
            </span>
            <span aria-hidden="true" className="h-3 w-px bg-line" />
          </>
        )}

        <span>
          <span className="text-muted">Valeur des heures</span>{' '}
          <strong className="font-semibold">{formatEuros(cents)}</strong>
        </span>
      </span>
    </div>
  )
}

/**
 * The arbitrage, stated the moment she types a different amount rather than discovered afterwards.
 *
 * <p>Her own sentence: « si le montant total des cases cochées est plus élevé que le montant de la
 * facture, alors mali ». So it is written from the selection, which is the thing she is adjusting.</p>
 */
export function VarianceNote({ varianceCents, compact }: {
  varianceCents: number
  compact?: boolean
}) {
  if (varianceCents === 0) return null

  const boni = varianceCents > 0

  return (
    <div
      className={cn(
        'rounded-sm border text-[12px]',
        compact ? 'px-2.5 py-1.5' : 'px-3 py-2',
        boni
          ? 'border-[#BFD3C5] bg-[#F4F8F5] text-brand-on-subtle'
          : 'border-[#E8D5AE] bg-[#FDF8ED] text-[#6E4A0E]',
      )}
    >
      <strong className="font-semibold">
        {boni ? 'Boni' : 'Mali'} de {formatEuros(Math.abs(varianceCents))}
      </strong>{' '}
      {boni
        ? 'facturé au-delà de la valeur des heures sélectionnées.'
        : 'accordé au client sur la valeur des heures sélectionnées.'}
    </div>
  )
}
