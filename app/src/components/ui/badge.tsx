import { cva, type VariantProps } from 'class-variance-authority'
import type { ReactNode } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * h 18, radius 3, 10.5/500. Colour is never the only signal, so callers pass a glyph or a bullet.
 *
 * The height is *derived* — 3px padding, a 12px line box, 3px padding — rather than pinned with a
 * fixed height. A pinned height plus an inherited 20px line-height leaves the line box taller than
 * the content box, which pushes the digits a pixel off centre, and the offset differs between the
 * variants that carry a border and the ones that do not. Deriving the box from the text cannot drift.
 */
const badge = cva(
  'inline-flex items-center justify-center gap-1.5 rounded-[3px] px-1.5 py-[3px] ' +
    'text-[10.5px] leading-3 font-medium',
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

/**
 * The numeric pill: a pièce number, a tab counter, a page number. Same construction as the badge, so
 * a bordered pill and a plain one are the same height and their digits sit on the same line.
 */
export function NumberPill({ bordered, className, children }: {
  bordered?: boolean
  className?: string
  children: ReactNode
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center justify-center rounded-[3px] px-1.5 font-mono text-[11px] leading-3 tnum',
        bordered ? 'border py-[3px]' : 'py-1',
        className,
      )}
    >
      {children}
    </span>
  )
}
