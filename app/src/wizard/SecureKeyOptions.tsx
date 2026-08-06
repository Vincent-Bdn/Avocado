import { useEffect, useState } from 'react'
import { Check, ChevronDown, FileDown, Printer, Usb } from 'lucide-react'

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

  return (
    <div className="secure-options">
      <section className="option option-recommended">
        <header>
          <Printer size={16} strokeWidth={1.75} />
          <h3>L’imprimer</h3>
          <span className="badge-recommended">recommandé</span>
        </header>

        <p>
          Une page A4 avec un QR code, la clé en toutes lettres et une ligne pour noter où vous la
          rangez. À classer là où vous classez déjà ce qui compte.
        </p>

        <span className="grow" />

        <div className="split-button">
          <button
            type="button"
            onClick={() => {
              onSecured({ ...secured, printed: true })
              window.print()
            }}
          >
            Imprimer la fiche
          </button>

          <button
            type="button"
            className="split-toggle"
            aria-label="Autres façons d’obtenir la fiche"
            onClick={() => setMenuOpen((open) => !open)}
          >
            <ChevronDown size={13} strokeWidth={2} />
          </button>

          {menuOpen && (
            <div className="split-menu">
              <button
                type="button"
                onClick={() => {
                  setMenuOpen(false)
                  onSecured({ ...secured, printed: true })
                  window.print()
                }}
              >
                <Printer size={13} strokeWidth={1.75} />
                Utiliser une imprimante
              </button>

              <button type="button" onClick={() => void exportPdf()}>
                <FileDown size={13} strokeWidth={1.75} />
                Télécharger le fichier PDF
              </button>
            </div>
          )}
        </div>

        {secured.printed && (
          <p className="done">
            <Check size={12} strokeWidth={2.5} /> Impression lancée
          </p>
        )}
        {secured.exportedTo && (
          <p className="done">
            <Check size={12} strokeWidth={2.5} /> PDF enregistré dans {secured.exportedTo}
          </p>
        )}
      </section>

      <section className="option">
        <header>
          <Usb size={16} strokeWidth={1.75} />
          <h3>L’enregistrer sur une clé USB</h3>
        </header>

        <p>
          Un petit fichier texte sur un support amovible, rangé ailleurs que près de l’ordinateur.
        </p>

        {drives.length === 0 ? (
          <div className="no-drive">
            <strong>Aucun support amovible branché</strong>
            <span className="muted">
              Branchez une clé USB : elle apparaîtra ici toute seule, en quelques secondes.
            </span>
            <span className="mono muted searching">recherche en cours…</span>
          </div>
        ) : (
          <ul className="drives">
            {drives.map((drive) => (
              <li key={drive.path}>
                <span className="drive-name">
                  <span className="mono">{drive.path}</span> {drive.label}
                  {drive.freeBytes > 0 && (
                    <span className="muted"> ({formatBytes(drive.freeBytes)} libres)</span>
                  )}
                </span>

                <button type="button" className="secondary-button" onClick={() => void saveToDrive(drive)}>
                  Enregistrer sur {drive.path}
                </button>
              </li>
            ))}
          </ul>
        )}

        <span className="grow" />

        <p className="muted micro">
          Les disques internes sont exclus volontairement : une clé enregistrée sur le disque de cet
          ordinateur disparaîtrait avec lui.
        </p>

        {secured.savedTo && (
          <p className="done">
            <Check size={12} strokeWidth={2.5} /> Écrite dans {secured.savedTo}
          </p>
        )}
        {error && <p className="danger">{error}</p>}
      </section>
    </div>
  )
}

export const isSecured = (secured: SecuredBy): boolean =>
  secured.printed || secured.savedTo !== null || secured.exportedTo !== null

function formatBytes(bytes: number): string {
  const giga = bytes / 1_000_000_000
  return giga >= 1000
    ? `${(giga / 1000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} To`
    : `${giga.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Go`
}
