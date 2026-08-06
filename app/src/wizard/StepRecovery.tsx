import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { Check, Copy, X } from 'lucide-react'
import { RecoverySheet, fingerprintOf } from './RecoverySheet.js'
import { SecureKeyOptions, isSecured, type SecuredBy } from './SecureKeyOptions.js'

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
  const [secured, setSecured] = useState<SecuredBy>({ printed: false, savedTo: null, exportedTo: null })
  const [acknowledged, setAcknowledged] = useState(false)

  const createdOn = new Date().toLocaleDateString('fr-FR')

  useEffect(() => {
    void fingerprintOf(recoveryCode).then(setFingerprint)
  }, [recoveryCode])

  const done = isSecured(secured)

  return (
    <>
      <div className="wizard-scroll">
        <div className="wizard-wide">
          <div className="wizard-main">
            <h1>Votre clé de récupération</h1>

            <p className="lead">
              Vos sauvegardes sont chiffrées avec cette clé. Sans elle, une sauvegarde n’est qu’un
              fichier illisible : c’est elle, et elle seule, qui vous permettra de rouvrir vos dossiers
              sur un autre ordinateur. <strong>Personne d’autre n’en possède de copie</strong>, ni
              nous, ni votre système, ni un service d’assistance.
            </p>

            <RecoveryKeyCard recoveryCode={recoveryCode} createdOn={createdOn} />

            <div className="secure-lead">Choisissez au moins une façon de la mettre à l’abri :</div>

            <SecureKeyOptions
              recoveryCode={recoveryCode}
              fingerprint={fingerprint}
              createdOn={createdOn}
              secured={secured}
              onSecured={setSecured}
            />
          </div>

          <aside className="wizard-aside">
            <section className="note-card">
              <h3>Ce que cette clé fait, et ne fait pas</h3>

              <p className="yes">
                <Check size={12} strokeWidth={2.5} />
                Elle rouvre vos sauvegardes sur un ordinateur neuf, après un vol, une panne ou un dégât
                des eaux.
              </p>
              <p className="no">
                <X size={12} strokeWidth={2.5} />
                Elle ne vous sera pas demandée au quotidien : sur cette machine, l’application s’ouvre
                seule.
              </p>
              <p className="no">
                <X size={12} strokeWidth={2.5} />
                Ce n’est pas un mot de passe oublié qu’on peut réinitialiser : il n’existe aucune autre
                copie.
              </p>
            </section>

            <section className="note-card">
              <h3>Si vous la perdez</h3>
              <p>
                Tant que cette application s’ouvre encore, vous pouvez en éditer une nouvelle en deux
                clics depuis les réglages. C’est perdre{' '}
                <strong>la clé et la machine en même temps</strong> qui est sans retour.
              </p>
            </section>

            <section className="note-card note-caution">
              <h3>Ce que nous vous déconseillons</h3>
              <p>
                Un fichier <span className="mono">.txt</span> sur le bureau, ou un courriel à
                soi-même : ils disparaissent avec l’ordinateur, précisément le jour où la clé
                servirait.
              </p>
            </section>
          </aside>
        </div>
      </div>

      <footer className="wizard-gate">
        <label className={`confirm ${done ? '' : 'confirm-waiting'}`}>
          <input
            type="checkbox"
            checked={acknowledged}
            disabled={!done}
            onChange={(event) => setAcknowledged(event.target.checked)}
          />
          J’ai mis cette clé à l’abri, hors de cet ordinateur.
        </label>

        <span className="grow" />

        <button type="button" className="secondary-button" onClick={onBack}>
          Retour
        </button>
        <button type="button" disabled={!done || !acknowledged} onClick={onContinue}>
          Continuer
        </button>
      </footer>

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

/** The key itself. Shared with Réglages, where the same card shows the current key. */
export function RecoveryKeyCard({ recoveryCode, createdOn }: {
  recoveryCode: string
  createdOn: string
}) {
  const [copied, setCopied] = useState(false)
  const groups = recoveryCode.split('-')

  function copy() {
    // One group per line, as it reads on screen and on the printed sheet. The parser ignores
    // whitespace, so pasting it back into the unlock field works either way.
    void navigator.clipboard.writeText(groups.join('\n'))
    setCopied(true)
    setTimeout(() => setCopied(false), 2500)
  }

  return (
    <div className="key-card">
      <div className="key-head">
        <span className="key-eyebrow mono">Clé du coffre · {createdOn}</span>
        <span className="grow" />
        <span className="key-hint">54 caractères, sans I, L, O ni U</span>
      </div>

      <div className="key-grid">
        {groups.map((group) => (
          <span key={group} className="key-group">{group}</span>
        ))}
      </div>

      <div className="key-caption">
        <span className="muted">
          Neuf groupes de six, lisibles à voix haute et recopiables à la main.
        </span>

        <button type="button" className="ghost-button" onClick={copy}>
          {copied ? <Check size={12} strokeWidth={2.5} /> : <Copy size={12} strokeWidth={1.75} />}
          {copied ? 'Copiée' : 'Copier'}
        </button>
      </div>
    </div>
  )
}
