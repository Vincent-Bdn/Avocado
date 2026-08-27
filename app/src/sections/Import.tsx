import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  AlertCircle, Check, ChevronRight, FileSpreadsheet, Folder, FolderOpen, Loader2, Search, X,
} from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'

/**
 * Réglages → Importer depuis Gestisoft.
 *
 * <p><b>It used to guess and then present its guesses, and that was the problem.</b> The scan is right
 * about most of a real export and wrong about some of it, and where it was wrong there was nothing to
 * be done: CHANTERACOISE came out as two dossiers called « Assignation et nos conclusions » and
 * « Conclusions adv », three documents and two, with nothing on screen saying whose they were and no
 * way to say that the dossier is CHANTERACOISE. The six files sitting loose in CHANTERACOISE belonged
 * to no dossier at all and would have gone in silence.</p>
 *
 * <p>So this is the source tree, and she says which folders are dossiers. The guess is still there,
 * as the marks it arrives with. Marking a folder takes everything below it, which is what replaced
 * the two controls that used to be here: choosing the children instead of the parent is the split,
 * and marking nothing is leaving it out.</p>
 */

interface Folder {
  path: string
  name: string
  client: string
  isOpen: boolean
  /** Directly in this folder. What says whether marking the parent would leave anything behind. */
  files: number
  emails: number
  totalFiles: number
  totalEmails: number
  totalBytes: number
  /** What the scan would have chosen on its own. */
  suggested: boolean
  gestisoftCode: string | null
  contactsFile: string | null
  billingFile: string | null
  /** Everything that could be the contacts list, best first, so she can pick another. */
  contactsCandidates: string[]
  billingCandidates: string[]
  children: Folder[]
}

