import { cva, type VariantProps } from 'class-variance-authority'
import type { ButtonHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * h 20, radius 3, 11px. The composer's type chips and the dashed attachment affordances.
 *
 * As with the badge, the height is derived from padding and an explicit line box rather than pinned,
 * and the bordered variants give a pixel back on each side so that a dashed chip and a filled one are
 * the same 20px and their labels sit on the same line.
 */
const chip = cva(
  'inline-flex items-center justify-center gap-1 rounded-[3px] px-2 text-[11px] leading-3 ' +
    'transition-colors whitespace-nowrap',
  {
    variants: {
      tone: {
        idle: 'py-1 bg-sunken text-ink-secondary hover:bg-hover',
        active: 'py-1 bg-brand text-on-brand',
        dashed: 'py-[3px] border border-dashed border-line-strong text-ink-secondary hover:bg-hover',
        // Ochre, because time is money and this chip is the point of the composer.
        time: 'py-[3px] border border-accent bg-accent-subtle text-warning',
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
