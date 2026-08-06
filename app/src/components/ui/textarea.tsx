import type { Ref, TextareaHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

export function Textarea({ className, ref, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement> & {
  ref?: Ref<HTMLTextAreaElement>
}) {
  return (
    <textarea
      ref={ref}
      className={cn(
        'min-h-[44px] resize-y rounded-md border border-line-strong bg-sunken px-2.5 py-2',
        'font-sans text-[12.5px] leading-[19px] text-ink placeholder:text-muted',
        'focus-visible:outline-none focus-visible:border-[var(--focus-ring)]',
        'focus-visible:ring-2 focus-visible:ring-[var(--focus-ring)]/30',
        className,
      )}
      {...props}
    />
  )
}
