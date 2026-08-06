import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * A band of the shell: --surface-panel, 1px --border-default, radius 8, and no shadow. Panels sit on
 * --surface-app separated by a 6px gutter; elevation is for things that float, not for the layout.
 */
export function Panel({ className, children }: { className?: string; children?: ReactNode }) {
  return (
    <div className={cn('flex min-w-0 flex-col overflow-hidden rounded-xl border border-line bg-panel', className)}>
      {children}
    </div>
  )
}

/** 32px, bottom rule, weight 500. */
export function PanelHeader({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <header
      className={cn(
        'flex h-8 shrink-0 items-center justify-between gap-2 border-b border-line-subtle px-2.5 font-medium',
        className,
      )}
    >
      {children}
    </header>
  )
}
