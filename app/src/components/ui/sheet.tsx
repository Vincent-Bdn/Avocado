import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { Button } from './button.js'
import { cn } from '../../lib/utils.js'

/**
 * The right side panel: 420px by default, full height, radius 10 on the left edge only.
 *
 * What is behind stays readable and is deliberately **not** dimmed. A sheet is for working alongside
 * the screen you came from, which is the whole reason it exists rather than a dialog.
 */
export function Sheet({ title, width = 420, onClose, footer, children }: {
  title: string
  width?: number
  onClose: () => void
  footer?: ReactNode
  children: ReactNode
}) {
  useEffect(() => {
    const close = (event: KeyboardEvent) => event.key === 'Escape' && onClose()
    window.addEventListener('keydown', close)
    return () => window.removeEventListener('keydown', close)
  }, [onClose])

  return (
    <aside
      role="dialog"
      aria-label={title}
      style={{ width }}
      className={cn(
        'fixed top-1.5 right-1.5 bottom-1.5 z-50 flex flex-col overflow-hidden',
        'rounded-l-xl border border-line bg-panel shadow-e3',
      )}
    >
      <header className="flex h-12 shrink-0 items-center gap-2 border-b border-line-subtle px-4">
        <h2 className="type-title m-0 flex-1 truncate">{title}</h2>
        <span className="type-kbd rounded-[3px] bg-sunken px-1.5 py-0.5 text-muted">esc</span>
        <Button variant="ghost" size="iconSm" aria-label="Fermer" onClick={onClose}>
          <X size={14} strokeWidth={2} />
        </Button>
      </header>

      <div className="grid flex-1 content-start gap-3 overflow-y-auto px-4 py-4">{children}</div>

      {footer && (
        <footer className="shrink-0 border-t border-line-subtle bg-app px-4 py-3">{footer}</footer>
      )}
    </aside>
  )
}
