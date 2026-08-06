import { cva, type VariantProps } from 'class-variance-authority'
import type { ButtonHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Control heights are the design system's: sm 24, md 28, lg 32, padding-x 10 / 12 / 14, radius 4,
 * icon-to-label gap 6, icon-only square. `md` everywhere by default; `lg` only in dialogs, the wizard
 * and the palette; `sm` in panel toolbars and filter bars.
 *
 * No focus ring is declared here. tokens.css carries the one focus treatment for the whole
 * application, unlayered, so anything written here would lose to it anyway.
 */
const button = cva(
  'inline-flex items-center justify-center gap-1.5 rounded-sm font-medium whitespace-nowrap ' +
    'transition-colors disabled:cursor-not-allowed',
  {
    variants: {
      variant: {
        primary:
          'bg-brand text-on-brand hover:bg-brand-hover active:bg-brand-active ' +
          'disabled:bg-sunken disabled:text-disabled disabled:hover:bg-sunken',
        secondary:
          'bg-panel text-ink border border-line-strong hover:bg-hover active:bg-sunken ' +
          'disabled:text-disabled disabled:border-line-subtle disabled:hover:bg-panel',
        ghost:
          'bg-transparent text-ink-secondary hover:bg-hover active:bg-sunken ' +
          'disabled:text-disabled disabled:hover:bg-transparent',
        danger:
          'bg-danger text-white hover:opacity-90 disabled:bg-sunken disabled:text-disabled',
      },
      size: {
        sm: 'h-6 px-2.5 text-[12px]',
        md: 'h-7 px-3 text-[13px]',
        lg: 'h-8 px-3.5 text-[13px]',
        icon: 'h-7 w-7 px-0',
        iconSm: 'h-6 w-6 px-0',
        iconLg: 'h-8 w-8 px-0',
      },
    },
    defaultVariants: { variant: 'primary', size: 'md' },
  },
)

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & VariantProps<typeof button>

/** The shortcut hint that sits inside a button when there is room. */
export function Kbd({ on = 'primary', children }: { on?: 'primary' | 'secondary'; children: string }) {
  return (
    <span
      className={cn(
        'type-kbd ml-1 rounded-[3px] px-1 py-px',
        on === 'primary' ? 'bg-white/16' : 'bg-sunken text-ink-secondary',
      )}
    >
      {children}
    </span>
  )
}

export function Button({ className, variant, size, type = 'button', ...props }: ButtonProps) {
  return <button type={type} className={cn(button({ variant, size }), className)} {...props} />
}
