import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from './api.js'
import { MatterView } from './MatterView.js'
import { NewMatter } from './NewMatter.js'
import { formatRelative } from './labels.js'
import type { MatterListPage } from './types.js'

/**
 * The four-band shell: rail, dossier list, content. The context panel lives inside the fiche dossier
 * for now rather than as a top-level band, because it is the only screen that has one.
 */
export function AppShell() {
  const [page, setPage] = useState<MatterListPage | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [showClosed, setShowClosed] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    api<MatterListPage>(`/api/matters?status=${showClosed ? 'Closed' : 'Open'}&sort=LastActivity&descending=true`)
      .then((result) => {
        setPage(result)
        // Land on something rather than an empty content pane.
        setSelected((current) =>
          current && result.items.some((item) => item.id === current)
            ? current
            : (result.items[0]?.id ?? null),
        )
      })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [showClosed])

  useEffect(reload, [reload])

  // ⌘N from anywhere.
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
    <div className="app">
      <nav className="rail">
        <img src="./icon.png" alt="Avocado" className="rail-mark" />
        <span className="rail-item rail-active" title="Dossiers">D</span>
        <span className="rail-item rail-todo" title="Tiers, à venir">T</span>
        <span className="rail-item rail-todo" title="Échéances, à venir">É</span>
      </nav>

      <aside className="secondary-panel">
        <header className="panel-header">
          <span>Dossiers · {page?.total ?? 0}</span>
          <button type="button" className="icon-button" onClick={() => setCreating(true)} title="Nouveau dossier (⌘N)">
            ＋
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
              onClick={() => setSelected(matter.id)}
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
            setSelected(id)
            reload()
          }}
        />
      )}
    </div>
  )
}
