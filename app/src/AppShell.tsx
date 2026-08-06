import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { CalendarClock, FolderClosed, Home as HomeIcon, Plus, Settings as Gear, Users } from 'lucide-react'
import { ApiError, api } from './api.js'
import { CommandPalette } from './CommandPalette.js'
import { MatterView } from './MatterView.js'
import { NewMatter } from './NewMatter.js'
import { Settings } from './Settings.js'
import { Contacts } from './sections/Contacts.js'
import { Home } from './sections/Home.js'
import { UpcomingDeadlines } from './sections/UpcomingDeadlines.js'
import { Button } from './components/ui/button.js'
import { Input } from './components/ui/input.js'
import { Panel, PanelHeader } from './components/ui/panel.js'
import { cn } from './lib/utils.js'
import { formatRelative } from './labels.js'
import type { MatterListPage } from './types.js'

type Section = 'home' | 'matters' | 'contacts' | 'deadlines' | 'settings'

/**
 * The shell: rail 48, secondary panel 232, content, 6px gutters, panels at radius 8 with no shadow
 * between them. Only Dossiers and Tiers carry a secondary panel, so the rest drop to two bands
 * rather than leaving an empty column.
 */
export function AppShell() {
  const [section, setSection] = useState<Section>('home')
  const [selectedMatter, setSelectedMatter] = useState<string | null>(null)
  const [selectedContact, setSelectedContact] = useState<string | null>(null)
  const [paletteOpen, setPaletteOpen] = useState(false)

  const openMatter = useCallback((id: string) => {
    setSelectedMatter(id)
    setSection('matters')
  }, [])

  const openContact = useCallback((id: string) => {
    setSelectedContact(id)
    setSection('contacts')
  }, [])

  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setPaletteOpen(true)
      }
    }

    window.addEventListener('keydown', shortcut)
    return () => window.removeEventListener('keydown', shortcut)
  }, [])

  const twoBand = section !== 'matters' && section !== 'contacts'

  return (
    <div
      className={cn(
        'grid h-full gap-1.5 bg-app p-1.5',
        twoBand ? 'grid-cols-[48px_minmax(480px,1fr)]' : 'grid-cols-[48px_232px_minmax(480px,1fr)]',
      )}
    >
      <Rail section={section} onSection={setSection} />

      {section === 'home' && <Home onOpenMatter={openMatter} />}
      {section === 'deadlines' && <UpcomingDeadlines onOpenMatter={openMatter} />}
      {section === 'settings' && <Settings />}

      {section === 'contacts' && (
        <Contacts selected={selectedContact} onSelect={setSelectedContact} onOpenMatter={openMatter} />
      )}

      {section === 'matters' && <Matters selected={selectedMatter} onSelect={setSelectedMatter} />}

      {paletteOpen && (
        <CommandPalette
          onClose={() => setPaletteOpen(false)}
          onOpenMatter={openMatter}
          onOpenContact={openContact}
        />
      )}
    </div>
  )
}

