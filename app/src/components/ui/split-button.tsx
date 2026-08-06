import { useEffect, useRef, useState, type ReactNode } from 'react'
import { ChevronDown } from 'lucide-react'
import { Button } from './button.js'
import { cn } from '../../lib/utils.js'

/**
 * One action with its variants behind a chevron, rather than two buttons of unequal importance side
 * by side. Radius 4 on the outside, a hairline between the halves, and the menu at radius 6 with
 * 26px items, as the design system's dropdown specifies.
 */
export function SplitButton({ label, icon, children }: {
  label: string
  icon?: ReactNode
  children: (close: () => void) => ReactNode
}) {
  const [open, setOpen] = useState(false)
  const container = useRef<HTMLDivElement>(null)

  // A menu that only closes on its own items is a menu that follows you around the screen.
  useEffect(() => {
    if (!open) return

    const dismiss = (event: MouseEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false)
    }

    const escape = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(false)

    window.addEventListener('mousedown', dismiss)
    window.addEventListener('keydown', escape)

    return () => {
      window.removeEventListener('mousedown', dismiss)
      window.removeEventListener('keydown', escape)
    }
  }, [open])

  return (
    <div ref={container} className="relative flex">
      <Button
        className="rounded-r-none"
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((current) => !current)}
      >
        {icon}
        {label}
        <ChevronDown size={13} strokeWidth={2} className={cn('transition-transform', open && 'rotate-180')} />
      </Button>

      {open && (
        <div
          role="menu"
          className="absolute top-[calc(100%+4px)] left-0 z-20 grid min-w-[260px] gap-0.5 rounded-md border border-line bg-raised p-[3px] shadow-e2"
        >
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  )
}

export function SplitButtonItem({ title, detail, icon, onClick }: {
  title: string
  detail?: string
  icon?: ReactNode
  onClick: () => void
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className="grid w-full grid-cols-[16px_minmax(0,1fr)] items-start gap-2 rounded-[3px] px-2 py-1.5 text-left hover:bg-hover"
    >
      <span className="mt-0.5 text-ink-secondary">{icon}</span>
      <span className="grid gap-0.5">
        <span className="text-[12.5px] font-medium">{title}</span>
        {detail && <span className="text-[11px] leading-4 text-muted">{detail}</span>}
      </span>
    </button>
  )
}
