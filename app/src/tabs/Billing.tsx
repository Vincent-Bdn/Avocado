import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, post } from '../api.js'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'

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

  if (!data) return <TabPanel>{error && <p className="m-0 text-danger">{error}</p>}</TabPanel>

  const { summary, statement } = data
  const refresh = () => { reload(); onChanged() }

  return (
    <TabPanel>
      {/* The subtraction has to be checkable by eye: a total you cannot recompute is never believed. */}
      <section className="rounded-md border border-accent bg-accent-subtle px-4 py-3.5 text-warning">
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

      <section className="grid gap-2">
        <h3 className="m-0 text-[13px] font-semibold">Factures émises</h3>
        <Micro>
          Ces factures ont été établies dans votre logiciel comptable. Avocado n’en produit aucune :
          il note ce qui est parti, pour savoir ce qui reste.
        </Micro>

        {isOpen && <InvoiceForm matterId={matterId} onAdded={refresh} />}

        <div className="grid">
          {data.invoices.map((invoice) => (
            <Row key={invoice.id}>
              <RowDate>{new Date(invoice.date).toLocaleDateString('fr-FR')}</RowDate>
              <RowMain><span>{invoice.externalReference ?? 'Sans référence'}</span></RowMain>
              <RowAmount>{formatEuros(invoice.amountExclVatCents)}</RowAmount>
              <Badge tone={invoice.isPaid ? 'brand' : 'accent'}>
                {invoice.isPaid ? 'Payée' : 'En attente'}
              </Badge>
            </Row>
          ))}
        </div>

        {data.invoices.length > 0 && (
          <Micro>Reste dû sur les factures émises : {formatEuros(data.invoicedOutstandingCents)}</Micro>
        )}
      </section>

      <section className="grid gap-2">
        <h3 className="m-0 text-[13px] font-semibold">Mouvements</h3>

        {isOpen && <MovementForm matterId={matterId} onAdded={refresh} />}

        <div className="grid">
          {data.ledger.map((entry) => (
            <Row key={entry.id}>
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
            </Row>
          ))}
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

const Line = ({ label, value }: { label: string; value: string }) => (
  <div className="flex justify-between">
    <span>{label}</span>
    <span>{value}</span>
  </div>
)

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
    <InlineForm>
      <Input type="date" value={date} onChange={(event) => setDate(event.target.value)} aria-label="Date" />
      <Input
        className="w-28 font-mono tnum"
        value={amount}
        placeholder="€ HT"
        onChange={(event) => setAmount(event.target.value)}
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
      <Button disabled={!amount} onClick={() => void add()}>Enregistrer</Button>
    </InlineForm>
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
    <InlineForm>
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
        value={amount}
        placeholder={kind === 'Receipt' ? 'Montant reçu' : 'Montant avancé'}
        onChange={(event) => setAmount(event.target.value)}
      />

      <Input
        className="flex-1 basis-[180px]"
        value={label}
        placeholder={kind === 'Receipt' ? 'Provision sur honoraires…' : 'Frais de greffe…'}
        onChange={(event) => setLabel(event.target.value)}
      />

      <Button disabled={!amount || !label.trim()} onClick={() => void add()}>
        {kind === 'Receipt' ? 'Enregistrer l’encaissement' : 'Enregistrer le débours'}
      </Button>
    </InlineForm>
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
        'h-5 rounded-[3px] px-2 text-[11px] transition-colors',
        !active && 'text-ink-secondary hover:bg-hover',
        active && tone === 'brand' && 'bg-brand-subtle text-brand-on-subtle',
        active && tone === 'accent' && 'bg-accent-subtle text-warning',
      )}
    >
      {children}
    </button>
  )
}
