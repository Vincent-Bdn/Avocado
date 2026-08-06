import type { Ref, TextareaHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

export function Textarea({ className, ref, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement> & {
  ref?: Ref<HTMLTextAreaElement>
}) {
  return (
    <textarea
      ref={ref}
      className={cn(
        'min-h-[60px] resize-y rounded-sm border border-line-strong bg-sunken px-2.5 py-2',
        'font-sans text-[12.5px] leading-[19px] text-ink placeholder:text-muted',
        'focus-visible:border-[var(--focus-ring)]',
        className,
      )}
      {...props}
    />
  )
}
