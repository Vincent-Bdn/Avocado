import { useEffect, useMemo, useState } from 'react'
import { Building2, FileText, FolderClosed, Search, User } from 'lucide-react'
import { api } from './api.js'
import { urgencyLabels } from './labels.js'
import type { ContactType, DeadlineUrgency } from './types.js'

interface SearchResultItem {
  id: string
  label: string
  meta: string | null
  contactType: ContactType | null
}

interface SearchResults {
  groups: { key: string; items: SearchResultItem[]; total: number }[]
  total: number
}

interface StartingPoints {
  recentMatters: { id: string; reference: string; label: string; lastActivityAt: string | null }[]
  nearestDeadlines: {
    id: string
    matterId: string
    label: string
    matterName: string
    date: string
    urgency: DeadlineUrgency
  }[]
}

const groupTitles: Record<string, string> = {
  matters: 'Dossiers',
  contacts: 'Tiers',
  documents: 'Documents & pièces',
}

/**
 * ⌘K. Groups in a fixed order, never reordered while typing, and results rebuilt at once rather than
 * animated: this surface is judged on feeling instant.
 */
export function CommandPalette({ onClose, onOpenMatter, onOpenContact }: {
  onClose: () => void
  onOpenMatter: (id: string) => void
  onOpenContact: (id: string) => void
}) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResults | null>(null)
  const [start, setStart] = useState<StartingPoints | null>(null)
  const [active, setActive] = useState(0)

  // `@` restricts to tiers, `#` to documents, as the design's prefixes do.
  const { scope, term } = useMemo(() => {
    if (query.startsWith('@')) return { scope: 'Contacts', term: query.slice(1) }
    if (query.startsWith('#')) return { scope: 'Documents', term: query.slice(1) }

    return { scope: 'All', term: query }
  }, [query])

  useEffect(() => {
    api<StartingPoints>('/api/search/starting-points').then(setStart).catch(() => setStart(null))
  }, [])

  useEffect(() => {
    if (term.trim().length === 0) {
      setResults(null)
      return
    }

    let cancelled = false

    api<SearchResults>(`/api/search?q=${encodeURIComponent(term)}&scope=${scope}`)
      .then((found) => {
        if (!cancelled) {
          setResults(found)
          setActive(0)
        }
      })
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [term, scope])

  const flat = useMemo(
    () =>
      results?.groups.flatMap((group) => group.items.map((item) => ({ group: group.key, item }))) ?? [],
    [results],
  )

  useEffect(() => {
    const keys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setActive((current) => Math.min(current + 1, Math.max(0, flat.length - 1)))
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setActive((current) => Math.max(0, current - 1))
      }
      if (event.key === 'Enter' && flat[active]) {
        event.preventDefault()
        open(flat[active].group, flat[active].item.id)
      }
    }

    window.addEventListener('keydown', keys)
    return () => window.removeEventListener('keydown', keys)
  })

  function open(group: string, id: string) {
    if (group === 'matters') onOpenMatter(id)
    if (group === 'contacts') onOpenContact(id)
    onClose()
  }

  let index = -1

  return (
    <div className="palette-scrim" onClick={onClose}>
      <div className="palette" onClick={(event) => event.stopPropagation()}>
        <div className="palette-field">
          <Search size={15} strokeWidth={1.75} />
          <input
            autoFocus
            value={query}
            placeholder="Chercher un dossier, un tiers, un document"
            onChange={(event) => setQuery(event.target.value)}
          />
          {results && <span className="muted micro nowrap">{results.total} résultats</span>}
        </div>

        <div className="palette-body">
          {!results && start && (
            <>
              <div className="palette-group mono">Dossiers récents</div>
              {start.recentMatters.map((matter) => (
                <button
                  key={matter.id}
                  type="button"
                  className="palette-row"
                  onClick={() => open('matters', matter.id)}
                >
                  <FolderClosed size={14} strokeWidth={1.75} />
                  <span className="palette-label">{matter.label}</span>
                  <span className="mono micro muted">{matter.reference}</span>
                </button>
              ))}

              <div className="palette-group mono">Échéances les plus proches</div>
              {start.nearestDeadlines.map((deadline) => (
                <button
                  key={deadline.id}
                  type="button"
                  className="palette-row"
                  onClick={() => open('matters', deadline.matterId)}
                >
                  <span className={`tier tier-${deadline.urgency.toLowerCase()}`} />
                  <span className="palette-label">
                    {deadline.label} · {deadline.matterName}
                  </span>
                  <span className="mono micro muted">{urgencyLabels[deadline.urgency]}</span>
                </button>
              ))}

              {start.recentMatters.length === 0 && start.nearestDeadlines.length === 0 && (
                <p className="muted micro palette-empty">Rien à reprendre pour l’instant.</p>
              )}
            </>
          )}

          {results?.groups.map((group) => (
            <div key={group.key}>
              <div className="palette-group mono">
                {groupTitles[group.key] ?? group.key}
                {group.total > group.items.length && ` · ${group.total}`}
              </div>

              {group.items.map((item) => {
                index += 1
                const isActive = index === active

                return (
                  <button
                    key={item.id}
                    type="button"
                    className={`palette-row ${isActive ? 'palette-active' : ''}`}
                    onClick={() => open(group.key, item.id)}
                  >
                    <ResultIcon group={group.key} contactType={item.contactType} />
                    <span className="palette-label">{item.label}</span>
                    {item.meta && <span className="mono micro muted">{item.meta}</span>}
                    {isActive && <span className="kbd mono">⏎</span>}
                  </button>
                )
              })}

              {group.total > group.items.length && (
                <div className="palette-more muted micro">
                  voir les {group.total - group.items.length} autres
                </div>
              )}
            </div>
          ))}

          {results && results.total === 0 && (
            <div className="palette-empty">
              <strong>Aucun résultat pour « {term} »</strong>
              <p className="muted micro">
                La recherche couvre le nom, la référence, le client, le n° RG et la description des
                dossiers, les tiers, et le nom des documents.
              </p>
            </div>
          )}
        </div>

        <div className="palette-foot mono">
          <span>↑↓ naviguer</span>
          <span>⏎ ouvrir</span>
          <span className="grow" />
          <span>@ tiers · # documents</span>
        </div>
      </div>
    </div>
  )
}

function ResultIcon({ group, contactType }: { group: string; contactType: ContactType | null }) {
  if (group === 'documents') return <FileText size={14} strokeWidth={1.75} />
  if (group === 'matters') return <FolderClosed size={14} strokeWidth={1.75} />

  // Round for a personne physique, square for a personne morale, as everywhere else.
  return contactType === 'Individual' ? (
    <User size={14} strokeWidth={1.75} />
  ) : (
    <Building2 size={14} strokeWidth={1.75} />
  )
}
