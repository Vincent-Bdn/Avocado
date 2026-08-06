import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from '../api.js'
import { formatDuration, formatEuros } from '../labels.js'

type MovementKind = 'Receipt' | 'Disbursement'

interface BillingOverview {
  summary: {
    billableTimeCents: number
    billableMinutes: number
    ledgerCents: number
    invoicedCents: number
    leftToBillCents: number
  }
  invoices: {
    id: string
    date: string
    externalReference: string | null
    amountExclVatCents: number
    isPaid: boolean
    paidOn: string | null
  }[]
  invoicedOutstandingCents: number
  ledger: { id: string; date: string; label: string; amountCents: number; kind: MovementKind }[]
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
 * remains is knowable.
 */
export function Billing({ matterId, isOpen, onChanged }: {
  matterId: string
  isOpen: boolean
  onChanged: () => void
}) {
  const [data, setData] = useState<BillingOverview | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<BillingOverview>(`/api/matters/${matterId}/billing`)
      .then(setData)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [matterId])

  useEffect(reload, [reload])

  if (!data) return <div className="tab-panel">{error && <p className="danger">{error}</p>}</div>

  const { summary, statement } = data

  return (
    <div className="tab-panel billing">
      {/* The subtraction has to be checkable by eye: a total you cannot recompute is never believed. */}
      <section className="billing-total">
        <div className="billing-label">Reste à facturer</div>
        <div className="billing-amount mono">{formatEuros(summary.leftToBillCents)}</div>

        <div className="billing-breakdown mono">
          <div>
            <span>Temps facturable</span>
            <span>{formatEuros(summary.billableTimeCents)}</span>
          </div>
          <div>
            <span>− Mouvements</span>
            <span>{formatEuros(summary.ledgerCents)}</span>
          </div>
          <div>
            <span>− Déjà facturé</span>
            <span>{formatEuros(summary.invoicedCents)}</span>
          </div>
          <div className="billing-result">
            <span>= Reste à facturer</span>
            <span>{formatEuros(summary.leftToBillCents)}</span>
          </div>
        </div>

        <p className="muted micro">
          {formatDuration(summary.billableMinutes)} facturables.{' '}
          {statement.since
            ? `Depuis la dernière facture du ${new Date(statement.since).toLocaleDateString('fr-FR')} : ${formatDuration(statement.billableMinutes)} soit ${formatEuros(statement.billableAmountCents)}.`
            : 'Aucune facture enregistrée pour l’instant.'}
        </p>
      </section>

      <section>
        <h3 className="tab-title">Factures émises</h3>
        <p className="muted micro">
          Ces factures ont été établies dans votre logiciel comptable. Avocado n’en produit aucune :
          il note ce qui est parti, pour savoir ce qui reste.
        </p>

        {isOpen && <InvoiceForm matterId={matterId} onAdded={() => { reload(); onChanged() }} />}

        <div className="rows">
          {data.invoices.map((invoice) => (
            <div key={invoice.id} className="billing-row">
              <span className="mono row-date">{new Date(invoice.date).toLocaleDateString('fr-FR')}</span>
              <span className="row-main">{invoice.externalReference ?? 'Sans référence'}</span>
              <span className="mono row-amount">{formatEuros(invoice.amountExclVatCents)}</span>
              <span className={`badge ${invoice.isPaid ? 'badge-open' : 'badge-pending'}`}>
                {invoice.isPaid ? 'Payée' : 'En attente'}
              </span>
            </div>
          ))}
        </div>

        {data.invoices.length > 0 && (
          <p className="muted micro">
            Reste dû sur les factures émises : {formatEuros(data.invoicedOutstandingCents)}
          </p>
        )}
      </section>

      <section>
        <h3 className="tab-title">Mouvements</h3>

        {isOpen && <MovementForm matterId={matterId} onAdded={() => { reload(); onChanged() }} />}

        <div className="rows">
          {data.ledger.map((entry) => (
            <div key={entry.id} className="billing-row">
              <span className="mono row-date">{new Date(entry.date).toLocaleDateString('fr-FR')}</span>
              <span className={`badge ${entry.kind === 'Receipt' ? 'badge-open' : 'badge-pending'}`}>
                {entry.kind === 'Receipt' ? 'Encaissement' : 'Débours'}
              </span>
              <span className="row-main">{entry.label}</span>
              <span className={`mono row-amount ${entry.amountCents < 0 ? 'amount-out' : 'amount-in'}`}>
                {entry.amountCents >= 0 ? '+ ' : '− '}
                {formatEuros(Math.abs(entry.amountCents))}
              </span>
            </div>
          ))}
        </div>

        {data.ledger.length > 0 && (
          <p className="muted micro">
            Solde : {formatEuros(data.receiptsCents)} encaissés,{' '}
            {formatEuros(data.disbursementsCents)} avancés.
          </p>
        )}
      </section>
    </div>
  )
}

