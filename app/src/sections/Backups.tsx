import { useCallback, useEffect, useState } from 'react'
import { AlertCircle, Check, HardDrive, Loader2, Plus, Trash2, Usb } from 'lucide-react'
import { ApiError, api, post } from '../api.js'
import { Button } from '../components/ui/button.js'
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

interface Status {
  exposedSince: string | null
  localSnapshotAt: string | null
  localSnapshotCount: number
  hasDestination: boolean
  anyReady: boolean
  exposure: Exposure
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

      <Explainers localCount={status.localSnapshotCount} />
    </>
  )
}

/**
 * « Si cet ordinateur disparaissait maintenant, que perdrais-je ? », answered in work rather than in
 * dates. The design's own copy: state a fact, then quantify it, because a date on its own gets read
 * past and a number does not.
 */
function Headline({ status }: { status: Status }) {
  const { exposure } = status
  const nothingAtRisk =
    exposure.activities === 0 && exposure.documents === 0 && exposure.timeEntries === 0

  const tone = !status.hasDestination || !nothingAtRisk ? 'warning' : 'success'

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
        {/* An empty vault is its own answer, and the design says so rather than warning about
            nothing: « Aucune sauvegarde nécessaire, le coffre est vide ». */}
        {nothingAtRisk
          ? status.hasDestination
            ? 'Vous ne perdriez rien : tout votre travail existe ailleurs.'
            : 'Rien pour l’instant, le coffre est vide. Choisissez une destination avant de commencer à y travailler.'
          : describeLoss(exposure)}
      </div>

      <div className="font-mono text-[11px] tnum opacity-80">
        {status.exposedSince
          ? `Dernière copie hors de cet ordinateur : ${formatMoment(status.exposedSince)}`
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
        Tout : la base de données, les documents, les modèles, et la copie chiffrée de votre clé. Une
        sauvegarde n’est pas un export partiel, c’est de quoi rouvrir votre cabinet entier sur un
        ordinateur neuf. Elle reste chiffrée de bout en bout : personne ne peut l’ouvrir sans votre
        clé de récupération, pas même le service qui l’héberge.
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
        Choisissez « Ajouter un dossier » et désignez le dossier que ces services synchronisent sur
        cet ordinateur. Avocado y écrit ses sauvegardes comme dans n’importe quel dossier, et leur
        logiciel les envoie dans le nuage. C’est la seule chose qu’ils aient à faire, et elle est sans
        risque : une sauvegarde est un fichier fermé, déjà chiffré.
        <br />
        <br />
        Le coffre lui-même, en revanche, ne doit jamais être placé dans un dossier synchronisé. C’est
        une base de données ouverte en permanence, et un logiciel de synchronisation qui la copie en
        pleine écriture finit par la corrompre. C’est la première cause de coffre illisible, et c’est
        pourquoi Avocado refuse cet emplacement à l’installation tout en vous le recommandant ici.
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
