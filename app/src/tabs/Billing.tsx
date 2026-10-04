import { useCallback, useEffect, useState } from 'react'
import {
  ArrowDownLeft, ArrowLeftRight, ArrowUpRight, FileSpreadsheet, FileText, HandCoins, ListChecks, PenLine,
  Pencil, Trash2, X,
} from 'lucide-react'
import { ApiError, api, post, saveAs } from '../api.js'
import { DropFactures } from './DropFactures.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions, Field } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { Select } from '../components/ui/select.js'
import { SplitButton, SplitButtonItem } from '../components/ui/split-button.js'
import { useToasts } from '../components/ui/toast.js'
import { cn } from '../lib/utils.js'
import { centsToAmount, parseAmountToCents } from '../lib/amount.js'
import { BillingFigures, billingTone, readBilling } from '../lib/billing.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAction, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'
import type { BillingSummary, ContactSummary } from '../types.js'

type MovementKind = 'Receipt' | 'Disbursement'

interface BillingInvoice {
  id: string
  date: string
  externalReference: string | null
  amountExclVatCents: number
  isPaid: boolean
  paidOn: string | null
  /** Brought over from before Avocado. Shown as a reprise; otherwise an ordinary facture. */
  isHistorical: boolean
  discountBasisPoints: number | null
  discountReason: string | null
  /** The facture's own PDF, in her folder. Null while she has only typed the line in. */
  documentPath: string | null
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

interface BillingCost {
  id: string
  date: string
  kind: string | null
  label: string
  amountExclVatCents: number
  contactId: string | null
  contactName: string | null
  externalReference: string | null
  isPaid: boolean
  paidOn: string | null
  invoiceId: string | null
  invoiceReference: string | null
}

interface BillingMovement {
  id: string
  date: string
  label: string
  amountCents: number
  kind: MovementKind
}

interface BillingOverview {
  summary: BillingSummary
  invoices: BillingInvoice[]
  invoicedOutstandingCents: number
  ledger: BillingMovement[]
  receiptsCents: number
  disbursementsCents: number
  costs: BillingCost[]
  costsOutstandingCents: number
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
export function Billing({ matterId, folder, isOpen, onChanged }: {
  matterId: string
  /** The dossier's folder, so an attached facture can be opened. Null until she points at one. */
  folder: string | null
  isOpen: boolean
  onChanged: () => void
}) {
  const [data, setData] = useState<BillingOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editingInvoice, setEditingInvoice] = useState<string | null>(null)
  const [editingMovement, setEditingMovement] = useState<string | null>(null)
  const [billing, setBilling] = useState<'time' | 'manual' | null>(null)
  const [movement, setMovement] = useState<MovementKind | null>(null)
  const [addingCost, setAddingCost] = useState(false)
  const [editingCost, setEditingCost] = useState<string | null>(null)
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
    setAddingCost(false)
    setEditingCost(null)
    reload()
    onChanged()
  }

