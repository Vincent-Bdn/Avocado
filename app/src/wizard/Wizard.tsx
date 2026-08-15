import { useEffect, useState, type ReactNode } from 'react'
import { Check, Clock, FolderSync, Lock, ShieldCheck, Usb } from 'lucide-react'
import { ApiError, post } from '../api.js'
import type { VaultCreated, VaultPrepared, VaultStatus } from '../api.js'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'
import { StepRecovery } from './StepRecovery.js'
import { StepVault } from './StepVault.js'
import { Point, Points, WizardFootnote, WizardGate, WizardLead, WizardScroll, WizardTitle } from './shared.js'

const steps = ['Bienvenue', 'Coffre', 'Clé de récupération', 'Terminé'] as const

/** Shaped exactly like the body POST /api/backups/destinations takes, so it goes straight across. */
interface Destination {
  kind: 'volume' | 'folder'
  label: string
  path: string
}

/**
 * First run. Full-screen, no rail, no status bar: these screens are read once and carefully, so the
 * type is larger than anywhere else in the application and the column is centred rather than dense.
 */
export function Wizard({ status, onReady }: { status: VaultStatus; onReady: () => void }) {
  const [step, setStep] = useState(0)
  const [directory, setDirectory] = useState(status.suggestedDirectory)
  const [prepared, setPrepared] = useState<VaultPrepared | null>(null)
  const [created, setCreated] = useState<VaultCreated | null>(null)

  /**
   * Going back from the recovery step throws the generated keys away. Nothing was ever written, so
   * there is no folder to delete and no half-made vault to trip over on the next attempt.
   */
  async function stepBackFromRecovery() {
    await post('/api/vault/discard', {})
    setPrepared(null)
    setStep(1)
  }

  return (
    <div className="grid h-full grid-rows-[56px_minmax(0,1fr)] bg-app">
      <header className="flex items-center gap-3 border-b border-line-subtle bg-panel px-7">
        <img src="./icon.png" alt="" className="h-6 w-6 rounded-md" />
        <span className="flex-1 type-title">Avocado</span>

        <ol className="m-0 flex list-none items-center gap-2 p-0 text-[11.5px]">
          {steps.map((label, index) => (
            <li key={label} className="flex items-center gap-2">
              {/* The 18px rule between steps is what makes the row read as a sequence. */}
              {index > 0 && <span aria-hidden="true" className="h-px w-[18px] bg-line" />}

              <span
                className={cn(
                  'flex items-center gap-1.5 leading-4',
                  index < step && 'text-ink-secondary',
                  index === step && 'font-medium text-ink',
                  index > step && 'text-muted',
                )}
              >
                {index < step ? (
                  <Check size={12} strokeWidth={3} className="text-brand" />
                ) : (
                  <span
                    className={cn(
                      'h-[7px] w-[7px] rounded-full',
                      index === step ? 'bg-brand' : 'border-[1.5px] border-[#c0c6bb]',
                    )}
                  />
                )}
                {label}
              </span>
            </li>
          ))}
        </ol>
      </header>

      <main className="grid grid-rows-[minmax(0,1fr)_auto] overflow-hidden">
        {step === 0 && <StepWelcome onContinue={() => setStep(1)} />}

        {step === 1 && (
          <StepVault
            suggested={directory}
            onBack={() => setStep(0)}
            onPrepared={(chosen, vault) => {
              setDirectory(chosen)
              setPrepared(vault)
              setStep(2)
            }}
          />
        )}

        {step === 2 && prepared && (
          <StepRecovery
            recoveryCode={prepared.recoveryCode}
            onBack={() => void stepBackFromRecovery()}
            onContinue={() => setStep(3)}
          />
        )}

        {step === 3 && prepared && (
          <StepDone
            directory={directory}
            created={created}
            onCommit={async () => setCreated(await post<VaultCreated>('/api/vault/commit', {}))}
            onFinish={onReady}
          />
        )}
      </main>
    </div>
  )
}