interface Plan {
  root: string
  folders: Folder[]
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

/** How many folders to draw when she is filtering, before asking for another word. */
const RESULTS = 120

/** The « choisir un fichier » entry. Not a path, so it can never collide with one. */
const BROWSE = '\u0000parcourir'

export function Import() {
  const [plan, setPlan] = useState<Plan | null>(null)
  const [marks, setMarks] = useState<Set<string>>(new Set())
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [query, setQuery] = useState('')

  /**
   * Only where she disagreed with the tree.
   *
   * <p>« Sous CLASSES veut dire clôturé » is right for almost all of hers and wrong for a few, and the
   * best-scoring PDF is the contacts list in seven dossiers and a letter in an eighth. Keeping the
   * corrections rather than the whole state means everything she did not touch still comes from one
   * place, and the screen and the scan cannot quietly drift apart.</p>
   */
  const [closed, setClosed] = useState<Set<string>>(new Set())
  const [reopened, setReopened] = useState<Set<string>>(new Set())
  const [contacts, setContacts] = useState<Record<string, string>>({})
  const [billing, setBilling] = useState<Record<string, string>>({})
  const [progress, setProgress] = useState<Progress | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
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
    const root = await window.avocado.chooseFolder(undefined, 'Le dossier contenant vos dossiers clients')
    if (!root) return

    setBusy(true)
    setError(null)

    try {
      const scanned = await post<Plan>('/api/imports/scan', { root })

      // The window and the server are two programs, and an installer that replaced one of them and
      // not the other is a thing that happens. Reading an older server's answer as a tree gave
      // « Cannot read properties of undefined », which tells her nothing she can act on.
      if (!Array.isArray(scanned?.folders)) {
        throw new Error(
          'Le serveur d’Avocado ne renvoie pas ce que cet écran attend. Fermez puis rouvrez ' +
          'Avocado ; si cela recommence, réinstallez la dernière version.',
        )
      }

      const suggested = pick(scanned.folders, (folder) => folder.suggested)

      // The first row is the folder she pointed at, and its path is the one the tree is written in.
      // plan.root is whatever she typed or picked, which is not always the same string.
      const top = scanned.folders[0]?.path ?? root

      setPlan(scanned)
      setMarks(new Set(suggested))

      // Unfolded down to the guesses but not past them: what she needs to see is which folders were
      // taken for dossiers, and everything inside one of them is simply its contents.
      setExpanded(new Set([top, ...suggested.flatMap((path) => ancestors(path, top))]))
      setQuery('')
      setTemplates(null)
      setClosed(new Set())
      setReopened(new Set())
      setContacts({})
      setBilling({})
    } catch (failure) {
      setPlan(null)
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  /**
   * A dossier cannot be inside a dossier.
   *
   * <p>Marking a folder therefore clears the marks above and below it, rather than refusing. She is
   * saying « this one is the dossier », and the useful reading of that is that whatever she had said
   * about its parent or its children no longer holds. Refusing would leave her to work out which
   * other row to change first.</p>
   */
  const mark = useCallback((folder: Folder) => {
    setMarks((current) => {
      const next = new Set(current)

      if (next.delete(folder.path)) return next

      for (const path of [...next]) {
        if (path.startsWith(folder.path + '\\') || path.startsWith(folder.path + '/')) next.delete(path)
        if (folder.path.startsWith(path + '\\') || folder.path.startsWith(path + '/')) next.delete(path)
      }

      next.add(folder.path)
      return next
    })
  }, [])

  const toggle = useCallback((path: string) => {
    setExpanded((current) => {
      const next = new Set(current)
      if (!next.delete(path)) next.add(path)
      return next
    })
  }, [])

  const chosen = useMemo(
    () => (plan ? gather(plan.folders, marks) : []),
    [plan, marks],
  )

  const orphans = (plan?.files ?? 0) - chosen.reduce((total, folder) => total + folder.totalFiles, 0)

  const found = useMemo(() => {
    const needles = fold(query).split(/\s+/).filter(Boolean)

    if (needles.length === 0 || plan === null) return []

    return flatten(plan.folders).filter((folder) =>
      needles.every((needle) => fold(folder.path).includes(needle)))
  }, [plan, query])

  const corrections = () => ({
    open: [...reopened],
    closed: [...closed],
    contacts,
    billing,
  })

  async function writeTemplates() {
    if (!plan) return

    setBusy(true)
    setError(null)

    try {
      const result = await post<{ written: string[]; kept: string[] }>('/api/imports/templates', {
        root: plan.root,
        dossiers: [...marks],
        choices: corrections(),
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
      await post('/api/imports/run', {
        root: plan.root,
        dossiers: [...marks],
        choices: corrections(),
      })
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

  const isOpen = (folder: Folder) =>
    reopened.has(folder.path) ? true : closed.has(folder.path) ? false : folder.isOpen

  const setStatus = (folder: Folder, open: boolean) => {
    const [add, remove] = open ? [setReopened, setClosed] : [setClosed, setReopened]

    add((current) => new Set(current).add(folder.path))
    remove((current) => {
      const next = new Set(current)
      next.delete(folder.path)
      return next
    })
  }

  const setSidecar = (which: 'contacts' | 'billing') => (folder: Folder, file: string) =>
    (which === 'contacts' ? setContacts : setBilling)((current) =>
      ({ ...current, [folder.path]: file }))

  const row = (folder: Folder, depth: number) => (
    <FolderRow
      key={folder.path}
      folder={folder}
      depth={depth}
      marked={marks.has(folder.path)}
      inside={insideMark(folder.path, marks)}
      open={expanded.has(folder.path)}
      isOpen={isOpen(folder)}
      contactsFile={contacts[folder.path] ?? folder.contactsFile ?? ''}
      billingFile={billing[folder.path] ?? folder.billingFile ?? ''}
      onToggle={() => toggle(folder.path)}
      onMark={() => mark(folder)}
      onStatus={(open) => setStatus(folder, open)}
      onContacts={(file) => setSidecar('contacts')(folder, file)}
      onBilling={(file) => setSidecar('billing')(folder, file)}
    />
  )

  return (
    <>
      {progress?.finished && <Finished progress={progress} />}

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="secondary" size="sm" onClick={() => void choose()} disabled={busy}>
          {busy ? <Loader2 size={13} className="animate-spin" /> : <FolderOpen size={13} strokeWidth={2} />}
          Choisir le dossier à importer
        </Button>

        <span className="text-[11.5px] leading-[17px] text-muted">
          Celui qui contient vos dossiers clients, quelle que soit la façon dont ils sont rangés.
        </span>
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {plan && (
        <>
          <div className="grid gap-1 rounded-sm border border-line-subtle bg-panel px-2.5 py-2">
            <div className="type-group text-ink-secondary">Ce qui sera importé</div>

            <div className="font-mono text-[12px] tnum">
              {chosen.length} dossiers ·{' '}
              {chosen.reduce((total, f) => total + f.totalFiles, 0).toLocaleString('fr-FR')} documents ·{' '}
              {chosen.reduce((total, f) => total + f.totalEmails, 0).toLocaleString('fr-FR')} courriels ·{' '}
              {(chosen.reduce((total, f) => total + f.totalBytes, 0) / 1e9)
                .toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go
            </div>

            <div className="font-mono text-[12px] tnum">
              {chosen.filter((f) => (contacts[f.path] ?? f.contactsFile)).length} listes de contacts ·{' '}
              {chosen.filter((f) => (billing[f.path] ?? f.billingFile)).length} exports de facturation ·{' '}
              {chosen.filter((f) => !isOpen(f)).length} clôturés
            </div>

            {/* The number the old screen never showed. Eighty-one files sat in folders it had walked
                past, and it imported everything else without a word about them. */}
            {orphans > 0 && (
              <div className="flex items-start gap-1.5 pt-0.5 text-[11.5px] leading-[17px] text-warning">
                <AlertCircle size={12} strokeWidth={2} className="mt-0.5 shrink-0" />
                <span>
                  {orphans.toLocaleString('fr-FR')} fichier{orphans > 1 ? 's' : ''} ne{' '}
                  {orphans > 1 ? 'sont' : 'est'} sous aucun dossier retenu et ne{' '}
                  {orphans > 1 ? 'seront' : 'sera'} pas importé{orphans > 1 ? 's' : ''}. Marquez un
                  dossier plus haut pour {orphans > 1 ? 'les' : 'l’'}inclure.
                </span>
              </div>
            )}

            <p className="m-0 max-w-[76ch] text-[11px] leading-[16px] text-muted">
              L’arborescence de chaque dossier est conservée telle quelle et chaque courriel classé au
              journal avec ses pièces jointes. Là où Gestisoft a exporté une liste de contacts ou une
              facturation, les tiers et les factures sont repris ; ailleurs ils restent vides, et rien
              ne sera inventé à leur place.
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
            <div className="flex flex-wrap items-baseline gap-2">
              <span className="type-label text-ink-secondary">Quels dossiers</span>
              <span className="text-[11px] leading-[16px] text-muted">
                Un dossier marqué prend tout ce qu’il contient. Marquez plus haut pour regrouper, plus
                bas pour séparer.
              </span>
            </div>

            <span className="flex items-center gap-1.5">
              <Search size={13} strokeWidth={1.75} className="shrink-0 text-muted" />
              <Input
                inputSize="sm"
                className="flex-1"
                value={query}
                placeholder="Chercher un dossier par son nom…"
                onChange={(event) => setQuery(event.target.value)}
              />
              {query !== '' && (
                <button
                  type="button"
                  aria-label="Effacer"
                  onClick={() => setQuery('')}
                  className="grid h-6 w-6 shrink-0 place-items-center rounded-[3px] text-ink-secondary hover:bg-hover"
                >
                  <X size={13} strokeWidth={2} />
                </button>
              )}
            </span>

            <div className="max-h-[420px] overflow-y-auto">
              {query.trim() !== '' ? (
                <>
                  {found.length === 0 && (
                    <p className="m-0 py-2 text-[11.5px] text-muted">Aucun dossier de ce nom.</p>
                  )}

                  {found.slice(0, RESULTS).map((folder) => row(folder, 0))}

                  {found.length > RESULTS && (
                    <p className="m-0 pt-1 text-[11px] text-muted">
                      Les {RESULTS} premiers sur {found.length}. Ajoutez un mot pour resserrer.
                    </p>
                  )}
                </>
              ) : (
                <Tree folders={plan.folders} depth={0} expanded={expanded} row={row} />
              )}
            </div>
          </div>

          <div className="grid gap-1.5 rounded-sm border border-line-subtle px-2.5 py-2">
            <span className="type-label text-ink-secondary">Tiers et facturation, si vous les avez</span>

            <p className="m-0 max-w-[76ch] text-[11.5px] leading-[17px] text-muted">
              Là où Gestisoft n’a rien exporté, Avocado n’inventera rien. Ces deux tableaux arrivent
              avec un nom de dossier par ligne : remplissez ce que vous voulez, laissez le reste vide,
              et relancez l’import. Rien n’est obligatoire, et vous pouvez le faire plus tard dossier
              par dossier.
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
            <Button size="sm" onClick={() => void run()} disabled={busy || chosen.length === 0}>
              <Check size={13} strokeWidth={2.5} />
              Importer {chosen.length} dossier{chosen.length > 1 ? 's' : ''}
            </Button>
          </div>
        </>
      )}
    </>
  )
}

/** Whatever the platform's separator is; the paths come from the server as it wrote them. */
const under = (path: string, ancestor: string) =>
  path.startsWith(ancestor + '\\') || path.startsWith(ancestor + '/')

const insideMark = (path: string, marks: Set<string>) =>
  [...marks].some((mark) => under(path, mark))

/** Every folder path where the predicate holds, without descending into one that already matched. */
function pick(folders: readonly Folder[] | undefined, holds: (folder: Folder) => boolean): string[] {
  return (folders ?? []).flatMap((folder) =>
    holds(folder) ? [folder.path] : pick(folder.children, holds))
}

/** The folders she marked, in the order they appear in the tree. */
function gather(folders: readonly Folder[] | undefined, marks: Set<string>): Folder[] {
  return (folders ?? []).flatMap((folder) =>
    marks.has(folder.path)
      ? (folder.totalFiles > 0 ? [folder] : [])
      : gather(folder.children, marks))
}

function flatten(folders: readonly Folder[] | undefined): Folder[] {
  return (folders ?? []).flatMap((folder) => [folder, ...flatten(folder.children)])
}

/** Every folder between the root and this one, so that unfolding them puts it on screen. */
function ancestors(path: string, root: string): string[] {
  const rest = path.slice(root.length).split(/[/\\]/).filter(Boolean)
  const separator = path.includes('\\') ? '\\' : '/'

  return rest.slice(0, -1).map((_, index) =>
    root.replace(/[/\\]$/, '') + separator + rest.slice(0, index + 1).join(separator))
}

/** Accent- and case-blind, so « procedure » finds « Procédure ». */
const fold = (value: string) =>
  value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

function Tree({ folders, depth, expanded, row }: {
  folders: readonly Folder[]
  depth: number
  expanded: Set<string>
  row: (folder: Folder, depth: number) => React.ReactNode
}) {
  return (
    <>
      {folders.map((folder) => (
        <div key={folder.path}>
          {row(folder, depth)}

          {expanded.has(folder.path) && (folder.children?.length ?? 0) > 0 && (
            <Tree folders={folder.children} depth={depth + 1} expanded={expanded} row={row} />
          )}
        </div>
      ))}
    </>
  )
}

/**
 * One folder, and the questions asked of it.
 *
 * <p>Three states rather than a checkbox: it is the dossier, it is inside one, or it is neither and
 * she may still say. The middle one carries no controls at all, because « inside CHANTERACOISE » is
 * not a thing she decides, it is what marking CHANTERACOISE meant.</p>
 *
 * <p>Everything a marked row shows is a correction waiting to happen. Whether it is closed was read
 * off the path and the path is somebody's filing habit; which file holds the tiers was read off a
 * name, and a letter called « pas de contact connu chez EDF OA » is a PDF with contact in its name
 * too. So each of them is the control that changes it, in place, rather than a fact.</p>
 */
function FolderRow({
  folder, depth, marked, inside, open, isOpen, contactsFile, billingFile,
  onToggle, onMark, onStatus, onContacts, onBilling,
}: {
  folder: Folder
  depth: number
  marked: boolean
  inside: boolean
  /** Unfolded in the tree. Nothing to do with the dossier being en cours. */
  open: boolean
  isOpen: boolean
  contactsFile: string
  billingFile: string
  onToggle: () => void
  onMark: () => void
  onStatus: (open: boolean) => void
  onContacts: (file: string) => void
  onBilling: (file: string) => void
}) {
  return (
    <div
      style={{ paddingLeft: 4 + depth * 15 }}
      className={cn(
        'flex min-h-[30px] items-center gap-1.5 border-t border-line-subtle pr-1 text-[11.5px]',
        marked && 'bg-brand-subtle',
        inside && 'text-muted',
      )}
    >
      <button
        type="button"
        aria-label={open ? 'Replier' : 'Déplier'}
        onClick={onToggle}
        disabled={(folder.children?.length ?? 0) === 0}
        className="grid h-5 w-5 shrink-0 place-items-center rounded-[3px] hover:bg-hover disabled:opacity-0"
      >
        <ChevronRight
          size={13}
          strokeWidth={2}
          className={cn('text-muted transition-transform', open && 'rotate-90')}
        />
      </button>

      {open
        ? <FolderOpen size={13} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />
        : <Folder size={13} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />}

      <span className={cn('min-w-0 truncate', marked && 'font-medium')}>{folder.name}</span>

      {marked && (
        <>
          {/* Read off the path, and the path is her filing habit rather than a fact, so it is a
              button. Everything under CLASSES arrives clôturé and any of them can be put back. */}
          <button
            type="button"
            onClick={() => onStatus(!isOpen)}
            title={isOpen
              ? 'Dossier en cours. Cliquer pour le marquer clôturé.'
              : 'Dossier clôturé. Cliquer pour le marquer en cours.'}
            className={cn(
              'shrink-0 rounded-full px-1.5 py-px font-mono text-[9.5px] leading-3',
              isOpen
                ? 'bg-success-bg text-success hover:brightness-95'
                : 'bg-sunken text-muted hover:bg-hover',
            )}
          >
            {isOpen ? 'en cours' : 'clôturé'}
          </button>

          <FilePick
            label="tiers"
            empty="tiers ?"
            title="Le PDF « Liste des contacts » de ce dossier"
            folder={folder.path}
            extensions={['pdf']}
            chosen={contactsFile}
            candidates={folder.contactsCandidates}
            onPick={onContacts}
          />

          <FilePick
            label="factu"
            empty="factu ?"
            title="L’export Excel de la facturation de ce dossier"
            folder={folder.path}
            extensions={['xlsx', 'xls']}
            chosen={billingFile}
            candidates={folder.billingCandidates}
            onPick={onBilling}
          />
        </>
      )}

      {/* « 2 ici · 40 » said nothing about what was being counted. Both numbers only where they
          differ, which is exactly where marking the parent instead of the children changes what gets
          imported. */}
      <span
        className="ml-auto shrink-0 pl-2 font-mono text-[10.5px] text-muted tnum"
        title={`${folder.totalFiles} document${folder.totalFiles > 1 ? 's' : ''} en tout`
          + (folder.files > 0 && folder.files !== folder.totalFiles
            ? `, dont ${folder.files} directement dans ce dossier`
            : '')}
      >
        {folder.files > 0 && folder.files !== folder.totalFiles
          ? `${folder.files} doc ici · ${folder.totalFiles.toLocaleString('fr-FR')} en tout`
          : `${folder.totalFiles.toLocaleString('fr-FR')} doc`}
      </span>

      {inside ? (
        <span className="w-[152px] shrink-0 text-right font-mono text-[9.5px] text-muted">inclus</span>
      ) : (
        <button
          type="button"
          onClick={onMark}
          title={marked
            ? 'Ce dossier sera importé avec tout ce qu’il contient. Cliquer pour annuler.'
            : 'Importer ce dossier, avec tout ce qu’il contient'}
          className={cn(
            'w-[152px] shrink-0 rounded-sm border px-1.5 py-px text-[10px] leading-4',
            marked
              ? 'border-brand bg-brand text-on-brand'
              : 'border-line text-muted hover:bg-hover',
          )}
        >
          {marked ? 'Dossier' : 'Définir comme dossier'}
        </button>
      )}
    </div>
  )
}

/**
 * The file she says holds the tiers, or the facturation.
 *
 * <p>A badge with a native select laid over it: the same trick the documents tab uses for « classer
 * dans », and it keeps a row that is already busy to one chip. The guess is pre-selected and « aucun »
 * is one of the options, because sometimes the answer is that Gestisoft exported nothing and the file
 * it found is a letter.</p>
 *
 * <p>What is offered is only what a name gave away, so the list stays short: every facture she filed
 * in « Contacts et factu » would otherwise be offered as a contacts list, and COULEYRE's real one came
 * back behind eleven invoices. Anything named neither is reached through « Choisir un fichier… »,
 * which opens where the dossier is.</p>
 */
function FilePick({ label, empty, title, folder, extensions, chosen, candidates, onPick }: {
  label: string
  empty: string
  title: string
  /** Where the picker opens, so she is not dropped in her home directory. */
  folder: string
  extensions: string[]
  chosen: string
  candidates: string[]
  onPick: (file: string) => void
}) {
  const options = chosen !== '' && !candidates.includes(chosen) ? [chosen, ...candidates] : candidates

  return (
    <span className="relative shrink-0">
      <span
        title={`${title}${chosen === '' ? '' : `\n${chosen}`}`}
        className={cn(
          'block rounded-full px-1.5 py-px font-mono text-[9.5px] leading-3',
          chosen === ''
            ? 'border border-dashed border-line text-muted'
            : 'bg-brand-subtle text-brand-on-subtle',
        )}
      >
        {chosen === '' ? empty : label}
      </span>

      <select
        aria-label={title}
        value={chosen}
        onChange={(event) => {
          if (event.target.value === BROWSE) {
            void window.avocado
              .chooseFile(title, folder, extensions)
              .then((file) => { if (file) onPick(file) })

            return
          }

          onPick(event.target.value)
        }}
        className="absolute inset-0 cursor-pointer opacity-0"
      >
        <option value="">aucun</option>
        {options.map((file) => (
          <option key={file} value={file}>{file.split(/[/\\]/).pop()}</option>
        ))}
        <option value={BROWSE}>Choisir un fichier…</option>
      </select>
    </span>
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
