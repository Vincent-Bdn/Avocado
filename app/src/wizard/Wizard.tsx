import { useState } from 'react'
import type { VaultCreated, VaultStatus } from '../api.js'
import { StepRecovery } from './StepRecovery.js'
import { StepVault } from './StepVault.js'

const steps = ['Bienvenue', 'Coffre', 'Clé de récupération', 'Terminé'] as const

/**
 * First run. Full-screen, no rail, no status bar: these screens are read once and carefully, so the
 * type is larger than anywhere else in the application and the column is centred rather than dense.
 */
export function Wizard({ status, onReady }: { status: VaultStatus; onReady: () => void }) {
  const [step, setStep] = useState(0)
  const [created, setCreated] = useState<VaultCreated | null>(null)

  return (
    <div className="wizard">
      <header className="wizard-bar">
        <img src="./icon.png" alt="" className="wizard-mark" />
        <span className="wizard-word">Avocado</span>

        <ol className="wizard-steps">
          {steps.map((label, index) => (
            <li
              key={label}
              className={index < step ? 'step-done' : index === step ? 'step-current' : 'step-later'}
            >
              <span className="step-dot">{index < step ? '✓' : ''}</span>
              {label}
            </li>
          ))}
        </ol>
      </header>

      <main className="wizard-content">
        {step === 0 && <StepWelcome onContinue={() => setStep(1)} />}

        {step === 1 && (
          <StepVault
            suggested={status.suggestedDirectory}
            onBack={() => setStep(0)}
            onCreated={(vault) => {
              setCreated(vault)
              setStep(2)
            }}
          />
        )}

        {step === 2 && created && (
          <StepRecovery
            recoveryCode={created.recoveryCode}
            onBack={() => setStep(1)}
            onContinue={() => setStep(3)}
          />
        )}

        {step === 3 && created && <StepDone created={created} onFinish={onReady} />}
      </main>
    </div>
  )
}

function StepWelcome({ onContinue }: { onContinue: () => void }) {
  return (
    <>
      <div className="wizard-column">
        <h1>Bonjour, et bienvenue dans Avocado.</h1>

        <p className="lead">
          Trois minutes de réglages, puis vous n’entendrez plus parler de tout ceci. Deux choses
          méritent votre attention : où vos dossiers sont rangés, et comment les retrouver si cet
          ordinateur disparaît.
        </p>

        <ul className="points">
          <li>
            <strong>Tout reste sur votre ordinateur, chiffré.</strong>
            <span>
              Aucun serveur, aucun compte, aucune synchronisation. Le secret professionnel n’a rien à
              négocier avec un hébergeur.
            </span>
          </li>
          <li>
            <strong>Aucun mot de passe à retenir au quotidien.</strong>
            <span>
              La clé est gardée par votre système et liée à cette machine et à votre session. Vous
              ouvrez l’application, elle s’ouvre.
            </span>
          </li>
          <li>
            <strong>Une clé de récupération, à mettre à l’abri une bonne fois.</strong>
            <span>
              C’est elle qui rendra vos sauvegardes lisibles sur une autre machine. Nous y viendrons à
              la troisième étape.
            </span>
          </li>
        </ul>

        <p className="footnote muted">
          Version 1.0 · logiciel libre · aucune donnée ne quitte ce poste
        </p>
      </div>

      <footer className="wizard-gate">
        <span className="grow" />
        <button type="button" onClick={onContinue}>
          Commencer
        </button>
      </footer>
    </>
  )
}

/**
 * Not a congratulation: a two-line recap, then the one remaining question.
 *
 * Dropbox reappears here and is recommended — « une sauvegarde est un fichier fermé, que la
 * synchronisation copie sans risque ». Saying so explicitly is what keeps the step-2 refusal from
 * reading as arbitrary.
 */
function StepDone({ created, onFinish }: { created: VaultCreated; onFinish: () => void }) {
  return (
    <>
      <div className="wizard-column">
        <h1>Tout est prêt.</h1>

        <ul className="recap">
          <li>
            ✓ Coffre créé et chiffré dans <span className="mono">{created.directory}</span>
          </li>
          <li>✓ Clé de récupération mise à l’abri</li>
        </ul>

        <h2 className="section-title">Où souhaitez-vous écrire les sauvegardes ?</h2>

        <p className="muted">
          Une sauvegarde est un fichier fermé, que la synchronisation copie sans risque. C’est le
          coffre lui-même qui ne devait pas s’y trouver.
        </p>

        <p className="footnote muted">
          Cette question n’est pas encore branchée : les sauvegardes automatiques arrivent avec les
          réglages. Rien n’est perdu — le coffre est chiffré et la clé est en sécurité.
        </p>
      </div>

      <footer className="wizard-gate">
        <span className="grow" />
        <button type="button" onClick={onFinish}>
          Ouvrir Avocado
        </button>
      </footer>
    </>
  )
}
