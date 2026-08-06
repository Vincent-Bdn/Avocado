import type { ReactNode } from 'react'
import { cn } from '../lib/utils.js'

/**
 * The wizard's shell pieces. Type here is larger than anywhere else in the application, 28/34 for
 * titles and 14/23 for body, because these screens are read once and carefully.
 */

/**
 * The scroll container stretches to fill its row; the block inside is centred with `margin: auto`.
 * That idiom is deliberate: `align-self: center` made the item content-sized, so its own overflow-y
 * never triggered and the excess was clipped with no scrollbar. Auto margins centre when the content
 * fits and collapse to zero when it does not, so the tall recovery step scrolls from its top instead
 * of losing its head and foot.
 */
export function WizardScroll({ width = 640, children }: {
  width?: 640 | 680 | 940
  children: ReactNode
}) {
  return (
    <div className="flex min-h-0 overflow-y-auto px-8 py-6">
      <div className="m-auto w-full" style={{ maxWidth: width }}>
        {children}
      </div>
    </div>
  )
}

export const WizardTitle = ({ children }: { children: ReactNode }) => (
  <h1 className="m-0 text-display font-semibold tracking-[-0.02em]">{children}</h1>
)

export const WizardLead = ({ children }: { children: ReactNode }) => (
  <p className="mt-2.5 mb-0 max-w-[760px] text-[14px] leading-[23px] text-ink-secondary [&_strong]:font-medium [&_strong]:text-ink">
    {children}
  </p>
)

export const WizardFootnote = ({ children }: { children: ReactNode }) => (
  <p className="mt-5 mb-0 text-[12px] leading-[18px] text-muted">{children}</p>
)

/** The bar that holds the step's actions. Always present, so the footing never moves between steps. */
export const WizardGate = ({ children }: { children: ReactNode }) => (
  <footer className="flex items-center gap-3 border-t border-line-subtle bg-panel px-7 py-3">
    {children}
  </footer>
)

/** A bordered point card: icon in the brand, a title, and one paragraph. */
export function Point({ icon, title, children, mono }: {
  icon: ReactNode
  title: string
  children: ReactNode
  mono?: boolean
}) {
  return (
    <article className="flex items-start gap-[11px] rounded-lg border border-line-subtle bg-panel px-3.5 py-3">
      <span className="mt-0.5 shrink-0 text-brand">{icon}</span>

      <div className="min-w-0">
        <span className="block text-[13px] leading-[19px] font-medium">{title}</span>
        <span
          className={cn(
            'mt-px block text-[12.5px] leading-[19px] text-ink-secondary [overflow-wrap:anywhere]',
            mono && 'font-mono',
          )}
        >
          {children}
        </span>
      </div>
    </article>
  )
}

export const Points = ({ children }: { children: ReactNode }) => (
  <div className="mt-6 grid gap-2.5">{children}</div>
)
