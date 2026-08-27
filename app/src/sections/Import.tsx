import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, Check, FileSpreadsheet, FolderOpen, Loader2 } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'

/**
 * Réglages → Importer depuis Gestisoft.
 *
 * <p>The export is folders, however deep she filed them, and the scan finds the dossiers by their
 * shape. It says what it will do before doing it, because thirteen thousand files is not something
 * anyone should start on trust.</p>
 *
 * <p>Two things it can find beside a dossier and could not before: the contacts list Gestisoft
 * prints, and export.xlsx. Both are shown per row, because « tiers » on a line is the difference
 * between an evening of retyping and none, and because a row that is missing one is the row worth
 * her looking at. Which folders hold several affaires stays her judgement, offered on every row.</p>
 */

interface Candidate {
  sourcePath: string
  client: string
  name: string
  isOpen: boolean
  files: number
  emails: number
  bytes: number
  subfolders: number
  /** Gestisoft's own number for the dossier, when the folder is named after it. */
  gestisoftCode: string | null
  /** The « Liste des contacts » PDF found beside it, if there is one. */
  contactsFile: string | null
  /** export.xlsx, likewise. */
  billingFile: string | null
}

interface Plan {
  root: string
  candidates: Candidate[]
  skipped: string[]
  files: number
  bytes: number
}

interface Progress {
  dossiers: number
  dossiersDone: number
  files: number
  done: number
  emails: number
  current: string | null
  finished: boolean
  error: string | null
  warnings: string[]
}