  /**
   * Says where the file went. An export that reports nothing is an export you have to go and check.
   *
   * <p>Two formats, because they are two different jobs. The Word document is the annexe she sends
   * with the facture, in her own layout. The spreadsheet is the one she works from: durations as
   * numbers, a rate column and a montant column, to sort, re-sum and paste into the platform that
   * issues the invoice.</p>
   */
  async function exportDetail(invoice: BillingInvoice, format: 'docx' | 'xlsx') {
    const stem = `detail-facture-${invoice.externalReference ?? invoice.date.slice(0, 10)}`

    try {
      const saved = await saveAs(`/api/invoices/${invoice.id}/detail.${format}`, `${stem}.${format}`)

      if (saved) {
        toasts.succeeded('Détail enregistré', saved)
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
  const reading = readBilling(summary)
  const forfait = summary.fixedFeeCents

  return (
    <TabPanel className="relative">
      {toasts.view}

      {/*
        One card, not two. It used to switch between « combien reste-t-il » and « qu'a rapporté ce
        dossier », because the old « reste à facturer » went negative whenever a provision outlived
        the time it stood against, and a negative headline had to be hidden. Provisions are cash now
        and the figure cannot go below zero, so there is one card and it says what is true.

        Nothing here is a compound subtraction except the forfait, where the subtraction actually
        ties. Three figures that each answer a question, then the adjustments, each on its own line.
      */}
      <section className={cn('rounded-md border px-4 py-3.5', billingTone[reading.tone])}>
        <BillingFigures summary={summary} />

        <div className="mt-2.5 grid gap-0.5 rounded-sm bg-panel px-2.5 py-2 font-mono text-[12px] text-ink tnum">
          <Line label="Travaillé" value={formatEuros(summary.workedCents)} />

          {forfait === null ? (
            <>
              <Line label="Facturé" value={formatEuros(summary.invoicedCents)} />
              <Line label="Encaissé" value={formatEuros(summary.collectedCents)} />
            </>
          ) : (
            <>
              <Line label="Encaissé" value={formatEuros(summary.collectedCents)} />
              <Line label="Forfait convenu" value={formatEuros(forfait)} />
              <Line label="− Facturé" value={formatEuros(summary.invoicedCents)} />
              <div className="mt-1 flex justify-between border-t border-line-subtle pt-1 font-semibold">
                <span>= Reste du forfait</span>
                <span>{formatEuros(summary.leftToBillCents)}</span>
              </div>
            </>
          )}
        </div>

        <div className="mt-2 grid gap-1.5">
          {summary.varianceCents !== 0 && (
            <Aside
              tone={summary.varianceCents > 0 ? 'good' : 'bad'}
              label={summary.varianceCents > 0 ? 'Boni' : 'Mali'}
              value={`${summary.varianceCents > 0 ? '+ ' : '− '}${formatEuros(Math.abs(summary.varianceCents))}`}
              detail="Écart entre ce qui a été facturé et ce que valaient les heures rattachées."
            />
          )}

          {summary.owedCents !== 0 && (
            <Aside
              label={summary.owedCents > 0 ? 'Reste dû par le client' : 'Payé d’avance'}
              value={formatEuros(Math.abs(summary.owedCents))}
              detail={summary.owedCents > 0
                ? 'Factures émises, pas encore encaissées.'
                : 'Provisions reçues au-delà de ce qui a été facturé.'}
            />
          )}

          {summary.disbursementsCents !== 0 && (
            <Aside
              label="Débours à refacturer"
              value={formatEuros(summary.disbursementsCents)}
              detail="Avancés pour le compte du client."
            />
          )}
        </div>

        <p className="m-0 mt-2 text-[11px] leading-4">
          {formatDuration(summary.unbilledMinutes)} non facturées.{' '}
          {statement.since
            ? `Dernière facture le ${new Date(statement.since).toLocaleDateString('fr-FR')}.`
            : 'Aucune facture enregistrée pour l’instant.'}
        </p>
      </section>

      {error && <p className="m-0 text-danger">{error}</p>}

      <section className="grid gap-2">
        <h3 className="type-title m-0">Factures émises</h3>
        <Micro>
          Établies dans votre logiciel comptable. Avocado note ce qui est parti.
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

        {/* Below the buttons rather than above the list: dropping a facture is something she does
            occasionally, and the list is what she came to read. */}
        {isOpen && (
          <DropFactures
            matterId={matterId}
            invoices={data.invoices.map((invoice) => ({
              id: invoice.id,
              date: invoice.date,
              externalReference: invoice.externalReference,
              amountExclVatCents: invoice.amountExclVatCents,
              hasDocument: invoice.documentPath !== null,
            }))}
            onFiled={() => { refresh(); onChanged() }}
          />
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
                  {/* Filed by dropping the PDF on this tab. Opening it is the whole point of having
                      attached it: « la facture 92, c'était quoi déjà » answered without leaving the
                      dossier. */}
                  {invoice.documentPath !== null && folder !== null && (
                    <RowAction
                      label="Ouvrir la facture"
                      onClick={() => void window.avocado.revealFile(
                        `${folder}\${invoice.documentPath!.replace(/\//g, '\')}`)}
                    >
                      <FileText size={13} strokeWidth={1.75} />
                    </RowAction>
                  )}

                  {invoice.billedEntryCount > 0 && (
                    <Micro>
                      {invoice.billedEntryCount} ligne{invoice.billedEntryCount > 1 ? 's' : ''} de temps
                      {invoice.varianceCents !== 0 &&
                        ` · ${invoice.varianceCents > 0 ? 'boni' : 'mali'} ${formatEuros(Math.abs(invoice.varianceCents))}`}
                    </Micro>
                  )}
                </RowMain>

                <RowAmount>{formatEuros(invoice.amountExclVatCents)}</RowAmount>
                {invoice.isHistorical && (
                  <span title="Reprise de l’ancien logiciel.">
                    <Badge tone="neutral">antérieure</Badge>
                  </span>
                )}

                <Badge tone={invoice.isPaid ? 'brand' : 'accent'}>
                  {invoice.isPaid ? 'Payée' : 'En attente'}
                </Badge>

                <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                  {invoice.billedEntryCount > 0 && (
                    <>
                      <RowAction
                        label="Détail à joindre à la facture (Word)"
                        onClick={() => void exportDetail(invoice, 'docx')}
                      >
                        <FileText size={13} strokeWidth={1.75} />
                      </RowAction>
                      <RowAction
                        label="Détail des temps passés (Excel), pour préparer la facture"
                        onClick={() => void exportDetail(invoice, 'xlsx')}
                      >
                        <FileSpreadsheet size={13} strokeWidth={1.75} />
                      </RowAction>
                    </>
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
          Provisions reçues et frais avancés pour le client. Sans effet sur le reste à facturer.
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

      <section className="grid gap-2">
        <h3 className="type-title m-0">Sous-traitance</h3>
        <Micro>
          Ce que le dossier a coûté au cabinet : rétrocessions, traductions, expertises. Sans effet
          sur ce que le client doit.
        </Micro>

        {isOpen && (
          <Button
            variant="secondary"
            className="justify-self-start"
            onClick={() => setAddingCost(true)}
          >
            <HandCoins size={14} strokeWidth={1.75} />
            Enregistrer une rétrocession
          </Button>
        )}

        {addingCost && (
          <CostDialog
            matterId={matterId}
            invoices={data.invoices}
            onCancel={() => setAddingCost(false)}
            onSaved={refresh}
          />
        )}

        {editingCost && (
          <CostDialog
            matterId={matterId}
            invoices={data.invoices}
            cost={data.costs.find((candidate) => candidate.id === editingCost)}
            onCancel={() => setEditingCost(null)}
            onSaved={refresh}
          />
        )}

        <div className="grid">
          {data.costs.map((cost) => (
            <Row key={cost.id} className="group">
              <RowDate>{new Date(cost.date).toLocaleDateString('fr-FR')}</RowDate>

              {cost.kind && <Badge>{cost.kind}</Badge>}

              <RowMain>
                <span>{cost.label}</span>
                {(cost.contactName || cost.invoiceReference) && (
                  <Micro>
                    {cost.contactName}
                    {cost.contactName && cost.invoiceReference && ' · '}
                    {cost.invoiceReference && `sur ${cost.invoiceReference}`}
                  </Micro>
                )}
              </RowMain>

              {/* Rendered as what leaves, because that is what it is. */}
              <RowAmount className="text-warning">
                − {formatEuros(cost.amountExclVatCents)}
              </RowAmount>

              <Badge tone={cost.isPaid ? 'brand' : 'accent'}>
                {cost.isPaid ? 'Réglée' : 'À régler'}
              </Badge>

              {isOpen && (
                <Actions
                  onEdit={() => setEditingCost(cost.id)}
                  onDelete={() => void remove(`/api/costs/${cost.id}`)}
                />
              )}
            </Row>
          ))}
        </div>

        {data.costs.length > 0 && (
          <Micro>
            {formatEuros(summary.subcontractedCents)} rétrocédés sur ce dossier
            {data.costsOutstandingCents > 0 &&
              `, dont ${formatEuros(data.costsOutstandingCents)} restant à régler`}
            .
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

/**
 * One adjustment, with what it means next to it. Boni, reste dû and débours used to be stacked into
 * a single figure, which is how « reste à facturer » became a number nobody could take apart.
 */
const Aside = ({ label, value, detail, tone }: {
  label: string
  value: string
  detail: string
  tone?: 'good' | 'bad'
}) => (
  <div className="flex items-baseline gap-2 text-[11.5px] leading-4">
    <span className="type-group opacity-80">{label}</span>
    <span
      className={cn(
        'font-mono text-[13px] font-semibold tnum',
        tone === 'good' && 'text-success',
        tone === 'bad' && 'text-warning',
      )}
    >
      {value}
    </span>
    <span className="min-w-0 flex-1 opacity-80">{detail}</span>
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
 * between them is the interesting one, billing below the recorded time is a mali, above it a boni,
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
        Pour une facture sans heures saisies ici. Comptée comme facturée, sans rattacher de temps.
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
  const [historical, setHistorical] = useState(invoice?.isHistorical ?? false)
  const [discount, setDiscount] = useState(
    invoice?.discountBasisPoints ? String(invoice.discountBasisPoints / 100).replace('.', ',') : '',
  )
  const [discountReason, setDiscountReason] = useState(invoice?.discountReason ?? '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save() {
    const cents = parseAmountToCents(amount)

    if (cents === null) {
      setError('Indiquez un montant positif, par exemple 1 480,00.')
      return
    }

    // Basis points, so « 12,5 » survives the trip: a percentage is not money and does not round to
    // the centime, but it still has to be exact on the détail the client reads.
    const points = discount.trim() === ''
      ? null
      : Math.round(Number(discount.trim().replace(',', '.')) * 100)

    if (points !== null && (!Number.isFinite(points) || points < 0 || points > 10_000)) {
      setError('Indiquez une remise entre 0 et 100, par exemple 30.')
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
        isHistorical: historical,
        discountBasisPoints: points,
        discountReason: discountReason.trim() || null,
      }

      if (invoice) {
        await api(`/api/invoices/${invoice.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/invoices`, body)
        setAmount('')
        setReference('')
        setPaid(false)
        setHistorical(false)
        setDiscount('')
        setDiscountReason('')
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

        {/* Says where a facture came from, and nothing more. It used to be the escape hatch that
            kept « reste à facturer » from subtracting a facture whose hours were never recorded
            here; no facture subtracts anything now, so it is a label. */}
        <label
          className="flex items-center gap-2 text-[13px]"
          title="Reprise de l’ancien logiciel, émise avant Avocado."
        >
          <input
            type="checkbox"
            checked={historical}
            onChange={(event) => setHistorical(event.target.checked)}
          />
          Antérieure
        </label>

        {/* Printed on the détail, not deducted here: the montant above is already what the client
            owes. This says why it is lower than the hours behind it, in the sentence she used to
            type by hand under the total. */}
        <span className="flex items-baseline gap-1">
          <Input
            className="w-[58px] text-right font-mono tnum"
            inputMode="decimal"
            placeholder="0"
            value={discount}
            aria-label="Remise en pourcentage"
            onChange={(event) => { setDiscount(event.target.value); setError(null) }}
          />
          <span className="text-[11.5px] text-muted">% de remise</span>
        </span>

        {discount.trim() !== '' && (
          <Input
            className="flex-1 basis-[170px]"
            value={discountReason}
            placeholder="pour fidélité client"
            aria-label="Motif de la remise"
            onChange={(event) => setDiscountReason(event.target.value)}
          />
        )}

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

/**
 * Suggestions, not a list to choose from. The field is free text and may be left empty; these are
 * only what a datalist offers so the four usual answers are one keystroke away, and a practice that
 * meets a fifth never waits for a release.
 */
const costKinds = [
  'Rétrocession d’honoraires',
  'Sous-traitance',
  'Traduction',
  'Expertise privée',
]

/**
 * Recording what the dossier cost the cabinet.
 *
 * Deliberately not a facture with a minus sign: this one was issued *to* her, « réglée » means she
 * paid rather than the client did, and it must never reach the détail de facturation the client
 * receives. It is also not a débours, a débours is advanced for the client and re-billed at cost, so
 * it raises what remains to invoice, where a rétrocession comes out of the margin and leaves what the
 * client owes exactly as it was.
 */
function CostDialog({ matterId, invoices, cost, onCancel, onSaved }: {
  matterId: string
  invoices: BillingInvoice[]
  cost?: BillingCost
  onCancel: () => void
  onSaved: () => void
}) {
  const [date, setDate] = useState(cost?.date.slice(0, 10) ?? new Date().toISOString().slice(0, 10))
  const [kind, setKind] = useState(cost?.kind ?? '')
  const [label, setLabel] = useState(cost?.label ?? '')
  const [amount, setAmount] = useState(cost ? centsToAmount(cost.amountExclVatCents) : '')
  const [contactId, setContactId] = useState(cost?.contactId ?? '')
  const [reference, setReference] = useState(cost?.externalReference ?? '')
  const [invoiceId, setInvoiceId] = useState(cost?.invoiceId ?? '')
  const [paid, setPaid] = useState(cost?.isPaid ?? false)
  const [contacts, setContacts] = useState<ContactSummary[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<ContactSummary[]>('/api/contacts').then(setContacts).catch(() => setContacts([]))
  }, [])

  async function save() {
    const cents = parseAmountToCents(amount)

    if (cents === null) {
      setError('Indiquez un montant positif, par exemple 3 000,00.')
      return
    }

    if (!label.trim()) {
      setError('Indiquez à quoi correspond cette rétrocession.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = {
        date,
        kind: kind.trim(),
        label: label.trim(),
        amountExclVatCents: cents,
        contactId: contactId || null,
        externalReference: reference.trim() || null,
        invoiceId: invoiceId || null,
        isPaid: paid,
        paidOn: paid ? date : null,
      }

      if (cost) {
        await api(`/api/costs/${cost.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/costs`, body)
      }

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog
      title={cost ? 'Modifier la rétrocession' : 'Enregistrer une rétrocession'}
      width={480}
      onClose={onCancel}
    >
      <div className="grid grid-cols-2 gap-3">
        <Field label="Date">
          <Input inputSize="lg" type="date" value={date} onChange={(event) => setDate(event.target.value)} />
        </Field>

        <Field label="Nature (facultatif)">
          <Input
            inputSize="lg"
            list="cost-kinds"
            value={kind}
            placeholder="Rétrocession d’honoraires"
            onChange={(event) => setKind(event.target.value)}
          />
          <datalist id="cost-kinds">
            {costKinds.map((candidate) => <option key={candidate} value={candidate} />)}
          </datalist>
        </Field>
      </div>

      <Field label="Objet">
        <Input
          inputSize="lg"
          autoFocus
          value={label}
          placeholder="Audience de plaidoirie du 14 mars, cour d’appel de Lyon"
          onChange={(event) => { setLabel(event.target.value); setError(null) }}
        />
      </Field>

      <Field label="Confrère ou prestataire">
        <Select className="h-8" value={contactId} onChange={(event) => setContactId(event.target.value)}>
          <option value="">Non renseigné</option>
          {contacts.map((contact) => (
            <option key={contact.id} value={contact.id}>{contact.displayName}</option>
          ))}
        </Select>
      </Field>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Montant HT">
          <Input
            inputSize="lg"
            className="font-mono tnum"
            invalid={Boolean(error)}
            value={amount}
            onChange={(event) => { setAmount(event.target.value); setError(null) }}
          />
        </Field>

        <Field label="Sa référence de facture">
          <Input
            inputSize="lg"
            value={reference}
            onChange={(event) => setReference(event.target.value)}
          />
        </Field>
      </div>

      {invoices.length > 0 && (
        <Field label="Sur quelle facture">
          <Select className="h-8" value={invoiceId} onChange={(event) => setInvoiceId(event.target.value)}>
            <option value="">Pas rattachée à une facture</option>
            {invoices.map((invoice) => (
              <option key={invoice.id} value={invoice.id}>
                {invoice.externalReference ?? new Date(invoice.date).toLocaleDateString('fr-FR')} ·{' '}
                {formatEuros(invoice.amountExclVatCents)}
              </option>
            ))}
          </Select>
        </Field>
      )}

      <label className="flex items-center gap-2 text-[13px]">
        <input type="checkbox" checked={paid} onChange={(event) => setPaid(event.target.checked)} />
        Déjà réglée au confrère
      </label>

      {error && <p className="m-0 text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={busy} onClick={() => void save()}>
          {cost ? 'Enregistrer' : 'Enregistrer la rétrocession'}
        </Button>
      </DialogActions>
    </Dialog>
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
          ? 'Reçue du client avant facturation : compte comme encaissée.'
          : 'Avancée pour le compte du client : à lui refacturer.'}
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
