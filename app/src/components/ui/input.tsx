import type { InputHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/** h 28, padding-x 8, radius 4, 1px --border-strong: the only border allowed on a control. */
export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        'h-7 min-w-0 rounded-md border border-line-strong bg-sunken px-2 text-[13px] text-ink',
        'placeholder:text-muted focus-visible:outline-none focus-visible:border-[var(--focus-ring)]',
        'focus-visible:ring-2 focus-visible:ring-[var(--focus-ring)]/30',
        'disabled:bg-app disabled:border-line-subtle disabled:text-disabled',
        className,
      )}
      {...props}
    />
  )
}
