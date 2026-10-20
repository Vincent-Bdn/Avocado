import { useEffect, useState } from 'react'
import { AlertTriangle, Loader2 } from 'lucide-react'
import { ApiError, api, post } from './api.js'
import { Button } from './components/ui/button.js'
import { Dialog, DialogActions } from './components/ui/dialog.js'
import { Input } from './components/ui/input.js'
import { Select } from './components/ui/select.js'
import { cn } from './lib/utils.js'
import { formatDuration, formatEuros } from './labels.js'
import type { MatterListItem } from './types.js'

interface MergePreview {
  sourceId: string
  sourceReference: string
  sourceName: string
  targetId: string
  targetReference: string
  targetName: string
  activities: number
  timeEntries: number
  billableMinutes: number
  deadlines: number
  invoices: number
  invoicedCents: number
  movements: number
  costs: number
  parties: number
  partiesAlreadyThere: number
  sourceFolder: string | null
  targetFolder: string | null
  warnings: string[]
}

/**
 * Fusionner ce dossier dans un autre.
 *
 * <p>« Problème avec dossier FINANCIERE CN NIETO / FINANCIERE NIETO, 2 dossiers. Peut-on merger sur
 * FINANCIERE CN (NIETO) ? » It happens when a client is created twice under two spellings, and the
 * practice then has one affair spread over two dossiers: two « reste à facturer », two sets of
 * échéances, and a chiffre d'affaires that is only right if you remember to add them up.</p>
 *
 * <p><b>It cannot be undone, so nothing happens until she has seen the figures.</b> The preview
 * counts every row that will move and names what the backend is worried about — differing taux
 * horaires, two forfaits, two documents folders — and the button repeats the two names. A merge that
 * went the wrong way round would take a dossier's whole history into another one's.</p>
 */
