import { useState } from 'react'
import { Folder, ShieldAlert } from 'lucide-react'
import { ApiError, post } from '../api.js'
import type { VaultPrepared } from '../api.js'

/**
 * Where the vault goes, and the refusal when that is a synced folder.
 *
 * The refusal explains the arrangement that works rather than only forbidding the wrong one, and puts
 * the corrected path inside the primary button: accepting takes a click, overriding takes a decision.
 */
export function StepVault({ suggested, onBack, onPrepared }: {
  suggested: string
  onBack: () => void
  onPrepared: (directory: string, prepared: VaultPrepared) => void
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

  async function prepare(target = directory, allowSyncedFolder = false) {
    setBusy(true)
    setError(null)

    try {
      // Validates the destination and generates the keys. Still nothing on disk.
      onPrepared(target, await post<VaultPrepared>('/api/vault/prepare', {
        directory: target,
        allowSyncedFolder,
      }))
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
      <div className="wizard-scroll">
        <div className="wizard-column wizard-column-wide">
          <h1>Où vivront vos dossiers ?</h1>

          <p className="lead">
            Un seul dossier sur ce disque contiendra tout : journal, documents, temps passé. Il est
            chiffré en permanence.
          </p>

          <div className="field">
            <label className="field-label" htmlFor="vault-path">
              Emplacement du coffre
            </label>

            <div className="path-row">
              <div className={`path-field ${refusal ? 'path-refused' : ''}`}>
                <Folder size={14} strokeWidth={1.75} />
                <input
                  id="vault-path"
                  className="mono"
                  value={directory}
                  onChange={(event) => {
                    setDirectory(event.target.value)
                    setRefusal(null)
                  }}
                />
              </div>

              <button type="button" className="secondary-button" onClick={() => void browse()}>
                Parcourir…
              </button>
            </div>
          </div>

          {refusal && (
            <div className="refusal">
              <ShieldAlert size={16} strokeWidth={1.75} className="refusal-icon" />

              <div className="refusal-body">
                <div className="refusal-title">Ce dossier est synchronisé.</div>

                <p className="refusal-detail">{refusal.detail}</p>

                <div className="arrangement">
                  <div className="arrangement-title">Le montage qui fonctionne</div>

                  <div className="arrangement-line">
                    <span className="bullet" />
                    <span>
                      <strong>Le coffre sur le disque local</strong>, par exemple{' '}
                      <code>{suggested}</code>
                    </span>
                  </div>

                  <div className="arrangement-line">
                    <span className="bullet" />
                    <span>
                      <strong>Les sauvegardes dans le dossier synchronisé</strong>. Avocado y dépose
                      une copie chiffrée, fermée et cohérente : c’est exactement l’usage pour lequel la
                      synchronisation est faite.
                    </span>
                  </div>
                </div>

                <div className="refusal-actions">
                  <button
                    type="button"
                    onClick={() => {
                      setDirectory(suggested)
                      setRefusal(null)
                      void prepare(suggested)
                    }}
                  >
                    Utiliser {suggested}
                  </button>

                  <button type="button" className="secondary-button" onClick={() => void browse()}>
                    Choisir un autre dossier
                  </button>
                </div>

                {/* Available, not inviting: quiet, right-aligned, below the two real buttons. */}
                <button
                  type="button"
                  className="override"
                  onClick={() => void prepare(directory, true)}
                >
                  Ce n’est pas un dossier synchronisé, passer outre
                </button>
              </div>
            </div>
          )}

          {error && <p className="danger">{error}</p>}
        </div>
      </div>

      <footer className="wizard-gate">
        <span className="grow" />
        <button type="button" className="secondary-button" onClick={onBack}>
          Retour
        </button>
        <button type="button" disabled={busy || !directory.trim()} onClick={() => void prepare()}>
          {busy ? 'Vérification…' : 'Continuer'}
        </button>
      </footer>
    </>
  )
}
