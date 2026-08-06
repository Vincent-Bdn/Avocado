import { useCallback, useEffect, useState } from 'react'
import { Pencil, Trash2, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { centsToAmount, parseAmountToCents } from '../lib/amount.js'
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

  return (
    <TabPanel>
      {/* The subtraction has to be checkable by eye: a total you cannot recompute is never believed. */}
      <section className="rounded-md border border-[#E8D5AE] bg-[#FDF8ED] px-4 py-3.5 text-[#6E4A0E]">
        <div className="text-[12px] font-medium">Reste à facturer</div>
        <div className="font-mono text-[28px] leading-[34px] font-semibold tracking-[-0.02em] tnum">
          {formatEuros(summary.leftToBillCents)}
        </div>

        <div className="mt-2.5 grid gap-0.5 rounded-sm bg-panel px-2.5 py-2 font-mono text-[12px] text-ink tnum">
          <Line label="Temps facturable" value={formatEuros(summary.billableTimeCents)} />
          <Line label="− Mouvements" value={formatEuros(summary.ledgerCents)} />
          <Line label="− Déjà facturé" value={formatEuros(summary.invoicedCents)} />
          <div className="mt-1 flex justify-between border-t border-line-subtle pt-1 font-semibold">
            <span>= Reste à facturer</span>
            <span>{formatEuros(summary.leftToBillCents)}</span>
          </div>
        </div>

        <p className="m-0 mt-2 text-[11px] leading-4">
          {formatDuration(summary.billableMinutes)} facturables.{' '}
          {statement.since
            ? `Depuis la dernière facture du ${new Date(statement.since).toLocaleDateString('fr-FR')} : ${formatDuration(statement.billableMinutes)} soit ${formatEuros(statement.billableAmountCents)}.`
            : 'Aucune facture enregistrée pour l’instant.'}
        </p>
      </section>

      {error && <p className="m-0 text-danger">{error}</p>}

      <section className="grid gap-2">
        <h3 className="type-title m-0">Factures émises</h3>
        <Micro>
          Ces factures ont été établies dans votre logiciel comptable. Avocado n’en produit aucune :
          il note ce qui est parti, pour savoir ce qui reste.
        </Micro>

        {isOpen && <InvoiceForm matterId={matterId} onSaved={refresh} />}

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
                <RowMain><span>{invoice.externalReference ?? 'Sans référence'}</span></RowMain>
                <RowAmount>{formatEuros(invoice.amountExclVatCents)}</RowAmount>
                <Badge tone={invoice.isPaid ? 'brand' : 'accent'}>
                  {invoice.isPaid ? 'Payée' : 'En attente'}
                </Badge>

                {isOpen && (
                  <Actions
                    onEdit={() => setEditingInvoice(invoice.id)}
                    onDelete={() => void remove(`/api/invoices/${invoice.id}`)}
                  />
                )}
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

        {isOpen && <MovementForm matterId={matterId} onSaved={refresh} />}

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

/** One form for both adding and correcting, so the two can never drift apart. */
function InvoiceForm({ matterId, invoice, onSaved, onCancel }: {
  matterId: string
  invoice?: BillingInvoice
  onSaved: () => void
  onCancel?: () => void
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

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(invoice)}>
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

        {onCancel && (
          <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
            <X size={13} strokeWidth={2} />
          </Button>
        )}
      </InlineForm>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </div>
  )
}

/**
 * Nature first, amount second, and the amount is always positive. A débours typed as a positive
 * number would silently corrupt every balance on the dossier, so the sign is never exposed here and
 * the server applies it on both create and update.
 */
function MovementForm({ matterId, movement, onSaved, onCancel }: {
  matterId: string
  movement?: BillingMovement
  onSaved: () => void
  onCancel?: () => void
}) {
  const [kind, setKind] = useState<MovementKind>(movement?.kind ?? 'Receipt')
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

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(movement)}>
        <div className="flex gap-0.5">
          <KindButton active={kind === 'Receipt'} tone="brand" onClick={() => setKind('Receipt')}>
            Encaissement
          </KindButton>
          <KindButton active={kind === 'Disbursement'} tone="accent" onClick={() => setKind('Disbursement')}>
            Débours
          </KindButton>
        </div>

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

        {onCancel && (
          <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
            <X size={13} strokeWidth={2} />
          </Button>
        )}
      </InlineForm>

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
