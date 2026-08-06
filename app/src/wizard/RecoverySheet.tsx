import { useEffect, useState } from 'react'
import QRCode from 'qrcode'

/**
 * The printed A4 sheet. Pure black on white — no colour, no grey — because it has to survive a nearly
 * empty laser cartridge, and because a lawyer already has somewhere safe for documents that matter.
 *
 * Rendered off-screen and revealed only by the print stylesheet, so `window.print()` produces this
 * page and nothing else.
 */
export function RecoverySheet({ recoveryCode, fingerprint, createdOn }: {
  recoveryCode: string
  fingerprint: string
  createdOn: string
}) {
  const [qr, setQr] = useState<string | null>(null)

  useEffect(() => {
    // Black on white, high correction: this is scanned off paper, possibly a poor print.
    void QRCode.toDataURL(recoveryCode, {
      errorCorrectionLevel: 'H',
      margin: 0,
      width: 296,
      color: { dark: '#000000', light: '#FFFFFF' },
    }).then(setQr)
  }, [recoveryCode])

  const groups = recoveryCode.split('-')

  return (
    <div className="sheet" aria-hidden="true">
      <header className="sheet-head">
        {/* The mark in solid black: the sheet has to survive a nearly empty cartridge, so no colour
            and no greys anywhere on it. */}
        <svg width="20" height="20" viewBox="0 0 64 64" className="sheet-mark" aria-hidden="true">
          <rect x="0" y="0" width="64" height="64" rx="16" fill="#000" />
          <circle cx="32" cy="25" r="10" fill="#fff" />
          <circle cx="32" cy="37" r="15.5" fill="#fff" />
          <circle cx="32" cy="37" r="6.4" fill="#000" />
        </svg>

        <div className="sheet-title">Avocado, clé de récupération</div>
        <span className="grow" />
        <div className="sheet-date">{createdOn}</div>
      </header>

      <p className="sheet-lead">
        Cette feuille contient la clé qui déchiffre les sauvegardes de votre coffre Avocado. Sans elle,
        ces sauvegardes ne peuvent être rouvertes par personne. Rangez-la où vous rangez vos originaux.
      </p>

      <div className="sheet-body">
        <figure className="sheet-qr">
          {qr && <img src={qr} alt="" />}
          <figcaption>à scanner depuis Avocado</figcaption>
        </figure>

        <div className="sheet-key">
          <div className="sheet-label">La clé, en toutes lettres</div>
          <div className="sheet-groups">
            {groups.map((group) => (
              <span key={group}>{group}</span>
            ))}
          </div>
          <p className="sheet-note">
            L’alphabet ne contient ni I, ni L, ni O, ni U : un 1 est toujours un chiffre, un 0 est
            toujours un zéro.
          </p>
        </div>
      </div>

      <section className="sheet-box">
        <div className="sheet-label">Pour restaurer, sur un ordinateur neuf</div>
        <ol>
          <li>Installer Avocado et choisir « Restaurer une sauvegarde ».</li>
          <li>Désigner le fichier de sauvegarde (clé USB, disque externe ou dossier synchronisé).</li>
          <li>Scanner le QR code ci-dessus, ou saisir les neuf groupes à la main.</li>
        </ol>
      </section>

      <section className="sheet-filed">
        <div className="sheet-label">Où cette feuille est rangée</div>
        {/* Two ruled lines: writing down where you filed it is what turns a printout into filing. */}
        <div className="rule" />
        <div className="rule" />
        <p className="sheet-note">
          Notez-le ici, puis notez ailleurs que cette feuille existe : coffre du cabinet, dossier
          « personnel » chez le notaire, classeur des statuts.
        </p>
      </section>

      <footer className="sheet-foot">
        <span>
          Cette feuille vaut accès à l’intégralité des dossiers. Elle ne doit pas être photographiée,
          ni envoyée par courriel, ni stockée en ligne.
        </span>
        <span className="sheet-fingerprint">
          Coffre créé le {createdOn}
          <br />
          Empreinte {fingerprint}
        </span>
      </footer>
    </div>
  )
}

/**
 * Four bytes of SHA-256 over the key, as `4F2A·9C71`. Lets a sheet found in a drawer be matched to a
 * vault without revealing anything about the key itself.
 */
export async function fingerprintOf(recoveryCode: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(recoveryCode))
  const bytes = [...new Uint8Array(digest).slice(0, 4)]
    .map((byte) => byte.toString(16).padStart(2, '0').toUpperCase())
    .join('')

  return `${bytes.slice(0, 4)}·${bytes.slice(4)}`
}
