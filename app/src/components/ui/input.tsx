import { cva, type VariantProps } from 'class-variance-authority'
import type { InputHTMLAttributes } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Radius 4, 1px --border-strong: the only border allowed on a control. Heights follow the buttons,
 * sm 24 / md 28 / lg 32, so a field and its action line up without hand-tuning.
 */
const input = cva(
  'min-w-0 rounded-md border border-line-strong bg-sunken text-ink placeholder:text-muted ' +
    'focus-visible:outline-none focus-visible:border-[var(--focus-ring)] ' +
    'focus-visible:ring-2 focus-visible:ring-[var(--focus-ring)]/30 ' +
    'disabled:bg-app disabled:border-line-subtle disabled:text-disabled',
  {
    variants: {
      inputSize: {
        sm: 'h-6 px-1.5 text-[11.5px]',
        md: 'h-7 px-2 text-[13px]',
        lg: 'h-8 px-2.5 text-[13px]',
      },
    },
    defaultVariants: { inputSize: 'md' },
  },
)

export type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> &
  VariantProps<typeof input>

export function Input({ className, inputSize, ...props }: InputProps) {
  return <input className={cn(input({ inputSize }), className)} {...props} />
}