export function Import() {
  const [plan, setPlan] = useState<Plan | null>(null)
  const [split, setSplit] = useState<Set<string>>(new Set())
  const [progress, setProgress] = useState<Progress | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showAll, setShowAll] = useState(false)
  const [templates, setTemplates] = useState<string[] | null>(null)

  const readProgress = useCallback(() => {
    api<Progress | undefined>('/api/imports/progress')
      .then((state) => setProgress(state ?? null))
      .catch(() => undefined)
  }, [])

  useEffect(readProgress, [readProgress])

  // While it runs, the only thing on screen that changes is this.
  useEffect(() => {
    if (progress === null || progress.finished) return
    const timer = setInterval(readProgress, 1_000)
    return () => clearInterval(timer)
  }, [progress, readProgress])

  async function choose() {
    const root = await window.avocado.chooseFolder(undefined, 'Le dossier exporté de Gestisoft')
    if (!root) return

    setBusy(true)
    setError(null)

    try {
      setPlan(await post<Plan>('/api/imports/scan', { root }))
      setSplit(new Set())
    } catch (failure) {
      setPlan(null)
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  async function writeTemplates() {
    if (!plan) return

    setBusy(true)
    setError(null)

    try {
      const result = await post<{ written: string[]; kept: string[] }>('/api/imports/templates', {
        root: plan.root,
      })

      setTemplates([...result.written, ...result.kept])
      await window.avocado.revealFolder(plan.root)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  async function run() {
    if (!plan) return

    setBusy(true)
    setError(null)

    try {
      await post('/api/imports/run', { root: plan.root, split: [...split], only: null })
      readProgress()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (progress !== null && !progress.finished) {
    return <Running progress={progress} />
  }

  return (
    <>
      {progress?.finished && <Finished progress={progress} />}

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="secondary" size="sm" onClick={() => void choose()} disabled={busy}>
          {busy ? <Loader2 size={13} className="animate-spin" /> : <FolderOpen size={13} strokeWidth={2} />}
          Choisir le dossier exporté
        </Button>

        <span className="text-[11.5px] leading-[17px] text-muted">
          Celui qui contient « EN COURS » et « CLASSES ».
        </span>
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {plan && (
        <>
          <div className="grid gap-1 rounded-sm border border-line-subtle bg-panel px-2.5 py-2">
            <div className="type-group text-ink-secondary">Ce qui sera importé</div>
            <div className="font-mono text-[12px] tnum">
              {plan.candidates.length} dossiers · {plan.files.toLocaleString('fr-FR')} documents ·{' '}
              {plan.candidates.reduce((total, c) => total + c.emails, 0).toLocaleString('fr-FR')} courriels ·{' '}
              {(plan.bytes / 1e9).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go
            </div>
            <div className="font-mono text-[12px] tnum">
              {plan.candidates.filter((c) => c.contactsFile).length} listes de contacts ·{' '}
              {plan.candidates.filter((c) => c.billingFile).length} exports de facturation
            </div>
            <p className="m-0 max-w-[76ch] text-[11px] leading-[16px] text-muted">
              L’arborescence est conservée telle quelle et chaque courriel classé au journal avec ses
              pièces jointes. Là où Gestisoft a exporté une liste de contacts ou une facturation, les
              tiers et les factures sont repris ; ailleurs ils restent vides, et rien ne sera inventé
              à leur place.
            </p>
          </div>

          {plan.skipped.length > 0 && (
            <ul className="m-0 grid list-none gap-0.5 p-0">
              {plan.skipped.map((line) => (
                <li key={line} className="flex items-start gap-1.5 text-[11px] leading-[16px] text-muted">
                  <AlertCircle size={12} strokeWidth={2} className="mt-px shrink-0" />
                  {line}
                </li>
              ))}
            </ul>
          )}

          <div className="grid gap-1.5 rounded-sm border border-line-subtle px-2.5 py-2">
            <span className="type-label text-ink-secondary">Tiers et facturation, si vous les avez</span>

            <p className="m-0 max-w-[76ch] text-[11.5px] leading-[17px] text-muted">
              L’export n’en contient pas, et Avocado n’en inventera pas. Ces deux tableaux arrivent avec
              un nom de dossier par ligne : remplissez ce que vous voulez, laissez le reste vide, et
              relancez l’import. Rien n’est obligatoire, et vous pouvez le faire plus tard dossier par
              dossier.
            </p>

            <div className="flex flex-wrap items-center gap-2">
              <Button variant="secondary" size="sm" onClick={() => void writeTemplates()} disabled={busy}>
                <FileSpreadsheet size={13} strokeWidth={2} />
                Préparer les deux tableaux
              </Button>

              {templates && (
                <span className="font-mono text-[10.5px] text-muted">
                  {templates.map((path) => path.split(/[/\\]/).pop()).join(' · ')}
                </span>
              )}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button size="sm" onClick={() => void run()} disabled={busy}>
              <Check size={13} strokeWidth={2.5} />
              Importer {plan.candidates.reduce((total, c) => total + (split.has(c.sourcePath) ? c.subfolders : 1), 0)} dossiers
            </Button>

            <button
              type="button"
              onClick={() => setShowAll((current) => !current)}
              className="text-[11.5px] text-ink-secondary underline-offset-2 hover:underline"
            >
              {showAll ? 'Masquer la liste' : 'Voir la liste'}
            </button>
          </div>

          {showAll && (
            <ul className="m-0 grid max-h-[320px] list-none gap-0.5 overflow-y-auto p-0">
              {plan.candidates.map((candidate) => (
                <li
                  key={candidate.sourcePath}
                  className="flex items-baseline gap-2 border-b border-line-subtle py-1 text-[11.5px]"
                >
                  <span className="truncate">{candidate.name}</span>
                  <span
                    className={cn(
                      'shrink-0 rounded-full px-1.5 py-px font-mono text-[9.5px] leading-3',
                      candidate.isOpen ? 'bg-success-bg text-success' : 'bg-sunken text-muted',
                    )}
                  >
                    {candidate.isOpen ? 'en cours' : 'clôturé'}
                  </span>
                  {/* Only when found. A badge on every row would say « this one has nothing »
                      ninety-four times, and the seven that do have something are what she is
                      looking for. */}
                  {candidate.contactsFile && (
                    <span
                      title={candidate.contactsFile}
                      className="shrink-0 rounded-full bg-brand-subtle px-1.5 py-px font-mono text-[9.5px] leading-3 text-brand-on-subtle"
                    >
                      tiers
                    </span>
                  )}

                  {candidate.billingFile && (
                    <span
                      title={candidate.billingFile}
                      className="shrink-0 rounded-full bg-brand-subtle px-1.5 py-px font-mono text-[9.5px] leading-3 text-brand-on-subtle"
                    >
                      factu
                    </span>
                  )}

                  <span className="ml-auto shrink-0 font-mono text-[10.5px] text-muted tnum">
                    {candidate.files} doc{candidate.files > 1 ? 's' : ''}
                    {candidate.emails > 0 && ` · ${candidate.emails} courriels`}
                  </span>

                  {/* On every row with subfolders, not only the six guessed at. JH TRANSPORT is six
                      affaires and 5,215 files and is never suggested, because one of them is called
                      « 700119 » and a leading digit reads as a filing scheme. */}
                  {candidate.subfolders > 1 && (
                    <button
                      type="button"
                      onClick={() =>
                        setSplit((current) => {
                          const next = new Set(current)
                          if (next.has(candidate.sourcePath)) next.delete(candidate.sourcePath)
                          else next.add(candidate.sourcePath)
                          return next
                        })
                      }
                      className={cn(
                        'shrink-0 rounded-sm border px-1.5 py-px text-[10px] leading-4',
                        split.has(candidate.sourcePath)
                          ? 'border-brand bg-brand-subtle text-brand-on-subtle'
                          : 'border-line text-muted hover:bg-hover',
                      )}
                    >
                      {split.has(candidate.sourcePath)
                        ? `${candidate.subfolders} dossiers`
                        : 'séparer'}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </>
  )
}

/** The only screen there is while it runs, so it says what is happening rather than just spinning. */
function Running({ progress }: { progress: Progress }) {
  const percent = progress.files === 0 ? 0 : Math.min(100, Math.round((progress.done / progress.files) * 100))

  return (
    <div className="grid gap-2 rounded-sm border border-line-subtle bg-panel px-2.5 py-2">
      <div className="flex items-center gap-1.5 text-[12.5px] font-medium">
        <Loader2 size={14} className="animate-spin text-brand" />
        Import en cours
      </div>

      <div className="h-1.5 overflow-hidden rounded-full bg-sunken">
        <div className="h-full rounded-full bg-brand transition-[width]" style={{ width: `${percent}%` }} />
      </div>

      <div className="font-mono text-[11px] text-muted tnum">
        {progress.dossiersDone} / {progress.dossiers} dossiers ·{' '}
        {progress.done.toLocaleString('fr-FR')} / {progress.files.toLocaleString('fr-FR')} documents ·{' '}
        {progress.emails.toLocaleString('fr-FR')} courriels
      </div>

      {progress.current && (
        <div className="truncate text-[11.5px] text-ink-secondary">En cours : {progress.current}</div>
      )}

      <p className="m-0 max-w-[76ch] text-[11px] leading-[16px] text-muted">
        Tout est chiffré au passage, ce qui prend le plus clair du temps. Vous pouvez continuer à
        travailler ; fermer Avocado interromprait l’import.
      </p>
    </div>
  )
}

function Finished({ progress }: { progress: Progress }) {
  const failed = progress.error !== null

  return (
    <div
      className={cn(
        'grid gap-1.5 rounded-sm border px-2.5 py-2',
        failed ? 'border-[#E8D5AE] bg-warning-bg text-warning' : 'border-[#BFD3C5] bg-success-bg text-success',
      )}
    >
      <div className="flex items-center gap-1.5 text-[12.5px] font-medium">
        {failed ? <AlertCircle size={14} strokeWidth={2} /> : <Check size={14} strokeWidth={2.5} />}
        {failed ? 'L’import s’est arrêté' : 'Import terminé'}
      </div>

      <div className="font-mono text-[11px] tnum">
        {progress.dossiersDone} dossiers · {progress.done.toLocaleString('fr-FR')} documents ·{' '}
        {progress.emails.toLocaleString('fr-FR')} courriels classés au journal
      </div>

      {progress.error && <p className="m-0 text-[11.5px] leading-[17px]">{progress.error}</p>}

      {progress.warnings.length > 0 && (
        <details className="text-[11px] leading-[16px]">
          <summary className="cursor-pointer">
            {progress.warnings.length} fichier{progress.warnings.length > 1 ? 's' : ''} n’a pas pu être lu
          </summary>
          <ul className="m-0 mt-1 grid list-none gap-0.5 p-0 font-mono text-[10.5px] opacity-80">
            {progress.warnings.slice(0, 40).map((line) => <li key={line}>{line}</li>)}
          </ul>
        </details>
      )}
    </div>
  )
}
