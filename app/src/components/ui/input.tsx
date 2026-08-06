import { cva, type VariantProps } from 'class-variance-authority'
import type { InputHTMLAttributes, Ref } from 'react'
import { cn } from '../../lib/utils.js'

/**
 * Radius 4, 1px --border-strong: the only border allowed on a control. Heights follow the buttons,
 * sm 24 / md 28 / lg 32, so a field and its action line up without hand-tuning.
 *
 * A placeholder in an enabled field is information, so it uses --text-muted (4.8:1). --text-disabled
 * is only for genuinely inoperative controls, where the flat fill doubles the signal. The focus
 * treatment is tokens.css's, unlayered and application-wide; only the border colour is added here.
 */
const input = cva(
  'min-w-0 rounded-sm border bg-sunken text-ink placeholder:text-muted ' +
    'focus-visible:border-[var(--focus-ring)] ' +
    'disabled:bg-app disabled:border-line-subtle disabled:text-disabled ' +
    'disabled:placeholder:text-disabled',
  {
    variants: {
      inputSize: {
        sm: 'h-6 px-1.5 text-[11.5px]',
        md: 'h-7 px-2 text-[13px]',
        lg: 'h-8 px-2.5 text-[13px]',
      },
      invalid: {
        true: 'border-danger shadow-[0_0_0_2px_color-mix(in_srgb,var(--status-danger)_16%,transparent)]',
        false: 'border-line-strong',
      },
    },
    defaultVariants: { inputSize: 'md', invalid: false },
  },
)

export type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> &
  VariantProps<typeof input> & { ref?: Ref<HTMLInputElement> }

export function Input({ className, inputSize, invalid, ref, ...props }: InputProps) {
  return <input ref={ref} className={cn(input({ inputSize, invalid }), className)} {...props} />
}
