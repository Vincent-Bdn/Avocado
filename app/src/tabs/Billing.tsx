import { useCallback, useEffect, useState } from 'react'
import {
  ArrowDownLeft, ArrowLeftRight, ArrowUpRight, Check, FileSpreadsheet, ListChecks, PenLine, Pencil,
  Trash2, X,
} from 'lucide-react'
import { ApiError, api, post, saveAs } from '../api.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions, Field } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { SplitButton, SplitButtonItem } from '../components/ui/split-button.js'
import { useToasts } from '../components/ui/toast.js'
import { cn } from '../lib/utils.js'
import { centsToAmount, parseAmountToCents } from '../lib/amount.js'
import { readBilling } from '../lib/billing.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAction, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'

type MovementKind = 'Receipt' | 'Disbursement'

interface BillingInvoice {
  id: string
  date: string
  externalReference: string | null
  amountExclVatCents: number
  isPaid: boolean
  paidOn: string | null
  billedTimeCents: number
  varianceCents: number
  billedEntryCount: number
}

interface UnbilledEntry {
  id: string
  date: string
  task: string
  durationMinutes: number
  isBillable: boolean
  amountCents: number
  invoiceId: string | null
}

interface BillingMovement {
  id: string
  date: string
  label: string
  amountCents: number
  kind: MovementKind
}

interface BillingOverview {
  summary: {
    billableTimeCents: number
    billableMinutes: number
    ledgerCents: number
    invoicedCents: number
    manualInvoicedCents: number
    varianceCents: number
    leftToBillCents: number
  }
  invoices: BillingInvoice[]
  invoicedOutstandingCents: number
  ledger: BillingMovement[]
  receiptsCents: number
  disbursementsCents: number
  statement: {
    since: string | null
    billableMinutes: number
    billableAmountCents: number
    disbursementsToRebillCents: number
    receiptsToOffsetCents: number
  }
}

/**
 * Tracking only. Avocado issues no invoice and computes no VAT; it records what left, so that what
 * remains is knowable. Everything recorded here can be corrected: a figure you cannot fix is a figure
 * that gets worked around in a spreadsheet.
 */
