import { cva, type VariantProps } from 'class-variance-authority'
import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/** h 18, radius 3, 10.5px/500. Colour is never the only signal, so callers pass a glyph or bullet. */
const badge = cva(
  'inline-flex items-center gap-1.5 h-[18px] rounded-sm px-1.5 text-[10.5px] font-medium leading-none',
  {
    variants: {
      tone: {
        brand: 'bg-brand-subtle text-brand-on-subtle',
        neutral: 'bg-sunken text-muted',
        accent: 'bg-accent-subtle text-warning',
        danger: 'bg-[color-mix(in_srgb,var(--status-danger)_12%,transparent)] text-danger',
        info: 'bg-[color-mix(in_srgb,var(--status-info)_12%,transparent)] text-info',
      },
    },
    defaultVariants: { tone: 'neutral' },
  },
)

export function Badge({ tone, className, children }: VariantProps<typeof badge> & {
  className?: string
  children: ReactNode
}) {
  return <span className={cn(badge({ tone }), className)}>{children}</span>
}
