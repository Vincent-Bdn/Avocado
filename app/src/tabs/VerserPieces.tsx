import { useEffect, useState } from 'react'
import { ArrowDown, ArrowDownAZ, ArrowUp, Loader2, Stamp, X } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'

export interface Picked {
  name: string
  relativePath: string
}

interface VersedPiece {
  /** « 12 », « 68 bis ». Written, because a pièce number is not an integer. */
  number: string
  name: string
  relativePath: string
  stamped: boolean
  error: string | null
}

interface Result {
  pieces: VersedPiece[]
  bordereauPath: string | null
  mergedPath: string | null
  failed: number
}

/** « Attestation Loubet.pdf » gives « Attestation Loubet »: a libellé is not a file name. */
const suggest = (name: string) => name.replace(/\.[^.]+$/, '')

/**
 * « 2 » before « 10 », and « Pièce 9 » before « Pièce 10 ».
 *
 * <p>Her request was « essayer d'ores et déjà de classer dans l'ordre les pièces (1, 2, 3) ». Plain
 * alphabetical ordering is the thing that puts 10 before 2, and it is what every file manager hands
 * her, so sorting by name without this would not have answered it.</p>
 */
const naturally = new Intl.Collator('fr', { numeric: true, sensitivity: 'base' })

/** One line of the panel: a file, its libellé, and the suffix she may give it. */
interface Line extends Picked {
  label: string
  /** « bis », « ter », « 1 ». Empty for an ordinary pièce. */
  suffix: string
}

/**
 * The numbers this batch will take, in order.
 *
 * <p><b>This mirrors <c>VerserPieces.HandleAsync</c> and the server stays authoritative</b> — the
 * result panel reports what was actually written. It exists so the column beside each libellé shows
 * the real number before she commits, « 68, 68 bis, 68 ter, 69 », rather than after.</p>
 *
 * <p>The rule: a pièce with a suffix stays on the current number, an ordinary one moves to the next.
 * That is what bis means — this document belongs with 68 — and it is why something arriving after
 * the bordereau was sent cannot simply take 69.</p>
 */
function numbering(lines: Line[], first: number | null): string[] {
  if (first === null) return lines.map(() => '—')

  const numbers: string[] = []
  let number = first
  let used = false

  for (const line of lines) {
    const suffix = line.suffix.trim().replace(/[^\p{L}\p{N}]/gu, '').toLowerCase()

    if (suffix === '' && used) {
      number += 1
      used = false
    }

    numbers.push(
      suffix === ''
        ? String(number)
        : /^\d+$/.test(suffix)
          ? `${number}-${suffix}`
          : `${number} ${suffix}`,
    )

    if (suffix === '') used = true
  }

  return numbers
}

/**
 * The panel that turns a selection of documents into numbered pièces.
 *
 * <p><b>Order, then libellés, then the number to start at.</b> Those are the three decisions and they
 * are in the order she makes them. The order is the argument: pièces are cited in the conclusions in
 * the sequence they are numbered, so it is hers, not alphabetical and not by date.</p>
 *
 * <p>The libellé is what a magistrate reads, « Contrat de travail de M. Dupont du 12 mars 2019 »,
 * never « scan_003.pdf ». It is pre-filled from the file's own name because that is a better start
 * than nothing, and it is a text box because it is not the file name.</p>
 */