export function MergeMatter({ matterId, matterName, onCancel, onMerged }: {
  matterId: string
  matterName: string
  onCancel: () => void
  /** The dossier this one became, so the window can open it instead of the one that is gone. */
  onMerged: (targetId: string) => void
}) {
  const [candidates, setCandidates] = useState<MatterListItem[]>([])
  const [targetId, setTargetId] = useState('')
  const [preview, setPreview] = useState<MergePreview | null>(null)
  const [folder, setFolder] = useState<string | null>(null)
  const [confirmation, setConfirmation] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Closed dossiers included: a duplicate created under a second spelling is very often the one
  // that was closed by mistake, and it is exactly the one she is trying to fold back in.
  useEffect(() => {
    api<{ items: MatterListItem[] }>('/api/matters?status=All&take=500&sort=Name')
      .then((page) => setCandidates(page.items.filter((matter) => matter.id !== matterId)))
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId])

  useEffect(() => {
    if (targetId === '') {
      setPreview(null)
      return
    }

    setPreview(null)
    setError(null)
    setConfirmation('')

    api<MergePreview>(`/api/matters/${matterId}/merge-into/${targetId}/preview`)
      .then((fetched) => {
        setPreview(fetched)
        // Null means « the default », which is the target's folder or the source's when it has none.
        setFolder(null)
      })
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [matterId, targetId])

  async function merge() {
    setBusy(true)
    setError(null)

    try {
      await post(`/api/matters/${matterId}/merge-into/${targetId}`, { documentsFolder: folder })
      onMerged(targetId)
    } catch (failure) {
      setError(messageOf(failure))
      setBusy(false)
    }
  }

  const bothHaveFolders =
    preview !== null
    && preview.sourceFolder !== null
    && preview.targetFolder !== null
    && preview.sourceFolder !== preview.targetFolder

  // Her typing the name back is the last gate. This deletes a dossier and moves its whole history,
  // and a confirm button alone is not enough standing between those two things.
  const ready = preview !== null && confirmation.trim() === preview.targetName.trim()

  return (
    <Dialog title="Fusionner ce dossier" width={640} onClose={busy ? undefined : onCancel}>
      <p className="m-0 text-[12.5px] leading-[19px] text-muted">
        Tout ce qui est rattaché à <strong className="font-medium text-ink">{matterName}</strong> —
        journal, temps passé, échéances, factures, mouvements, tiers — passe sur le dossier que vous
        choisissez, et <strong className="font-medium text-ink">{matterName}</strong> disparaît.
        Aucun fichier n’est déplacé sur votre disque.
      </p>

      <label className="grid gap-1">
        <span className="text-[12px] font-medium">Fusionner dans</span>
        <Select
          className="h-8"
          value={targetId}
          onChange={(event) => setTargetId(event.target.value)}
        >
          <option value="">Choisir le dossier à conserver…</option>
          {candidates.map((matter) => (
            <option key={matter.id} value={matter.id}>
              {matter.reference} · {matter.name}
            </option>
          ))}
        </Select>
      </label>

      {preview !== null && (
        <>
          <div className="grid gap-0.5 rounded-sm bg-sunken px-3 py-2 font-mono text-[11.5px] tnum">
            <Line label="Entrées de journal" value={String(preview.activities)} />
            <Line
              label="Lignes de temps passé"
              value={`${preview.timeEntries} · ${formatDuration(preview.billableMinutes)} facturables`}
            />
            <Line label="Échéances" value={String(preview.deadlines)} />
            <Line
              label="Factures"
              value={`${preview.invoices} · ${formatEuros(preview.invoicedCents)}`}
            />
            <Line label="Mouvements" value={String(preview.movements)} />
            <Line label="Sous-traitance" value={String(preview.costs)} />
            <Line
              label="Tiers"
              value={preview.partiesAlreadyThere > 0
                ? `${preview.parties}, dont ${preview.partiesAlreadyThere} déjà présent${preview.partiesAlreadyThere > 1 ? 's' : ''}`
                : String(preview.parties)}
            />
          </div>

          {preview.warnings.map((warning) => (
            <p
              key={warning}
              className="m-0 flex items-start gap-1.5 text-[11.5px] leading-[17px] text-warning"
            >
              <AlertTriangle size={13} strokeWidth={2} className="mt-px shrink-0" />
              {warning}
            </p>
          ))}

          {bothHaveFolders && (
            <label className="grid gap-1">
              <span className="text-[12px] font-medium">Répertoire de documents à conserver</span>
              <Select
                className="h-8"
                value={folder ?? preview.targetFolder ?? ''}
                onChange={(event) => setFolder(event.target.value)}
              >
                <option value={preview.targetFolder!}>
                  {preview.targetReference} · {preview.targetFolder}
                </option>
                <option value={preview.sourceFolder!}>
                  {preview.sourceReference} · {preview.sourceFolder}
                </option>
              </Select>
            </label>
          )}

          <label className="grid gap-1">
            <span className="text-[12px]">
              Pour confirmer, écrivez le nom du dossier conservé :{' '}
              <strong className="font-medium">{preview.targetName}</strong>
            </span>
            <Input
              inputSize="lg"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
            />
          </label>

          <p className={cn('m-0 text-[11.5px] leading-[17px]', ready ? 'text-ink-secondary' : 'text-muted')}>
            Cette opération est définitive.
          </p>
        </>
      )}

      {error !== null && <p className="m-0 text-[12px] text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" size="lg" disabled={busy} onClick={onCancel}>Annuler</Button>
        <Button size="lg" disabled={!ready || busy} onClick={() => void merge()}>
          {busy && <Loader2 size={13} className="animate-spin" />}
          Fusionner
        </Button>
      </DialogActions>
    </Dialog>
  )
}

const Line = ({ label, value }: { label: string; value: string }) => (
  <div className="flex justify-between gap-3">
    <span className="text-muted">{label}</span>
    <span>{value}</span>
  </div>
)

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)
