import { useEffect, useState, type ReactNode } from 'react'
import { Check, ChevronDown, FileDown, Printer, Usb } from 'lucide-react'
import { Badge } from '../components/ui/badge.js'
import { Button } from '../components/ui/button.js'
import { cn } from '../lib/utils.js'

interface Drive {
  path: string
  label: string
  freeBytes: number
}

export interface SecuredBy {
  printed: boolean
  savedTo: string | null
  exportedTo: string | null
}

/**
 * The two ways to put the key out of reach, shared by the setup wizard and by Réglages when a new key
 * is issued.
 *
 * Printing carries a second action rather than one: Electron's print path has no preview, so on a
 * machine without a printer it opens a dialog that says so. Producing the PDF directly is the honest
 * alternative, and it is what most people were reaching for anyway.
 */
export function SecureKeyOptions({ recoveryCode, fingerprint, createdOn, secured, onSecured }: {
  recoveryCode: string
  fingerprint: string
  createdOn: string
  secured: SecuredBy
  onSecured: (next: SecuredBy) => void
}) {
  const [drives, setDrives] = useState<Drive[]>([])
  const [menuOpen, setMenuOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const groups = recoveryCode.split('-')

  // The list refreshes itself: « branchez une clé USB, elle apparaîtra ici toute seule ».
  useEffect(() => {
    const poll = () => void window.avocado.removableDrives().then(setDrives).catch(() => setDrives([]))

    poll()
    const timer = setInterval(poll, 3000)
    return () => clearInterval(timer)
  }, [])

  async function saveToDrive(drive: Drive) {
    setError(null)

    try {
      const contents =
        `Avocado, clé de récupération\r\n` +
        `Coffre créé le ${createdOn}\r\n` +
        `Empreinte ${fingerprint}\r\n\r\n` +
        `${groups.join('\r\n')}\r\n\r\n` +
        `Sans cette clé, les sauvegardes de ce coffre ne peuvent être rouvertes par personne.\r\n`

      onSecured({ ...secured, savedTo: await window.avocado.saveRecoveryKey(drive.path, contents) })
    } catch (failure) {
      setError(`Écriture impossible sur ${drive.path} : ${String(failure)}`)
    }
  }

  async function exportPdf() {
    setMenuOpen(false)
    setError(null)

    try {
      const saved = await window.avocado.exportRecoverySheet()
      if (saved) {
        onSecured({ ...secured, exportedTo: saved })
      }
    } catch (failure) {
      setError(`Enregistrement impossible : ${String(failure)}`)
    }
  }

  function print() {
    onSecured({ ...secured, printed: true })
    window.print()
  }

  return (
    <div className="mt-2 grid items-stretch gap-3 sm:grid-cols-2">
      <Option
        recommended
        icon={<Printer size={16} strokeWidth={1.75} />}
        title="L’imprimer"
      >
        <p className="m-0 text-[12.5px] leading-[19px] text-ink-secondary">
          Une page A4 avec un QR code, la clé en toutes lettres et une ligne pour noter où vous la
          rangez. À classer là où vous classez déjà ce qui compte.
        </p>

        <span className="flex-1" />

        {/* Split button: the common action, and the two honest ways to get the sheet behind it. */}
        <div className="relative flex self-start">
          <Button className="rounded-r-none" onClick={print}>Imprimer la fiche</Button>

          <Button
            aria-label="Autres façons d’obtenir la fiche"
            onClick={() => setMenuOpen((open) => !open)}
            className="w-[26px] rounded-l-none border-l border-l-white/20 px-0"
          >
            <ChevronDown size={13} strokeWidth={2} />
          </Button>

          {menuOpen && (
            <div className="absolute top-[calc(100%+4px)] left-0 z-10 grid min-w-[232px] gap-0.5 rounded-lg border border-line bg-raised p-[3px] shadow-e2">
              <MenuItem onClick={() => { setMenuOpen(false); print() }}>
                <Printer size={13} strokeWidth={1.75} />
                Utiliser une imprimante
              </MenuItem>

              <MenuItem onClick={() => void exportPdf()}>
                <FileDown size={13} strokeWidth={1.75} />
                Télécharger le fichier PDF
              </MenuItem>
            </div>
          )}
        </div>

        {secured.printed && <Done>Impression lancée</Done>}
        {secured.exportedTo && <Done>PDF enregistré dans {secured.exportedTo}</Done>}
      </Option>

      <Option icon={<Usb size={16} strokeWidth={1.75} />} title="L’enregistrer sur une clé USB">
        <p className="m-0 text-[12.5px] leading-[19px] text-ink-secondary">
          Un petit fichier texte sur un support amovible, rangé ailleurs que près de l’ordinateur.
        </p>

        {drives.length === 0 ? (
          <div className="grid gap-[5px] rounded-lg border border-dashed border-line-strong p-3.5 text-[12px]">
            <strong className="font-medium">Aucun support amovible branché</strong>
            <span className="text-muted">
              Branchez une clé USB : elle apparaîtra ici toute seule, en quelques secondes.
            </span>
            <span className="font-mono text-[11px] text-muted">recherche en cours…</span>
          </div>
        ) : (
          <ul className="m-0 grid list-none gap-2 p-0">
            {drives.map((drive) => (
              <li key={drive.path} className="grid gap-1.5 text-[12px]">
                <span className="truncate">
                  <span className="font-mono">{drive.path}</span> {drive.label}
                  {drive.freeBytes > 0 && (
                    <span className="text-muted"> ({formatBytes(drive.freeBytes)} libres)</span>
                  )}
                </span>

                <Button variant="secondary" size="sm" className="justify-self-start" onClick={() => void saveToDrive(drive)}>
                  Enregistrer sur {drive.path}
                </Button>
              </li>
            ))}
          </ul>
        )}

        <span className="flex-1" />

        <p className="m-0 text-[11px] leading-4 text-muted">
          Les disques internes sont exclus volontairement : une clé enregistrée sur le disque de cet
          ordinateur disparaîtrait avec lui.
        </p>

        {secured.savedTo && <Done>Écrite dans {secured.savedTo}</Done>}
        {error && <p className="m-0 text-danger">{error}</p>}
      </Option>
    </div>
  )
}

function Option({ icon, title, recommended, children }: {
  icon: ReactNode
  title: string
  recommended?: boolean
  children: ReactNode
}) {
  return (
    <section
      className={cn(
        'flex flex-col gap-2 rounded-lg px-[15px] py-3.5',
        recommended ? 'border-[1.5px] border-brand bg-[#f4f8f5]' : 'border border-line bg-panel',
      )}
    >
      <header className="flex items-center gap-2">
        <span className="shrink-0 text-brand">{icon}</span>
        <h3 className="m-0 text-[13.5px] leading-[19px] font-semibold">{title}</h3>
        {recommended && <Badge tone="brand" className="bg-brand text-on-brand">recommandé</Badge>}
      </header>

      {children}
    </section>
  )
}

const MenuItem = ({ onClick, children }: { onClick: () => void; children: ReactNode }) => (
  <button
    type="button"
    onClick={onClick}
    className="flex h-[26px] w-full items-center gap-2 rounded-sm px-2 text-left text-[12.5px] hover:bg-hover"
  >
    {children}
  </button>
)

const Done = ({ children }: { children: ReactNode }) => (
  <p className="m-0 flex items-center gap-1.5 text-[12px] text-success">
    <Check size={12} strokeWidth={2.5} />
    {children}
  </p>
)

export const isSecured = (secured: SecuredBy): boolean =>
  secured.printed || secured.savedTo !== null || secured.exportedTo !== null

function formatBytes(bytes: number): string {
  const giga = bytes / 1_000_000_000
  return giga >= 1000
    ? `${(giga / 1000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} To`
    : `${giga.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
}
