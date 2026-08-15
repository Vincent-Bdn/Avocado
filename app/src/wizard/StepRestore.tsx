import { useState } from 'react'
import { ArrowLeft, FileText, FolderOpen, HardDrive, Loader2 } from 'lucide-react'
import { ApiError, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { cn } from '../lib/utils.js'
import { WizardGate, WizardLead, WizardScroll, WizardTitle } from './shared.js'

interface RestorePoint {
  path: string
  takenAt: string
  sizeBytes: number
}

interface Candidate {
  vaultId: string
  updatedAt: string | null
  documents: number
  documentBytes: number
  points: RestorePoint[]
}

/**
 * The other first run: this machine is the replacement.
 *
 * <p>Written as its own screen rather than as a link in Réglages, because the day it runs there is no
 * Réglages, no vault and no data. The person doing it has lost a computer, is not calm, and needs to
 * be told at each step what will happen next.</p>
 *
 * <p>Order matters here for the same reason it does in the engine: find the backup and say what is in
 * it *before* asking for the recovery key. Asking first and revealing afterwards that the folder was
 * the wrong one is how people conclude their backups were worthless.</p>
 */
export function StepRestore({ onBack, onRestored }: { onBack: () => void; onRestored: () => void }) {
  const [source, setSource] = useState<string | null>(null)
  const [candidates, setCandidates] = useState<Candidate[] | null>(null)
  const [point, setPoint] = useState<RestorePoint | null>(null)
  const [chosen, setChosen] = useState<Candidate | null>(null)
  const [destination, setDestination] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function browse() {
    const path = await window.avocado.chooseFolder(undefined, 'Où se trouve la sauvegarde')
    if (!path) return

    setBusy(true)
    setError(null)
    setCandidates(null)

    try {
      const found = await post<Candidate[]>('/api/vault/restore/discover', { source: path })
      setSource(path)
      setCandidates(found)

      // One backup on the key is the overwhelmingly common case, so choose it rather than making
      // someone pick from a list of one.
      if (found.length === 1 && found[0]) {
        setChosen(found[0])
        setPoint(found[0].points[0] ?? null)
      }
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  /**
   * Accepts the PDF the wizard exported, or the text file it wrote to a USB key. The code carries a
   * checksum, so a file that happens to contain something code-shaped is rejected rather than
   * silently filling the field with the wrong thing.
   */
  async function readSheet() {
    const path = await window.avocado.chooseFile('Votre fiche de clé de récupération')
    if (!path) return

    setBusy(true)
    setError(null)

    try {
      const found = await post<{ code: string }>('/api/vault/restore/recovery-file', { path })
      setCode(found.code)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  async function restore() {
    if (!source || !chosen || !point || !destination) return

    setBusy(true)
    setError(null)

    try {
      await post('/api/vault/restore', {
        source,
        vaultId: chosen.vaultId,
        snapshotPath: point.path,
        destination,
        recoveryCode: code,
      })

      onRestored()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <WizardScroll>
        <WizardTitle>Retrouver vos dossiers.</WizardTitle>

        <WizardLead>
          Il vous faut deux choses : la sauvegarde, sur une clé, un disque ou dans un dossier
          synchronisé, et <strong>votre clé de récupération</strong>. Elles ne servent qu’ensemble :
          la sauvegarde est chiffrée, et rien d’autre ne l’ouvre.
        </WizardLead>

        <ol className="m-0 grid list-none gap-4 p-0">
          <Stage index={1} title="Où est la sauvegarde ?" done={candidates !== null}>
            <div className="flex items-center gap-2">
              <Button variant="secondary" size="sm" onClick={() => void browse()} disabled={busy}>
                <FolderOpen size={13} strokeWidth={2} />
                Parcourir
              </Button>
              {source && <span className="truncate font-mono text-[11px] text-muted">{source}</span>}
            </div>

            {candidates?.length === 0 && (
              <p className="m-0 text-[12px] leading-[18px] text-warning">
                Aucune sauvegarde Avocado dans ce dossier. Si votre clé en contient une, choisissez sa
                racine plutôt qu’un sous-dossier : Avocado range ses copies dans un dossier
                « avocado » qu’il saura retrouver seul.
              </p>
            )}

            {candidates?.map((candidate) => (
              <button
                key={candidate.vaultId}
                type="button"
                onClick={() => {
                  setChosen(candidate)
                  setPoint(candidate.points[0] ?? null)
                }}
                className={cn(
                  'flex w-full items-center gap-2.5 rounded-md border px-3 py-2.5 text-left',
                  chosen?.vaultId === candidate.vaultId
                    ? 'border-brand bg-brand-subtle'
                    : 'border-line hover:bg-hover',
                )}
              >
                <HardDrive size={15} strokeWidth={1.75} className="text-ink-secondary" />
                <span className="grid gap-0.5">
                  <span className="text-[12.5px] font-medium">
                    Sauvegarde du {formatDay(candidate.points[0]?.takenAt ?? candidate.updatedAt)}
                  </span>
                  <span className="font-mono text-[10.5px] text-muted">
                    {candidate.documents} document{candidate.documents > 1 ? 's' : ''} ·{' '}
                    {formatBytes(candidate.documentBytes)} · {candidate.points.length} copie
                    {candidate.points.length > 1 ? 's' : ''} datée
                    {candidate.points.length > 1 ? 's' : ''}
                  </span>
                </span>
              </button>
            ))}

            {/* Offering the history is the point: the reason for restoring is sometimes that
                something went wrong, and the newest copy would carry it too. */}
            {chosen && chosen.points.length > 1 && (
              <label className="grid gap-1">
                <span className="type-label text-ink-secondary">Quelle date rétablir</span>
                <select
                  value={point?.path ?? ''}
                  onChange={(event) =>
                    setPoint(chosen.points.find((candidate) => candidate.path === event.target.value) ?? null)
                  }
                  className="h-8 rounded-sm border border-line bg-panel px-2 text-[12px]"
                >
                  {chosen.points.map((candidate) => (
                    <option key={candidate.path} value={candidate.path}>
                      {formatMoment(candidate.takenAt)}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </Stage>

          <Stage index={2} title="Où la rétablir sur cet ordinateur ?" done={destination !== null}>
            <div className="flex items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={busy}
                onClick={() =>
                  void window.avocado
                    .chooseFolder(undefined, 'Emplacement du coffre')
                    .then((path) => path && setDestination(path))
                }
              >
                <FolderOpen size={13} strokeWidth={2} />
                Choisir un dossier
              </Button>
              {destination && (
                <span className="truncate font-mono text-[11px] text-muted">{destination}</span>
              )}
            </div>

            <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
              Un dossier vide, sur le disque de cet ordinateur. Pas dans un dossier synchronisé : le
              coffre est une base ouverte en permanence, qu’une synchronisation finirait par abîmer.
            </p>
          </Stage>

          <Stage index={3} title="Votre clé de récupération" done={false}>
            {/* The straightforward path, offered first. Someone who saved the sheet as a PDF should
                not be made to transcribe fifty-four characters out of a file the computer can read. */}
            <div className="flex items-center gap-2">
              <Button variant="secondary" size="sm" disabled={busy} onClick={() => void readSheet()}>
                <FileText size={13} strokeWidth={2} />
                Lire depuis la fiche
              </Button>
              <span className="text-[11.5px] text-muted">ou saisissez-la ci-dessous</span>
            </div>

            <Input
              value={code}
              onChange={(event) => setCode(event.target.value)}
              placeholder="87CQ1X-382EVN-6SCJ9Q-1P5K46-SS9RQ0-RJK5MW-9ESAWM-VNN5HT-W130ZD"
              className="font-mono tracking-[0.06em]"
              spellCheck={false}
            />
            <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
              Les neuf groupes de votre feuille. La casse et les tirets n’ont pas d’importance. Elle
              est vérifiée avant que quoi que ce soit ne soit téléchargé, donc une erreur de frappe ne
              vous coûtera que de la retaper.
            </p>
          </Stage>
        </ol>

        {error && <p className="mt-4 mb-0 text-[12px] text-danger">{error}</p>}
      </WizardScroll>

      <WizardGate>
        <Button variant="ghost" onClick={onBack} disabled={busy}>
          <ArrowLeft size={14} strokeWidth={2} />
          Retour
        </Button>

        <span className="flex-1" />

        <Button
          size="lg"
          disabled={busy || !chosen || !point || !destination || code.trim().length < 20}
          onClick={() => void restore()}
        >
          {busy && <Loader2 size={14} className="animate-spin" />}
          {busy ? 'Restauration…' : 'Restaurer et ouvrir Avocado'}
        </Button>
      </WizardGate>
    </>
  )
}

function Stage({ index, title, done, children }: {
  index: number
  title: string
  done: boolean
  children: React.ReactNode
}) {
  return (
    <li className="grid grid-cols-[24px_minmax(0,1fr)] gap-x-2.5 gap-y-2">
      <span
        className={cn(
          'flex h-6 w-6 items-center justify-center rounded-full font-mono text-[11px]',
          done ? 'bg-brand text-on-brand' : 'bg-sunken text-ink-secondary',
        )}
      >
        {index}
      </span>
      <span className="self-center text-[13px] font-medium">{title}</span>
      <span className="col-start-2 grid gap-2">{children}</span>
    </li>
  )
}

function formatBytes(bytes: number) {
  const giga = bytes / 1_000_000_000
  return giga >= 1
    ? `${giga.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
    : `${Math.round(bytes / 1_000_000)} Mo`
}

function formatDay(iso: string | null) {
  if (!iso) return 'date inconnue'
  return new Date(iso).toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' })
}

function formatMoment(iso: string) {
  const moment = new Date(iso)
  return `${moment.toLocaleDateString('fr-FR', { day: 'numeric', month: 'long' })} à ${moment.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`
}
