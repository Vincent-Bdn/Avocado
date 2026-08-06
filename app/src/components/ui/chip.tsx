import { cva, type VariantProps } from 'class-variance-authority'
import type { ButtonHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/** h 20, radius 3, 11px. The composer's type chips and the dashed attachment affordances. */
const chip = cva(
  'inline-flex h-5 items-center gap-1 rounded-sm px-2 text-[11px] transition-colors whitespace-nowrap',
  {
    variants: {
      tone: {
        idle: 'bg-sunken text-ink-secondary hover:bg-hover',
        active: 'bg-brand text-on-brand',
        dashed: 'border border-dashed border-line-strong text-ink-secondary hover:bg-hover',
        // Ochre, because time is money and this chip is the point of the composer.
        time: 'border border-accent bg-accent-subtle text-warning',
      },
    },
    defaultVariants: { tone: 'idle' },
  },
)

export function Chip({ tone, className, type = 'button', ...props }: ButtonHTMLAttributes<HTMLButtonElement> &
  VariantProps<typeof chip>) {
  return <button type={type} className={cn(chip({ tone }), className)} {...props} />
}

export function ChipSpan({ tone, className, children }: VariantProps<typeof chip> & {
  className?: string
  children: React.ReactNode
}) {
  return <span className={cn(chip({ tone }), className)}>{children}</span>
}
