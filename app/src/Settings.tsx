import { useEffect, useState, type ReactNode } from 'react'
import { Check, ChevronDown, RefreshCw, Trash2, X } from 'lucide-react'
import { createPortal } from 'react-dom'
import { ApiError, api, blob, post, saveAs } from './api.js'
import { Button } from './components/ui/button.js'
import { Input } from './components/ui/input.js'
import { Select } from './components/ui/select.js'
import {
  fetchLatest,
  isNewer,
  setUpdateChecksEnabled,
  updateChecksEnabled,
  type LatestVersion,
} from './lib/updates.js'
import { PageHeader } from './components/ui/page-header.js'
import { Panel } from './components/ui/panel.js'
import { Unavailable } from './components/ui/unavailable.js'
import { useToasts } from './components/ui/toast.js'
import { DiskEncryptionBanner } from './components/DiskEncryptionBanner.js'
import { Backups } from './sections/Backups.js'
import { Import } from './sections/Import.js'
import { Templates } from './sections/Templates.js'
import { cn } from './lib/utils.js'
import { centsToAmount, parseAmountToCents } from './lib/amount.js'
import type { PracticeSettings } from './types.js'
import { RecoveryKeyCard } from './wizard/StepRecovery.js'
import { RecoverySheet } from './wizard/RecoverySheet.js'
import { SecureKeyOptions, isSecured, nothingSecured, type SecuredBy } from './wizard/SecureKeyOptions.js'
import { deriveFromSvg } from './lib/stampSvg.js'
import { StampTester } from './sections/StampTester.js'

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
 * Réglages. Full-width accordion sections separated by rules, so this page can keep growing as more
 * settings arrive without turning into a wall of half-empty cards.
 */
export function Settings() {
  const [key, setKey] = useState<RecoveryKeyState | null>(null)
  // Nothing open to begin with. It used to land on the taux horaire, which was a reasonable way to
  // show the page was not broken back when it was eleven undifferentiated rows. Grouped, the map is
  // the thing worth seeing first, and one section already expanded pushes a third of it off screen.
  const [open, setOpen] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = () => {
    api<RecoveryKeyState>('/api/vault/recovery-key')
      .then(setKey)
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }

  useEffect(reload, [])

  const toggle = (id: string) => setOpen((current) => (current === id ? null : id))

  return (
    <Panel>
      <PageHeader title="Réglages" meta={<span>Le cabinet, vos données, l’application</span>} />

      <div className="flex-1 overflow-y-auto">
        {error && <p className="px-4 py-3 text-danger">{error}</p>}

        <Group
          title="Le cabinet"
          summary="Taux, adresses, modèles, tampon"
        >
          <Section
            id="rate"
            title="Taux horaire et exercice"
            summary="Le taux par défaut et l’année comptable"
            open={open === 'rate'}
            onToggle={toggle}
          >
            <HourlyRate />
          </Section>

          <Section
            id="addresses"
            title="Vos adresses électroniques"
            summary="Reçu ou envoyé, au journal"
            open={open === 'addresses'}
            onToggle={toggle}
          >
            <OwnAddresses />
          </Section>

          <Section
            id="templates"
            title="Modèle du détail de facture"
            summary="Le document Word joint à vos factures"
            open={open === 'templates'}
            onToggle={toggle}
          >
            <Templates />
          </Section>

          <Section
            id="stamp"
            title="Tampon des pièces"
            summary="L’image et la place du numéro"
            open={open === 'stamp'}
            onToggle={toggle}
          >
            <Stamp />
          </Section>
        </Group>

        <Group
          title="Vos données"
          summary="Sauvegarde, clé, chiffrement"
        >
          <Section
            id="backups"
            title="Sauvegarde"
            summary="Où vos dossiers sont copiés"
            open={open === 'backups'}
            onToggle={toggle}
          >
            <Backups />
          </Section>

          {key && !key.code && (
            <Section
              id="renew"
              title="Clé de récupération"
              summary="Non consultable sur ce coffre"
              open={open === 'renew'}
              onToggle={toggle}
            >
              <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
                Ce coffre est antérieur à la conservation de la clé : elle ne peut plus être
                affichée ni contrôlée. Éditez-en une nouvelle. Votre fiche reste valable jusque-là.
              </p>

              <Regenerate onDone={reload} />
            </Section>
          )}

          {key?.code && (
            <>
              <Section
                id="check"
                title="Contrôle de la clé"
                summary="Vérifier votre fiche"
                open={open === 'check'}
                onToggle={toggle}
              >
                <QuarterlyCheck />
              </Section>

              <Section
                id="renew"
                title="Renouveler la clé"
                summary={key.fingerprint ? `Empreinte ${key.fingerprint}` : undefined}
                open={open === 'renew'}
                onToggle={toggle}
              >
                <p className="m-0 text-[12.5px] leading-[19px] text-muted">
                  Clé actuelle : empreinte{' '}
                  <span className="font-mono text-ink">{key.fingerprint}</span>
                  {key.createdAt && `, créée le ${new Date(key.createdAt).toLocaleDateString('fr-FR')}`}.
                </p>

                <Regenerate onDone={reload} />
              </Section>
            </>
          )}

          <Section
            id="chiffrement"
            title="Chiffrement du disque"
            summary="Si l’ordinateur est perdu ou volé"
            open={open === 'chiffrement'}
            onToggle={toggle}
          >
            <DiskEncryptionBanner />

            <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
              Vos documents restent dans vos répertoires : Avocado ne les déplace pas. Sur cette
              machine, c’est le chiffrement du disque qui les protège.
            </p>
          </Section>

          <Section
            id="storage"
            title="Emplacements"
            summary="Coffre et dossier de travail"
            open={open === 'storage'}
            onToggle={toggle}
          >
            <Storage />
          </Section>
        </Group>

        <Group
          title="L’application"
          summary="Version et import"
        >
          <Section
            id="version"
            title="Version et mises à jour"
            summary="La version installée"
            open={open === 'version'}
            onToggle={toggle}
          >
            <About />
          </Section>

          <Section
            id="import"
            title="Importer depuis Gestisoft"
            summary="Reprendre les dossiers exportés"
            open={open === 'import'}
            onToggle={toggle}
          >
            <Import />
          </Section>
        </Group>

      </div>
    </Panel>
  )
}

