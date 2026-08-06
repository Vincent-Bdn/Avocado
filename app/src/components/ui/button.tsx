import { cva, type VariantProps } from 'class-variance-authority'
import type { ButtonHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Control heights are the design system's: sm 24, md 28, lg 32. `md` everywhere by default; `lg`
 * only in dialogs, the wizard and the palette field; `sm` in panel toolbars and filter bars.
 */
const button = cva(
  'inline-flex items-center justify-center gap-1.5 rounded-md font-medium whitespace-nowrap ' +
    'transition-colors disabled:cursor-not-allowed focus-visible:outline-none ' +
    'focus-visible:ring-2 focus-visible:ring-[var(--focus-ring)] focus-visible:ring-offset-1 ' +
    'focus-visible:ring-offset-[var(--surface-panel)]',
  {
    variants: {
      variant: {
        primary: 'bg-brand text-on-brand hover:bg-brand-hover disabled:bg-sunken disabled:text-disabled',
        secondary:
          'bg-panel text-ink border border-line-strong hover:bg-hover disabled:text-disabled disabled:border-line-subtle',
        ghost: 'bg-transparent text-ink-secondary hover:bg-hover disabled:text-disabled',
        danger: 'bg-danger text-white hover:opacity-90 disabled:bg-sunken disabled:text-disabled',
      },
      size: {
        sm: 'h-6 px-2.5 text-[12px]',
        md: 'h-7 px-3 text-[13px]',
        lg: 'h-8 px-3.5 text-[13px]',
        icon: 'h-7 w-7 px-0',
        iconSm: 'h-6 w-6 px-0',
      },
    },
    defaultVariants: { variant: 'primary', size: 'md' },
  },
)

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & VariantProps<typeof button>

export function Button({ className, variant, size, type = 'button', ...props }: ButtonProps) {
  return <button type={type} className={cn(button({ variant, size }), className)} {...props} />
}