function StepWelcome({ onContinue }: { onContinue: () => void }) {
  return (
    <>
      <WizardScroll>
        <WizardTitle>Bonjour, et bienvenue dans Avocado.</WizardTitle>

        <WizardLead>
          Trois minutes de réglages, puis vous n’entendrez plus parler de tout ceci. Deux choses
          méritent votre attention : <strong>où vivront vos dossiers</strong>, et{' '}
          <strong>comment les retrouver si cet ordinateur disparaît</strong>.
        </WizardLead>

        <Points>
          <Point icon={<Lock size={16} strokeWidth={1.75} />} title="Tout reste sur votre ordinateur, chiffré">
            Aucun serveur, aucun compte, aucune synchronisation. Le secret professionnel n’a rien à
            négocier avec un hébergeur.
          </Point>

          <Point icon={<Clock size={16} strokeWidth={1.75} />} title="Aucun mot de passe à retenir au quotidien">
            La clé est gardée par votre système et liée à cette machine et à votre session. Vous
            ouvrez l’application, elle s’ouvre.
          </Point>

          <Point
            icon={<ShieldCheck size={16} strokeWidth={1.75} />}
            title="Une clé de récupération, à mettre à l’abri une bonne fois"
          >
            C’est elle qui rendra vos sauvegardes lisibles sur une autre machine. Nous y viendrons à
            la troisième étape.
          </Point>
        </Points>

        <WizardFootnote>Version 1.0 · logiciel libre · aucune donnée ne quitte ce poste</WizardFootnote>
      </WizardScroll>

      <WizardGate>
        <span className="flex-1" />
        <Button size="lg" onClick={onContinue}>Commencer</Button>
      </WizardGate>
    </>
  )
}

/**
 * Not a congratulation: a short recap, then the last action. The vault is written here and only here,
 * so everything before this could be abandoned without leaving anything behind.
 */
