import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { ApiError, blob } from '../api.js'
import { Dialog } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'
import { PdfViewer } from '../components/ui/pdf-viewer.js'

/**
 * The stamp on a page, here, now.
 *
 * <p><b>Why this exists.</b> Checking the number used to mean opening a dossier, ticking a document,
 * versing it, finding the file and opening it in a reader, then deleting the pièce and the number it
 * had consumed. Nobody does that twice, so nobody nudged the percentages, so the number sat wherever
 * the defaults left it.</p>
 *
 * <p><b>And why it shows a real PDF.</b> It drew the page itself at first, in HTML, repeating the
 * backend's arithmetic. That is a second implementation of the only thing this screen exists to
 * verify, and the day the two drift apart the page becomes a confident liar. What is on screen now
 * is the file the backend produced, drawn by the same code that stamps her pièces.</p>
 */
export function StampTester({ onClose }: { onClose: () => void }) {
  const [number, setNumber] = useState('7')
  const [file, setFile] = useState<Uint8Array | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(true)

  useEffect(() => {
    let cancelled = false
    const wanted = Number.parseInt(number, 10)

    setBusy(true)

    // Re-stamped on every change rather than moved about in the browser, so the number she is
    // looking at was placed by the code that will place it on a pièce.
    void blob(`/api/settings/stamp/preview.pdf?numero=${Number.isFinite(wanted) ? wanted : 7}`)
      .then(async (fetched) => {
        if (cancelled) return
        setFile(new Uint8Array(await fetched.arrayBuffer()))
        setError(null)
      })
      .catch((failure: unknown) => {
        if (!cancelled) setError(failure instanceof ApiError ? failure.message : String(failure))
      })
      .finally(() => {
        if (!cancelled) setBusy(false)
      })

    return () => { cancelled = true }
  }, [number])

  return (
    <Dialog title="Essayer le tampon" width={640} onClose={onClose}>
      <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-muted">
        La page telle qu’elle sortira, tamponnée par Avocado. Changez le numéro, ou les réglages
        derrière, et regardez.
      </p>

      {error !== null && <p className="m-0 text-[12.5px] text-danger">{error}</p>}

      <PdfViewer file={file} height={520}>
        <label className="ml-auto flex items-center gap-2 text-[12.5px]">
          Numéro d’essai
          <Input
            inputSize="sm"
            className="w-16 text-right font-mono tnum"
            inputMode="numeric"
            value={number}
            onChange={(event) => setNumber(event.target.value.replace(/\D/g, '').slice(0, 4))}
          />
          {busy && <Loader2 size={13} className="animate-spin text-muted" />}
        </label>
      </PdfViewer>
    </Dialog>
  )
}