export function Billing({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [data, setData] = useState<BillingOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editingInvoice, setEditingInvoice] = useState<string | null>(null)
  const [editingMovement, setEditingMovement] = useState<string | null>(null)
  const [billing, setBilling] = useState<'time' | 'manual' | null>(null)
  const [movement, setMovement] = useState<MovementKind | null>(null)
  const toasts = useToasts()

  const reload = useCallback(() => {
    api<BillingOverview>(`/api/matters/${matterId}/billing`)
      .then(setData)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(reload, [reload])

  const refresh = () => {
    setEditingInvoice(null)
    setEditingMovement(null)
    reload()
    onChanged()
  }

  /** Says where the file went. An export that reports nothing is an export you have to go and check. */
  async function exportDetail(invoice: BillingInvoice) {
    try {
      const saved = await saveAs(
        `/api/invoices/${invoice.id}/detail.xlsx`,
        `detail-facturation-${invoice.externalReference ?? invoice.date.slice(0, 10)}.xlsx`,
      )

      if (saved) {
        toasts.succeeded('Détail de facturation enregistré', saved)
      }
    } catch (failure) {
      toasts.failed('Export impossible', messageOf(failure))
    }
  }

  async function remove(path: string) {
    setError(null)

    try {
      await api(path, { method: 'DELETE' })
      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  if (!data) return <TabPanel>{error && <p className="m-0 text-danger">{error}</p>}</TabPanel>

  const { summary, statement } = data

  // Same reading as the fiche's context panel, from the same place, so the two cannot disagree.
  const { settled } = readBilling(summary)

  return (
    <TabPanel className="relative">
      {toasts.view}

      {/*
        Two different questions, and only one of them is ever the live one. While there are hours to
        bill, the answer is « combien » and the subtraction has to be checkable by eye. Once
        everything is billed, « reste à facturer : −150 € » is not an answer at all — it is a
        provision showing through — so the card switches to what she actually wants then: what the
        dossier earned against what its hours were worth.
      */}
      {settled ? (
        <section className="rounded-md border border-[#BFD3C5] bg-[#F4F8F5] px-4 py-3.5 text-brand-on-subtle">
          <div className="flex items-center gap-1.5 text-[12px] font-medium">
            <Check size={13} strokeWidth={2.5} />
            Tout le temps saisi est facturé
          </div>

          <div className="mt-1 font-mono text-[28px] leading-[34px] font-semibold tracking-[-0.02em] tnum">
            {formatEuros(summary.invoicedCents)}
          </div>
          <div className="font-mono text-[11px] tnum">facturés sur ce dossier</div>

          {summary.varianceCents !== 0 && (
            <div className="mt-2.5 grid gap-0.5 rounded-sm bg-panel px-2.5 py-2 font-mono text-[12px] text-ink tnum">
              <Line
                label="Valeur des heures facturées"
                value={formatEuros(summary.invoicedCents - summary.varianceCents)}
              />
              <Line label="Facturé" value={formatEuros(summary.invoicedCents)} />
              <div
                className={cn(
                  'mt-1 flex justify-between border-t border-line-subtle pt-1 font-semibold',
                  summary.varianceCents > 0 ? 'text-success' : 'text-warning',
                )}
              >
                <span>= {summary.varianceCents > 0 ? 'Boni' : 'Mali'}</span>
                <span>
                  {summary.varianceCents > 0 ? '+ ' : '− '}
                  {formatEuros(Math.abs(summary.varianceCents))}
                </span>
              </div>
            </div>
          )}

          {summary.leftToBillCents < 0 && (
            <p className="m-0 mt-2 text-[11px] leading-4">
              Le client est en avance de {formatEuros(-summary.leftToBillCents)} : provision reçue ou
              facture émise au-delà du temps saisi.
            </p>
          )}
        </section>
      ) : (
        <section className="rounded-md border border-[#E8D5AE] bg-[#FDF8ED] px-4 py-3.5 text-[#6E4A0E]">
          <div className="text-[12px] font-medium">Reste à facturer</div>
          <div className="font-mono text-[28px] leading-[34px] font-semibold tracking-[-0.02em] tnum">
            {formatEuros(summary.leftToBillCents)}
          </div>

          <div className="mt-2.5 grid gap-0.5 rounded-sm bg-panel px-2.5 py-2 font-mono text-[12px] text-ink tnum">
            <Line label="Temps non facturé" value={formatEuros(summary.billableTimeCents)} />
            <Line label="− Mouvements" value={formatEuros(summary.ledgerCents)} />
            <Line label="− Factures libres" value={formatEuros(summary.manualInvoicedCents)} />
            <div className="mt-1 flex justify-between border-t border-line-subtle pt-1 font-semibold">
              <span>= Reste à facturer</span>
              <span>{formatEuros(summary.leftToBillCents)}</span>
            </div>
          </div>

          {/* Boni or mali: what she chose to bill above or below the hours, accumulated. */}
          {summary.varianceCents !== 0 && (
            <div className="mt-2 border-t border-[#E8D5AE] pt-1.5">
              <div className="type-group opacity-80">
                {summary.varianceCents > 0 ? 'Boni' : 'Mali'}
              </div>
              <div className="font-mono text-[15px] leading-5 font-semibold tnum">
                {summary.varianceCents > 0 ? '+ ' : '− '}
                {formatEuros(Math.abs(summary.varianceCents))}
              </div>
            </div>
          )}

          <p className="m-0 mt-2 text-[11px] leading-4">
            {formatDuration(summary.billableMinutes)} facturables.{' '}
            {statement.since
              ? `Dernière facture le ${new Date(statement.since).toLocaleDateString('fr-FR')}.`
              : 'Aucune facture enregistrée pour l’instant.'}
          </p>
        </section>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      <section className="grid gap-2">
        <h3 className="type-title m-0">Factures émises</h3>
        <Micro>
          Ces factures ont été établies dans votre logiciel comptable. Avocado n’en produit aucune :
          il note ce qui est parti, pour savoir ce qui reste.
        </Micro>

        {isOpen && (
          <SplitButton label="Facturer…" icon={<ListChecks size={14} strokeWidth={1.75} />}>
            {(close) => (
              <>
                <SplitButtonItem
                  icon={<ListChecks size={14} strokeWidth={1.75} />}
                  title="À partir du temps passé"
                  detail="Choisir les lignes, voir ce qu’elles valent, décider du montant"
                  onClick={() => { close(); setBilling('time') }}
                />
                <SplitButtonItem
                  icon={<PenLine size={14} strokeWidth={1.75} />}
                  title="Saisir une facture établie ailleurs"
                  detail="Un montant et une référence, sans rattacher d’heures"
                  onClick={() => { close(); setBilling('manual') }}
                />
              </>
            )}
          </SplitButton>
        )}

        {billing === 'time' && (
          <BillTimeDialog
            matterId={matterId}
            onCancel={() => setBilling(null)}
            onBilled={() => { setBilling(null); refresh() }}
          />
        )}

        {billing === 'manual' && (
          <InvoiceDialog
            matterId={matterId}
            onCancel={() => setBilling(null)}
            onSaved={() => { setBilling(null); refresh() }}
          />
        )}

        <div className="grid">
          {data.invoices.map((invoice) =>
            editingInvoice === invoice.id ? (
              <InvoiceForm
                key={invoice.id}
                matterId={matterId}
                invoice={invoice}
                onSaved={refresh}
                onCancel={() => setEditingInvoice(null)}
              />
            ) : (
              <Row key={invoice.id} className="group">
                <RowDate>{new Date(invoice.date).toLocaleDateString('fr-FR')}</RowDate>
                <RowMain>
                  <span>{invoice.externalReference ?? 'Sans référence'}</span>
                  {invoice.billedEntryCount > 0 && (
                    <Micro>
                      {invoice.billedEntryCount} ligne{invoice.billedEntryCount > 1 ? 's' : ''} de temps
                      {invoice.varianceCents !== 0 &&
                        ` · ${invoice.varianceCents > 0 ? 'boni' : 'mali'} ${formatEuros(Math.abs(invoice.varianceCents))}`}
                    </Micro>
                  )}
                </RowMain>

                <RowAmount>{formatEuros(invoice.amountExclVatCents)}</RowAmount>
                <Badge tone={invoice.isPaid ? 'brand' : 'accent'}>
                  {invoice.isPaid ? 'Payée' : 'En attente'}
                </Badge>

                <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                  {invoice.billedEntryCount > 0 && (
                    <RowAction
                      label="Détail de facturation (Excel), à joindre à la facture"
                      onClick={() => void exportDetail(invoice)}
                    >
                      <FileSpreadsheet size={13} strokeWidth={1.75} />
                    </RowAction>
                  )}

                  {isOpen && (
                    <>
                      <RowAction label="Modifier" onClick={() => setEditingInvoice(invoice.id)}>
                        <Pencil size={13} strokeWidth={1.75} />
                      </RowAction>
                      <RowAction
                        label="Supprimer"
                        danger
                        onClick={() => void remove(`/api/invoices/${invoice.id}`)}
                      >
                        <Trash2 size={13} strokeWidth={1.75} />
                      </RowAction>
                    </>
                  )}
                </span>
              </Row>
            ),
          )}
        </div>

        {data.invoices.length > 0 && (
          <Micro>Reste dû sur les factures émises : {formatEuros(data.invoicedOutstandingCents)}</Micro>
        )}
      </section>

      <section className="grid gap-2">
        <h3 className="type-title m-0">Mouvements</h3>
        <Micro>
          Les provisions reçues et les frais avancés pour le compte du client : ils viennent en
          déduction de ce qui reste à facturer.
        </Micro>

        {isOpen && (
          <SplitButton label="Enregistrer un mouvement…" icon={<ArrowLeftRight size={14} strokeWidth={1.75} />}>
            {(close) => (
              <>
                <SplitButtonItem
                  icon={<ArrowDownLeft size={14} strokeWidth={1.75} />}
                  title="Un encaissement"
                  detail="Provision sur honoraires, acompte reçu"
                  onClick={() => { close(); setMovement('Receipt') }}
                />
                <SplitButtonItem
                  icon={<ArrowUpRight size={14} strokeWidth={1.75} />}
                  title="Un débours"
                  detail="Frais de greffe, huissier, expertise avancés pour le client"
                  onClick={() => { close(); setMovement('Disbursement') }}
                />
              </>
            )}
          </SplitButton>
        )}

        {movement && (
          <MovementDialog
            matterId={matterId}
            kind={movement}
            onCancel={() => setMovement(null)}
            onSaved={() => { setMovement(null); refresh() }}
          />
        )}

        <div className="grid">
          {data.ledger.map((entry) =>
            editingMovement === entry.id ? (
              <MovementForm
                key={entry.id}
                matterId={matterId}
                movement={entry}
                onSaved={refresh}
                onCancel={() => setEditingMovement(null)}
              />
            ) : (
              <Row key={entry.id} className="group">
                <RowDate>{new Date(entry.date).toLocaleDateString('fr-FR')}</RowDate>
                <Badge tone={entry.kind === 'Receipt' ? 'brand' : 'accent'}>
                  {entry.kind === 'Receipt' ? 'Encaissement' : 'Débours'}
                </Badge>
                <RowMain><span>{entry.label}</span></RowMain>
                {/* Rendered with its explicit sign and its type colour. */}
                <RowAmount className={entry.amountCents < 0 ? 'text-warning' : 'text-success'}>
                  {entry.amountCents >= 0 ? '+ ' : '− '}
                  {formatEuros(Math.abs(entry.amountCents))}
                </RowAmount>

                {isOpen && (
                  <Actions
                    onEdit={() => setEditingMovement(entry.id)}
                    onDelete={() => void remove(`/api/ledger-entries/${entry.id}`)}
                  />
                )}
              </Row>
            ),
          )}
        </div>

        {data.ledger.length > 0 && (
          <Micro>
            Solde : {formatEuros(data.receiptsCents)} encaissés, {formatEuros(data.disbursementsCents)}{' '}
            avancés.
          </Micro>
        )}
      </section>
    </TabPanel>
  )
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

const Line = ({ label, value }: { label: string; value: string }) => (
  <div className="flex justify-between">
    <span>{label}</span>
    <span>{value}</span>
  </div>
)

/** Revealed on hover, so a list of figures stays a list of figures. */
const Actions = ({ onEdit, onDelete }: { onEdit: () => void; onDelete: () => void }) => (
  <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
    <RowAction label="Modifier" onClick={onEdit}>
      <Pencil size={13} strokeWidth={1.75} />
    </RowAction>
    <RowAction label="Supprimer" danger onClick={onDelete}>
      <Trash2 size={13} strokeWidth={1.75} />
    </RowAction>
  </span>
)

/**
 * Facturer du temps passé.
 *
 * Lawyers rarely bill everything at once, so this is the normal path: pick the lines, see what they
 * are worth, then decide what to bill. The two figures are shown side by side because the difference
 * between them is the interesting one — billing below the recorded time is a mali, above it a boni,
 * and both are deliberate acts a practice benefits from watching.
 *
 * The selected lines are hard-linked to the facture, so they stop counting towards « reste à
 * facturer ». Answering that question by date instead would break the first time an old entry is
 * corrected.
 */
function BillTimeDialog({ matterId, onCancel, onBilled }: {
  matterId: string
  onCancel: () => void
  onBilled: () => void
}) {
  const [entries, setEntries] = useState<UnbilledEntry[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [reference, setReference] = useState('')
  const [amount, setAmount] = useState('')
  const [paid, setPaid] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<{ items: UnbilledEntry[] }>(`/api/matters/${matterId}/time-entries`)
      .then((page) => {
        const billable = page.items.filter((entry) => entry.isBillable && entry.invoiceId === null)
        setEntries(billable)
        // Everything is selected to begin with: billing all of it is the common case, and unticking
        // three lines is less work than ticking twenty.
        setSelected(new Set(billable.map((entry) => entry.id)))
      })
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  const chosen = entries.filter((entry) => selected.has(entry.id))
  const timeCents = chosen.reduce((total, entry) => total + entry.amountCents, 0)
  const minutes = chosen.reduce((total, entry) => total + entry.durationMinutes, 0)

  const billedCents = amount.trim() ? parseAmountToCents(amount) : timeCents
  const variance = billedCents === null ? 0 : billedCents - timeCents

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)

      return next
    })
  }

  async function bill() {
    if (chosen.length === 0) {
      setError('Choisissez au moins une ligne.')
      return
    }

    if (amount.trim() && billedCents === null) {
      setError('Indiquez un montant positif, par exemple 6 000,00.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await post(`/api/matters/${matterId}/invoices/from-time`, {
        timeEntryIds: [...selected],
        date,
        amountExclVatCents: amount.trim() ? billedCents : null,
        externalReference: reference.trim() || null,
        isPaid: paid,
      })

      onBilled()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title="Facturer du temps passé" width={640} onClose={onCancel}>
      {entries.length === 0 ? (
        <p className="m-0 text-[12.5px] text-muted">
          Tout le temps facturable de ce dossier est déjà rattaché à une facture.
        </p>
      ) : (
        <>
          <div className="max-h-[280px] overflow-y-auto rounded-sm border border-line-subtle">
            {entries.map((entry) => (
              <label
                key={entry.id}
                className="flex cursor-pointer items-center gap-2.5 border-b border-line-subtle px-2.5 py-1.5 text-[12px] last:border-b-0 hover:bg-hover"
              >
                <input
                  type="checkbox"
                  checked={selected.has(entry.id)}
                  onChange={() => toggle(entry.id)}
                />

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
            <span>
              <span className="text-muted">Valeur des heures</span>{' '}
              <strong className="font-semibold">{formatEuros(timeCents)}</strong>
            </span>
          </div>

          <div className="grid grid-cols-3 gap-3">
            <Field label="Date de la facture">
              <Input
                inputSize="lg"
                type="date"
                value={date}
                onChange={(event) => setDate(event.target.value)}
              />
            </Field>

            <Field label="Référence">
              <Input
                inputSize="lg"
                value={reference}
                placeholder="F-2026-014"
                onChange={(event) => setReference(event.target.value)}
              />
            </Field>

            <Field label="Montant facturé HT">
              <Input
                inputSize="lg"
                className="font-mono tnum"
                value={amount}
                placeholder={centsToAmount(timeCents)}
                onChange={(event) => { setAmount(event.target.value); setError(null) }}
              />
            </Field>
          </div>

          {/* Stated the moment she types a different amount, not discovered afterwards. */}
          {variance !== 0 && (
            <div
              className={cn(
                'rounded-sm border px-3 py-2 text-[12px]',
                variance > 0
                  ? 'border-[#BFD3C5] bg-[#F4F8F5] text-brand-on-subtle'
                  : 'border-[#E8D5AE] bg-[#FDF8ED] text-[#6E4A0E]',
              )}
            >
              <strong className="font-semibold">
                {variance > 0 ? 'Boni' : 'Mali'} de {formatEuros(Math.abs(variance))}
              </strong>{' '}
              {variance > 0
                ? 'facturé au-delà de la valeur des heures sélectionnées.'
                : 'accordé au client sur la valeur des heures sélectionnées.'}
            </div>
          )}

          <label className="flex items-center gap-2 text-[13px]">
            <input type="checkbox" checked={paid} onChange={(event) => setPaid(event.target.checked)} />
            Déjà payée
          </label>
        </>
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy || chosen.length === 0} onClick={() => void bill()}>
          Établir la facture
        </Button>
      </DialogActions>
    </Dialog>
  )
}

/** Saisir une facture établie ailleurs. The same fields as the inline correction, in a dialog. */
function InvoiceDialog({ matterId, onCancel, onSaved }: {
  matterId: string
  onCancel: () => void
  onSaved: () => void
}) {
  return (
    <Dialog title="Saisir une facture établie ailleurs" onClose={onCancel}>
      <p className="m-0 text-[12.5px] leading-[19px] text-muted">
        Pour une facture qui ne correspond pas à des heures saisies ici : un forfait, une provision
        facturée, une régularisation. Son montant est retranché du reste à facturer.
      </p>

      <InvoiceForm matterId={matterId} onSaved={onSaved} onCancel={onCancel} bare />
    </Dialog>
  )
}

/** One form for both adding and correcting, so the two can never drift apart. */
function InvoiceForm({ matterId, invoice, onSaved, onCancel, bare }: {
  matterId: string
  invoice?: BillingInvoice
  onSaved: () => void
  onCancel?: () => void
  /** Inside a dialog the bordered strip would be a box in a box. */
  bare?: boolean
}) {
  const [date, setDate] = useState(invoice?.date.slice(0, 10) ?? new Date().toISOString().slice(0, 10))
  const [amount, setAmount] = useState(invoice ? centsToAmount(invoice.amountExclVatCents) : '')
  const [reference, setReference] = useState(invoice?.externalReference ?? '')
  const [paid, setPaid] = useState(invoice?.isPaid ?? false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save() {
    const cents = parseAmountToCents(amount)

    if (cents === null) {
      setError('Indiquez un montant positif, par exemple 1 480,00.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = {
        date,
        amountExclVatCents: cents,
        externalReference: reference.trim() || null,
        isPaid: paid,
        paidOn: paid ? date : null,
      }

      if (invoice) {
        await api(`/api/invoices/${invoice.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/invoices`, body)
        setAmount('')
        setReference('')
        setPaid(false)
      }

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  const fields = (
    <>
        <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />
        <Input
          className="w-28 font-mono tnum"
          invalid={Boolean(error)}
          value={amount}
          placeholder="€ HT"
          onChange={(event) => { setAmount(event.target.value); setError(null) }}
        />
        <Input
          className="flex-1 basis-[180px]"
          value={reference}
          placeholder="Référence externe"
          onChange={(event) => setReference(event.target.value)}
        />
        <label className="flex items-center gap-2 text-[13px]">
          <input type="checkbox" checked={paid} onChange={(event) => setPaid(event.target.checked)} />
          Payée
        </label>

        <Button disabled={busy} onClick={() => void save()}>
          {invoice ? 'Enregistrer' : 'Ajouter la facture'}
        </Button>

        {onCancel && !bare && (
          <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
            <X size={13} strokeWidth={2} />
          </Button>
        )}
    </>
  )

  if (bare) {
    return (
      <div className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">{fields}</div>
        {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
      </div>
    )
  }

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(invoice)}>{fields}</InlineForm>
      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </div>
  )
}

/** Recording a mouvement. The nature is already chosen by the menu item that opened this. */
function MovementDialog({ matterId, kind, onCancel, onSaved }: {
  matterId: string
  kind: MovementKind
  onCancel: () => void
  onSaved: () => void
}) {
  return (
    <Dialog
      title={kind === 'Receipt' ? 'Enregistrer un encaissement' : 'Enregistrer un débours'}
      onClose={onCancel}
    >
      <p className="m-0 text-[12.5px] leading-[19px] text-muted">
        {kind === 'Receipt'
          ? 'Une somme reçue du client avant facturation. Elle réduit ce qui reste à facturer.'
          : 'Une somme avancée pour le compte du client. Elle augmente ce qui reste à facturer.'}
      </p>

      <MovementForm matterId={matterId} initialKind={kind} onSaved={onSaved} onCancel={onCancel} bare />
    </Dialog>
  )
}

/**
 * Nature first, amount second, and the amount is always positive. A débours typed as a positive
 * number would silently corrupt every balance on the dossier, so the sign is never exposed here and
 * the server applies it on both create and update.
 */
function MovementForm({ matterId, movement, initialKind, onSaved, onCancel, bare }: {
  matterId: string
  movement?: BillingMovement
  initialKind?: MovementKind
  onSaved: () => void
  onCancel?: () => void
  /** Inside a dialog the bordered strip would be a box in a box. */
  bare?: boolean
}) {
  const [kind, setKind] = useState<MovementKind>(movement?.kind ?? initialKind ?? 'Receipt')
  const [date, setDate] = useState(movement?.date.slice(0, 10) ?? new Date().toISOString().slice(0, 10))
  const [amount, setAmount] = useState(movement ? centsToAmount(movement.amountCents) : '')
  const [label, setLabel] = useState(movement?.label ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save() {
    const cents = parseAmountToCents(amount)

    if (cents === null) {
      setError('Indiquez un montant positif, par exemple 800,00.')
      return
    }

    if (!label.trim()) {
      setError('Indiquez à quoi correspond ce mouvement.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = { kind, date, amountCents: cents, label: label.trim() }

      if (movement) {
        await api(`/api/ledger-entries/${movement.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/ledger-entries`, body)
        setAmount('')
        setLabel('')
      }

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  const fields = (
    <>
        {/* Only where the nature is still open. In the dialog it was chosen by the menu item that
            opened it, and the title says so, so asking again is asking twice. */}
        {!bare && (
          <div className="flex gap-0.5">
            <KindButton active={kind === 'Receipt'} tone="brand" onClick={() => setKind('Receipt')}>
              Encaissement
            </KindButton>
            <KindButton active={kind === 'Disbursement'} tone="accent" onClick={() => setKind('Disbursement')}>
              Débours
            </KindButton>
          </div>
        )}

        <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

        <Input
          className="w-32 font-mono tnum"
          invalid={Boolean(error)}
          value={amount}
          placeholder={kind === 'Receipt' ? 'Montant reçu' : 'Montant avancé'}
          onChange={(event) => { setAmount(event.target.value); setError(null) }}
        />

        <Input
          className="flex-1 basis-[180px]"
          value={label}
          placeholder={kind === 'Receipt' ? 'Provision sur honoraires…' : 'Frais de greffe…'}
          onChange={(event) => { setLabel(event.target.value); setError(null) }}
        />

        <Button disabled={busy} onClick={() => void save()}>
          {movement
            ? 'Enregistrer'
            : kind === 'Receipt'
              ? 'Enregistrer l’encaissement'
              : 'Enregistrer le débours'}
        </Button>

        {onCancel && !bare && (
          <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
            <X size={13} strokeWidth={2} />
          </Button>
        )}
    </>
  )

  if (bare) {
    return (
      <div className="grid gap-2">
        <div className="flex flex-wrap items-center gap-2">{fields}</div>
        {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
      </div>
    )
  }

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(movement)}>{fields}</InlineForm>
      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </div>
  )
}

function KindButton({ active, tone, onClick, children }: {
  active: boolean
  tone: 'brand' | 'accent'
  onClick: () => void
  children: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-[3px] px-2 py-1 text-[11px] leading-3 transition-colors',
        !active && 'text-ink-secondary hover:bg-hover',
        active && tone === 'brand' && 'bg-brand-subtle text-brand-on-subtle',
        active && tone === 'accent' && 'bg-accent-subtle text-warning',
      )}
    >
      {children}
    </button>
  )
}
