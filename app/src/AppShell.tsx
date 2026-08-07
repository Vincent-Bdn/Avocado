import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CalendarClock, FolderClosed, Home as HomeIcon, Plus, Settings as Gear, Star, Users,
} from 'lucide-react'
import { ApiError, api } from './api.js'
import { CommandPalette } from './CommandPalette.js'
import { MatterView } from './MatterView.js'
import { MatterForm } from './MatterForm.js'
import { Settings } from './Settings.js'
import { Contacts, NewContact } from './sections/Contacts.js'
import { Home } from './sections/Home.js'
import { UpcomingDeadlines } from './sections/UpcomingDeadlines.js'
import { Button } from './components/ui/button.js'
import { EmptyState } from './components/ui/empty-state.js'
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
 *
 * Creation lives here rather than inside a section, because « Créer un dossier » is offered from the
 * accueil, from the dossier list and from ⌘N, and all three have to open the same dialog.
 */
export function AppShell() {
  const [section, setSection] = useState<Section>('home')
  const [selectedMatter, setSelectedMatter] = useState<string | null>(null)
  const [selectedContact, setSelectedContact] = useState<string | null>(null)
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [creatingMatter, setCreatingMatter] = useState(false)
  const [creatingContact, setCreatingContact] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  const refresh = useCallback(() => setReloadToken((token) => token + 1), [])

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
      if (!(event.metaKey || event.ctrlKey)) return

      if (event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setPaletteOpen(true)
      }

      if (event.key.toLowerCase() === 'n') {
        event.preventDefault()
        setCreatingMatter(true)
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

      {section === 'home' && (
        <Home
          key={reloadToken}
          onOpenMatter={openMatter}
          onNewMatter={() => setCreatingMatter(true)}
          onNewContact={() => setCreatingContact(true)}
          onSearch={() => setPaletteOpen(true)}
        />
      )}

      {section === 'deadlines' && <UpcomingDeadlines onOpenMatter={openMatter} />}
      {section === 'settings' && <Settings />}

      {section === 'contacts' && (
        <Contacts
          key={reloadToken}
          selected={selectedContact}
          onSelect={setSelectedContact}
          onOpenMatter={openMatter}
          onNewContact={() => setCreatingContact(true)}
        />
      )}

      {section === 'matters' && (
        <Matters
          key={reloadToken}
          selected={selectedMatter}
          onSelect={setSelectedMatter}
          onNewMatter={() => setCreatingMatter(true)}
        />
      )}

      {paletteOpen && (
        <CommandPalette
          onClose={() => setPaletteOpen(false)}
          onOpenMatter={openMatter}
          onOpenContact={openContact}
        />
      )}

      {creatingMatter && (
        <MatterForm
          onCancel={() => setCreatingMatter(false)}
          onSaved={(id) => {
            setCreatingMatter(false)
            openMatter(id)
            refresh()
          }}
        />
      )}

      {creatingContact && (
        <NewContact
          onCancel={() => setCreatingContact(false)}
          onCreated={(id) => {
            setCreatingContact(false)
            openContact(id)
            refresh()
          }}
        />
      )}
    </div>
  )
}

