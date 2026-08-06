import { useCallback, useRef, useState } from 'react'
import { Check, TriangleAlert, X } from 'lucide-react'
import { cn } from '../../lib/utils.js'

type Tone = 'success' | 'danger'

interface Toast {
  id: number
  tone: Tone
  title: string
  detail?: string
}

/**
 * Bottom-right, radius 6, a 3px left border in the status colour.
 *
 * Success dismisses itself after four seconds; a failure stays until it is dismissed. A message that
 * disappears before it is read is the same as no message, and an export that says nothing at all is
 * how you end up opening a folder to check whether it worked.
 */
export function useToasts() {
  const [toasts, setToasts] = useState<Toast[]>([])
  const next = useRef(0)

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id))
  }, [])

  const show = useCallback((tone: Tone, title: string, detail?: string) => {
    const id = next.current++
    setToasts((current) => [...current, { id, tone, title, detail }])

    if (tone === 'success') {
      setTimeout(() => dismiss(id), 4000)
    }
  }, [dismiss])

  const view = <ToastStack toasts={toasts} onDismiss={dismiss} />

  return {
    view,
    succeeded: useCallback((title: string, detail?: string) => show('success', title, detail), [show]),
    failed: useCallback((title: string, detail?: string) => show('danger', title, detail), [show]),
  }
}

function ToastStack({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  if (toasts.length === 0) return null

  return (
    <div
      role="status"
      aria-live="polite"
      className="pointer-events-none absolute right-4 bottom-4 z-40 grid justify-items-end gap-1.5"
    >
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={cn(
            'pointer-events-auto flex max-w-[380px] items-start gap-2 rounded-md border border-l-[3px] bg-raised px-3 py-2 shadow-e1',
            toast.tone === 'success' ? 'border-line border-l-success' : 'border-line border-l-danger',
          )}
        >
          <span className={cn('mt-0.5 shrink-0', toast.tone === 'success' ? 'text-success' : 'text-danger')}>
            {toast.tone === 'success'
              ? <Check size={13} strokeWidth={2.5} />
              : <TriangleAlert size={13} strokeWidth={2} />}
          </span>

          <span className="grid min-w-0 gap-0.5">
            <span className="text-[12px] font-medium">{toast.title}</span>
            {toast.detail && (
              <span className="text-[11px] leading-4 break-words text-muted">{toast.detail}</span>
            )}
          </span>

          <button
            type="button"
            aria-label="Fermer"
            onClick={() => onDismiss(toast.id)}
            className="ml-1 shrink-0 text-muted hover:text-ink"
          >
            <X size={12} strokeWidth={2} />
          </button>
        </div>
      ))}
    </div>
  )
}
