import type { ReactNode } from 'react'
import { cn } from '../lib/utils.js'

/** Every tab body: scrolls independently, 12/16 padding, 12px gaps. */
export function TabPanel({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <div className={cn('grid content-start gap-3 overflow-y-auto px-4 pt-3 pb-5', className)}>
      {children}
    </div>
  )
}

/** The inline add row at the top of a tab: bordered, wrapping, controls at 28px. */
export function InlineForm({ editing, children }: { editing?: boolean; children: ReactNode }) {
  return (
    <div
      className={cn(
        'flex flex-wrap items-center gap-2 rounded-md border px-3 py-2.5',
        editing ? 'border-[var(--focus-ring)]' : 'border-line-strong',
      )}
    >
      {children}
    </div>
  )
}

/** A dense list row: 34px minimum, top rule, never wrapping. */
export function Row({ className, onDoubleClick, children }: {
  className?: string
  onDoubleClick?: () => void
  children: ReactNode
}) {
  return (
    <div
      onDoubleClick={onDoubleClick}
      className={cn('flex min-h-[34px] items-center gap-2.5 border-t border-line-subtle px-2 py-1.5 text-[12px]', className)}
    >
      {children}
    </div>
  )
}

export const RowDate = ({ children }: { children: ReactNode }) => (
  <span className="w-[104px] shrink-0 font-mono text-[11.5px] text-ink-secondary tnum">{children}</span>
)

export const RowMain = ({ children }: { children: ReactNode }) => (
  <span className="grid min-w-0 flex-1 [&>span]:truncate">{children}</span>
)

export const RowAmount = ({ className, children }: { className?: string; children: ReactNode }) => (
  <span className={cn('ml-auto text-right font-mono tnum', className)}>{children}</span>
)

export const Caption = ({ children }: { children: ReactNode }) => (
  <div className="flex items-center gap-1.5 pt-3.5 pb-1 font-mono text-[10px] tracking-[0.05em] uppercase text-muted">
    {children}
  </div>
)

export const Micro = ({ className, title, children }: {
  className?: string
  title?: string
  children: ReactNode
}) => (
  <span title={title} className={cn('text-[11px] leading-4 text-muted', className)}>{children}</span>
)

/** 24px icon action revealed in a row's right-hand gutter. */
export function RowAction({ label, danger, onClick, children }: {
  label: string
  danger?: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      className={cn(
        'grid h-6 w-6 shrink-0 place-items-center rounded-[3px] border border-transparent hover:border-line-subtle hover:bg-hover',
        danger ? 'text-danger' : 'text-ink-secondary',
      )}
    >
      {children}
    </button>
  )
}