/**
 * Where the two folders are, and why they are two.
 *
 * The distinction matters and is not obvious, so it is stated rather than left to be discovered:
 * losing the coffre is what the recovery key exists for, and losing the working folder costs at most
 * the last few seconds of typing.
 */
function Storage() {
  const [settings, setSettings] = useState<PracticeSettings | null>(null)

  const reload = () =>
    api<PracticeSettings>('/api/settings').then(setSettings).catch(() => setSettings(null))

  useEffect(() => {
    void reload()
  }, [])

  if (!settings) return null

  return (
    <>
      <div className="grid gap-1">
        <span className="type-label text-ink-secondary">Le coffre</span>
        <code className="rounded-sm bg-sunken px-2 py-1.5 font-mono text-[11.5px] break-all">
          {settings.vaultDirectory}
        </code>
        <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
          Tout y est chiffré : journal, tiers, facturation, temps passé, modèles. Vos documents
          restent dans vos répertoires. C’est ce dossier qu’il faut sauvegarder.
        </p>
      </div>

    </>
  )
}

/**
 * The rate a new dossier starts from, and only that. It is copied onto the dossier at creation, so
 * changing it here prices tomorrow's work and leaves every hour already recorded exactly as it was.
 */
/**
 * Her own addresses, which is the whole of « reçu ou envoyé ».
 *
 * <p>Nothing in the vault used to say who she was, so every message went into the journal as reçu,
 * 4 713 of them in the real import, including every one she had sent. There is no clever way to infer
 * it: an earlier attempt asked whether the sender was in the carnet, which answers a different
 * question. So it is asked, once.</p>
 */
function OwnAddresses() {
  const [addresses, setAddresses] = useState('')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<PracticeSettings>('/api/settings')
      .then((settings) => setAddresses((settings.emailAddresses ?? []).join('\n')))
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [])

  async function save() {
    setBusy(true)
    setError(null)

    try {
      await api('/api/settings', {
        method: 'PUT',
        // The rate travels with it because the endpoint takes the practice's settings whole. Reading
        // it back first would race with her typing; sending what is on screen would need it on screen.
        body: JSON.stringify({
          hourlyRateCents: (await api<PracticeSettings>('/api/settings')).hourlyRateCents,
          emailAddresses: addresses.split('\n'),
        }),
      })

      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Une par ligne. Un courriel parti de l’une d’elles est classé « envoyé », les autres
        « reçu ».{' '}
        <strong className="font-medium text-ink-secondary">
          @votre-cabinet.fr prend tout le domaine d’un coup.
        </strong>
      </p>

      <textarea
        rows={3}
        value={addresses}
        aria-label="Vos adresses électroniques"
        placeholder={'@mon-cabinet.fr\nmaitre@gmail.com'}
        onChange={(event) => { setAddresses(event.target.value); setError(null) }}
        className="w-full max-w-[52ch] rounded-sm border border-line-strong bg-sunken px-2 py-1.5 font-mono text-[12px] text-ink placeholder:text-muted focus-visible:border-[var(--focus-ring)]"
      />

      <div className="flex flex-wrap items-center gap-2">
        <Button disabled={busy} onClick={() => void save()}>Enregistrer</Button>

        {saved && (
          <span className="flex items-center gap-1.5 text-[12.5px] text-success">
            <Check size={13} strokeWidth={2.5} />
            Enregistré
          </span>
        )}
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </>
  )
}

