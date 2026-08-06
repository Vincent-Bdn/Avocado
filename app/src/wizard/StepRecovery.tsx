import { useEffect, useState } from 'react'
import { RecoverySheet, fingerprintOf } from './RecoverySheet.js'

interface Drive {
  path: string
  label: string
  freeBytes: number
}

/**
 * The hardest screen in the application, and the only one that cannot be dismissed.
 *
 * The framing is deliberate: not « in case you forget your password », but *this key is what makes
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
  const [drives, setDrives] = useState<Drive[]>([])
  const [fingerprint, setFingerprint] = useState('')
  const [savedTo, setSavedTo] = useState<string | null>(null)
  const [printed, setPrinted] = useState(false)
  const [acknowledged, setAcknowledged] = useState(false)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const groups = recoveryCode.split('-')
  const createdOn = new Date().toLocaleDateString('fr-FR')

  useEffect(() => {
    void fingerprintOf(recoveryCode).then(setFingerprint)
  }, [recoveryCode])

  // The list refreshes itself: « branchez une clé USB, elle apparaîtra ici toute seule ».
  useEffect(() => {
    const poll = () => void window.avocado.removableDrives().then(setDrives).catch(() => setDrives([]))

    poll()
    const timer = setInterval(poll, 3000)
    return () => clearInterval(timer)
  }, [])

  async function saveTo(drive: Drive) {
    setError(null)

    try {
      const contents =
        `Avocado — clé de récupération\r\n` +
        `Coffre créé le ${createdOn}\r\n` +
        `Empreinte ${fingerprint}\r\n\r\n` +
        `${recoveryCode}\r\n\r\n` +
        `Sans cette clé, les sauvegardes de ce coffre ne peuvent être rouvertes par personne.\r\n`

      setSavedTo(await window.avocado.saveRecoveryKey(drive.path, contents))
    } catch (failure) {
      setError(`Écriture impossible sur ${drive.path} — ${String(failure)}`)
    }
  }

  // Genuinely disabled until something real happened *and* the box is ticked.
  const secured = printed || savedTo !== null
  const canContinue = secured && acknowledged

  return (
    <>
      <div className="wizard-wide">
        <div className="wizard-main">
          <h1>Votre clé de récupération</h1>

          <p className="lead">
            Vos sauvegardes sont chiffrées avec cette clé. Sans elle, une sauvegarde n’est qu’un
            fichier illisible : c’est elle, et elle seule, qui vous permettra de rouvrir vos dossiers
            sur un autre ordinateur. <strong>Personne d’autre n’en possède de copie</strong> — ni nous,
            ni votre système, ni un service d’assistance.
          </p>

          <div className="key-card">
            <div className="key-grid">
              {groups.map((group) => (
                <span key={group} className="key-group">{group}</span>
              ))}
            </div>

            <div className="key-caption">
              <span className="muted">
                Neuf groupes de six, lisibles à voix haute et recopiables à la main.
              </span>
              <button
                type="button"
                className="ghost-button"
                onClick={() => {
                  void navigator.clipboard.writeText(recoveryCode)
                  setCopied(true)
                }}
              >
                {copied ? 'Copié' : 'Copier'}
              </button>
            </div>
          </div>

          <h2 className="section-title">Choisissez au moins une façon de la mettre à l’abri :</h2>

          <div className="secure-options">
            <section className="option option-recommended">
              <header>
                <h3>L’imprimer</h3>
                <span className="badge-recommended">recommandé</span>
              </header>
              <p>
                Une page A4 avec un QR code, la clé en toutes lettres et une ligne pour noter où vous
                la rangez. À classer là où vous classez déjà ce qui compte.
              </p>
              <button
                type="button"
                onClick={() => {
                  setPrinted(true)
                  window.print()
                }}
              >
                Imprimer la fiche
              </button>
              {printed && <p className="done">✓ Impression lancée</p>}
            </section>

            <section className="option">
              <header>
                <h3>L’enregistrer sur une clé USB</h3>
              </header>
              <p>
                Un petit fichier texte sur un support amovible, rangé ailleurs que près de
                l’ordinateur.
              </p>

              {drives.length === 0 ? (
                <div className="no-drive">
                  <strong>Aucun support amovible détecté</strong>
                  <span className="muted">
                    Branchez une clé USB : elle apparaîtra ici toute seule, en quelques secondes.
                  </span>
                  <span className="mono muted searching">recherche en cours…</span>
                  <span className="muted footnote">
                    Les disques internes sont exclus volontairement : une clé enregistrée sur le disque
                    de cet ordinateur disparaîtrait avec lui.
                  </span>
                </div>
              ) : (
                <ul className="drives">
                  {drives.map((drive) => (
                    <li key={drive.path}>
                      <span className="mono">{drive.path}</span>
                      <span>— {drive.label}</span>
                      {drive.freeBytes > 0 && (
                        <span className="muted mono">({formatBytes(drive.freeBytes)} libres)</span>
                      )}
                      <button type="button" className="secondary-button" onClick={() => void saveTo(drive)}>
                        Enregistrer sur {drive.path}
                      </button>
                    </li>
                  ))}
                </ul>
              )}

              {savedTo && <p className="done">✓ Écrite dans {savedTo}</p>}
              {error && <p className="danger">{error}</p>}
            </section>
          </div>
        </div>

        <aside className="wizard-aside">
          <section className="note-card">
            <h3>Ce que cette clé fait, et ne fait pas</h3>
            <p className="yes">
              ✓ Elle rouvre vos sauvegardes sur un ordinateur neuf, après un vol, une panne ou un
              dégât des eaux.
            </p>
            <p className="no">
              ✕ Elle ne vous sera pas demandée au quotidien : sur cette machine, l’application s’ouvre
              seule.
            </p>
            <p className="no">
              ✕ Ce n’est pas un mot de passe oublié qu’on peut réinitialiser : il n’existe aucune autre
              copie.
            </p>
          </section>

          <section className="note-card">
            <h3>Si vous la perdez</h3>
            <p>
              Tant que cette application s’ouvre encore, vous pouvez en éditer une nouvelle en deux
              clics depuis les réglages. C’est perdre <strong>la clé et la machine en même temps</strong>{' '}
              qui est sans retour.
            </p>
          </section>

          <section className="note-card note-caution">
            <h3>Ce que nous vous déconseillons</h3>
            <p>
              Un fichier <span className="mono">.txt</span> sur le bureau, ou un courriel à soi-même :
              ils disparaissent avec l’ordinateur, précisément le jour où la clé servirait.
            </p>
          </section>
        </aside>
      </div>

      <footer className="wizard-gate">
        <label className="confirm">
          <input
            type="checkbox"
            checked={acknowledged}
            onChange={(event) => setAcknowledged(event.target.checked)}
          />
          J’ai mis cette clé à l’abri, hors de cet ordinateur.
        </label>

        <span className="grow" />

        <button type="button" className="secondary-button" onClick={onBack}>
          Retour
        </button>
        <button type="button" disabled={!canContinue} onClick={onContinue}>
          Continuer
        </button>
      </footer>

      <RecoverySheet recoveryCode={recoveryCode} fingerprint={fingerprint} createdOn={createdOn} />
    </>
  )
}

function formatBytes(bytes: number): string {
  const giga = bytes / 1_000_000_000
  return giga >= 1000
    ? `${(giga / 1000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} To`
    : `${giga.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
}
