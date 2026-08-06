import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Padding 28/24, radius 6, on the recessed app ground with a subtle rule. A 34px rounded-square icon
 * holder carries an 18px glyph, then the title at 13.5/600 and the body at 12/18.
 *
 * Teach, do not apologise, and never a decorative illustration. Action rows wrap, because French
 * labels are long.
 */
export function EmptyState({ icon, title, children, actions, className }: {
  icon?: ReactNode
  title: string
  children: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'grid justify-items-start gap-2 rounded-md border border-line-subtle bg-app px-6 py-7',
        className,
      )}
    >
      {icon && (
        <span className="mb-1 grid h-[34px] w-[34px] place-items-center rounded-sm border border-line-subtle bg-panel text-ink-secondary">
          {icon}
        </span>
      )}

      <h3 className="m-0 text-[13.5px] leading-[19px] font-semibold">{title}</h3>
      <p className="m-0 text-[12px] leading-[18px] text-muted">{children}</p>
      {actions && <div className="mt-1 flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
