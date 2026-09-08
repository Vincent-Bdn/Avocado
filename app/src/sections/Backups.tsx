import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, Check, FolderOpen, HardDrive, Loader2, Plus, Trash2, Usb } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Select } from '../components/ui/select.js'
import { cn } from '../lib/utils.js'

/**
 * Réglages → Sauvegarde.
 *
 * This screen is also the documentation, because there is nowhere else. A lawyer has no reason to
 * know what a backup is made of, why a small JSON file appears on her USB key, or why the vault must
 * not live in Google Drive while its backups happily can. None of that is obvious, all of it changes
 * what she does, and a manual nobody opens is not where it belongs. So the explanations sit next to
 * the thing they explain, in the register the rest of Réglages already uses.
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

  const reload = useCallback(() => {
    api<Status>('/api/backups').then(setStatus).catch(() => setStatus(null))
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

  const addVolume = (volume: Volume) =>
    run(() =>
      post('/api/backups/destinations', {
        kind: 'volume',
        label: volume.label,
        path: volume.path,
      }),
    )

  if (!status) return null

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
            Aucune destination pour l’instant. Tout ce que vous saisissez n’existe donc qu’ici, sur
            cet ordinateur.
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
            onClick={() => addVolume(volume)}
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
        Si vous copiez ce dossier ailleurs par un autre moyen, une tâche planifiée, un disque Time
        Machine, un outil de votre cabinet, alors c'est un choix valable et Avocado n'a aucun moyen de
        le savoir. Sinon, préférez une clé USB ou un dossier synchronisé.
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
            ? 'Nous ne pouvons pas le dire : vos sauvegardes sont sur cet ordinateur.'
            : nothingChanged
              ? 'Rien pour l’instant, le coffre est vide. Choisissez une destination avant de commencer à y travailler.'
              : describeLoss(exposure)}
      </div>

      {onlyHere && (
        <div className="text-[11.5px] leading-[17px] opacity-90">
          Une copie a bien été écrite, mais dans un dossier de cette machine. Si vous la recopiez
          ailleurs par un moyen qu’Avocado ne voit pas, tout va bien. Sinon, ajoutez une clé USB ou un
          dossier synchronisé, et cette phrase deviendra une certitude.
        </div>
      )}

      <div className="font-mono text-[11px] tnum opacity-80">
        {status.exposedSince
          ? `Dernière copie hors de cet ordinateur : ${formatMoment(status.exposedSince)}`
          : status.hasOffMachineDestination
            ? 'Destination configurée, mais aucune copie ne lui a encore été envoyée.'
            : 'Aucune copie n’est jamais sortie de cet ordinateur.'}
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

/**
 * Everything a person would reasonably wonder and has no way to find out. Written as prose rather
 * than as tooltips: these are things you read once, at the moment you set this up, and never again.
 */
