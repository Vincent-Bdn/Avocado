import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Building2, FileText, FolderClosed, Search, User } from 'lucide-react'
import { api } from './api.js'
import { cn } from './lib/utils.js'
import { TierBullet } from './lib/urgency.js'
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

  /**
   * Every row the arrows can reach, in the order they are drawn — the starting points as well as the
   * results.
   *
   * It used to be built from `results` alone, which is null until something is typed, so the footer
   * promised « ↑↓ naviguer » on precisely the screen where the arrows did nothing: the one you land
   * on when you press ⌘K.
   */
  const rows = useMemo<{ group: string; id: string }[]>(() => {
    if (results) {
      return results.groups.flatMap((group) =>
        group.items.map((item) => ({ group: group.key, id: item.id })))
    }

    if (!start) return []

    return [
      ...start.recentMatters.map((matter) => ({ group: 'matters', id: matter.id })),
      ...start.nearestDeadlines.map((deadline) => ({ group: 'matters', id: deadline.matterId })),
    ]
  }, [results, start])

  // Landing on nothing when the list changes under you is worse than landing on the first row.
  useEffect(() => setActive(0), [rows.length])

  const open = useCallback((group: string, id: string) => {
    if (group === 'matters') onOpenMatter(id)
    if (group === 'contacts') onOpenContact(id)
    onClose()
  }, [onOpenMatter, onOpenContact, onClose])

  useEffect(() => {
    const keys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setActive((current) => Math.min(current + 1, Math.max(0, rows.length - 1)))
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setActive((current) => Math.max(0, current - 1))
      }
      if (event.key === 'Enter' && rows[active]) {
        event.preventDefault()
        open(rows[active].group, rows[active].id)
      }
    }

    window.addEventListener('keydown', keys)
    return () => window.removeEventListener('keydown', keys)
  })

  // The list scrolls, so the highlighted row has to be brought along with the selection.
  const body = useRef<HTMLDivElement>(null)

  useEffect(() => {
    body.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' })
  }, [active])

  let index = -1

  return (
    <div
      onClick={onClose}
      className="fixed inset-0 z-50 flex justify-center bg-[var(--surface-scrim)] pt-[12vh]"
    >
      <div
        onClick={(event) => event.stopPropagation()}
        className="flex h-fit max-h-[68vh] w-[640px] max-w-[calc(100%-48px)] flex-col overflow-hidden rounded-xl bg-panel shadow-e3"
      >
        <div className="flex h-12 shrink-0 items-center gap-2.5 border-b border-line-subtle px-3.5 text-ink-secondary">
          <Search size={15} strokeWidth={1.75} className="shrink-0" />

          <input
            autoFocus
            value={query}
            placeholder="Chercher un dossier, un tiers, un document"
            onChange={(event) => setQuery(event.target.value)}
            className="min-w-0 flex-1 border-0 bg-transparent text-[14px] text-ink placeholder:text-muted focus:outline-none"
          />

          {results && (
            <span className="shrink-0 font-mono text-[11px] whitespace-nowrap text-muted tnum">
              {results.total} résultats
            </span>
          )}
        </div>

        <div ref={body} className="flex-1 overflow-y-auto p-1.5">
          {!results && start && (
            <>
              <Group>Dossiers récents</Group>
              {start.recentMatters.map((matter) => {
                index += 1
                const isActive = index === active

                return (
                  <Result
                    key={matter.id}
                    active={isActive}
                    onClick={() => open('matters', matter.id)}
                  >
                    <FolderClosed size={14} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />
                    <Label>{matter.label}</Label>
                    <Meta>{matter.reference}</Meta>
                    {isActive && <Enter />}
                  </Result>
                )
              })}

              <Group>Échéances les plus proches</Group>
              {start.nearestDeadlines.map((deadline) => {
                index += 1
                const isActive = index === active

                return (
                  <Result
                    key={deadline.id}
                    active={isActive}
                    onClick={() => open('matters', deadline.matterId)}
                  >
                    <TierBullet urgency={deadline.urgency} className="mx-[3.5px]" />
                    <Label>
                      {deadline.label} · {deadline.matterName}
                    </Label>
                    <Meta>{urgencyLabels[deadline.urgency]}</Meta>
                    {isActive && <Enter />}
                  </Result>
                )
              })}

              {start.recentMatters.length === 0 && start.nearestDeadlines.length === 0 && (
                <p className="px-2.5 py-4 text-[11px] text-muted">Rien à reprendre pour l’instant.</p>
              )}
            </>
          )}

          {results?.groups.map((group) => (
            <div key={group.key}>
              <Group>
                {groupTitles[group.key] ?? group.key}
                {group.total > group.items.length && ` · ${group.total}`}
              </Group>

              {group.items.map((item) => {
                index += 1
                const isActive = index === active

                return (
                  <Result key={item.id} active={isActive} onClick={() => open(group.key, item.id)}>
                    <ResultIcon group={group.key} contactType={item.contactType} />
                    <Label>{item.label}</Label>
                    {item.meta && <Meta>{item.meta}</Meta>}
                    {isActive && <Enter />}
                  </Result>
                )
              })}

              {group.total > group.items.length && (
                <div className="px-2.5 py-1 text-[11px] text-muted">
                  voir les {group.total - group.items.length} autres
                </div>
              )}
            </div>
          ))}

          {results && results.total === 0 && (
            <div className="grid gap-1 px-2.5 py-5">
              <strong className="text-[13px] font-medium">Aucun résultat pour « {term} »</strong>
              <p className="m-0 max-w-[52ch] text-[11px] leading-4 text-muted">
                La recherche couvre le nom, la référence, le client, le n° RG et la description des
                dossiers, les tiers, et le nom des documents.
              </p>
            </div>
          )}
        </div>

        <div className="flex h-7 shrink-0 items-center gap-3.5 border-t border-line-subtle px-3.5 font-mono text-[10px] text-muted">
          <span>↑↓ naviguer</span>
          <span>⏎ ouvrir</span>
          <span className="flex-1" />
          <span>@ tiers · # documents</span>
        </div>
      </div>
    </div>
  )
}

