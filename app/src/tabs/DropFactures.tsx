import { useEffect, useState } from 'react'
import { AlertTriangle, FileText, Loader2, Upload } from 'lucide-react'
import { ApiError, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { centsToAmount, parseAmountToCents } from '../lib/amount.js'
import { formatEuros } from '../labels.js'
import { cn } from '../lib/utils.js'
import { LinePicker, SelectionStrip, VarianceNote, preselect, useUnbilledLines } from './TimeLines.js'

interface ReadInvoice {
  number: string | null
  issuedOn: string | null
  dueOn: string | null
  totalExclVatCents: number | null
  vatCents: number | null
  totalInclVatCents: number | null
  paid: boolean
  clientName: string | null
  clientSiren: string | null
  subject: string | null
  balances: boolean
  notes: string[]
}

interface Dropped {
  path: string
  fileName: string
  read: ReadInvoice | null
  suggestedInvoiceId: string | null
  suggestedReason: string | null
  wrongClient: boolean
  error: string | null
}

export interface ExistingInvoice {
  id: string
  date: string
  externalReference: string | null
  amountExclVatCents: number
  hasDocument: boolean
}

/** What she decided about one dropped file, before anything is written. */
interface Decision {
  attachTo: string | null
  amount: string
  date: string
  reference: string
  paid: boolean
  /** The lines this facture settles. Null until the temps passé has been read and pre-ticked. */
  lines: Set<string> | null
}

/**
 * Factures dropped onto a dossier.
 *
 * <p><b>Nothing is recorded until she has looked.</b> The figures are read off a PDF by pattern
 * matching, and a facture is money: a wrong amount imported silently is one she would discover at
 * the end of the year, against a chiffre d'affaires she had been trusting. So reading proposes and
 * she disposes, every time, even when the reading is obviously right.</p>
 *
 * <p>The same gesture does two jobs, which is why there is one zone rather than two. A facture she
 * has just issued lands on the line Avocado already knows about. A facture from before Avocado
 * becomes a line.</p>
 *
 * <p><b>And it settles its hours.</b> Dropping a facture used to record it and leave the time it
 * paid for sitting in « reste à facturer », with no boni and no mali, because nothing here ever
 * attached a line. On the dossier PIRBAY that meant the hours were then soldées en mali by hand —
 * and a mali appeared on a dossier that had never granted one. The picker below is the same one as
 * « facturer du temps passé », and it arrives pre-ticked up to the amount read off the PDF.</p>
 */
export function DropFactures({ matterId, invoices, onFiled }: {
  matterId: string
  invoices: ExistingInvoice[]
  onFiled: () => void
}) {
  const [over, setOver] = useState(false)
  const [reading, setReading] = useState(false)
  const [dropped, setDropped] = useState<Dropped[]>([])
  const [decisions, setDecisions] = useState<Record<string, Decision>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Read once for the dossier, not once per file. Reloaded after each facture is filed, so a second
  // file dropped in the same batch is offered only the lines the first one left.
  const { lines, reload: reloadLines } = useUnbilledLines(matterId)

  // Pre-ticking waits for both halves: the PDF has to have been read to know the amount, and the
  // temps passé has to have arrived to know what there is to tick.
  useEffect(() => {
    if (lines === null) return

    setDecisions((current) => {
      let changed = false
      const next = { ...current }

      for (const [path, decision] of Object.entries(current)) {
        if (decision.lines !== null) continue

        next[path] = { ...decision, lines: preselect(lines, parseAmountToCents(decision.amount)) }
        changed = true
      }

      return changed ? next : current
    })
  }, [lines, dropped])

  async function take(files: FileList) {
    // Electron 34 removed File.path; the preload resolves it through webUtils instead. The renderer
    // learns where the file came from and still cannot open it: reading stays the backend's job.
    const paths = Array.from(files).map((file) => window.avocado.pathForFile(file)).filter(Boolean)

    if (paths.length === 0) return

    setReading(true)
    setError(null)

    try {
      const answers = await post<Dropped[]>(`/api/matters/${matterId}/invoices/read-dropped`, { paths })

      setDropped((current) => [...current, ...answers])
      setDecisions((current) => {
        const next = { ...current }

        for (const answer of answers) {
          next[answer.path] = {
            attachTo: answer.suggestedInvoiceId,
            amount: answer.read?.totalExclVatCents ? centsToAmount(answer.read.totalExclVatCents) : '',
            date: answer.read?.issuedOn?.slice(0, 10) ?? new Date().toISOString().slice(0, 10),
            reference: answer.read?.number ?? '',
            paid: answer.read?.paid ?? false,
            lines: null,
          }
        }

        return next
      })
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setReading(false)
    }
  }

  async function file(item: Dropped) {
    const decision = decisions[item.path]
    if (!decision) return

    const cents = decision.attachTo === null ? parseAmountToCents(decision.amount) : null

    if (decision.attachTo === null && cents === null) {
      setError('Indiquez le montant HT de cette facture.')
      return
    }

    setBusy(item.path)
    setError(null)

    try {
      await post(`/api/matters/${matterId}/invoices/file`, {
        path: item.path,
        invoiceId: decision.attachTo,
        date: decision.attachTo === null ? decision.date : null,
        amountExclVatCents: cents,
        externalReference: decision.reference.trim() || null,
        isPaid: decision.paid,
        // « Antérieure » is no longer asked. It decided no arithmetic — a facture consumes the lines
        // it actually took, so one that took none subtracts nothing — and the question had no
        // consequence she could act on. The Gestisoft import still sets it, which is the one place
        // the answer is known.
        isHistorical: false,
        timeEntryIds: [...(decision.lines ?? [])],
      })

      setDropped((current) => current.filter((other) => other.path !== item.path))

      // The lines this facture took are gone; whatever is still waiting has to be re-read before the
      // next file in the batch pre-ticks against it.
      reloadLines()
      setDecisions((current) => Object.fromEntries(
        Object.entries(current)
          .filter(([path]) => path !== item.path)
          .map(([path, other]) => [path, { ...other, lines: null }]),
      ))

      onFiled()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(null)
    }
  }

  const set = (path: string, change: Partial<Decision>) =>
    setDecisions((current) => ({ ...current, [path]: { ...current[path]!, ...change } }))

  const free = invoices.filter((invoice) => !invoice.hasDocument)
  const waiting = lines ?? []

  return (
    <div className="grid gap-2">
      <div
        onDragOver={(event) => { event.preventDefault(); setOver(true) }}
        onDragLeave={() => setOver(false)}
        onDrop={(event) => {
          event.preventDefault()
          setOver(false)
          void take(event.dataTransfer.files)
        }}
        className={cn(
          'grid place-items-center gap-1 rounded-md border border-dashed px-3 py-4 text-center transition-colors',
          over ? 'border-brand bg-brand-subtle' : 'border-line-strong',
        )}
      >
        {reading ? (
          <span className="flex items-center gap-2 text-[12.5px] text-muted">
            <Loader2 size={14} className="animate-spin" />
            Lecture…
          </span>
        ) : (
          <>
            <Upload size={16} strokeWidth={1.75} className="text-ink-secondary" />
            <span className="text-[12.5px] text-ink-secondary">
              Déposez ici vos factures en PDF
            </span>
            <span className="type-caption text-muted">
              Avocado les lit. Rien n’est enregistré sans vous.
            </span>
          </>
        )}
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {dropped.map((item) => {
        const decision = decisions[item.path]
        const ticked = decision?.lines ?? new Set<string>()
        const chosen = waiting.filter((line) => ticked.has(line.id))
        const worth = chosen.reduce((total, line) => total + line.amountCents, 0)
        const billed = decision ? parseAmountToCents(decision.amount) : null

        return (
          <div key={item.path} className="grid gap-2 rounded-md border border-line-strong px-3 py-2.5">
            <div className="flex items-center gap-2">
              <FileText size={13} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />
              <span className="min-w-0 flex-1 truncate text-[12.5px] font-medium">{item.fileName}</span>

              <button
                type="button"
                onClick={() => setDropped((current) => current.filter((other) => other.path !== item.path))}
                className="text-[11.5px] text-muted hover:underline"
              >
                Retirer
              </button>
            </div>

            {item.error && <p className="m-0 text-[11.5px] text-danger">{item.error}</p>}

            {item.read && decision && (
              <>
                {/* HT first and named as such: it is the figure Avocado records and the only one it
                    can state honestly. The TTC is kept beside it because that is the number printed
                    largest on the facture, and seeing them disagree is the point. */}
                <p className="m-0 text-[11.5px] leading-[17px] text-muted">
                  {item.read.clientName && <>{item.read.clientName} · </>}
                  {item.read.subject}
                  {item.read.totalExclVatCents !== null && (
                    <> · {formatEuros(item.read.totalExclVatCents)} HT</>
                  )}
                  {item.read.totalInclVatCents !== null && (
                    <> · {formatEuros(item.read.totalInclVatCents)} TTC</>
                  )}
                </p>

                {/* The reading checked against the invoice's own arithmetic. A total lifted from the
                    wrong line almost never balances, so this is the difference between « read » and
                    « read correctly », and it is worth showing her which one she has. */}
                {!item.read.balances && (
                  <p className="m-0 flex items-start gap-1.5 text-[11.5px] leading-[17px] text-warning">
                    <AlertTriangle size={13} strokeWidth={2} className="mt-px shrink-0" />
                    Les montants lus ne s’additionnent pas : vérifiez le montant HT.
                  </p>
                )}

                {item.wrongClient && (
                  <p className="m-0 flex items-start gap-1.5 text-[11.5px] leading-[17px] text-warning">
                    <AlertTriangle size={13} strokeWidth={2} className="mt-px shrink-0" />
                    Facture au nom de {item.read.clientName}, pas le client de ce dossier.
                  </p>
                )}

                <div className="flex flex-wrap items-center gap-2">
                  <select
                    value={decision.attachTo ?? ''}
                    onChange={(event) => set(item.path, { attachTo: event.target.value || null })}
                    className="h-7 rounded-sm border border-line-strong bg-sunken px-1.5 font-sans text-[12.5px] text-ink"
                  >
                    <option value="">Créer une facture</option>
                    {free.map((invoice) => (
                      <option key={invoice.id} value={invoice.id}>
                        {invoice.externalReference ?? 'Sans référence'} ·{' '}
                        {new Date(invoice.date).toLocaleDateString('fr-FR')} ·{' '}
                        {formatEuros(invoice.amountExclVatCents)}
                      </option>
                    ))}
                  </select>

                  {item.suggestedReason && decision.attachTo !== null && (
                    <span className="type-caption text-muted">{item.suggestedReason}</span>
                  )}

                  {decision.attachTo === null && (
                    <>
                      <Input
                        inputSize="sm"
                        type="date"
                        value={decision.date}
                        aria-label="Date de la facture"
                        onChange={(event) => set(item.path, { date: event.target.value })}
                      />
                      <Input
                        inputSize="sm"
                        className="w-24 text-right font-mono tnum"
                        value={decision.amount}
                        placeholder="€ HT"
                        aria-label="Montant HT"
                        onChange={(event) => set(item.path, { amount: event.target.value })}
                      />
                      <Input
                        inputSize="sm"
                        className="w-24"
                        value={decision.reference}
                        placeholder="N°"
                        aria-label="Numéro de facture"
                        onChange={(event) => set(item.path, { reference: event.target.value })}
                      />
                      <label className="flex items-center gap-1.5 text-[12px]">
                        <input
                          type="checkbox"
                          checked={decision.paid}
                          onChange={(event) => set(item.path, { paid: event.target.checked })}
                        />
                        Payée
                      </label>
                    </>
                  )}

                  <span className="flex-1" />

                  <Button size="sm" disabled={busy === item.path} onClick={() => void file(item)}>
                    {busy === item.path && <Loader2 size={13} className="animate-spin" />}
                    {decision.attachTo === null ? 'Créer et joindre' : 'Joindre'}
                  </Button>
                </div>

                {/* The part that was missing entirely. Her own words: « dès que je glisse une
                    facture, ça me propose de cocher les cases de temps passé correspondant à la
                    facture ». */}
                {waiting.length > 0 && (
                  <div className="grid gap-1.5 border-t border-line-subtle pt-2">
                    <span className="type-caption text-muted">
                      Lignes de temps passé que cette facture solde
                    </span>

                    <LinePicker
                      lines={waiting}
                      selected={ticked}
                      onChange={(next) => set(item.path, { lines: next })}
                      height="max-h-[180px]"
                    />

                    <SelectionStrip chosen={chosen} />

                    <VarianceNote
                      compact
                      varianceCents={billed === null || chosen.length === 0 ? 0 : billed - worth}
                    />
                  </div>
                )}
              </>
            )}
          </div>
        )
      })}
    </div>
  )
}