function Matters({ selected, onSelect, onNewMatter }: {
  selected: string | null
  onSelect: (id: string | null) => void
  onNewMatter: () => void
}) {
  const [page, setPage] = useState<MatterListPage | null>(null)
  const [showClosed, setShowClosed] = useState(false)
  const [search, setSearch] = useState('')
  const [error, setError] = useState<string | null>(null)

  /**
   * Read through a ref, never through the closure.
   *
   * `selected` cannot be a dependency of `reload`, the list would refetch every time she clicked a
   * different dossier. But leaving it out of the deps and reading it directly captured the value from
   * the render that created the callback, which is `null` on mount. Every later reload therefore took
   * the « nothing selected » branch and jumped to the first row: since favourites sort to the top,
   * recording an invoice, a time entry or an échéance quietly moved her to a favourite dossier. A ref
   * keeps the callback stable and the value current, which is what was wanted in the first place.
   */
  const selectedRef = useRef(selected)
  selectedRef.current = selected

  const reload = useCallback(() => {
    const status = showClosed ? 'Closed' : 'Open'

    api<MatterListPage>(
      `/api/matters?status=${status}&search=${encodeURIComponent(search)}&sort=LastActivity&descending=true`,
    )
      .then((result) => {
        setPage(result)

        const current = selectedRef.current

        // Land on something rather than an empty content pane, but only when what she was reading
        // has genuinely gone, never merely because the list was refreshed underneath her.
        onSelect(
          current && result.items.some((item) => item.id === current)
            ? current
            : (result.items[0]?.id ?? null),
        )
      })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [showClosed, search, onSelect])

  useEffect(reload, [reload])

  return (
    <>
      <Panel>
        <PanelHeader>
          <span>Dossiers · {page?.total ?? 0}</span>
          <Button variant="ghost" size="iconSm" title="Nouveau dossier (Ctrl+N)" onClick={onNewMatter}>
            <Plus size={14} strokeWidth={2} />
          </Button>
        </PanelHeader>

        <div className="flex shrink-0 gap-0.5 border-b border-line-subtle px-1.5 py-1">
          <SegmentGroup>
            <Segment active={!showClosed} onClick={() => setShowClosed(false)}>En cours</Segment>
            <Segment active={showClosed} onClick={() => setShowClosed(true)}>Clôturés</Segment>
          </SegmentGroup>
        </div>

        <div className="shrink-0 border-b border-line-subtle px-1.5 py-1">
          <Input
            inputSize="sm"
            className="w-full"
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

          {page?.items.map((matter, index) => (
            <div key={matter.id}>
              {/* Favourites are pinned above a rule. One divider, drawn where the pinning stops. */}
              {index > 0 && page.items[index - 1]?.isFavourite && !matter.isFavourite && (
                <div className="my-1 border-t border-line" />
              )}

              {/*
                Flex, not grid. A two-column grid auto-places three children as (1,1) (1,2) (2,1),
                so the relative time landed on its own second row under the name and the two text
                lines shared the first, which is what put « il y a 1 h » in a box of its own.
                Nesting the two text lines makes the placement unambiguous.
              */}
              <button
                type="button"
                onClick={() => onSelect(matter.id)}
                className={cn(
                  'flex h-11 w-full items-center gap-2 rounded-sm px-2 py-1 text-left transition-colors',
                  matter.id === selected
                    ? 'bg-brand-subtle shadow-[inset_2px_0_0_var(--brand)]'
                    : 'hover:bg-hover',
                )}
              >
                <span className="grid min-w-0 flex-1 gap-0.5">
                  {/* Dense rows never wrap; they truncate. */}
                  <span className="flex min-w-0 items-center gap-1.5">
                    {matter.isFavourite && (
                      <Star size={11} strokeWidth={2} fill="currentColor" className="shrink-0 text-accent" />
                    )}
                    <span className="truncate text-[12px] leading-4">{matter.name}</span>
                  </span>

                  <span className="truncate font-mono text-[10px] leading-[13px] text-muted">
                    {matter.reference}
                    {matter.clientName && ` · ${matter.clientName}`}
                  </span>
                </span>

                <span className="shrink-0 font-mono text-[10px] whitespace-nowrap text-muted tnum">
                  {formatRelative(matter.lastActivityAt)}
                </span>
              </button>
            </div>
          ))}
        </div>
      </Panel>

      {selected ? (
        <MatterView matterId={selected} onChanged={reload} />
      ) : (
        <Panel className="items-center justify-center">
          <EmptyState
            icon={<FolderClosed size={18} strokeWidth={1.8} />}
            title="Votre premier dossier"
            className="max-w-[460px]"
            actions={<Button onClick={onNewMatter}>Nouveau dossier</Button>}
          >
            Un dossier réunit son client, le journal de tout ce qui s’y passe, ses documents, ses
            échéances et le temps que vous y consacrez.
          </EmptyState>
        </Panel>
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
    <nav className="flex flex-col items-center gap-1 rounded-lg bg-sunken py-2">
      <img src="./icon.png" alt="Avocado" className="mb-2 h-[26px] w-[26px] rounded-md" />

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
        'grid h-8 w-8 place-items-center rounded-md transition-colors',
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

/**
 * Segmented control: outer 26 with 22px segments, 2px padding, sunken well, radius 5 / 3. The active
 * thumb does not slide, it changes instantly: a 170ms transition on a list filter reads as lag.
 */
export function SegmentGroup({ children }: { children: ReactNode }) {
  return (
    <div className="inline-flex h-[26px] items-center gap-0.5 rounded-[5px] border border-line-subtle bg-sunken p-0.5">
      {children}
    </div>
  )
}

export function Segment({ active, onClick, children }: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-[22px] rounded-[3px] px-2.5 text-[11.5px]',
        active ? 'bg-panel font-medium text-ink shadow-e1' : 'text-ink-secondary hover:text-ink',
      )}
    >
      {children}
    </button>
  )
}