const Group = ({ children }: { children: React.ReactNode }) => (
  <div className="px-2.5 pt-2.5 pb-1 font-mono text-[10px] tracking-[0.05em] uppercase text-muted">
    {children}
  </div>
)

const Label = ({ children }: { children: React.ReactNode }) => (
  <span className="min-w-0 flex-1 truncate text-[13px]">{children}</span>
)

const Meta = ({ children }: { children: React.ReactNode }) => (
  <span className="shrink-0 font-mono text-[11px] whitespace-nowrap text-muted">{children}</span>
)

const Enter = () => (
  <span className="type-kbd shrink-0 rounded-[3px] border border-line-strong bg-panel px-1.5 py-px text-ink-secondary">
    &#9166;
  </span>
)

function Result({ active, onClick, children }: {
  active?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      data-active={active ? 'true' : undefined}
      onClick={onClick}
      className={cn(
        'flex h-[34px] w-full items-center gap-2.5 rounded-sm px-2.5 text-left transition-colors',
        // The 2px marker, as everywhere else a selection is shown.
        active ? 'bg-brand-subtle shadow-[inset_2px_0_0_var(--brand)]' : 'hover:bg-hover',
      )}
    >
      {children}
    </button>
  )
}

function ResultIcon({ group, contactType }: { group: string; contactType: ContactType | null }) {
  const style = 'shrink-0 text-ink-secondary'

  if (group === 'documents') return <FileText size={14} strokeWidth={1.75} className={style} />
  if (group === 'matters') return <FolderClosed size={14} strokeWidth={1.75} className={style} />

  // Round for a personne physique, square for a personne morale, as everywhere else.
  return contactType === 'Individual' ? (
    <User size={14} strokeWidth={1.75} className={style} />
  ) : (
    <Building2 size={14} strokeWidth={1.75} className={style} />
  )
}
