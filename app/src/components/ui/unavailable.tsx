import { RefreshCw } from 'lucide-react'
import { Button } from './button.js'

/**
 * What a panel shows when the call behind it failed.
 *
 * <p><b>Why this exists as its own thing.</b> Three sections of Réglages were written as
 * <c>.catch(() => setState(null))</c> followed by <c>if (state === null) return null</c>, which
 * renders nothing at all. A blank panel is the worst way to fail: it does not look like a failure, it
 * looks like a feature that is empty, or missing, or that never worked. « Tampon des pièces is empty,
 * nothing shows » is exactly what that produces, and the actual cause was a 404 nobody could see.</p>
 *
 * <p>So the panel says it could not load, says why, and offers to try again. The reason is shown
 * verbatim rather than softened, because the person reading it is usually the one who can act on
 * it.</p>
 */
export function Unavailable({ detail, onRetry }: { detail?: string | null; onRetry?: () => void }) {
  return (
    <div className="grid gap-2 rounded-sm bg-sunken px-2.5 py-2">
      <span className="text-[12.5px] text-ink">Cette section n’a pas pu être chargée.</span>

      {detail && (
        <p className="m-0 max-w-[72ch] font-mono text-[11px] leading-[16px] text-muted">{detail}</p>
      )}

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        Si Avocado vient d’être mis à jour, fermez-le complètement et rouvrez-le : la fenêtre et son
        moteur doivent être de la même version.
      </p>

      {onRetry && (
        <div>
          <Button variant="secondary" size="sm" onClick={onRetry}>
            <RefreshCw size={13} strokeWidth={2} />
            Réessayer
          </Button>
        </div>
      )}
    </div>
  )
}
