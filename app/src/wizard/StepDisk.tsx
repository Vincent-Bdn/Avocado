import { HardDrive, Lock } from 'lucide-react'
import { Button } from '../components/ui/button.js'
import { DiskEncryptionBanner } from '../components/DiskEncryptionBanner.js'
import { Point, Points, WizardLead, WizardScroll, WizardTitle } from './shared.js'

/**
 * A step of its own, because this is now the thing that protects her documents.
 *
 * <p>It used to be a footnote: documents lived encrypted inside the vault, so the disk underneath
 * hardly mattered. Ten lawyers refused that arrangement, documents live in her own folders now, and
 * what stands between a stolen laptop and a client's file is BitLocker or FileVault. That is worth a
 * screen rather than a line.</p>
 *
 * <p>It never blocks. She may not have the rights to switch it on, it may be her firm's machine, and
 * a wizard that refuses to continue over something Avocado cannot check is a wizard she abandons.</p>
 */
export function StepDisk({ onBack, onContinue }: { onBack: () => void; onContinue: () => void }) {
  return (
    <>
      <WizardScroll>
        <WizardTitle>Le disque de cet ordinateur</WizardTitle>

        <WizardLead>
          Vos documents restent dans vos dossiers, là où vous travaillez déjà. Sur cette machine, ce
          qui les protège n’est donc pas Avocado : c’est le chiffrement du disque, que Windows et
          macOS savent faire tout seuls, une fois pour toutes.
        </WizardLead>

        <div className="mt-[22px]">
          <DiskEncryptionBanner />
        </div>

        <Points>
          <Point icon={<HardDrive size={16} strokeWidth={1.75} />} title="Ce que cela change, concrètement">
            Un ordinateur perdu ou volé, un disque revendu, un portable oublié dans un train : sans le
            chiffrement, tout se lit. Avec, il n’y a rien à lire.
          </Point>

          <Point icon={<Lock size={16} strokeWidth={1.75} />} title="Ce qu’Avocado chiffre de son côté">
            Le coffre, où vivent votre journal, vos tiers, votre facturation et votre temps passé, et
            vos sauvegardes, qui sont illisibles sans votre clé de récupération.
          </Point>
        </Points>
      </WizardScroll>

      <div className="flex flex-wrap items-center gap-2 border-t border-line-subtle px-6 py-3">
        <Button variant="secondary" size="lg" onClick={onBack}>Retour</Button>
        <Button size="lg" onClick={onContinue}>Continuer</Button>

        <span className="text-[11.5px] leading-[17px] text-muted">
          Vous pouvez continuer sans, et l’activer plus tard : Réglages, Chiffrement du disque.
        </span>
      </div>
    </>
  )
}