function Matters({ selected, onSelect }: {
  selected: string | null
  onSelect: (id: string | null) => void
}) {
  const [page, setPage] = useState<MatterListPage | null>(null)
  const [creating, setCreating] = useState(false)
  const [showClosed, setShowClosed] = useState(false)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    const status = showClosed ? 'Closed' : 'Open'

    api<MatterListPage>(
      `/api/matters?status=${status}&search=${encodeURIComponent(search)}&sort=LastActivity&descending=true`,
    )
      .then((result) => {
        setPage(result)
        // Land on something rather than an empty content pane.
        onSelect(
          selected && result.items.some((item) => item.id === selected)
            ? selected
            : (result.items[0]?.id ?? null),
        )
      })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
    // `selected` is deliberately excluded: including it would refetch on every selection change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showClosed, search])

  useEffect(reload, [reload])

  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'n') {
        event.preventDefault()
        setCreating(true)
      }
    }

    window.addEventListener('keydown', shortcut)
    return () => window.removeEventListener('keydown', shortcut)
  }, [])

  return (
    <>
      <Panel>
        <PanelHeader>
          <span>Dossiers · {page?.total ?? 0}</span>
          <Button
            variant="ghost"
            size="iconSm"
            title="Nouveau dossier (Ctrl+N)"
            onClick={() => setCreating(true)}
          >
            <Plus size={14} strokeWidth={2} />
          </Button>
        </PanelHeader>

        <div className="flex shrink-0 gap-0.5 border-b border-line-subtle px-1.5 py-1">
          <Segment active={!showClosed} onClick={() => setShowClosed(false)}>
            En cours
          </Segment>
          <Segment active={showClosed} onClick={() => setShowClosed(true)}>
            Clôturés
          </Segment>
        </div>

        <div className="shrink-0 border-b border-line-subtle px-1.5 py-1">
          <Input
            className="h-6 w-full text-[11.5px]"
            value={search}
            placeholder="Nom, référence, client…"
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>

        <div className="flex-1 overflow-y-auto p-1">
          {error && <p className="p-2 text-danger">{error}</p>}

          {page?.items.length === 0 && (
            <p className="px-2 py-3 text-muted">
              {showClosed ? 'Aucun dossier clôturé.' : 'Aucun dossier en cours.'}
            </p>
          )}

          {page?.items.map((matter) => (
            <button
              key={matter.id}
              type="button"
              onClick={() => onSelect(matter.id)}
              className={cn(
                'grid h-9 w-full grid-cols-[minmax(0,1fr)_auto] content-center gap-x-2',
                'rounded-md px-2 py-1 text-left transition-colors',
                matter.id === selected
                  ? 'bg-brand-subtle shadow-[inset_2px_0_0_var(--brand)]'
                  : 'hover:bg-hover',
              )}
            >
              {/* Dense rows never wrap; they truncate. */}
              <span className="truncate text-[12px] leading-4">{matter.name}</span>
              <span className="truncate font-mono text-[10px] leading-[13px] text-muted">
                {matter.reference}
                {matter.clientName && ` · ${matter.clientName}`}
              </span>
              <span className="row-span-2 self-center font-mono text-[10px] text-muted">
                {formatRelative(matter.lastActivityAt)}
              </span>
            </button>
          ))}
        </div>
      </Panel>

      {selected ? (
        <MatterView matterId={selected} onChanged={reload} />
      ) : (
        <Panel className="items-center justify-center">
          <div className="grid max-w-[460px] justify-items-start gap-2 rounded-lg border border-line-subtle bg-app px-6 py-7">
            <h3 className="m-0 text-[13.5px] font-semibold">Votre premier dossier</h3>
            <p className="m-0 text-[12px] leading-[18px] text-muted">
              Un dossier réunit son client, le journal de tout ce qui s’y passe, ses documents, ses
              échéances et le temps que vous y consacrez.
            </p>
            <Button onClick={() => setCreating(true)}>Nouveau dossier</Button>
          </div>
        </Panel>
      )}

      {creating && (
        <NewMatter
          onCancel={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false)
            setShowClosed(false)
            onSelect(id)
            reload()
          }}
        />
      )}
    </>
  )
}

/** The icon rail. Accueil is a real destination; Réglages is pinned to the bottom. */
function Rail({ section, onSection }: { section: Section; onSection: (next: Section) => void }) {
  const items: [Section, string, typeof HomeIcon][] = [
    ['home', 'Accueil', HomeIcon],
    ['matters', 'Dossiers', FolderClosed],
    ['contacts', 'Tiers', Users],
    ['deadlines', 'Échéances', CalendarClock],
  ]

  return (
    <nav className="flex flex-col items-center gap-1 rounded-xl bg-sunken py-2">
      <img src="./icon.png" alt="Avocado" className="mb-2 h-[26px] w-[26px] rounded-lg" />

      {items.map(([id, title, Icon]) => (
        <RailItem key={id} label={title} active={section === id} onClick={() => onSection(id)}>
          <Icon size={18} strokeWidth={1.75} />
        </RailItem>
      ))}

      <span className="flex-1" />

      <RailItem label="Réglages" active={section === 'settings'} onClick={() => onSection('settings')}>
        <Gear size={18} strokeWidth={1.75} />
      </RailItem>
    </nav>
  )
}

function RailItem({ label, active, onClick, children }: {
  label: string
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      aria-current={active ? 'page' : undefined}
      onClick={onClick}
      className={cn(
        'grid h-8 w-8 place-items-center rounded-lg transition-colors',
        // Collapsed, the 2px marker alone identifies the section.
        active
          ? 'bg-brand-subtle text-brand-on-subtle shadow-[inset_2px_0_0_var(--brand)]'
          : 'text-ink-secondary hover:bg-hover',
      )}
    >
      {children}
    </button>
  )
}

/** The filter strip's segmented control: h 20, radius 3, 11px. */
function Segment({ active, onClick, children }: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-5 rounded-sm px-2 text-[11px] transition-colors',
        active ? 'bg-brand-subtle text-brand-on-subtle' : 'text-ink-secondary hover:bg-hover',
      )}
    >
      {children}
    </button>
  )
}