function HourlyRate() {
  const [rate, setRate] = useState('')
  const [exercice, setExercice] = useState('01-01')
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<PracticeSettings>('/api/settings')
      .then((settings) => {
        setRate(centsToAmount(settings.hourlyRateCents))
        setExercice(settings.exerciceStart)
      })
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)),
      )
  }, [])

  async function save() {
    const cents = parseAmountToCents(rate)

    if (cents === null) {
      setError('Indiquez un taux horaire positif, par exemple 240,00.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await api('/api/settings', {
        method: 'PUT',
        body: JSON.stringify({ hourlyRateCents: cents, exerciceStart: exercice }),
      })
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Pré-remplit les nouveaux dossiers. Chaque dossier garde ensuite le sien : le modifier ici ne
        retarife rien de déjà saisi.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <Input
          inputSize="lg"
          className="w-28 font-mono tnum"
          invalid={Boolean(error)}
          value={rate}
          aria-label="Taux horaire"
          onChange={(event) => { setRate(event.target.value); setError(null) }}
        />
        <span className="text-[12.5px] text-muted">€ HT / heure</span>

        <Button disabled={busy} onClick={() => void save()}>Enregistrer</Button>

        {saved && (
          <span className="flex items-center gap-1.5 text-[12.5px] text-success">
            <Check size={13} strokeWidth={2.5} />
            Enregistré
          </span>
        )}
      </div>

      {/* Beside the rate because they are the two numbers the facturation is counted with, and
          because a chiffre d'affaires counted against the wrong year is a figure she would trust. */}
      <div className="flex flex-wrap items-center gap-2 border-t border-line-subtle pt-3">
        <span className="text-[12.5px] text-muted">L’exercice comptable commence le</span>

        <Select
          value={exercice.slice(3)}
          aria-label="Jour de début d’exercice"
          onChange={(event) => setExercice(`${exercice.slice(0, 2)}-${event.target.value}`)}
        >
          {Array.from({ length: 28 }, (_, index) => String(index + 1).padStart(2, '0')).map((day) => (
            <option key={day} value={day}>{Number(day)}</option>
          ))}
        </Select>

        <Select
          value={exercice.slice(0, 2)}
          aria-label="Mois de début d’exercice"
          onChange={(event) => setExercice(`${event.target.value}-${exercice.slice(3)}`)}
        >
          {MONTHS.map((month, index) => (
            <option key={month} value={String(index + 1).padStart(2, '0')}>{month}</option>
          ))}
        </Select>
      </div>

      <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        La date à laquelle votre chiffre d’affaires repart de zéro sur l’accueil.
      </p>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </>
  )
}

/**
 * Ce qui est installé, et s’il existe mieux.
 *
 * <p>The version was shown nowhere at all until this existed, which made « j’ai remplacé
 * l’application et ça n’a pas marché » a sentence nobody could check. It is the first line here for
 * that reason: whatever else this section does, it answers that question.</p>
 */
function About() {
  const [running, setRunning] = useState<string | null>(null)
  const [latest, setLatest] = useState<LatestVersion | null>(null)
  const [checking, setChecking] = useState(false)
  const [enabled, setEnabled] = useState(updateChecksEnabled)

  useEffect(() => {
    void window.avocado.connection().then((connection) => setRunning(connection.version))
  }, [])

  useEffect(() => {
    if (!enabled) return

    const controller = new AbortController()
    void fetchLatest(controller.signal).then(setLatest)

    return () => controller.abort()
  }, [enabled])

  async function checkNow() {
    setChecking(true)
    try {
      // Forced: the once-a-day rule is there so the feed is not polled, not to stop her asking.
      localStorage.removeItem('avocado.updates.lastCheck')
      setLatest(await fetchLatest())
    } finally {
      setChecking(false)
    }
  }

  const behind = latest !== null && running !== null && isNewer(latest.version, running)

  return (
    <>
      <div className="flex flex-wrap items-baseline gap-2">
        <span className="text-[12.5px] text-muted">Version installée</span>
        <span className="font-mono text-[13px] tnum text-ink">{running ?? 'n/d'}</span>
      </div>

      {behind && latest && (
        <div className="flex flex-wrap items-center gap-2.5 rounded-sm border border-[#E8D5AE] bg-warning-bg px-2.5 py-2">
          <span className="text-[12.5px] text-ink">
            La version <strong>{latest.version}</strong> est disponible.
          </span>
          <Button
            size="sm"
            variant="secondary"
            onClick={() => window.open(latest.url, '_blank', 'noopener,noreferrer')}
          >
            Voir les nouveautés
          </Button>
        </div>
      )}

      {enabled && !behind && latest !== null && (
        <p className="m-0 text-[12.5px] text-success">Vous avez la dernière version.</p>
      )}

      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Avocado ne se met jamais à jour tout seul. Une fois par jour, il demande au dépôt public le
        dernier numéro publié, et rien d’autre ne sort de votre ordinateur.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <label className="flex items-center gap-2 text-[12.5px]">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) => {
              setUpdateChecksEnabled(event.target.checked)
              setEnabled(event.target.checked)
              if (!event.target.checked) setLatest(null)
            }}
          />
          Vérifier si une nouvelle version est disponible
        </label>

        {enabled && (
          <Button variant="secondary" size="sm" disabled={checking} onClick={() => void checkNow()}>
            {checking ? 'Vérification…' : 'Vérifier maintenant'}
          </Button>
        )}
      </div>

      {!enabled && (
        <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
          Aucune requête n’est faite. Les nouvelles versions sont annoncées sur le site.
        </p>
      )}
    </>
  )
}

