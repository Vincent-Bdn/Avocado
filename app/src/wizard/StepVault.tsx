import { useState } from 'react'
import { ApiError, post } from '../api.js'
import type { VaultCreated } from '../api.js'

/**
 * Where the vault goes, and the refusal when that is a synced folder.
 *
 * The refusal explains the right arrangement rather than only forbidding the wrong one — vault on the
 * local disk, *backups* in the synced folder — and puts the corrected path inside the primary button,
 * so accepting takes a click and overriding takes a decision.
 */
export function StepVault({ suggested, onBack, onCreated }: {
  suggested: string
  onBack: () => void
  onCreated: (created: VaultCreated) => void
}) {
  const [directory, setDirectory] = useState(suggested)
  const [refusal, setRefusal] = useState<{ detail: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function browse() {
    const chosen = await window.avocado.chooseFolder(directory)
    if (chosen) {
      setDirectory(chosen)
      setRefusal(null)
      setError(null)
    }
  }

  async function create(allowSyncedFolder = false) {
    setBusy(true)
    setError(null)

    try {
      onCreated(await post<VaultCreated>('/api/vault', { directory, allowSyncedFolder }))
    } catch (failure) {
      if (failure instanceof ApiError && failure.code === 'synced-folder') {
        setRefusal({ detail: failure.message })
      } else {
        setError(failure instanceof ApiError ? failure.message : String(failure))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="wizard-column">
        <h1>Où ranger le coffre</h1>

        <p className="lead">
          Un seul dossier sur ce disque contiendra tout : journal, documents, temps passé. Il est
          chiffré en permanence.
        </p>

        <div className="path-row">
          <input
            className={`mono path ${refusal ? 'path-refused' : ''}`}
            value={directory}
            onChange={(event) => {
              setDirectory(event.target.value)
              setRefusal(null)
            }}
          />
          <button type="button" className="secondary-button" onClick={() => void browse()}>
            Parcourir…
          </button>
        </div>

        {refusal && (
          <div className="refusal">
            <p>{refusal.detail}</p>

            <div className="arrangement">
              <div className="sub-title">Le montage qui fonctionne</div>
              <ul>
                <li>
                  <strong>Le coffre</strong> sur le disque local de cet ordinateur, où rien ne le copie
                  pendant qu’Avocado y écrit.
                </li>
                <li>
                  <strong>Les sauvegardes</strong> dans le dossier synchronisé — Avocado y dépose une
                  copie chiffrée, fermée et cohérente. C’est exactement l’usage pour lequel la
                  synchronisation est faite.
                </li>
              </ul>
            </div>

            <div className="refusal-actions">
              <button type="button" onClick={() => void useHome()}>
                Utiliser {homeSuggestion()}
              </button>
              <button type="button" className="secondary-button" onClick={() => void browse()}>
                Choisir un autre dossier
              </button>
            </div>

            {/* Available, not inviting: quiet, right-aligned, below the two real buttons. */}
            <button type="button" className="override" onClick={() => void create(true)}>
              Ce n’est pas un dossier synchronisé — passer outre
            </button>
          </div>
        )}

        {error && <p className="danger">{error}</p>}
      </div>

      <footer className="wizard-gate">
        <span className="grow" />
        <button type="button" className="secondary-button" onClick={onBack}>
          Retour
        </button>
        <button type="button" disabled={busy || !directory.trim()} onClick={() => void create()}>
          {busy ? 'Création…' : 'Continuer'}
        </button>
      </footer>
    </>
  )

  function homeSuggestion(): string {
    // The suggestion the server made, which it has already checked is not itself synced.
    return suggested
  }

  async function useHome() {
    setDirectory(suggested)
    setRefusal(null)
    setBusy(true)
    setError(null)

    try {
      onCreated(await post<VaultCreated>('/api/vault', { directory: suggested, allowSyncedFolder: false }))
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }
}