function InvoiceForm({ matterId, onAdded }: { matterId: string; onAdded: () => void }) {
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [paid, setPaid] = useState(false)

  async function add() {
    await post(`/api/matters/${matterId}/invoices`, {
      date,
      amountExclVatCents: Math.round(Number(amount.replace(',', '.')) * 100),
      externalReference: reference || null,
      isPaid: paid,
      paidOn: paid ? date : null,
    })

    setAmount('')
    setReference('')
    setPaid(false)
    onAdded()
  }

  return (
    <div className="inline-form">
      <input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />
      <input className="narrow" value={amount} placeholder="€ HT" onChange={(event) => setAmount(event.target.value)} />
      <input className="flex" value={reference} placeholder="Référence externe" onChange={(event) => setReference(event.target.value)} />
      <label className="confirm">
        <input type="checkbox" checked={paid} onChange={(event) => setPaid(event.target.checked)} />
        Payée
      </label>
      <button type="button" disabled={!amount} onClick={() => void add()}>
        Enregistrer
      </button>
    </div>
  )
}

/**
 * Nature first, amount second, and the amount is always positive. A débours typed as a positive
 * number would silently corrupt every balance on the dossier, so the sign is never exposed here and
 * the server refuses one anyway.
 */
function MovementForm({ matterId, onAdded }: { matterId: string; onAdded: () => void }) {
  const [kind, setKind] = useState<MovementKind>('Receipt')
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [amount, setAmount] = useState('')
  const [label, setLabel] = useState('')

  async function add() {
    await post(`/api/matters/${matterId}/ledger-entries`, {
      kind,
      date,
      amountCents: Math.round(Number(amount.replace(',', '.')) * 100),
      label,
    })

    setAmount('')
    setLabel('')
    onAdded()
  }

  return (
    <div className="inline-form">
      <div className="kind-toggle">
        <button
          type="button"
          className={`segment ${kind === 'Receipt' ? 'segment-active' : ''}`}
          onClick={() => setKind('Receipt')}
        >
          Encaissement
        </button>
        <button
          type="button"
          className={`segment ${kind === 'Disbursement' ? 'segment-accent' : ''}`}
          onClick={() => setKind('Disbursement')}
        >
          Débours
        </button>
      </div>

      <input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />

      <input
        className="narrow"
        value={amount}
        placeholder={kind === 'Receipt' ? 'Montant reçu' : 'Montant avancé'}
        onChange={(event) => setAmount(event.target.value)}
      />

      <input
        className="flex"
        value={label}
        placeholder={kind === 'Receipt' ? 'Provision sur honoraires…' : 'Frais de greffe…'}
        onChange={(event) => setLabel(event.target.value)}
      />

      <button type="button" disabled={!amount || !label.trim()} onClick={() => void add()}>
        {kind === 'Receipt' ? 'Enregistrer l’encaissement' : 'Enregistrer le débours'}
      </button>
    </div>
  )
}
