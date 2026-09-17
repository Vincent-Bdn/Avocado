/**
 * The second, and last, request Avocado makes: has a newer version been published?
 *
 * <p><b>It asks, it never installs.</b> The answer is a version number. Nothing is downloaded, no
 * binary is replaced, and nothing at all happens without her going to the site and deciding. « Pas
 * de mise à jour silencieuse » is still true, and is the reason this is a notice rather than an
 * updater.</p>
 *
 * <p><b>Why it exists.</b> A lawyer in the beta was running a version four releases old and had no
 * way of knowing: nothing in the application said which one was installed, and replacing an app on
 * macOS gives no feedback either. Bugs fixed in August were still being reported in September. A
 * cabinet that never hears about a fix is running the bug for as long as it takes someone to think
 * of asking.</p>
 *
 * <p><b>What leaves the machine.</b> A GET to the public releases feed. No account, no token, no
 * identifier, no version of ours in the query, no dossier, no name. GitHub learns that somebody's IP
 * looked at a public page, which is what it learns from anyone opening the repository in a browser.
 * It runs from the renderer, like the annuaire, so the backend and the vault keep no outbound
 * capability at all. Off turns it off completely: no request is made and the section says so.</p>
 */

const ENDPOINT = 'https://api.github.com/repos/Vincent-Bdn/Avocado/releases/latest'
const SETTING = 'avocado.updates'
const LAST_CHECK = 'avocado.updates.lastCheck'

export const RELEASES_PAGE = 'https://vincent-bdn.github.io/Avocado/installation.html'

/** Once a day is enough for a desktop application nobody ships twice in an afternoon. */
const A_DAY = 24 * 60 * 60 * 1000

export interface LatestVersion {
  version: string
  url: string
}

export function updateChecksEnabled(): boolean {
  return localStorage.getItem(SETTING) !== 'off'
}

export function setUpdateChecksEnabled(enabled: boolean): void {
  localStorage.setItem(SETTING, enabled ? 'on' : 'off')

  // Turning it back on should answer now rather than tomorrow.
  if (enabled) localStorage.removeItem(LAST_CHECK)
}

/**
 * The newest published version, or null when we are not asking, have asked recently, or could not
 * reach the feed.
 *
 * <p>Failure is silent on purpose. Being offline, or behind a palais de justice's wifi, is not
 * something to interrupt her with: the check is a convenience and a convenience that complains is
 * worse than none.</p>
 */
export async function fetchLatest(signal?: AbortSignal): Promise<LatestVersion | null> {
  if (!updateChecksEnabled()) return null

  const last = Number(localStorage.getItem(LAST_CHECK) ?? 0)
  if (Number.isFinite(last) && Date.now() - last < A_DAY) return null

  try {
    const response = await fetch(ENDPOINT, {
      signal,
      headers: { Accept: 'application/vnd.github+json' },
    })

    if (!response.ok) return null

    const payload = (await response.json()) as { tag_name?: string; html_url?: string }
    const tag = payload.tag_name?.trim()

    if (!tag) return null

    // Written down whatever the answer was: a rate-limited feed must not be asked again in a loop.
    localStorage.setItem(LAST_CHECK, String(Date.now()))

    return { version: tag.replace(/^v/, ''), url: payload.html_url ?? RELEASES_PAGE }
  } catch {
    return null
  }
}

/**
 * Whether <paramref name="latest"/> is genuinely newer than what is running.
 *
 * <p>Compared number by number rather than as strings, because « 0.0.10 » sorts before « 0.0.9 » as
 * text and would hide exactly the release somebody needs. A pre-release suffix loses to the plain
 * version of the same number, which is what semantic versioning says and what the release workflow
 * already assumes.</p>
 */
export function isNewer(latest: string, running: string): boolean {
  const parse = (value: string): { numbers: [number, number, number]; pre: string | null } => {
    const [core = '', pre] = value.split('-', 2)
    const parts = core.split('.').map((part) => Number(part) || 0)

    return {
      numbers: [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0],
      pre: pre ?? null,
    }
  }

  const a = parse(latest)
  const b = parse(running)

  for (let index = 0; index < 3; index++) {
    if (a.numbers[index] !== b.numbers[index]) {
      return (a.numbers[index] ?? 0) > (b.numbers[index] ?? 0)
    }
  }

  // Same numbers: a release beats the beta that led to it, and nothing beats a release.
  if (a.pre === b.pre) return false
  if (a.pre === null) return true
  if (b.pre === null) return false

  return a.pre > b.pre
}
