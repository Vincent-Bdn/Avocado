import { useState } from 'react'
import { Folder, ShieldAlert } from 'lucide-react'
import { ApiError, post } from '../api.js'
import type { VaultPrepared } from '../api.js'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'
import { WizardGate, WizardLead, WizardScroll, WizardTitle } from './shared.js'

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
      <WizardScroll width={680}>
        <WizardTitle>Où vivront vos dossiers ?</WizardTitle>

        <WizardLead>
          Un seul dossier sur ce disque contiendra tout : journal, documents, temps passé. Il est
          chiffré en permanence.
        </WizardLead>

        <div className="mt-[22px]">
          <label htmlFor="vault-path" className="mb-1.5 block text-label font-medium text-ink-secondary">
            Emplacement du coffre
          </label>

          <div className="flex items-center gap-2">
            <div
              className={cn(
                'flex h-[34px] min-w-0 flex-1 items-center gap-2 rounded-md border bg-panel px-2.5',
                refusal
                  ? 'border-danger shadow-[0_0_0_2px_color-mix(in_srgb,var(--status-danger)_16%,transparent)]'
                  : 'border-line-strong',
              )}
            >
              <Folder size={14} strokeWidth={1.75} className="shrink-0 text-muted" />

              <input
                id="vault-path"
                value={directory}
                onChange={(event) => {
                  setDirectory(event.target.value)
                  setRefusal(null)
                }}
                className="min-w-0 flex-1 border-0 bg-transparent p-0 font-mono text-[12.5px] text-ink focus:outline-none"
              />
            </div>

            <Button variant="secondary" size="lg" onClick={() => void browse()}>Parcourir…</Button>
          </div>
        </div>

        {refusal && (
          <div className="mt-3 flex items-start gap-[11px] rounded-lg border border-[#ebc9c5] border-l-[3px] border-l-danger bg-[#fdf4f3] px-4 py-3.5">
            <ShieldAlert size={16} strokeWidth={1.75} className="mt-0.5 shrink-0 text-danger" />

            <div className="min-w-0 flex-1">
              <div className="text-[13px] leading-[19px] font-semibold text-[#8a211a]">
                Ce dossier est synchronisé.
              </div>

              <p className="mt-[3px] mb-0 text-[12.5px] leading-[19px] text-[#8a211a]">
                {refusal.detail}
              </p>

              {/* The arrangement that works, on its own white card inside the refusal. */}
              <div className="mt-[11px] rounded-lg border border-line-subtle bg-panel px-3 py-[11px]">
                <div className="text-[12px] leading-[17px] font-medium">Le montage qui fonctionne</div>

                <ArrangementLine>
                  <strong className="font-medium text-ink">Le coffre sur le disque local</strong>, par
                  exemple <code className="font-mono text-[11.5px]">{suggested}</code>
                </ArrangementLine>

                <ArrangementLine>
                  <strong className="font-medium text-ink">
                    Les sauvegardes dans le dossier synchronisé
                  </strong>
                  . Avocado y dépose une copie chiffrée, fermée et cohérente : c’est exactement
                  l’usage pour lequel la synchronisation est faite.
                </ArrangementLine>
              </div>

              <div className="mt-3 flex flex-wrap gap-2">
                <Button
                  onClick={() => {
                    setDirectory(suggested)
                    setRefusal(null)
                    void prepare(suggested)
                  }}
                >
                  Utiliser {suggested}
                </Button>

                <Button variant="secondary" onClick={() => void browse()}>
                  Choisir un autre dossier
                </Button>
              </div>

              {/* Available, not inviting: quiet, right-aligned, below the two real buttons. */}
              <button
                type="button"
                onClick={() => void prepare(directory, true)}
                className="mt-2.5 ml-auto block text-[12px] text-ink-secondary underline"
              >
                Ce n’est pas un dossier synchronisé, passer outre
              </button>
            </div>
          </div>
        )}

        {error && <p className="mt-3 mb-0 text-danger">{error}</p>}
      </WizardScroll>

      <WizardGate>
        <span className="flex-1" />
        <Button variant="secondary" size="lg" onClick={onBack}>Retour</Button>
        <Button size="lg" disabled={busy || !directory.trim()} onClick={() => void prepare()}>
          {busy ? 'Vérification…' : 'Continuer'}
        </Button>
      </WizardGate>
    </>
  )
}

const ArrangementLine = ({ children }: { children: React.ReactNode }) => (
  <div className="mt-1.5 flex items-baseline gap-2 text-[12.5px] leading-[19px] text-ink-secondary">
    <span className="h-[5px] w-[5px] shrink-0 -translate-y-0.5 rounded-full bg-brand" />
    <span>{children}</span>
  </div>
)
