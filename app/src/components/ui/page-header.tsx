import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/** The 52px band at the top of a screen: title line, then one line of context in 11px. */
export function PageHeader({ title, meta, actions, className }: {
  title: ReactNode
  meta?: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <header className={cn('relative shrink-0 border-b border-line-subtle px-4 py-2', className)}>
      <div className="flex items-baseline gap-2.5">
        {typeof title === 'string' ? (
          <h2 className="m-0 truncate text-[20px] leading-[26px] font-semibold tracking-[-0.015em]">
            {title}
          </h2>
        ) : (
          title
        )}
      </div>

      {meta && (
        <div className="mt-0.5 flex items-center gap-2 text-[11px] text-ink-secondary">{meta}</div>
      )}

      {actions && <div className="absolute top-3 right-4 flex gap-2">{actions}</div>}
    </header>
  )
}

/** The 1px vertical rule that separates the segments of a meta line. */
export const MetaDivider = () => <span className="h-2.5 w-px shrink-0 bg-line" />

/** Uppercase mono caption above a group of rows. */
export function SectionTitle({ className, children }: { className?: string; children: ReactNode }) {
  return (
    <h3 className={cn('m-0 flex items-center gap-1.5 text-[12px] font-semibold', className)}>
      {children}
    </h3>
  )
}
