import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/** Radius 10, elevation 3, on the scrim. Widths 380 / 480 / 640 per the design. */
export function Dialog({ title, width = 480, onClose, children }: {
  title: string
  width?: 380 | 480 | 640
  onClose?: () => void
  children: ReactNode
}) {
  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-[var(--surface-scrim)]"
      onClick={onClose}
    >
      <div
        onClick={(event) => event.stopPropagation()}
        style={{ width }}
        className={cn(
          'grid max-w-[calc(100%-48px)] gap-3 rounded-xl bg-panel p-5 shadow-e3',
        )}
      >
        <h2 className="m-0 type-title">{title}</h2>
        {children}
      </div>
    </div>
  )
}

export function DialogActions({ children }: { children: ReactNode }) {
  return <div className="flex justify-end gap-2">{children}</div>
}

/**
 * Field labels sit above their control at the `label` step, 12/16 weight 500.
 *
 * The step goes on a span rather than on the <label> itself: Tailwind's preflight makes form controls
 * inherit their font, so a weight-500 label would quietly render the input's own text in medium too.
 */
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="grid gap-1">
      <span className="type-label text-ink-secondary">{label}</span>
      {children}
    </label>
  )
}
