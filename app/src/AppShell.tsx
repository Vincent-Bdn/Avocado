import { useCallback, useEffect, useState } from 'react'
import { CalendarClock, FolderClosed, Home as HomeIcon, Plus, Settings as Gear, Users } from 'lucide-react'
import { ApiError, api } from './api.js'
import { CommandPalette } from './CommandPalette.js'
import { MatterView } from './MatterView.js'
import { NewMatter } from './NewMatter.js'
import { Settings } from './Settings.js'
import { Contacts } from './sections/Contacts.js'
import { Home } from './sections/Home.js'
import { UpcomingDeadlines } from './sections/UpcomingDeadlines.js'
import { formatRelative } from './labels.js'
import type { MatterListPage } from './types.js'

type Section = 'home' | 'matters' | 'contacts' | 'deadlines' | 'settings'

/**
 * The shell. The rail chooses a section; only Dossiers and Tiers carry a secondary panel, so the
 * grid drops to two bands for the rest rather than leaving an empty column.
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

  // ⌘K from anywhere.
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
    <div className={`app ${twoBand ? 'app-wide' : ''}`}>
      <Rail section={section} onSection={setSection} />

      {section === 'home' && <Home onOpenMatter={openMatter} />}
      {section === 'deadlines' && <UpcomingDeadlines onOpenMatter={openMatter} />}
      {section === 'settings' && <Settings />}

      {section === 'contacts' && (
        <Contacts selected={selectedContact} onSelect={setSelectedContact} onOpenMatter={openMatter} />
      )}

      {section === 'matters' && (
        <Matters selected={selectedMatter} onSelect={setSelectedMatter} />
      )}

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
      <aside className="secondary-panel">
        <header className="panel-header">
          <span>Dossiers · {page?.total ?? 0}</span>
          <button
            type="button"
            className="icon-button"
            onClick={() => setCreating(true)}
            title="Nouveau dossier (Ctrl+N)"
          >
            <Plus size={14} strokeWidth={2} />
          </button>
        </header>

        <div className="filters">
          <button
            type="button"
            className={`segment ${!showClosed ? 'segment-active' : ''}`}
            onClick={() => setShowClosed(false)}
          >
            En cours
          </button>
          <button
            type="button"
            className={`segment ${showClosed ? 'segment-active' : ''}`}
            onClick={() => setShowClosed(true)}
          >
            Clôturés
          </button>
        </div>

        <div className="filters">
          <input
            className="panel-search"
            value={search}
            placeholder="Nom, référence, client…"
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>

        <div className="matter-list">
          {error && <p className="danger">{error}</p>}

          {page?.items.length === 0 && (
            <p className="muted empty-list">
              {showClosed ? 'Aucun dossier clôturé.' : 'Aucun dossier en cours.'}
            </p>
          )}

          {page?.items.map((matter) => (
            <button
              key={matter.id}
              type="button"
              className={`matter-row ${matter.id === selected ? 'matter-row-selected' : ''}`}
              onClick={() => onSelect(matter.id)}
            >
              <span className="matter-name">{matter.name}</span>
              <span className="mono matter-meta">
                {matter.reference} · {matter.clientName ?? '—'}
              </span>
              <span className="mono matter-when">{formatRelative(matter.lastActivityAt)}</span>
            </button>
          ))}
        </div>
      </aside>

      {selected ? (
        <MatterView matterId={selected} onChanged={reload} />
      ) : (
        <div className="content">
          <div className="empty centred">
            <h3>Votre premier dossier</h3>
            <p className="muted">
              Un dossier réunit son client, le journal de tout ce qui s’y passe, ses documents, ses
              échéances et le temps que vous y consacrez.
            </p>
            <button type="button" onClick={() => setCreating(true)}>
              Nouveau dossier
            </button>
          </div>
        </div>
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
    <nav className="rail">
      <img src="./icon.png" alt="Avocado" className="rail-mark" />

      {items.map(([id, title, Icon]) => (
        <button
          key={id}
          type="button"
          className={`rail-item ${section === id ? 'rail-active' : ''}`}
          title={title}
          onClick={() => onSection(id)}
        >
          <Icon size={18} strokeWidth={1.75} />
        </button>
      ))}

      <span className="grow" />

      <button
        type="button"
        className={`rail-item ${section === 'settings' ? 'rail-active' : ''}`}
        title="Réglages"
        onClick={() => onSection('settings')}
      >
        <Gear size={18} strokeWidth={1.75} />
      </button>
    </nav>
  )
}
