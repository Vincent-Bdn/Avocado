import { useState } from 'react'
import { Panel, PanelHeader } from '../components/ui/panel.js'
import { cn } from '../lib/utils.js'
import { AgendaDoc } from './docs/Agenda.js'
import { FacturationDoc } from './docs/Facturation.js'

interface DocPage {
  id: string
  title: string
  summary: string
  view: () => React.ReactNode
}

/**
 * Documentation. One page for now, and a list beside it because there will be more.
 *
 * <p>Not a help centre and not a tour: pages that explain how a part of Avocado decides what it
 * shows, with the real screens in them. Lawyers asked the same question about facturation three
 * times between them, which is the sign that an answer written once would have done.</p>
 */
const pages: DocPage[] = [
  {
    id: 'facturation',
    title: 'Facturation',
    summary: 'Taux, forfait, boni et mali',
    view: () => <FacturationDoc />,
  },
  {
    id: 'agenda',
    title: 'Agenda Google',
    summary: 'Connecter, et ce qui part',
    view: () => <AgendaDoc />,
  },
]

export function Docs() {
  const [selected, setSelected] = useState(pages[0]!.id)
  const page = pages.find((candidate) => candidate.id === selected) ?? pages[0]!

  return (
    <>
      <Panel>
        <PanelHeader>
          <span>Documentation</span>
        </PanelHeader>

        <nav className="flex-1 overflow-y-auto p-1.5">
          {pages.map((candidate) => (
            <button
              key={candidate.id}
              type="button"
              onClick={() => setSelected(candidate.id)}
              aria-current={candidate.id === page.id ? 'page' : undefined}
              className={cn(
                'block w-full rounded-md px-2.5 py-2 text-left transition-colors',
                candidate.id === page.id ? 'bg-brand-subtle' : 'hover:bg-hover',
              )}
            >
              <span className="block text-[12.5px] leading-4 font-medium">{candidate.title}</span>
              <span className="block font-mono text-[11px] leading-4 text-muted">
                {candidate.summary}
              </span>
            </button>
          ))}
        </nav>
      </Panel>

      {page.view()}
    </>
  )
}
