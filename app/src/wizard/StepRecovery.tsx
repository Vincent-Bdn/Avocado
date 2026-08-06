import { useEffect, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Check, Copy, X } from 'lucide-react'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'
import { RecoverySheet, fingerprintOf } from './RecoverySheet.js'
import { SecureKeyOptions, isSecured, nothingSecured, type SecuredBy } from './SecureKeyOptions.js'
import { WizardGate, WizardLead, WizardScroll, WizardTitle } from './shared.js'

/**
 * The hardest screen in the application, and the only one that cannot be dismissed.
 *
 * The framing is deliberate: not « en cas d'oubli de votre mot de passe », but *this key is what makes
 * your backups readable*. A backup encrypted with a key that only ever existed on a drowned laptop is
 * a useless file, and a lawyer understands that immediately.
 *
 * Tone: no red, no warning triangle, no exclamation mark, never the word « attention ». The only
 * ochre on the screen is the « ce que nous vous déconseillons » block. An alarming screen gets
 * crossed faster, not slower.
 */
export function StepRecovery({ recoveryCode, onBack, onContinue }: {
  recoveryCode: string
  onBack: () => void
  onContinue: () => void
}) {
  const [fingerprint, setFingerprint] = useState('')
  const [secured, setSecured] = useState<SecuredBy>(nothingSecured)
  const [acknowledged, setAcknowledged] = useState(false)

  const createdOn = new Date().toLocaleDateString('fr-FR')

  useEffect(() => {
    void fingerprintOf(recoveryCode).then(setFingerprint)
  }, [recoveryCode])

  const done = isSecured(secured)

  return (
    <>
      <WizardScroll width={940}>
        {/* Wider than the other steps, because the three side notes belong beside the key, not under it. */}
        <div className="grid items-start gap-[18px] lg:grid-cols-[minmax(0,1fr)_300px]">
          <div className="min-w-0">
            <WizardTitle>Votre clé de récupération</WizardTitle>

            <WizardLead>
              Vos sauvegardes sont chiffrées avec cette clé. Sans elle, une sauvegarde n’est qu’un
              fichier illisible : c’est elle, et elle seule, qui vous permettra de rouvrir vos
              dossiers sur un autre ordinateur.{' '}
              <strong>Personne d’autre n’en possède de copie</strong>, ni nous, ni votre système, ni
              un service d’assistance.
            </WizardLead>

            <RecoveryKeyCard
              recoveryCode={recoveryCode}
              createdOn={createdOn}
              onCopied={() => setSecured((current) => ({ ...current, copied: true }))}
            />

            <div className="mt-3.5 text-[12px] leading-[17px] font-medium text-ink-secondary">
              Choisissez au moins une façon de la mettre à l’abri :
            </div>

            <SecureKeyOptions
              recoveryCode={recoveryCode}
              fingerprint={fingerprint}
              createdOn={createdOn}
              secured={secured}
              onSecured={setSecured}
            />
          </div>

          <aside className="grid gap-3">
            <NoteCard title="Ce que cette clé fait, et ne fait pas">
              <NoteLine kind="yes">
                Elle rouvre vos sauvegardes sur un ordinateur neuf, après un vol, une panne ou un
                dégât des eaux.
              </NoteLine>
              <NoteLine kind="no">
                Elle ne vous sera pas demandée au quotidien : sur cette machine, l’application s’ouvre
                seule.
              </NoteLine>
              <NoteLine kind="no">
                Ce n’est pas un mot de passe oublié qu’on peut réinitialiser : il n’existe aucune
                autre copie.
              </NoteLine>
            </NoteCard>

            <NoteCard title="Si vous la perdez">
              <p className="m-0 text-[11.5px] leading-[18px] text-ink-secondary">
                Tant que cette application s’ouvre encore, vous pouvez en éditer une nouvelle en deux
                clics depuis les réglages. C’est perdre{' '}
                <strong className="font-medium text-ink">la clé et la machine en même temps</strong>{' '}
                qui est sans retour.
              </p>
            </NoteCard>

            {/* The single note of alarm on the screen. */}
            <NoteCard title="Ce que nous vous déconseillons" caution>
              <p className="m-0 text-[11.5px] leading-[18px]">
                Un fichier <span className="font-mono">.txt</span> sur le bureau, ou un courriel à
                soi-même : ils disparaissent avec l’ordinateur, précisément le jour où la clé
                servirait.
              </p>
            </NoteCard>
          </aside>
        </div>
      </WizardScroll>

      <WizardGate>
        {/* Ticking it before securing the key would be a claim about something that has not happened. */}
        <label
          className={cn(
            'flex items-center gap-2 text-[13px]',
            done ? 'text-ink' : 'cursor-not-allowed text-muted',
          )}
        >
          <input
            type="checkbox"
            checked={acknowledged}
            disabled={!done}
            onChange={(event) => setAcknowledged(event.target.checked)}
          />
          J’ai mis cette clé à l’abri, hors de cet ordinateur.
        </label>

        <span className="flex-1" />

        <Button variant="secondary" size="lg" onClick={onBack}>Retour</Button>
        <Button size="lg" disabled={!done || !acknowledged} onClick={onContinue}>Continuer</Button>
      </WizardGate>

      {/*
        Portalled to the body: the print stylesheet hides #root, and a sheet rendered inside the
        wizard would be hidden along with it. That is exactly why printing produced a blank page.
      */}
      {createPortal(
        <RecoverySheet recoveryCode={recoveryCode} fingerprint={fingerprint} createdOn={createdOn} />,
        document.body,
      )}
    </>
  )
}

