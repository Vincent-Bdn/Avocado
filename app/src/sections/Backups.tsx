import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, Check, FolderOpen, HardDrive, Loader2, Plus, Trash2, Usb } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Input } from '../components/ui/input.js'
import { Unavailable } from '../components/ui/unavailable.js'
import { Select } from '../components/ui/select.js'
import { cn } from '../lib/utils.js'

/**
 * Réglages → Sauvegarde.
 *
 * The few explanations kept here are the ones that change what she does: what a backup holds, why a
 * JSON file appears on her USB key, why the vault must not live in Google Drive while its backups
 * happily can. Everything else was cut.
 */

interface Destination {
  id: string
  kind: string
  label: string
  path: string | null
  isEnabled: boolean
  status: 'Ready' | 'Absent' | 'Unreachable' | 'Denied'
  reach: 'OffMachine' | 'SameMachine' | 'InsideVault'
  reachDetail: string
  location: string | null
  lastBackupAt: string | null
  lastError: string | null
  keepNewest: number
  keepDailyForDays: number
}

interface Exposure {
  activities: number
  documents: number
  timeEntries: number
  minutes: number
}

/** A dossier whose folder was not on this machine when the copy ran. */
interface DetachableFolder {
  matterId: string
  dossier: string
  folder: string
}

interface CapturedProblem {
  dossier: string
  path: string
  reason: string
}

interface CapturedDocuments {
  isEnabled: boolean
  hour: number
  files: number
  bytes: number
  dossiers: number
  capturedAt: string | null
  unreadable: number
  unreachable: number
  examples: CapturedProblem[]
  detachable: DetachableFolder[]
  running: { dossier: number; dossiers: number; files: number; reference: string | null } | null
}

interface Status {
  exposedSince: string | null
  localSnapshotAt: string | null
  localSnapshotCount: number
  hasDestination: boolean
  hasOffMachineDestination: boolean
  anyReady: boolean
  exposure: Exposure
  documents: CapturedDocuments
  destinations: Destination[]
}

interface Volume {
  path: string
  label: string
  alreadyPrepared: boolean
  freeBytes: number
}

