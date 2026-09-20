import { useState } from 'react'
import { ArrowDown, ArrowUp, Loader2, Stamp, X } from 'lucide-react'
import { ApiError, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Dialog, DialogActions } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'

export interface Picked {
  name: string
  relativePath: string
}

interface VersedPiece {
  number: number
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
  onDone: (result: Result) => void
}) {
  const [order, setOrder] = useState(() =>
    picked.map((file) => ({ ...file, label: suggest(file.name) })))
  const [startAt, setStartAt] = useState('')
  const [stamp, setStamp] = useState(true)
  const [merge, setMerge] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

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
        pieces: order.map((file) => ({ relativePath: file.relativePath, label: file.label.trim() || null })),
        // Blank continues past the highest number already used, which is what she wants unless she
        // is rebuilding a bordereau from scratch.
        startNumber: startAt.trim() === '' ? null : Number(startAt.trim()),
        stamp,
        merge,
      })

      onDone(result)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
      setBusy(false)
    }
  }

  const first = startAt.trim() === '' ? null : Number(startAt.trim())

  return (
    <Dialog title={`Verser ${order.length} pièce${order.length > 1 ? 's' : ''}`} width={640} onClose={busy ? undefined : onClose}>
      <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-muted">
        Numérotées dans cet ordre et copiées dans « Pièces ». Vos originaux ne bougent pas.
      </p>

      <div className="max-h-[320px] overflow-y-auto rounded-md border border-line-subtle">
        {order.map((file, index) => (
          <div
            key={file.relativePath}
            className="flex items-center gap-2 border-b border-[#F1F3EE] px-2 py-1.5 last:border-b-0"
          >
            <span className="w-8 shrink-0 text-right font-mono text-[11px] text-muted tnum">
              {first === null ? '' : first + index}
            </span>

            <Input
              inputSize="sm"
              className="min-w-0 flex-1"
              value={file.label}
              aria-label={`Libellé de ${file.name}`}
              onChange={(event) => setOrder(order.map((other, other_index) =>
                other_index === index ? { ...other, label: event.target.value } : other))}
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
            placeholder="suite"
            value={startAt}
            onChange={(event) => setStartAt(event.target.value.replace(/\D/g, ''))}
          />
        </label>

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
        Le tampon ne s’applique qu’aux PDF, sur la première page. Il se règle dans Réglages.
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
