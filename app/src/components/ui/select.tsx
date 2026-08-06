import type { SelectHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/** Native select: the design's menu spec is a popover, but nothing here needs group headers yet. */
export function Select({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={cn(
        'h-7 rounded-sm border border-line-strong bg-sunken px-1.5 font-sans text-[12.5px] text-ink',
        'focus-visible:border-[var(--focus-ring)]',
        className,
      )}
      {...props}
    />
  )
}