/** What Réglages knows about the stamp before it has fetched the image itself. */
interface StampView {
  configured: boolean
  svg: boolean
  awaitingRender: boolean
  options: StampOptions
}

interface StampOptions {
  corner: 'TopRight' | 'TopLeft' | 'BottomRight' | 'BottomLeft'
  widthPoints: number
  marginPoints: number
  numberXPercent: number
  numberYPercent: number
  numberSizePoints: number
  numberColour: string | null
  everyPage: boolean
}

const CORNERS: { value: StampOptions['corner']; label: string }[] = [
  { value: 'TopRight', label: 'en haut à droite' },
  { value: 'TopLeft', label: 'en haut à gauche' },
  { value: 'BottomRight', label: 'en bas à droite' },
  { value: 'BottomLeft', label: 'en bas à gauche' },
]

/**
 * Le tampon du cabinet.
 *
 * <p>The preview is the point. Lining a number up with the dotted line drawn on somebody else's logo
 * is not something anyone gets right from three percentages typed into a form, so Avocado stamps a
 * blank page with a plausible number and she nudges until it sits where it should. Without it these
 * are four numbers guessed at, with a fifteen-pièce batch as the feedback loop.</p>
 */
/**
 * What colour the number has to be to be seen on this stamp.
 *
 * <p>Read off the image, where the number will actually land, because the answer is a property of
 * her logo and not something she should have to think about. A black square with « Pièce n° ....... »
 * in pale grey wants a pale number; a letterhead on white wants a black one. Both exist among the
 * ten practices in the beta, and one of them filed pièces carrying an invisible number.</p>
 *
 * <p>Composited over white before measuring, because a transparent stamp sits on a white page and it
 * is the page that shows through, not the transparency.</p>
 *
 * <p>Returns null when the canvas will not give up its pixels. That is not a failure worth reporting:
 * black is what it has always been, and the picker is right there.</p>
 */
function inkFor(image: HTMLImageElement, options: StampOptions): string | null {
  const width = image.naturalWidth
  const height = image.naturalHeight

  if (width === 0 || height === 0) return null

  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height

  const context = canvas.getContext('2d', { willReadFrequently: true })
  if (context === null) return null

  context.drawImage(image, 0, 0)

  // The box the digits occupy, in the image's own pixels. Widened to a couple of characters and
  // raised by the font size, the way the PDF draws it: the anchor is the text's top-left.
  const size = (options.numberSizePoints / Math.max(1, options.widthPoints)) * width
  const left = (options.numberXPercent / 100) * width
  const top = (options.numberYPercent / 100) * height - size

  const x = Math.max(0, Math.min(width - 1, Math.round(left)))
  const y = Math.max(0, Math.min(height - 1, Math.round(top)))
  const w = Math.max(1, Math.min(width - x, Math.round(size * 2)))
  const h = Math.max(1, Math.min(height - y, Math.round(size * 1.4)))

  let pixels: Uint8ClampedArray
  try {
    pixels = context.getImageData(x, y, w, h).data
  } catch {
    // A tainted canvas. The stamp comes from our own blob: URL so this should not happen, but a
    // thrown exception here would take the whole settings panel with it.
    return null
  }

  let sum = 0
  for (let index = 0; index < pixels.length; index += 4) {
    const alpha = pixels[index + 3]! / 255
    const over = (channel: number) => channel * alpha + 255 * (1 - alpha)

    // Rec. 709 luma. Green carries most of what the eye reads as brightness.
    sum += 0.2126 * over(pixels[index]!) + 0.7152 * over(pixels[index + 1]!) + 0.0722 * over(pixels[index + 2]!)
  }

  return sum / (pixels.length / 4) < 128 ? '#FFFFFF' : '#000000'
}

