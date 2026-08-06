import { cn } from '../../lib/utils.js'
import { initials } from '../../lib/urgency.js'
import type { ContactType } from '../../types.js'

/**
 * Round = personne physique, rounded square = personne morale. Initials only, never a photo. The
 * client takes the brand fill; everyone else the sunken one.
 *
 * The line-height is set explicitly rather than left to inherit. Centring a 20px line box inside an
 * 18px content box leaves the initials a pixel high, which is why they looked off in the header and
 * in the parties list: a `place-items-center` only centres the box it is given.
 */
export function Avatar({ name, type, client, size = 20, className }: {
  name: string
  type: ContactType
  client?: boolean
  size?: 16 | 20 | 24 | 28
  className?: string
}) {
  const text = { 16: 8, 20: 9, 24: 10, 28: 11 }[size]

  return (
    <span
      aria-hidden="true"
      style={{ width: size, height: size, fontSize: text, lineHeight: `${size}px` }}
      className={cn(
        'inline-flex shrink-0 items-center justify-center font-medium select-none',
        type === 'Individual' ? 'rounded-full' : 'rounded-sm',
        client ? 'bg-brand text-on-brand' : 'bg-sunken text-ink-secondary',
        className,
      )}
    >
      {initials(name)}
    </span>
  )
}