function StepDone({ directory, created, onCommit, onFinish }: {
  directory: string
  created: VaultCreated | null
  onCommit: () => Promise<void>
  onFinish: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [drives, setDrives] = useState<{ path: string; label: string; freeBytes: number }[]>([])
  const [choice, setChoice] = useState<Destination | null>(null)
  const [sameMachine, setSameMachine] = useState<string | null>(null)

  // The same bridge the recovery step uses. Not /api/backups/volumes: that route needs an unlocked
  // vault, and at this instant there is not one yet.
  useEffect(() => {
    window.avocado.removableDrives().then(setDrives).catch(() => setDrives([]))
  }, [])

  async function chooseFolder() {
    const path = await window.avocado.chooseFolder(undefined, 'Dossier de sauvegarde')
    if (path) {
      setChoice({ kind: 'folder', label: path.split(/[/\\]/).filter(Boolean).pop() ?? 'Dossier', path })
    }
  }

  async function finish(accepted = false) {
    setBusy(true)
    setError(null)
    setSameMachine(null)

    try {
      if (!created) {
        await onCommit()
      }

      // Only now: the destination is a row in the vault's own database, so it cannot exist before
      // the vault does. Then one backup runs straight away, because a destination that has never
      // been written to is a promise rather than a fact.
      if (choice) {
        try {
          await post('/api/backups/destinations', { ...choice, acceptSameMachine: accepted })
        } catch (failure) {
          // The folder never leaves this computer. Ask rather than refuse, and stop here rather than
          // opening the app with a destination silently dropped. The vault is already created, and
          // the guard above means pressing the button again will not try to create it twice.
          if (failure instanceof ApiError && failure.code === 'same-machine') {
            setSameMachine(failure.message)
            return
          }

          throw failure
        }

        await post('/api/backups/run', {})
      }

      onFinish()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <WizardScroll>
        <WizardTitle>Tout est prêt.</WizardTitle>

        <Points>
          <Point icon={<Lock size={16} strokeWidth={1.75} />} title="Le coffre sera créé et chiffré" mono>
            {directory}
          </Point>

          <Point icon={<ShieldCheck size={16} strokeWidth={1.75} />} title="Clé de récupération mise à l’abri">
            Elle seule rouvrira vos sauvegardes sur un autre ordinateur.
          </Point>
        </Points>

        <div className="mt-5 grid gap-2 border-t border-line-subtle pt-4">
          <span className="type-label text-ink-secondary">Où vos sauvegardes seront écrites</span>

          <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-ink-secondary">
            Le coffre vit sur cet ordinateur. Si celui-ci est volé, tombe en panne ou prend l’eau, il
            part avec lui : c’est une copie ailleurs, et elle seule, qui vous permettra de rouvrir vos
            dossiers. Avocado s’en chargera tout seul, mais il lui faut un endroit.
          </p>

          {drives.map((drive) => (
            <DestinationChoice
              key={drive.path}
              icon={<Usb size={15} strokeWidth={1.75} />}
              title={drive.label}
              detail={`${drive.path} · ${formatBytes(drive.freeBytes)} libres`}
              selected={choice?.path === drive.path}
              onSelect={() => setChoice({ kind: 'volume', label: drive.label, path: drive.path })}
            />
          ))}

          <DestinationChoice
            icon={<FolderSync size={15} strokeWidth={1.75} />}
            title={choice?.kind === 'folder' ? choice.label : 'Un dossier de cet ordinateur'}
            detail={
              choice?.kind === 'folder'
                ? choice.path
                : 'Y compris un dossier Google Drive, OneDrive ou Dropbox, qui l’enverra dans le nuage'
            }
            selected={choice?.kind === 'folder'}
            onSelect={() => void chooseFolder()}
          />

          <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
            Un dossier synchronisé est ici une bonne réponse, alors qu’il était refusé pour le coffre.
            Ce n’est pas une contradiction : une sauvegarde est un fichier fermé, que la
            synchronisation recopie sans risque, là où le coffre est une base ouverte en permanence
            qu’elle finirait par abîmer. Tout est chiffré dans les deux cas, et votre clé de
            récupération reste le seul moyen de le rouvrir.
          </p>
        </div>

        {sameMachine && (
          <div className="mt-3 grid gap-2 rounded-md border border-[#E8D5AE] bg-warning-bg px-3 py-2.5">
            <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-warning">{sameMachine}</p>
            <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-warning opacity-90">
              Si ce dossier est recopié ailleurs par un moyen qu’Avocado ne voit pas, c’est un choix
              valable. Sinon, préférez une clé USB ou un dossier synchronisé.
            </p>
            <div className="flex gap-1.5">
              <Button size="sm" variant="secondary" onClick={() => void chooseFolder()}>
                Choisir un autre dossier
              </Button>
              <Button size="sm" variant="ghost" disabled={busy} onClick={() => void finish(true)}>
                Utiliser ce dossier quand même
              </Button>
            </div>
          </div>
        )}

        {error && <p className="mt-3 mb-0 text-danger">{error}</p>}
      </WizardScroll>

      <WizardGate>
        <span className="flex-1" />
        {/* Never a dead end. Réglages holds the same question, and the Accueil says so until it is
            answered, so "later" is a real choice rather than a way of losing the user. */}
        {!choice && (
          <span className="text-[11.5px] text-muted">
            Vous pourrez le choisir plus tard dans les réglages.
          </span>
        )}

        <Button size="lg" disabled={busy} onClick={() => void finish()}>
          {busy
            ? 'Création du coffre…'
            : choice
              ? 'Créer le coffre, sauvegarder et ouvrir Avocado'
              : 'Créer le coffre et ouvrir Avocado'}
        </Button>
      </WizardGate>
    </>
  )
}

/**
 * One answer to « où vos sauvegardes seront écrites ». A row rather than a bare radio: the thing
 * being chosen is a physical object, and its name, its letter and its free space are what let someone
 * recognise the key on their desk.
 */
function DestinationChoice({ icon, title, detail, selected, onSelect }: {
  icon: ReactNode
  title: string
  detail: string
  selected: boolean
  onSelect: () => void
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        'flex w-full items-center gap-2.5 rounded-md border px-3 py-2.5 text-left transition-colors',
        selected
          ? 'border-brand bg-brand-subtle'
          : 'border-line hover:border-line-strong hover:bg-hover',
      )}
    >
      <span className={selected ? 'text-brand' : 'text-ink-secondary'}>{icon}</span>

      <span className="grid min-w-0 flex-1 gap-0.5">
        <span className="truncate text-[12.5px] font-medium">{title}</span>
        <span className="truncate font-mono text-[10.5px] text-muted">{detail}</span>
      </span>

      {selected && <Check size={14} strokeWidth={2.5} className="shrink-0 text-brand" />}
    </button>
  )
}

function formatBytes(bytes: number) {
  const giga = bytes / 1_000_000_000
  return giga >= 1
    ? `${giga.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
    : `${Math.round(bytes / 1_000_000)} Mo`
}