function Explainers({ localCount }: { localCount: number }) {
  return (
    <div className="grid gap-3 border-t border-line-subtle pt-3">
      <Explain title="Ce qu’une sauvegarde contient">
        Votre journal, vos tiers, votre facturation, votre temps passé, vos modèles, la copie chiffrée
        de votre clé, et vos documents. Elle reste chiffrée de bout en bout : personne ne peut
        l’ouvrir sans votre clé de récupération, pas même le service qui l’héberge.
      </Explain>

      <Explain title="Vos documents, alors qu’ils sont chez vous">
        Vos fichiers restent dans vos répertoires, en clair, à vous. Avocado en prend simplement une
        copie chiffrée et compressée chaque nuit, pour pouvoir vous les rendre. C’est la seule copie
        de vos dossiers que personne ne peut lire en la trouvant, ce qui n’est pas le cas d’une clé
        USB oubliée dans le métro.
      </Explain>

      <Explain title="Les copies locales, et pourquoi elles ne suffisent pas">
        Avocado garde en permanence {localCount > 0 ? `${localCount} copies datées` : 'des copies datées'} de
        votre base dans le coffre lui-même. Elles servent à revenir en arrière : une fiche modifiée
        par erreur ce matin, une manipulation malheureuse hier. Elles ne protègent de rien d’autre,
        puisqu’elles disparaissent avec le disque qui les porte. C’est à cela que servent les
        destinations ci-dessus, et à rien d’autre.
      </Explain>

      <Explain title="Le petit fichier sur votre clé USB">
        En préparant un support, Avocado y dépose un fichier nommé{' '}
        <code className="font-mono text-[11px]">.avocado-sink.json</code>. Il ne contient aucune
        donnée du cabinet : juste un identifiant et le nom que vous avez donné au support. Il existe
        parce qu’une clé USB n’a pas d’adresse fixe : elle est E:\ aujourd’hui et F:\ demain selon ce
        qui est branché. Sans ce repère, Avocado ne pourrait que faire confiance à la lettre du
        lecteur, et finirait un jour par écrire vos sauvegardes sur la clé d’un client. Vous pouvez le
        supprimer : le support cessera simplement d’être reconnu.
      </Explain>

      <Explain title="Google Drive, Dropbox, OneDrive">
        Avocado ne se connecte pas à ces services par un compte : il écrit dans le dossier que leur
        logiciel garde synchronisé sur cet ordinateur, et c’est ce logiciel qui l’envoie dans le nuage.
        Rien à autoriser, aucun mot de passe à confier, et le jour où vous changez de service, il n’y a
        qu’un dossier à redésigner.
      </Explain>

      <div className="grid gap-1">
        <span className="type-label text-ink-secondary">Mettre en place Google Drive, en trois temps</span>
        <ol className="m-0 grid list-decimal gap-1 pl-4 text-[11.5px] leading-[17px] text-muted">
          <li>
            Installez « Google Drive pour ordinateur » depuis{' '}
            <span className="font-mono text-[11px]">google.com/drive/download</span>, puis connectez-vous
            à votre compte Google. C’est un logiciel de Google, pas d’Avocado.
          </li>
          <li>
            Il ajoute un dossier « Google Drive » à cet ordinateur, souvent sous la forme d’un nouveau
            lecteur. Créez-y un dossier, « Sauvegardes Avocado » par exemple.
          </li>
          <li>
            Revenez ici, « Ajouter un dossier », et désignez-le. La première copie part aussitôt.
          </li>
        </ol>
        <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
          Pour vérifier que cela fonctionne vraiment : après la première sauvegarde, ouvrez
          drive.google.com depuis n’importe quel navigateur. Si le dossier et son contenu y sont, la
          copie a bien quitté cet ordinateur. C’est la seule preuve qui compte, et elle prend dix
          secondes. OneDrive et Dropbox se mettent en place de la même façon.
        </p>
      </div>

      <Explain title="Ce que ces services voient de vos dossiers">
        Rien. Tout ce qu’Avocado écrit est déjà chiffré avant d’être posé dans le dossier : Google,
        Microsoft ou Dropbox hébergent des fichiers qu’ils ne peuvent pas ouvrir, et personne chez eux
        ne peut lire un nom de client ni une pièce. Seule votre clé de récupération les rouvre. C’est
        aussi pourquoi cette clé doit rester ailleurs que dans ce même nuage.
      </Explain>

      <Explain title="À quel rythme">
        Automatiquement : à l’ouverture, régulièrement tant que vous travaillez, à la fermeture, et
        dès qu’une destination réapparaît. Rebrancher la clé suffit, il n’y a rien à lancer. Seul ce
        qui a changé est envoyé, donc une sauvegarde qui suit de peu la précédente prend quelques
        secondes, même avec des milliers de documents.
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
          {documents.running.reference ? ` (${documents.running.reference})` : ''}. La première fois
          prend un moment ; vous pouvez continuer à travailler.
        </p>
      ) : (
      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        {documents.files === 0 ? (
          documents.dossiers === 0
            ? 'Aucun dossier n’indique encore où vivent ses documents, il n’y a donc rien à copier.'
            : 'Vos documents n’ont pas encore été copiés. Ce sera fait à la prochaine nuit, ou tout de suite avec « Sauvegarder maintenant ».'
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
              ? 'Un disque externe débranché, un lecteur réseau absent, un répertoire déplacé. Ce qui avait déjà été copié est intact : rien n’a été effacé.'
              : 'Ces fichiers ne sont pas dans la sauvegarde. Ils seront repris à la prochaine nuit si ce qui les retenait les a relâchés.'}
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
