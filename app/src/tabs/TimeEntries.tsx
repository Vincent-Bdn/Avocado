import { useCallback, useEffect, useState } from 'react'
import { Pencil, ReceiptText, Trash2, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { parseDuration, toDurationInput } from '../lib/duration.js'
import { centsToAmount, parseAmountToCents } from '../lib/amount.js'
import { formatDuration, formatEuros } from '../labels.js'
import { InlineForm, Micro, Row, RowAction, RowAmount, RowDate, RowMain, TabPanel } from './shared.js'

interface TimeEntryItem {
  id: string
  date: string
  startedAt: string | null
  task: string
  durationMinutes: number
  isBillable: boolean
  appliedRateCents: number
  isRateOverridden: boolean
  amountCents: number
  fromActivityId: string | null
  invoiceId: string | null
  invoiceReference: string | null
}

interface TimeEntryPage {
  items: TimeEntryItem[]
  totals: {
    todayMinutes: number
    weekMinutes: number
    matterMinutes: number
    billableMinutes: number
    nonBillableMinutes: number
    billableAmountCents: number
  }
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

/**
 * Temps passé. No chronometer: most lawyers write the time down at the end, and a running timer that
 * a crash could lose is a worse promise than none at all. Every line can be corrected, because a
 * duration typed in a hurry is exactly the kind of thing that needs correcting.
 */
export function TimeEntries({ matterId, hourlyRateCents, isOpen, onChanged }: {
  matterId: string
  hourlyRateCents: number
  isOpen: boolean
  onChanged: () => void
}) {
  const [page, setPage] = useState<TimeEntryPage | null>(null)
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<TimeEntryPage>(`/api/matters/${matterId}/time-entries`)
      .then(setPage)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(reload, [reload])

  const refresh = () => {
    setEditing(null)
    reload()
    onChanged()
  }

  async function remove(entry: TimeEntryItem) {
    setError(null)

    try {
      await api(`/api/time-entries/${entry.id}`, { method: 'DELETE' })
      refresh()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  return (
    <TabPanel>
      {isOpen && (
        <EntryForm matterId={matterId} matterHourlyRateCents={hourlyRateCents} onSaved={refresh} />
      )}

      {error && <p className="m-0 text-danger">{error}</p>}

      {page && (
        <div className="flex flex-wrap items-center gap-3 rounded-md bg-sunken px-2.5 py-2 font-mono text-[11.5px] tnum">
          <Total label="Aujourd’hui" value={formatDuration(page.totals.todayMinutes)} />
          <Rule />
          <Total label="Cette semaine" value={formatDuration(page.totals.weekMinutes)} />
          <Rule />
          <Total label="Total" value={formatDuration(page.totals.matterMinutes)} />

          <span className="flex-1" />

          <span>
            {formatDuration(page.totals.billableMinutes)} facturables ·{' '}
            <strong className="font-semibold">{formatEuros(page.totals.billableAmountCents)}</strong>
          </span>
        </div>
      )}

      {page?.items.length === 0 && (
        <EmptyState title="Aucun temps saisi">
          Ce que vous ne notez pas maintenant ne se facturera jamais. Le plus simple reste de
          l’attacher à l’entrée de journal, au moment où vous la notez.
        </EmptyState>
      )}

      <div className="grid">
        {page?.items.map((entry) =>
          editing === entry.id ? (
            <EntryForm
              matterHourlyRateCents={hourlyRateCents}
              key={entry.id}
              matterId={matterId}
              entry={entry}
              onSaved={refresh}
              onCancel={() => setEditing(null)}
            />
          ) : (
            <Row
              key={entry.id}
              className={cn(
                'group',
                // A half-rate agreed in February must still be visible in June.
                entry.isRateOverridden && 'bg-accent-subtle text-warning',
                !entry.isBillable && 'text-muted',
                // Billed hours are still worth reading, so they are dimmed rather than hidden: the
                // dossier's history is the point of the tab.
                entry.invoiceId && 'text-muted',
              )}
            >
              <RowDate>
                {new Date(entry.date).toLocaleDateString('fr-FR')}
                {entry.startedAt && ` · ${entry.startedAt.slice(0, 5)}`}
              </RowDate>

              <RowMain>
                <span>{entry.task}</span>
                {entry.fromActivityId && <Micro>depuis le journal</Micro>}
              </RowMain>

              {entry.invoiceId && (
                <span
                  className="flex shrink-0 items-center gap-1 rounded-[3px] bg-sunken px-1.5 py-1 text-[10.5px] leading-3 text-muted"
                  title={
                    entry.invoiceReference
                      ? `Facturée sur ${entry.invoiceReference}`
                      : 'Facturée'
                  }
                >
                  <ReceiptText size={10} strokeWidth={2} />
                  facturée
                </span>
              )}

              <span className="font-mono tnum">{formatDuration(entry.durationMinutes)}</span>

              <span className="font-mono text-muted tnum">
                {entry.isBillable ? `${formatEuros(entry.appliedRateCents)}/h` : 'non facturable'}
              </span>

              <RowAmount className={entry.isRateOverridden ? 'font-medium' : ''}>
                {entry.isBillable ? formatEuros(entry.amountCents) : ''}
              </RowAmount>

              {/* A billed line is not editable here: correcting it would silently change what the
                  client was invoiced. The way back is to delete the facture, which releases its
                  hours. */}
              {isOpen && !entry.invoiceId && (
                <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                  <RowAction label="Modifier" onClick={() => setEditing(entry.id)}>
                    <Pencil size={13} strokeWidth={1.75} />
                  </RowAction>
                  <RowAction label="Supprimer" danger onClick={() => void remove(entry)}>
                    <Trash2 size={13} strokeWidth={1.75} />
                  </RowAction>
                </span>
              )}
            </Row>
          ),
        )}
      </div>
    </TabPanel>
  )
}

const Rule = () => <span aria-hidden="true" className="h-3 w-px bg-line" />

const Total = ({ label, value }: { label: string; value: string }) => (
  <span>
    <span className="text-muted">{label}</span> {value}
  </span>
)

/** One form for both adding and correcting, so the two can never drift apart. */
function EntryForm({ matterId, matterHourlyRateCents, entry, onSaved, onCancel }: {
  matterId: string
  matterHourlyRateCents: number
  entry?: TimeEntryItem
  onSaved: () => void
  onCancel?: () => void
}) {
  const [date, setDate] = useState(entry?.date.slice(0, 10) ?? new Date().toISOString().slice(0, 10))
  const [duration, setDuration] = useState(entry ? toDurationInput(entry.durationMinutes) : '')
  const [task, setTask] = useState(entry?.task ?? '')
  const [billable, setBillable] = useState(entry?.isBillable ?? true)
  const [rate, setRate] = useState(
    entry?.isRateOverridden ? centsToAmount(entry.appliedRateCents) : '',
  )
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const durationMinutes = parseDuration(duration)

  async function save() {
    if (durationMinutes === null || durationMinutes <= 0) {
      setError('Indiquez une durée : 0,25 pour un quart d’heure, 2 pour deux heures, ou 1h30.')
      return
    }

    // Empty is « the dossier's rate », which is not the same as zero and must not become it.
    const rateCents = rate.trim() === '' ? null : parseAmountToCents(rate)

    if (rate.trim() !== '' && rateCents === null) {
      setError('Indiquez un taux horaire positif, par exemple 200,00, ou laissez le champ vide.')
      return
    }

    if (!task.trim()) {
      setError('Décrivez ce qui a été fait.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = {
        date,
        startedAt: entry?.startedAt ?? null,
        task: task.trim(),
        durationMinutes,
        isBillable: billable,
        hourlyRateCentsOverride: rateCents,
      }

      if (entry) {
        await api(`/api/time-entries/${entry.id}`, { method: 'PUT', body: JSON.stringify(body) })
      } else {
        await post(`/api/matters/${matterId}/time-entries`, body)
        setDuration('')
        setTask('')
        // The rate is deliberately kept: a dérogation applies to a run of entries, not to one.
      }

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  /**
   * Entrée saves, from any field. She records time between two telephone calls and reaching for the
   * mouse to finish a line she has already typed is the friction that stops it being recorded at all.
   */
  function submitOnEnter(event: React.KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey && !busy) {
      event.preventDefault()
      void save()
    }
  }

  const reading = durationMinutes !== null && durationMinutes > 0 ? formatDuration(durationMinutes) : null

  return (
    <div className="grid gap-1">
      <InlineForm editing={Boolean(entry)}>
        <Input
          type="date"
          value={date}
          onChange={(event) => setDate(event.target.value)}
          aria-label="Date"
          onKeyDown={submitOnEnter}
        />

        {/* One field, in decimal hours, because that is how a convention d'honoraires is written
            and how she reads her own time back. It still accepts « 1h30 ». Whatever she types, the
            échoed reading underneath says what was understood before anything is saved: parsing
            input without showing the reading is how a duration silently lands wrong. */}
        <span className="flex items-baseline gap-1.5">
          <Input
            className="w-[68px] text-center font-mono tnum"
            inputMode="decimal"
            placeholder="0,25"
            value={duration}
            aria-label="Durée en heures"
            onChange={(event) => { setDuration(event.target.value); setError(null) }}
            onKeyDown={submitOnEnter}
          />
          <span className={cn('text-[11.5px] tabular-nums', reading ? 'text-muted' : 'text-disabled')}>
            {reading ?? 'heures'}
          </span>
        </span>

        <Input
          className="flex-1 basis-[200px]"
          value={task}
          placeholder="Rédaction des conclusions, appel du confrère…"
          onChange={(event) => { setTask(event.target.value); setError(null) }}
          onKeyDown={submitOnEnter}
        />

        {/* Blank means the dossier's rate. Filled, it is a taux dérogatoire on this line alone,
            which is the only way to bill one diligence differently without repricing history. */}
        <span className="flex items-baseline gap-1">
          <Input
            className="w-[76px] text-right font-mono tnum"
            inputMode="decimal"
            placeholder={centsToAmount(matterHourlyRateCents)}
            value={rate}
            aria-label="Taux horaire de cette ligne"
            onChange={(event) => { setRate(event.target.value); setError(null) }}
            onKeyDown={submitOnEnter}
          />
          <span className="text-[11.5px] text-muted">€/h</span>
        </span>

        <label className="flex items-center gap-2 text-[13px]">
          <input
            type="checkbox"
            checked={billable}
            onChange={(event) => setBillable(event.target.checked)}
          />
          Facturable
        </label>

        <Button disabled={busy} onClick={() => void save()}>
          {entry ? 'Enregistrer' : 'Ajouter'}
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
