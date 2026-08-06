import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Teach, do not apologise, and never a decorative illustration. Action rows wrap, because French
 * labels are long.
 */
export function EmptyState({ title, children, actions, className }: {
  title: string
  children: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'grid justify-items-start gap-2 rounded-lg border border-line-subtle bg-app px-6 py-7',
        className,
      )}
    >
      <h3 className="m-0 text-[13.5px] font-semibold">{title}</h3>
      <p className="m-0 text-[12px] leading-[18px] text-muted">{children}</p>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