export function VerserPiecesPanel({ matterId, picked, onClose, onDone }: {
  matterId: string
  picked: Picked[]
  onClose: () => void
  /** The second argument says whether stamping was asked for, so « non tamponné » can be told
   *  apart from « je n'ai pas demandé de tampon ». */
  onDone: (result: Result, wanted: boolean) => void
}) {
  // Sorted to begin with, then hers. The order is the argument — pièces are cited in the conclusions
  // in the sequence they are numbered — so the arrows stay and nothing re-sorts behind her. What
  // changes is the starting point: she ticked fifteen files in whatever order her eye found them,
  // and « scan_12 » after « scan_3 » is the order she meant.
  const [order, setOrder] = useState<Line[]>(() =>
    [...picked]
      .sort((left, right) => naturally.compare(left.name, right.name))
      .map((file) => ({ ...file, label: suggest(file.name), suffix: '' })))
  const [startAt, setStartAt] = useState('')
  const [stamp, setStamp] = useState(true)
  const [merge, setMerge] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The number the dossier is actually at, so the column below shows real numbers and the field
  // opens on a value she can see. « 1 » on a dossier whose Pièces folder is empty, which is what she
  // asked for: « par défaut c'est le n°1 ».
  useEffect(() => {
    api<{ next: number; free: number[] }>(`/api/matters/${matterId}/exhibits/next-number`)
      .then((answer) => setStartAt((current) => current === '' ? String(answer.next) : current))
      .catch(() => setStartAt((current) => current === '' ? '1' : current))
  }, [matterId])

  function move(index: number, by: number) {
    const to = index + by
    if (to < 0 || to >= order.length) return

    const next = [...order]
    const [moved] = next.splice(index, 1)
    next.splice(to, 0, moved!)
    setOrder(next)
  }

  async function verser() {
    setBusy(true)
    setError(null)

    try {
      const result = await post<Result>(`/api/matters/${matterId}/exhibits/verser-batch`, {
        pieces: order.map((file) => ({
          relativePath: file.relativePath,
          label: file.label.trim() || null,
          suffix: file.suffix.trim() || null,
        })),
        // Pre-filled from the dossier, so this is normally her own number sent back. Blank only
        // happens if she clears the field, and the backend then continues past the highest number
        // already used, which is the same answer.
        startNumber: startAt.trim() === '' ? null : Number(startAt.trim()),
        stamp,
        merge,
      })

      onDone(result, stamp)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
      setBusy(false)
    }
  }

  const first = startAt.trim() === '' ? null : Number(startAt.trim())
  const numbers = numbering(order, first)

  return (
    <Dialog title={`Verser ${order.length} pièce${order.length > 1 ? 's' : ''}`} width={640} onClose={busy ? undefined : onClose}>
      <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-muted">
        Numérotées dans cet ordre et copiées dans « Pièces Avocado », à côté de vos propres dossiers.
        Vos originaux ne bougent pas.
      </p>

      <div className="max-h-[320px] overflow-y-auto rounded-md border border-line-subtle">
        {order.map((file, index) => (
          <div
            key={file.relativePath}
            className="flex items-center gap-2 border-b border-[#F1F3EE] px-2 py-1.5 last:border-b-0"
          >
            {/* The number this file will carry, never blank: the column used to be empty until she
                typed a start number, so the panel showed fifteen libellés and no numbering at all. */}
            <span className="w-[52px] shrink-0 text-right font-mono text-[11px] text-ink-secondary tnum">
              {numbers[index]}
            </span>

            <Input
              inputSize="sm"
              className="min-w-0 flex-1"
              value={file.label}
              aria-label={`Libellé de ${file.name}`}
              onChange={(event) => setOrder(order.map((other, other_index) =>
                other_index === index ? { ...other, label: event.target.value } : other))}
            />

            {/* « bis » keeps this pièce on the number above it. Which is what it means: a document
                that arrives after the bordereau was sent cannot take 69, already cited as something
                else, and cannot take 68 either. */}
            <Input
              inputSize="sm"
              className="w-[54px] shrink-0 text-center"
              value={file.suffix}
              placeholder="bis"
              aria-label={`Suffixe de ${file.name}`}
              onChange={(event) => setOrder(order.map((other, other_index) =>
                other_index === index ? { ...other, suffix: event.target.value } : other))}
            />

            <span className="flex shrink-0 gap-0.5">
              <button
                type="button"
                aria-label="Monter"
                disabled={index === 0}
                onClick={() => move(index, -1)}
                className="grid h-6 w-6 place-items-center rounded-[3px] text-ink-secondary hover:bg-hover disabled:opacity-30"
              >
                <ArrowUp size={12} strokeWidth={2} />
              </button>
              <button
                type="button"
                aria-label="Descendre"
                disabled={index === order.length - 1}
                onClick={() => move(index, 1)}
                className="grid h-6 w-6 place-items-center rounded-[3px] text-ink-secondary hover:bg-hover disabled:opacity-30"
              >
                <ArrowDown size={12} strokeWidth={2} />
              </button>
              <button
                type="button"
                aria-label="Retirer"
                onClick={() => setOrder(order.filter((_, other) => other !== index))}
                className="grid h-6 w-6 place-items-center rounded-[3px] text-ink-secondary hover:bg-hover"
              >
                <X size={12} strokeWidth={2} />
              </button>
            </span>
          </div>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-1.5 text-[12px]">
          Commencer au n°
          <Input
            inputSize="sm"
            className="w-[64px] text-center font-mono tnum"
            inputMode="numeric"
            value={startAt}
            onChange={(event) => setStartAt(event.target.value.replace(/\D/g, ''))}
          />
        </label>

        <button
          type="button"
          onClick={() => setOrder((current) =>
            [...current].sort((left, right) => naturally.compare(left.name, right.name)))}
          className="flex items-center gap-1.5 text-[12px] text-brand-on-subtle underline-offset-2 hover:underline"
        >
          <ArrowDownAZ size={13} strokeWidth={1.75} />
          Trier par nom
        </button>

        <label className="flex items-center gap-2 text-[12px]">
          <input type="checkbox" checked={stamp} onChange={(event) => setStamp(event.target.checked)} />
          Tamponner
        </label>

        <label className="flex items-center gap-2 text-[12px]">
          <input type="checkbox" checked={merge} onChange={(event) => setMerge(event.target.checked)} />
          Aussi un PDF unique
        </label>
      </div>

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        Le tampon s’applique aux PDF et aux photos, sur la première page. Il se règle dans Réglages.
      </p>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      <DialogActions>
        <Button variant="secondary" disabled={busy} onClick={onClose}>Annuler</Button>
        <Button disabled={busy || order.length === 0} onClick={() => void verser()}>
          {busy ? <Loader2 size={13} className="animate-spin" /> : <Stamp size={13} strokeWidth={1.75} />}
          {busy ? 'Versement…' : 'Verser'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