function NoteCard({ title, caution, children }: {
  title: string
  caution?: boolean
  children: ReactNode
}) {
  return (
    <section
      className={cn(
        'grid gap-1.5 rounded-md border px-3.5 py-3',
        caution ? 'border-accent bg-accent-subtle text-warning' : 'border-line-subtle bg-panel',
      )}
    >
      <h3 className="m-0 text-[12px] font-semibold">{title}</h3>
      {children}
    </section>
  )
}

/** Check or cross, never colour alone: this list is read as two columns of yes and no. */
function NoteLine({ kind, children }: { kind: 'yes' | 'no'; children: ReactNode }) {
  return (
    <p className="m-0 grid grid-cols-[14px_1fr] items-start gap-1.5 text-[11.5px] leading-[18px] text-ink-secondary">
      {kind === 'yes' ? (
        <Check size={12} strokeWidth={2.5} className="mt-[3px] text-success" />
      ) : (
        <X size={12} strokeWidth={2.5} className="mt-[3px] text-muted" />
      )}
      <span>{children}</span>
    </p>
  )
}

/** The key itself. Shared with Réglages, where the same card shows the current key. */
export function RecoveryKeyCard({ recoveryCode, createdOn, onCopied }: {
  recoveryCode: string
  createdOn: string
  onCopied?: () => void
}) {
  const [copied, setCopied] = useState(false)
  const groups = recoveryCode.split('-')

  function copy() {
    // One group per line, as it reads on screen and on the printed sheet. The parser ignores
    // whitespace, so pasting it back into the unlock field works either way.
    void navigator.clipboard.writeText(groups.join('\n'))
    setCopied(true)
    onCopied?.()
    setTimeout(() => setCopied(false), 2500)
  }

  return (
    <div className="mt-5 rounded-md border border-line-strong bg-panel px-5 py-[18px]">
      <div className="mb-3 flex items-baseline gap-2">
        <span className="font-mono text-[10px] leading-[13px] tracking-[0.05em] uppercase text-muted">
          Clé du coffre · {createdOn}
        </span>
        <span className="flex-1" />
        <span className="text-[11px] leading-4 text-muted">54 caractères, sans I, L, O ni U</span>
      </div>

      <div className="grid grid-cols-3 gap-2">
        {groups.map((group) => (
          <span
            key={group}
            className="rounded-sm border border-line-subtle bg-sunken py-2 text-center font-mono text-[17px] tracking-[0.09em]"
          >
            {group}
          </span>
        ))}
      </div>

      <div className="mt-3 flex items-center gap-2.5 text-[11.5px] leading-4">
        <span className="min-w-0 flex-1 text-muted">
          Neuf groupes de six, lisibles à voix haute et recopiables à la main.
        </span>

        <Button variant="secondary" size="sm" onClick={copy}>
          {copied ? <Check size={12} strokeWidth={2.5} /> : <Copy size={12} strokeWidth={1.75} />}
          {copied ? 'Copiée' : 'Copier'}
        </Button>
      </div>
    </div>
  )
}
