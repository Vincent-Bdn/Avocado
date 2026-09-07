import { useEffect, useState } from 'react'
import { AlertCircle, Check, HardDrive } from 'lucide-react'
import { api } from '../api.js'
import { Button } from './ui/button.js'
import { cn } from '../lib/utils.js'

export interface DiskEncryption {
  state: 'on' | 'off' | 'unknown'
  mechanism: string
  pane: string
}

/**
 * Whether the disk her documents sit on is encrypted at rest.
 *
 * <p>Documents live in her own folders now, so this is the operating system's job, which it does
 * invisibly and better than a store she had to check files out of. What is left for Avocado is to
 * know whether it is switched on: a dossier of client files on an unencrypted laptop is a breach of
 * secret professionnel waiting for a theft.</p>
 *
 * <p><b>Three tones, and the neutral one is not a failure.</b> Green when the system answered yes,
 * orange when it answered no, and grey when it did not answer. On Windows it never answers: the
 * authoritative check needs elevation Avocado does not ask for. Grey says so and points at the
 * settings page, because reassuring her on a guess is the one mistake that matters here.</p>
 */
export function DiskEncryptionBanner({ compact }: { compact?: boolean }) {
  const [disk, setDisk] = useState<DiskEncryption | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    api<DiskEncryption>('/api/system/disk-encryption').then(setDisk).catch(() => setDisk(null))
  }, [])

  if (disk === null) {
    return null
  }

  const tone = {
    on: 'border-[#BFD3C5] bg-success-bg text-success',
    off: 'border-[#E8D5AE] bg-warning-bg text-warning',
    unknown: 'border-line bg-sunken text-ink-secondary',
  }[disk.state]

  const title = {
    on: `${disk.mechanism} est actif sur ce disque`,
    off: 'Ce disque n’est pas chiffré',
    unknown: `Avocado ne peut pas vérifier ${disk.mechanism} lui-même`,
  }[disk.state]

  return (
    <div className={cn('grid gap-1.5 rounded-sm border px-2.5 py-2', tone)}>
      <div className="flex items-center gap-1.5 text-[12.5px] font-medium">
        {disk.state === 'on' && <Check size={14} strokeWidth={2.5} />}
        {disk.state === 'off' && <AlertCircle size={14} strokeWidth={2} />}
        {disk.state === 'unknown' && <HardDrive size={14} strokeWidth={2} />}
        {title}
      </div>

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px]">
        {disk.state === 'on' && (
          <>
            Vos dossiers sont illisibles pour qui prendrait cet ordinateur sans votre mot de passe.
            Rien d’autre à faire.
          </>
        )}
        {disk.state === 'off' && (
          <>
            Vos documents restent dans vos dossiers, et sur ce disque ils sont lisibles par quiconque
            met la main sur la machine. Activez {disk.mechanism} : une minute à lancer, le reste se
            fait en arrière-plan, et vous ne le remarquerez plus ensuite.
          </>
        )}
        {disk.state === 'unknown' && (
          <>
            La vérification demande des droits d’administrateur qu’Avocado ne réclame pas. Ouvrez le
            réglage : s’il indique que le chiffrement est activé, il n’y a rien à faire.
          </>
        )}
      </p>

      {disk.state !== 'on' && (
        <div>
          <Button
            variant="secondary"
            size={compact ? 'sm' : 'md'}
            onClick={() => {
              void window.avocado.openDiskEncryptionSettings(disk.pane).then(setFailure)
            }}
          >
            Ouvrir le réglage {disk.mechanism}
          </Button>
        </div>
      )}

      {failure && <p className="m-0 text-[11px] text-danger">{failure}</p>}
    </div>
  )
}