export function Backups() {
  const [status, setStatus] = useState<Status | null>(null)
  const [volumes, setVolumes] = useState<Volume[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<{ body: object; detail: string } | null>(null)
  // The volume she is about to add, while she says where on it to write.
  const [picking, setPicking] = useState<Volume | null>(null)

  const reload = useCallback(() => {
    api<Status>('/api/backups')
      .then((fetched) => { setStatus(fetched); setError(null) })
      // A backup screen that renders nothing when it cannot load is the one screen that must never
      // do that: « rien ne s'affiche » reads as « rien à sauvegarder ».
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
    api<Volume[]>('/api/backups/volumes').then(setVolumes).catch(() => setVolumes([]))
  }, [])

  useEffect(reload, [reload])

  // A key plugged in while this screen is open should appear on it. Thirty seconds is the same beat
  // the backup service runs on, so the screen and the scheduler agree about what is connected.
  useEffect(() => {
    const timer = setInterval(reload, 30_000)
    return () => clearInterval(timer)
  }, [reload])

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      reload()
    } catch (failure: unknown) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  const addFolder = () =>
    run(async () => {
      const path = await window.avocado.chooseFolder(undefined, 'Dossier de sauvegarde')
      if (!path) return

      await post('/api/backups/destinations', {
        kind: 'folder',
        label: path.split(/[/\\]/).filter(Boolean).pop() ?? 'Dossier',
        path,
      })
    })

  const addVolume = (volume: Volume, subpath: string) =>
    run(async () => {
      await post('/api/backups/destinations', {
        kind: 'volume',
        label: volume.label,
        path: volume.path,
        subpath,
      })
      setPicking(null)
    })

  if (!status) {
    return error === null ? null : <Unavailable detail={error} onRetry={reload} />
  }

  const unconfigured = volumes.filter(
    (volume) => !status.destinations.some((destination) => destination.location === volume.path),
  )

  return (
    <>
      <Headline status={status} />

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      <div className="grid gap-1.5">
        {status.destinations.map((destination) => (
          <DestinationRow
            key={destination.id}
            destination={destination}
            onRemove={() =>
              run(() => api(`/api/backups/destinations/${destination.id}`, { method: 'DELETE' }))
            }
          />
        ))}

        {status.destinations.length === 0 && (
          <p className="m-0 max-w-[72ch] rounded-sm bg-sunken px-2.5 py-2 text-[11.5px] leading-[17px] text-muted">
            Aucune destination : tout n’existe qu’ici, sur cet ordinateur.
          </p>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-1.5">
        <Button variant="secondary" size="sm" onClick={addFolder} disabled={busy}>
          <Plus size={13} strokeWidth={2} />
          Ajouter un dossier
        </Button>

        {unconfigured.map((volume) => (
          <Button
            key={volume.path}
            variant="secondary"
            size="sm"
            onClick={() => setPicking(volume)}
            disabled={busy}
          >
            <Usb size={13} strokeWidth={2} />
            Utiliser « {volume.label} »
          </Button>
        ))}

        {status.hasDestination && (
          <Button
            size="sm"
            onClick={() => run(() => post('/api/backups/run', {}))}
            disabled={busy || !status.anyReady}
          >
            {busy ? <Loader2 size={13} className="animate-spin" /> : <Check size={13} strokeWidth={2.5} />}
            Sauvegarder maintenant
          </Button>
        )}
      </div>

      {picking !== null && (
        <WhereOnIt
          volume={picking}
          busy={busy}
          onCancel={() => setPicking(null)}
          onConfirm={(subpath) => addVolume(picking, subpath)}
        />
      )}

      <Documents documents={status.documents} busy={busy} onChange={reload} />

      <Explainers localCount={status.localSnapshotCount} />

      {pending && (
        <SameMachineWarning
          detail={pending.detail}
          onCancel={() => setPending(null)}
          onAccept={() => {
            const { body } = pending
            setPending(null)
            void run(() => post('/api/backups/destinations', { ...body, acceptSameMachine: true }))
          }}
        />
      )}
    </>
  )
}

/**
 * Where on the device to write, asked once, when she adds it.
 *
 * <p>A USB key kept for this is fine at its root. A cabinet's NAS is not: it is shared, it holds
 * years of archives, and dropping an <c>avocado/</c> folder and a marker file beside them is untidy
 * at best and alarming to whoever else opens that share. Avocado writes nowhere else, but nobody can
 * see that from the outside.</p>
 */
function WhereOnIt({ volume, busy, onCancel, onConfirm }: {
  volume: Volume
  busy: boolean
  onCancel: () => void
  onConfirm: (subpath: string) => void
}) {
  const [subpath, setSubpath] = useState(volume.alreadyPrepared ? '' : 'Sauvegardes/Avocado')

  return (
    <div className="grid gap-2 rounded-md border border-line-strong bg-panel px-3 py-2.5">
      <span className="text-[12.5px] font-medium">Où écrire sur « {volume.label} »</span>

      <label className="flex flex-wrap items-center gap-2 text-[12.5px] text-muted">
        Sous-dossier
        <Input
          inputSize="sm"
          className="w-[260px] font-mono"
          value={subpath}
          placeholder="à la racine du support"
          aria-label="Sous-dossier de sauvegarde"
          onChange={(event) => setSubpath(event.target.value)}
        />
      </label>

      <p className="m-0 max-w-[72ch] text-[11px] leading-[16px] text-muted">
        Laissez vide pour écrire à la racine, ce qui va bien pour une clé USB réservée à cela. Sur un
        NAS partagé, donnez un sous-dossier : Avocado n’écrit que dedans et ne touche à rien d’autre.
      </p>

      <div className="flex gap-1.5">
        <Button size="sm" disabled={busy} onClick={() => onConfirm(subpath)}>
          Utiliser ce support
        </Button>
        <Button size="sm" variant="ghost" disabled={busy} onClick={onCancel}>
          Annuler
        </Button>
      </div>
    </div>
  )
}

/**
 * The override. Not a confirmation dialog in the « êtes-vous sûr » sense: it says what the folder
 * does and does not protect against, and then gets out of the way, because someone syncing that
 * folder by means we cannot see is right and we are not.
 */
function SameMachineWarning({ detail, onCancel, onAccept }: {
  detail: string
  onCancel: () => void
  onAccept: () => void
}) {
  return (
    <div className="grid gap-2 rounded-md border border-[#E8D5AE] bg-warning-bg px-3 py-2.5">
      <div className="flex items-center gap-1.5 text-[12.5px] font-medium text-warning">
        <AlertCircle size={14} strokeWidth={2} />
        Ce dossier ne quitte pas cet ordinateur
      </div>

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-warning">
        {detail}
      </p>

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-warning opacity-90">
        Si vous le copiez ailleurs par un autre moyen, c'est un choix valable. Sinon, préférez une
        clé USB ou un dossier synchronisé.
      </p>

      <div className="flex gap-1.5">
        <Button size="sm" variant="secondary" onClick={onCancel}>
          Choisir autre chose
        </Button>
        <Button size="sm" variant="ghost" onClick={onAccept}>
          Utiliser ce dossier quand même
        </Button>
      </div>
    </div>
  )
}

/**
 * « Si cet ordinateur disparaissait maintenant, que perdrais-je ? », answered in work rather than in
 * dates. The design's own copy: state a fact, then quantify it, because a date on its own gets read
 * past and a number does not.
 */
function Headline({ status }: { status: Status }) {
  const { exposure } = status
  const nothingChanged =
    exposure.activities === 0 && exposure.documents === 0 && exposure.timeEntries === 0

  // Three states, not two. The middle one is the honest answer for a folder on this computer: a copy
  // exists, and whether anything carries it away is not something Avocado can see. Claiming safety
  // there is the bug that was reported; claiming loss would be just as wrong, since the person may
  // well have a sync tool of their own.
  //
  // Note this keys off a copy having been *written*, not off a destination being configured. A
  // destination nothing has ever been sent to is a promise, and saying « vous ne perdriez rien »
  // because one exists is how the screen managed to contradict itself in consecutive lines.
  const safe = status.exposedSince !== null && nothingChanged
  const onlyHere =
    !safe &&
    status.destinations.some(
      (destination) => destination.reach !== 'OffMachine' && destination.lastBackupAt !== null,
    )

  const tone = safe ? 'success' : 'warning'

  return (
    <div
      className={cn(
        'grid gap-1 rounded-sm border px-2.5 py-2',
        tone === 'success'
          ? 'border-[#BFD3C5] bg-success-bg text-success'
          : 'border-[#E8D5AE] bg-warning-bg text-warning',
      )}
    >
      <div className="type-group opacity-80">Si cet ordinateur disparaissait maintenant</div>

      <div className="text-[13px] leading-[19px] font-medium">
        {safe
          ? 'Vous ne perdriez rien : tout votre travail existe ailleurs.'
          : onlyHere
            ? 'Impossible à dire : vos sauvegardes sont sur cet ordinateur.'
            : nothingChanged
              ? 'Rien : le coffre est vide. Choisissez une destination.'
              : describeLoss(exposure)}
      </div>

      {onlyHere && (
        <div className="text-[11.5px] leading-[17px] opacity-90">
          Ajoutez une clé USB ou un dossier synchronisé.
        </div>
      )}

      <div className="font-mono text-[11px] tnum opacity-80">
        {status.exposedSince
          ? `Dernière copie hors de cet ordinateur : ${formatMoment(status.exposedSince)}`
          : status.hasOffMachineDestination
            ? 'Destination configurée, aucune copie envoyée.'
            : 'Aucune copie n’est sortie de cet ordinateur.'}
      </div>
    </div>
  )
}

function describeLoss(exposure: Exposure) {
  const pieces: string[] = []

  if (exposure.activities > 0) {
    pieces.push(`${exposure.activities} entrée${plural(exposure.activities)} de journal`)
  }
  if (exposure.documents > 0) {
    pieces.push(`${exposure.documents} document${plural(exposure.documents)}`)
  }
  if (exposure.minutes > 0) {
    pieces.push(`${formatDuration(exposure.minutes)} de temps saisi`)
  }

  if (pieces.length === 0) {
    return 'Vous perdriez tout ce que contient ce coffre.'
  }

  const last = pieces.pop()!
  return `Vous perdriez ${pieces.length > 0 ? `${pieces.join(', ')} et ${last}` : last}.`
}

function DestinationRow({
  destination,
  onRemove,
}: {
  destination: Destination
  onRemove: () => void
}) {
  const connected = destination.status === 'Ready'
  const removable = destination.kind === 'volume'

  return (
    <div className="grid gap-1 rounded-sm border border-line-subtle bg-panel px-2.5 py-2">
      <div className="flex items-center gap-2">
        {removable ? (
          <Usb size={14} strokeWidth={2} className={connected ? 'text-success' : 'text-muted'} />
        ) : (
          <HardDrive size={14} strokeWidth={2} className={connected ? 'text-success' : 'text-muted'} />
        )}

        <span className="text-[12.5px] font-medium">{destination.label}</span>

        <span
          className={cn(
            'rounded-full px-1.5 py-px font-mono text-[10px] leading-3',
            connected ? 'bg-success-bg text-success' : 'bg-sunken text-muted',
          )}
        >
          {connected ? 'connectée' : statusLabel[destination.status]}
        </span>

        {/* The distinction that was invisible, and that made the headline lie. */}
        {destination.reach !== 'OffMachine' && (
          <span
            className="rounded-full bg-warning-bg px-1.5 py-px font-mono text-[10px] leading-3 text-warning"
            title={destination.reachDetail}
          >
            sur cet ordinateur
          </span>
        )}

        <button
          type="button"
          onClick={onRemove}
          title="Retirer cette destination"
          className="ml-auto text-muted hover:text-danger"
        >
          <Trash2 size={13} strokeWidth={2} />
        </button>
      </div>

      <div className="font-mono text-[10.5px] text-muted tnum">
        {destination.location ?? destination.path ?? 'emplacement inconnu'}
        {destination.lastBackupAt
          ? ` · sauvegardé ${formatMoment(destination.lastBackupAt)}`
          : ' · jamais sauvegardé'}
      </div>

      {destination.lastError && (
        <div className="flex items-start gap-1.5 text-[11px] leading-[16px] text-warning">
          <AlertCircle size={12} strokeWidth={2} className="mt-px shrink-0" />
          {destination.lastError}
        </div>
      )}
    </div>
  )
}

const statusLabel: Record<Destination['status'], string> = {
  Ready: 'connectée',
  Absent: 'débranchée',
  Unreachable: 'injoignable',
  Denied: 'écriture refusée',
}

/** The few things worth knowing here, read once au moment de la mise en place. */
function Explainers({ localCount }: { localCount: number }) {
  return (
    <div className="grid gap-3 border-t border-line-subtle pt-3">
      <Explain title="Ce qu’une sauvegarde contient">
        Journal, tiers, facturation, temps passé, modèles et la copie de votre clé, chiffrés :
        personne ne les ouvre sans votre clé de récupération. Vos documents, eux, sont copiés tels
        quels, dans un dossier « Dossiers » que vous pouvez ouvrir et lire directement.
      </Explain>

      <Explain title="Les copies locales">
        {localCount > 0 ? `${localCount} copies datées` : 'Des copies datées'} de votre base restent
        dans le coffre, pour revenir en arrière. Elles disparaissent avec le disque : c’est à cela
        que servent les destinations ci-dessus.
      </Explain>

      <Explain title="Le fichier sur votre clé USB">
        Avocado y dépose <code className="font-mono text-[11px]">.avocado-sink.json</code> : un
        identifiant et un nom, aucune donnée du cabinet. Une clé change de lettre selon ce qui est
        branché ; sans ce repère, Avocado écrirait un jour sur la clé d’un client. Vous pouvez le
        supprimer, le support ne sera plus reconnu.
      </Explain>

      <Explain title="Google Drive, Dropbox, OneDrive">
        Avocado écrit dans le dossier que leur logiciel synchronise sur cet ordinateur : rien à
        autoriser, aucun mot de passe. Installez-le, créez-y un dossier, puis « Ajouter un dossier »
        ici. Ils hébergent des fichiers qu’ils ne peuvent pas ouvrir, d’où l’intérêt de garder votre
        clé de récupération ailleurs.
      </Explain>

      <Explain title="À quel rythme">
        À l’ouverture, pendant que vous travaillez, à la fermeture, et dès qu’une destination
        réapparaît. Seul ce qui a changé est envoyé.
      </Explain>
    </div>
  )
}

/**
 * Vos documents, in the Sauvegarde screen.
 *
 * <p>Three numbers and an hour. What she needs to know is whether her files are in the copy and when
 * they were last looked at, and the one thing that must never be quiet is the count of what could not
 * be read: a document the sauvegarde does not hold is a fact she is entitled to learn on a Tuesday
 * rather than on the day she needs it.</p>
 */
function Documents({ documents, busy, onChange }: {
  documents: CapturedDocuments
  busy: boolean
  onChange: () => void
}) {
  const [saving, setSaving] = useState(false)
  const [restored, setRestored] = useState<string | null>(null)

  const save = async (next: { isEnabled: boolean; hour: number }) => {
    setSaving(true)
    try {
      await api('/api/backups/documents/schedule', { method: 'PUT', body: JSON.stringify(next) })
      onChange()
    } finally {
      setSaving(false)
    }
  }

  /**
   * The way back, outside a restore. It is the same operation the wizard runs, offered here because
   * the wizard lets her postpone it: no disk plugged in that morning, no room on the laptop, or
   * simply one thing at a time. It never overwrites, so offering it costs nothing.
   */
  async function putBack() {
    const ongoing = await window.avocado.chooseFolder(undefined, 'Dossiers en cours')
    if (!ongoing) return

    const closed = await window.avocado.chooseFolder(ongoing, 'Dossiers clôturés, le même convient')
    if (!closed) return

    setSaving(true)
    setRestored(null)
    try {
      const outcome = await post<{ files: number; dossiers: unknown[] }>(
        '/api/backups/documents/restore',
        { ongoing, closed },
      )

      setRestored(
        `${outcome.files.toLocaleString('fr-FR')} fichier${plural(outcome.files)} rendu${plural(outcome.files)} à ${outcome.dossiers.length} dossier${plural(outcome.dossiers.length)}.`,
      )
      onChange()
    } finally {
      setSaving(false)
    }
  }

  /**
   * Forgets where those dossiers' documents were, on her say-so.
   *
   * <p>Nothing is removed from any destination: the copy up there is usually the only one left, and
   * is the reason she cleared the folder off the laptop in the first place.</p>
   */
  async function detach(matterIds: string[]) {
    setSaving(true)
    try {
      await post('/api/backups/documents/detach', { matterIds })
      onChange()
    } finally {
      setSaving(false)
    }
  }

  const problems = documents.unreadable + documents.unreachable

  return (
    <div className="grid gap-2 border-t border-line-subtle pt-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <span className="type-label text-ink-secondary">Vos documents</span>

        <label className="flex items-center gap-1.5 text-[11.5px] text-muted">
          <input
            type="checkbox"
            checked={documents.isEnabled}
            disabled={busy || saving}
            onChange={(event) => void save({ isEnabled: event.target.checked, hour: documents.hour })}
          />
          Copier chaque nuit à
          <Select
            value={documents.hour}
            disabled={busy || saving || !documents.isEnabled}
            onChange={(event) =>
              void save({ isEnabled: documents.isEnabled, hour: Number(event.target.value) })
            }
          >
            {Array.from({ length: 24 }, (_, hour) => (
              <option key={hour} value={hour}>
                {String(hour).padStart(2, '0')} h
              </option>
            ))}
          </Select>
        </label>
      </div>

      {documents.running ? (
        <p className="m-0 flex max-w-[72ch] items-center gap-1.5 text-[11.5px] leading-[17px] text-muted">
          <Loader2 size={13} className="animate-spin" />
          Copie en cours, dossier {documents.running.dossier} sur {documents.running.dossiers}
          {documents.running.reference ? ` (${documents.running.reference})` : ''}.
        </p>
      ) : (
      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        {documents.files === 0 ? (
          documents.dossiers === 0
            ? 'Aucun dossier n’indique où sont ses documents : rien à copier.'
            : 'Pas encore copiés. Ce sera fait cette nuit, ou maintenant avec « Sauvegarder maintenant ».'
        ) : (
          <>
            {documents.files.toLocaleString('fr-FR')} fichier{plural(documents.files)} de{' '}
            {documents.dossiers.toLocaleString('fr-FR')} dossier{plural(documents.dossiers)}, soit{' '}
            {formatBytes(documents.bytes)} avant compression
            {documents.capturedAt ? `, lus ${formatMoment(documents.capturedAt)}` : ''}.
          </>
        )}
      </p>
      )}

      {documents.files > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" disabled={busy || saving} onClick={() => void putBack()}>
            <FolderOpen size={13} strokeWidth={2} />
            Remettre les documents sur ce disque
          </Button>
          {restored && <span className="text-[11.5px] text-muted">{restored}</span>}
        </div>
      )}

      {documents.detachable.length > 0 && (
        <div className="grid gap-1.5 rounded-sm bg-sunken px-2.5 py-2">
          <span className="flex items-center gap-1.5 text-[11.5px] font-medium text-ink">
            <AlertCircle size={13} strokeWidth={2} className="text-warning" />
            {documents.detachable.length} dossier{plural(documents.detachable.length)} dont le
            répertoire n’est plus sur cet ordinateur
          </span>

          <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
            Leur copie sur la destination est intacte et le restera : rien n’y est jamais effacé. Si
            vous les avez archivés exprès, détachez-les pour qu’Avocado cesse de les chercher.
          </p>

          <ul className="m-0 grid list-none gap-0.5 p-0">
            {documents.detachable.map((folder) => (
              <li key={folder.matterId} className="text-[11px] text-muted">
                <span className="font-mono">{folder.dossier}</span> · {folder.folder}
              </li>
            ))}
          </ul>

          <div>
            <Button
              variant="secondary"
              size="sm"
              disabled={busy || saving}
              onClick={() => void detach(documents.detachable.map((folder) => folder.matterId))}
            >
              Détacher ces dossiers
            </Button>
          </div>
        </div>
      )}

      {problems > 0 && (
        <div className="grid gap-1 rounded-sm bg-sunken px-2.5 py-2">
          <span className="flex items-center gap-1.5 text-[11.5px] font-medium text-ink">
            <AlertCircle size={13} strokeWidth={2} className="text-warning" />
            {documents.unreachable > 0 && (
              <>
                {documents.unreachable} dossier{plural(documents.unreachable)} introuvable
                {plural(documents.unreachable)}
                {documents.unreadable > 0 ? ', ' : ' la nuit dernière.'}
              </>
            )}
            {documents.unreadable > 0 && (
              <>
                {documents.unreadable.toLocaleString('fr-FR')} fichier{plural(documents.unreadable)} illisible
                {plural(documents.unreadable)}.
              </>
            )}
          </span>

          <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
            {documents.unreachable > 0
              ? 'Disque débranché, lecteur réseau absent, répertoire déplacé. Ce qui était déjà copié est intact.'
              : 'Ces fichiers ne sont pas dans la sauvegarde. Ils seront repris cette nuit.'}
          </p>

          <ul className="m-0 grid list-none gap-0.5 p-0">
            {documents.examples.map((problem) => (
              <li key={`${problem.dossier}/${problem.path}`} className="text-[11px] text-muted">
                <span className="font-mono">{problem.dossier}</span> · {problem.path} · {problem.reason}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

function formatBytes(bytes: number) {
  if (bytes >= 1e9) return `${(bytes / 1e9).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
  if (bytes >= 1e6) return `${Math.round(bytes / 1e6).toLocaleString('fr-FR')} Mo`
  return `${Math.max(1, Math.round(bytes / 1e3)).toLocaleString('fr-FR')} Ko`
}

function Explain({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-1">
      <span className="type-label text-ink-secondary">{title}</span>
      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">{children}</p>
    </div>
  )
}

const plural = (count: number) => (count > 1 ? 's' : '')

function formatDuration(minutes: number) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60

  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${String(rest).padStart(2, '0')}`
}

function formatMoment(iso: string) {
  const moment = new Date(iso)
  const minutes = Math.round((Date.now() - moment.getTime()) / 60_000)

  if (minutes < 1) return 'à l’instant'
  if (minutes < 60) return `il y a ${minutes} min`

  const sameDay = moment.toDateString() === new Date().toDateString()
  const time = moment.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })

  return sameDay
    ? `aujourd’hui à ${time}`
    : `le ${moment.toLocaleDateString('fr-FR', { day: 'numeric', month: 'long' })} à ${time}`
}
