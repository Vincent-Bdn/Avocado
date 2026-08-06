import { useState } from 'react'
import { ApiError, post, type VaultCreated, type VaultStatus } from './api.js'

/**
 * First run: choose a folder, create the vault, put the recovery key out of reach.
 *
 * This is the *functional* wizard, not the designed one — the eight frames in
 * `ds/assistant-demarrage/` still have to be built on Tailwind and shadcn/ui. What it does implement
 * is the rule that matters: the recovery key is shown once, and the user cannot continue until they
 * have confirmed it is somewhere other than this computer.
 */
export function Setup({ status, onReady }: { status: VaultStatus; onReady: () => void }) {
  const [directory, setDirectory] = useState(status.suggestedDirectory)
  const [allowSynced, setAllowSynced] = useState(false)
  const [syncedFolder, setSyncedFolder] = useState(false)
  const [created, setCreated] = useState<VaultCreated | null>(null)
  const [savedIt, setSavedIt] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function browse() {
    const chosen = await window.avocado.chooseFolder(directory)
    if (chosen) {
      setDirectory(chosen)
      setError(null)
      setSyncedFolder(false)
      setAllowSynced(false)
    }
  }

  async function create() {
    setBusy(true)
    setError(null)

    try {
      setCreated(await post<VaultCreated>('/api/vault', { directory, allowSyncedFolder: allowSynced }))
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
      setSyncedFolder(failure instanceof ApiError && failure.code === 'synced-folder')
    } finally {
      setBusy(false)
    }
  }

  if (created) {
    return (
      <section className="pane">
        <h1>Votre clé de récupération</h1>

        <p>
          Vos sauvegardes sont chiffrées avec cette clé. Sans elle, une sauvegarde n’est qu’un fichier
          illisible : c’est elle, et elle seule, qui vous permettra de rouvrir vos dossiers sur un
          autre ordinateur. <strong>Personne d’autre n’en possède de copie.</strong>
        </p>

        <div className="key">
          {created.recoveryCode.split('-').map((group) => (
            <span key={group}>{group}</span>
          ))}
        </div>

        <p className="muted">
          Neuf groupes de six, lisibles à voix haute et recopiables à la main. L’alphabet exclut I, L,
          O et U : un 1 est toujours un chiffre, un 0 toujours un zéro.
        </p>

        <label className="confirm">
          <input type="checkbox" checked={savedIt} onChange={(e) => setSavedIt(e.target.checked)} />
          J’ai mis cette clé à l’abri, hors de cet ordinateur.
        </label>

        <button type="button" disabled={!savedIt} onClick={onReady}>
          Ouvrir Avocado
        </button>
      </section>
    )
  }

  return (
    <section className="pane">
      <h1>Bienvenue</h1>
      <p>Choisissez où Avocado conservera vos dossiers. Tout y sera chiffré.</p>

      <div className="row">
        <input value={directory} onChange={(e) => setDirectory(e.target.value)} className="mono" />
        <button type="button" onClick={() => void browse()}>
          Parcourir…
        </button>
      </div>

      {error && (
        <div className="callout">
          <p>{error}</p>

          {/* The detector is a heuristic, so the way past it exists — quietly. */}
          {syncedFolder && (
            <label className="confirm">
              <input
                type="checkbox"
                checked={allowSynced}
                onChange={(e) => setAllowSynced(e.target.checked)}
              />
              Ce n’est pas un dossier synchronisé — passer outre.
            </label>
          )}
        </div>
      )}

      <button type="button" disabled={busy || !directory} onClick={() => void create()}>
        {busy ? 'Création…' : 'Créer le coffre'}
      </button>
    </section>
  )
}
