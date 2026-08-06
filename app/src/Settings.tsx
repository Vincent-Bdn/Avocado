import { useEffect, useState } from 'react'
import { Check, RefreshCw, X } from 'lucide-react'
import { createPortal } from 'react-dom'
import { ApiError, api, post } from './api.js'
import { RecoveryKeyCard } from './wizard/StepRecovery.js'
import { RecoverySheet } from './wizard/RecoverySheet.js'
import { SecureKeyOptions, isSecured, type SecuredBy } from './wizard/SecureKeyOptions.js'

interface RecoveryKeyState {
  code: string | null
  fingerprint: string | null
  createdAt: string | null
}

/** Two of nine, drawn once per visit. Enough to prove the sheet was fetched, short enough not to
 *  feel like an exam. */
function pickTwo(): [number, number] {
  const first = Math.floor(Math.random() * 9)
  let second = Math.floor(Math.random() * 8)
  if (second >= first) second += 1

  return first < second ? [first, second] : [second, first]
}

/**
 * Réglages: the two recurring moments the setup wizard sets up.
 *
 * A recovery system nobody ever tested is one that does not work, so the check verifies real groups
 * against the real key. Issuing a new key stays two clicks away, because that is what keeps a lost
 * sheet from being fatal.
 */
export function Settings() {
  const [key, setKey] = useState<RecoveryKeyState | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = () => {
    api<RecoveryKeyState>('/api/vault/recovery-key')
      .then(setKey)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }

  useEffect(reload, [])

  return (
    <div className="content settings">
      <header className="matter-header">
        <div className="line1">
          <h2>Réglages</h2>
        </div>
        <div className="line2">Clé de récupération et contrôle du coffre</div>
      </header>

      <div className="settings-body">
        {error && <p className="danger">{error}</p>}

        {key && !key.code && (
          <section className="panel-card">
            <h3>Clé de récupération</h3>
            <p className="muted">
              Ce coffre a été créé avant que la clé ne soit conservée : elle ne peut donc plus être
              affichée ni contrôlée. Éditez-en une nouvelle pour retrouver ces deux possibilités. La
              fiche imprimée que vous détenez reste valable jusque-là.
            </p>
            <Regenerate onDone={reload} />
          </section>
        )}

        {key?.code && (
          <>
            <QuarterlyCheck code={key.code} />
            <RenewKey current={key} onDone={reload} />
          </>
        )}
      </div>
    </div>
  )
}

/** « Retrouvez votre fiche, et recopiez deux groupes. » */
function QuarterlyCheck({ code }: { code: string }) {
  const [indices, setIndices] = useState<[number, number]>(pickTwo)
  const [values, setValues] = useState<Record<number, string>>({})
  const [result, setResult] = useState<Record<number, boolean> | null>(null)
  const [busy, setBusy] = useState(false)

  async function verify() {
    setBusy(true)

    try {
      const response = await post<{ passed: boolean; correct: Record<number, boolean> }>(
        '/api/vault/recovery-key/check',
        { groups: Object.fromEntries(indices.map((index) => [index, values[index] ?? ''])) },
      )

      setResult(response.correct)
    } finally {
      setBusy(false)
    }
  }

  const passed = result !== null && indices.every((index) => result[index])

  return (
    <section className="panel-card">
      <h3>Contrôle de la clé</h3>

      <p>
        Retrouvez votre fiche, et recopiez deux groupes. Un dispositif de secours jamais testé est un
        dispositif qui ne marche pas.
      </p>

      <div className="check-groups">
        {indices.map((index) => (
          <label key={index}>
            <span className="muted">Groupe n° {index + 1}</span>
            <input
              className="mono"
              maxLength={8}
              value={values[index] ?? ''}
              placeholder="······"
              onChange={(event) => {
                setValues({ ...values, [index]: event.target.value })
                setResult(null)
              }}
            />
            {result?.[index] === true && <Check size={13} strokeWidth={2.5} className="ok" />}
            {result?.[index] === false && <X size={13} strokeWidth={2.5} className="ko" />}
          </label>
        ))}
      </div>

      {passed && (
        <p className="done">
          <Check size={12} strokeWidth={2.5} /> Votre fiche est la bonne. Rangez-la où vous l’avez
          prise.
        </p>
      )}

      {result !== null && !passed && (
        <p className="muted">
          Un groupe ne correspond pas. Vérifiez la ligne, ou éditez une nouvelle clé si la fiche est
          introuvable.
        </p>
      )}

      <div className="card-actions">
        <button
          type="button"
          disabled={busy || indices.some((index) => !values[index])}
          onClick={() => void verify()}
        >
          Vérifier
        </button>

        <button
          type="button"
          className="secondary-button"
          onClick={() => {
            setIndices(pickTwo())
            setValues({})
            setResult(null)
          }}
        >
          Deux autres groupes
        </button>
        {/* Hidden from `code` deliberately: the point is to read the sheet, not the screen. */}
        <span className="grow" />
        <span className="muted micro">{code.split('-').length} groupes au total</span>
      </div>
    </section>
  )
}

function RenewKey({ current, onDone }: { current: RecoveryKeyState; onDone: () => void }) {
  return (
    <section className="panel-card">
      <h3>Renouveler la clé</h3>

      <p className="muted">
        Clé actuelle : empreinte <span className="mono">{current.fingerprint}</span>
        {current.createdAt && `, créée le ${new Date(current.createdAt).toLocaleDateString('fr-FR')}`}.
      </p>

      <Regenerate onDone={onDone} />
    </section>
  )
}

/** The renewal itself: the ochre statement of consequence, then the same securing step as at setup. */
function Regenerate({ onDone }: { onDone: () => void }) {
  const [issued, setIssued] = useState<RecoveryKeyState | null>(null)
  const [secured, setSecured] = useState<SecuredBy>({ printed: false, savedTo: null, exportedTo: null })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const createdOn = new Date().toLocaleDateString('fr-FR')

  async function regenerate() {
    setBusy(true)
    setError(null)

    try {
      setIssued(await post<RecoveryKeyState>('/api/vault/recovery-key/regenerate', {}))
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (!issued?.code) {
    return (
      <>
        <div className="caution-card">
          <strong>Ce qui change</strong>
          <p>
            Les sauvegardes faites <strong>à partir de maintenant</strong> s’ouvriront avec la
            nouvelle clé. Les sauvegardes plus anciennes continueront d’exiger l’ancienne : gardez la
            fiche précédente tant que ces sauvegardes comptent.
          </p>
        </div>

        {error && <p className="danger">{error}</p>}

        <div className="card-actions">
          <button type="button" disabled={busy} onClick={() => void regenerate()}>
            <RefreshCw size={13} strokeWidth={1.75} />
            {busy ? 'Génération…' : 'Éditer une nouvelle clé'}
          </button>
        </div>
      </>
    )
  }

  return (
    <>
      <RecoveryKeyCard recoveryCode={issued.code} createdOn={createdOn} />

      <div className="secure-lead">Mettez cette nouvelle clé à l’abri :</div>

      <SecureKeyOptions
        recoveryCode={issued.code}
        fingerprint={issued.fingerprint ?? ''}
        createdOn={createdOn}
        secured={secured}
        onSecured={setSecured}
      />

      <div className="card-actions">
        <button type="button" disabled={!isSecured(secured)} onClick={onDone}>
          Terminé
        </button>
      </div>

      {createPortal(
        <RecoverySheet
          recoveryCode={issued.code}
          fingerprint={issued.fingerprint ?? ''}
          createdOn={createdOn}
        />,
        document.body,
      )}
    </>
  )
}