function Stamp() {
  const [view, setView] = useState<StampView | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [image, setImage] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const toasts = useToasts()

  const reload = () => {
    void api<StampView>('/api/settings/stamp')
      .then((fetched) => { setView(fetched); setError(null) })
      // Named, never swallowed. This used to set the view to null and the section then rendered
      // nothing, so a 404 from an out-of-date backend looked like a feature that was simply empty.
      .catch((failure: unknown) =>
        setError(failure instanceof ApiError ? failure.message : String(failure)))
  }

  useEffect(reload, [])

  /**
   * Fetched as a blob, not pointed at with a src.
   *
   * <p>Every call to the backend carries this launch's bearer token, and an <img> would reach it
   * without one and collect a 401. The same reason the download helper exists.</p>
   */
  useEffect(() => {
    if (view?.configured !== true) {
      setImage(null)
      return
    }

    let url: string | null = null

    void blob('/api/settings/stamp/image').then((fetched) => {
      url = URL.createObjectURL(fetched)
      setImage(url)
    })

    return () => {
      if (url !== null) URL.revokeObjectURL(url)
    }
  }, [view?.configured])

  async function choose() {
    const path = await window.avocado.chooseFile(
      'Choisir le tampon du cabinet', undefined, ['svg', 'png', 'jpg', 'jpeg'])

    if (!path) return

    setBusy(true)
    setError(null)

    try {
      await api('/api/settings/stamp', { method: 'PUT', body: JSON.stringify({ path }) })

      // An SVG is stored as she wrote it and is not drawable yet: the window renders it and sends
      // back the drawing, along with the place its placeholder was sitting.
      if (path.toLowerCase().endsWith('.svg')) {
        await render()
      }

      reload()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  /**
   * Turns her SVG into the image a pièce carries, and reads the number's place out of it.
   *
   * <p>Run again whenever the stamp's width changes, because the number's size is expressed in
   * points on the page and that is what the width decides.</p>
   */
  async function render(width?: number) {
    const source = await (await blob('/api/settings/stamp/svg')).text()
    const derived = await deriveFromSvg(source, width ?? view?.options.widthPoints ?? 120)

    await api('/api/settings/stamp/rendered', {
      method: 'PUT',
      body: await derived.png.arrayBuffer(),
      headers: { 'content-type': 'image/png' },
    })

    await api('/api/settings/stamp/options', {
      method: 'PUT',
      body: JSON.stringify({
        ...(view?.options ?? {}),
        widthPoints: width ?? view?.options.widthPoints ?? 120,
        numberXPercent: derived.numberXPercent,
        numberYPercent: derived.numberYPercent,
        numberSizePoints: derived.numberSizePoints,
        numberColour: derived.numberColour,
      }),
    })
  }

  async function save(options: StampOptions) {
    setView((current) => (current === null ? current : { ...current, options }))

    try {
      await api('/api/settings/stamp/options', { method: 'PUT', body: JSON.stringify(options) })
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  async function preview() {
    try {
      const saved = await saveAs('/api/settings/stamp/preview.pdf', 'apercu-tampon.pdf')
      if (saved) toasts.succeeded('Aperçu enregistré', saved)
    } catch (failure) {
      toasts.failed('Aperçu impossible', failure instanceof ApiError ? failure.message : String(failure))
    }
  }

  /**
   * Forgets the stamp. Not asked about first: nothing filed changes, and the way back is the file
   * she picked it from. What it does change is that pièces versed from now on carry no stamp, so the
   * panel says so rather than looking merely empty.
   */
  async function remove() {
    setBusy(true)
    setError(null)
    setTesting(false)

    try {
      await api('/api/settings/stamp', { method: 'DELETE' })
      reload()
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  if (view === null) {
    return error === null ? null : <Unavailable detail={error} onRetry={reload} />
  }

  const options = view.options

  return (
    <>
      {toasts.view}

      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Votre tampon, tel que vous l’utilisez déjà. Avocado écrit le numéro de pièce par-dessus.
      </p>

      <div className="grid gap-1.5 rounded-md border border-line-subtle bg-app px-3 py-2.5">
        <span className="type-label text-ink-secondary">Deux façons de le donner</span>
        <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
          <strong className="font-medium text-ink">Un SVG, de préférence.</strong> Écrivez{' '}
          <code className="rounded-[3px] bg-sunken px-1 py-px font-mono text-[11px]">{'{{numero}}'}</code>{' '}
          dedans, à l’endroit du numéro : Avocado le trouve, et reprend sa position, sa taille et sa
          couleur. Rien à régler.
        </p>
        <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
          <strong className="font-medium text-ink">Ou une image</strong>, PNG à fond transparent de
          préférence, pour ne pas masquer la pièce. Vous placez le numéro vous-même, ci-dessous.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        {image !== null && (
          <img
            src={image}
            alt="Tampon du cabinet"
            className="h-16 w-16 rounded-sm border border-line-subtle bg-app object-contain p-1"
            // Only for an image she placed herself, and only while nobody has chosen. An SVG brings
            // its own colour from the placeholder, and once she has touched the picker it is hers.
            onLoad={(event) => {
              if (view.svg || view.options.numberColour !== null) return

              const ink = inkFor(event.currentTarget, view.options)
              if (ink !== null) void save({ ...view.options, numberColour: ink })
            }}
          />
        )}

        <Button variant="secondary" disabled={busy} onClick={() => void choose()}>
          {view.configured ? 'Changer le tampon' : 'Choisir un tampon'}
        </Button>

        {view.configured && image !== null && (
          <Button variant="secondary" onClick={() => setTesting(true)}>Essayer</Button>
        )}

        {view.configured && (
          <Button variant="secondary" onClick={() => void preview()}>Enregistrer un aperçu PDF</Button>
        )}

        {/* Shown for a stored SVG too, even when it never drew: a stamp that cannot be rendered is
            exactly the one she needs to get rid of, and there was no way out of that state. */}
        {(view.configured || view.svg) && (
          <Button
            variant="secondary"
            size="icon"
            aria-label="Retirer le tampon"
            title="Retirer le tampon"
            disabled={busy}
            onClick={() => void remove()}
          >
            <Trash2 size={13} strokeWidth={1.75} />
          </Button>
        )}
      </div>

      {!view.configured && !view.svg && (
        <p className="m-0 text-[11.5px] text-muted">
          Aucun tampon : vos pièces seront numérotées et copiées, sans tampon.
        </p>
      )}

      {view.svg && (
        <p className="m-0 flex items-center gap-1.5 text-[11.5px] text-muted">
          <Check size={13} strokeWidth={2.5} className="text-success" />
          Tampon SVG : le numéro suit {'{{numero}}'}, vous n’avez rien à placer.
        </p>
      )}

      {view.awaitingRender && (
        <div className="grid gap-1.5">
          <p className="m-0 max-w-[72ch] text-[11.5px] leading-[17px] text-danger">
            Ce SVG est enregistré mais n’a pas encore été dessiné, donc rien ne sera tamponné.
            Vérifiez qu’il contient {'{{numero}}'} en vrai texte, puis réessayez.
          </p>
          <div>
            <Button
              variant="secondary"
              size="sm"
              disabled={busy}
              onClick={() => {
                setBusy(true)
                setError(null)
                void render()
                  .then(reload)
                  .catch((failure: unknown) =>
                    setError(failure instanceof Error ? failure.message : String(failure)))
                  .finally(() => setBusy(false))
              }}
            >
              Dessiner le tampon
            </Button>
          </div>
        </div>
      )}

      {testing && image !== null && (
        <StampTester image={image} options={options} onClose={() => setTesting(false)} />
      )}

      <AiPrompt />

      {view.configured && (
        <>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <label className="flex items-center gap-1.5 text-[12.5px]">
              Posé
              <Select
                value={options.corner}
                onChange={(event) => void save({ ...options, corner: event.target.value as StampOptions['corner'] })}
              >
                {CORNERS.map((corner) => (
                  <option key={corner.value} value={corner.value}>{corner.label}</option>
                ))}
              </Select>
            </label>

            <Nudge label="Largeur" value={options.widthPoints} unit="pt"
              onChange={(widthPoints) => {
                // The number's size is in points on the page, and the width is what sets the scale,
                // so an SVG stamp works its placement out again rather than keeping a stale size.
                if (view.svg) {
                  void render(widthPoints).then(reload).catch((failure: unknown) =>
                    setError(failure instanceof Error ? failure.message : String(failure)))
                  return
                }

                void save({ ...options, widthPoints })
              }} />
            <Nudge label="Marge" value={options.marginPoints} unit="pt"
              onChange={(marginPoints) => void save({ ...options, marginPoints })} />
          </div>

          {!view.svg && (
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <span className="text-[12.5px] text-muted">Le numéro, dans le tampon :</span>
            <Nudge label="depuis la gauche" value={options.numberXPercent} unit="%"
              onChange={(numberXPercent) => void save({ ...options, numberXPercent })} />
            <Nudge label="depuis le haut" value={options.numberYPercent} unit="%"
              onChange={(numberYPercent) => void save({ ...options, numberYPercent })} />
            <Nudge label="taille" value={options.numberSizePoints} unit="pt"
              onChange={(numberSizePoints) => void save({ ...options, numberSizePoints })} />
            <label className="flex items-center gap-1.5 text-[12.5px] text-muted">
              couleur
              <input
                type="color"
                aria-label="Couleur du numéro"
                value={options.numberColour ?? '#000000'}
                onChange={(event) => void save({ ...options, numberColour: event.target.value })}
                className="h-6 w-9 cursor-pointer rounded-sm border border-line-strong bg-app p-0.5"
              />
            </label>
          </div>
          )}

          <label className="flex items-center gap-2 text-[12.5px]">
            <input
              type="checkbox"
              checked={options.everyPage}
              onChange={(event) => void save({ ...options, everyPage: event.target.checked })}
            />
            Tamponner toutes les pages, et pas seulement la première
          </label>
        </>
      )}

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}
    </>
  )
}

/**
 * What to ask an assistant for, when the cabinet's logo exists only as a PNG.
 *
 * <p>Most cabinets have their stamp as an image and no way to make an SVG of it. Asking one of the
 * assistants they already have open is now the shortest route, so the instructions are here, exact
 * and ready to paste, rather than left as « convertissez-le en SVG » and an afternoon lost.</p>
 *
 * <p>The wording matters more than it looks: it has to insist on live text for the placeholder. Most
 * tools convert text to outlines by default, and an outlined {{numero}} is a drawing of a
 * placeholder that nothing can find.</p>
 */
function AiPrompt() {
  const [copied, setCopied] = useState(false)

  const prompt = [
    'Convertis ce logo en SVG pour servir de tampon de pièces.',
    '',
    'Contraintes :',
    '- fond transparent, aucun rectangle de fond ;',
    '- le dessin doit remplir le viewBox, sans marge inutile ;',
    '- ajoute le texte « Pièce n° {{numero}} » là où le numéro doit figurer ;',
    '- {{numero}} doit rester du vrai texte dans un élément <text>, surtout pas vectorisé ;',
    '- donne à ce texte la taille et la couleur voulues pour le numéro ;',
    '- pas de police externe, pas d’image incorporée, pas de script.',
    '',
    'Réponds uniquement avec le contenu du fichier .svg.',
  ].join('\n')

  return (
    <details className="rounded-md border border-line-subtle bg-app px-3.5 py-3">
      <summary className="cursor-pointer text-[12.5px] font-medium">
        Vous n’avez qu’une image ? Faites-en un SVG
      </summary>

      <p className="m-0 mt-2.5 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
        Copiez ce texte dans Claude, ChatGPT ou l’assistant de votre choix, joignez votre logo, et
        enregistrez la réponse dans un fichier <code className="font-mono text-[11px]">.svg</code>.
        Revenez ensuite ici le choisir.
      </p>

      <pre className="m-0 mt-2 max-h-[220px] overflow-auto rounded-sm bg-sunken px-2.5 py-2 font-mono text-[11px] leading-[16px] whitespace-pre-wrap">
        {prompt}
      </pre>

      <div className="mt-2 flex items-center gap-2">
        <Button
          variant="secondary"
          size="sm"
          onClick={() => {
            void navigator.clipboard.writeText(prompt)
            setCopied(true)
            setTimeout(() => setCopied(false), 2000)
          }}
        >
          {copied ? <Check size={13} strokeWidth={2.5} /> : null}
          {copied ? 'Copié' : 'Copier le texte'}
        </Button>
      </div>
    </details>
  )
}

function Nudge({ label, value, unit, onChange }: {
  label: string
  value: number
  unit: string
  onChange: (value: number) => void
}) {
  return (
    <label className="flex items-center gap-1.5 text-[12.5px] text-muted">
      {label}
      <Input
        inputSize="sm"
        className="w-[62px] text-right font-mono tnum"
        inputMode="decimal"
        value={String(value)}
        onChange={(event) => {
          const parsed = Number(event.target.value.replace(',', '.'))
          if (Number.isFinite(parsed)) onChange(parsed)
        }}
      />
      {unit}
    </label>
  )
}

const MONTHS = [
  'janvier', 'février', 'mars', 'avril', 'mai', 'juin',
  'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre',
]

/** Full-width row, separated by rules rather than boxed: this list is meant to grow. */
/**
 * A theme, and the sections under it.
 *
 * <p>Réglages had grown to eleven sections in one flat list, in the order they happened to be
 * written, so finding « le tampon des pièces » meant reading all eleven titles. Three themes answer
 * the question she actually arrives with, which is never « which setting is this » but « where would
 * this live »: what the cabinet is, where its data is, and what the application itself is doing.</p>
 *
 * <p>The groups stay open. They are not a second thing to expand before reaching the thing she came
 * for; they are headings, and their whole job is to be read at a glance.</p>
 */
function Group({ title, summary, children }: {
  title: string
  summary: string
  children: ReactNode
}) {
  return (
    <section className="border-b border-line-strong last:border-b-0">
      <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
        <h2 className="m-0 type-label text-ink-secondary">{title}</h2>
        <p className="m-0 text-[11.5px] leading-4 text-muted">{summary}</p>
      </header>

      {/* The last section in a group drops its own rule: the group's already closes it, and two
          lines a pixel apart read as a mistake. */}
      <div className="[&>section:last-child]:border-b-0">{children}</div>
    </section>
  )
}

function Section({ id, title, summary, open, onToggle, children }: {
  id: string
  title: string
  summary?: string
  open: boolean
  onToggle: (id: string) => void
  children: ReactNode
}) {
  return (
    <section className="border-b border-line-subtle">
      <button
        type="button"
        onClick={() => onToggle(id)}
        className="flex w-full items-center gap-2 px-4 py-3 text-left hover:bg-hover"
      >
        <ChevronDown
          size={14}
          strokeWidth={2}
          className={cn(
            'shrink-0 text-ink-secondary transition-transform',
            !open && '-rotate-90',
          )}
        />

        <span className="text-[13px] font-medium">{title}</span>

        {summary && (
          <span className="truncate font-mono text-[11px] text-muted">{summary}</span>
        )}
      </button>

      {open && <div className="grid gap-3 px-4 pt-0 pb-4 pl-10">{children}</div>}
    </section>
  )
}

/** « Retrouvez votre fiche, et recopiez deux groupes. » */
function QuarterlyCheck() {
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
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px]">
        Retrouvez votre fiche et recopiez deux groupes.
      </p>

      <div className="flex flex-wrap gap-4">
        {indices.map((index) => (
          <label key={index} className="grid gap-1 text-[11px] text-muted">
            Groupe n° {index + 1}
            <span className="flex items-center gap-1.5">
              <Input
                inputSize="lg"
                maxLength={8}
                value={values[index] ?? ''}
                placeholder="······"
                className="w-[120px] text-center font-mono tracking-[0.12em] uppercase"
                onChange={(event) => {
                  setValues({ ...values, [index]: event.target.value })
                  setResult(null)
                }}
              />

              {/* The glyph, not just a colour: this is the one screen where being sure matters. */}
              {result?.[index] === true && <Check size={14} strokeWidth={2.5} className="text-success" />}
              {result?.[index] === false && <X size={14} strokeWidth={2.5} className="text-danger" />}
            </span>
          </label>
        ))}
      </div>

      {passed && (
        <p className="m-0 flex items-center gap-1.5 text-[12.5px] text-success">
          <Check size={13} strokeWidth={2.5} />
          Votre fiche est la bonne.
        </p>
      )}

      {result !== null && !passed && (
        <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
          Un groupe ne correspond pas. Vérifiez la ligne, ou éditez une nouvelle clé si la fiche est
          introuvable.
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        <Button
          disabled={busy || indices.some((index) => !values[index])}
          onClick={() => void verify()}
        >
          Vérifier
        </Button>

        <Button
          variant="secondary"
          onClick={() => {
            setIndices(pickTwo())
            setValues({})
            setResult(null)
          }}
        >
          Deux autres groupes
        </Button>
      </div>
    </>
  )
}

/** The renewal itself: the ochre statement of consequence, then the same securing step as at setup. */
function Regenerate({ onDone }: { onDone: () => void }) {
  const [issued, setIssued] = useState<RecoveryKeyState | null>(null)
  const [secured, setSecured] = useState<SecuredBy>(nothingSecured)
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

  /** Clears the issued key from the screen as well as reloading, or "Terminé" appears to do nothing. */
  function finish() {
    setIssued(null)
    setSecured(nothingSecured)
    onDone()
  }

  if (!issued?.code) {
    return (
      <>
        <div className="grid max-w-[72ch] gap-1 rounded-md border border-accent bg-accent-subtle px-3.5 py-3 text-warning">
          <strong className="text-[12.5px] font-semibold">Ce qui change</strong>
          <p className="m-0 text-[12px] leading-[18px]">
            Les sauvegardes faites <strong className="font-semibold">à partir de maintenant</strong>{' '}
            s’ouvriront avec la nouvelle clé. Les précédentes exigeront toujours l’ancienne : gardez
            la fiche.
          </p>
        </div>

        {error && <p className="m-0 text-danger">{error}</p>}

        <div className="flex">
          <Button disabled={busy} onClick={() => void regenerate()}>
            <RefreshCw size={13} strokeWidth={1.75} />
            {busy ? 'Génération…' : 'Éditer une nouvelle clé'}
          </Button>
        </div>
      </>
    )
  }

  return (
    <>
      <RecoveryKeyCard
        recoveryCode={issued.code}
        createdOn={createdOn}
        onCopied={() => setSecured((current) => ({ ...current, copied: true }))}
      />

      <div className="text-[12.5px] font-medium">Mettez cette nouvelle clé à l’abri :</div>

      <SecureKeyOptions
        recoveryCode={issued.code}
        fingerprint={issued.fingerprint ?? ''}
        createdOn={createdOn}
        secured={secured}
        onSecured={setSecured}
      />

      <div className="flex">
        <Button disabled={!isSecured(secured)} onClick={finish}>Terminé</Button>
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
