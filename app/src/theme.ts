export type Theme = 'light' | 'dark'

/**
 * The design tokens define every colour under `[data-theme="light"]` or `[data-theme="dark"]` and
 * nothing under `:root`. Without this attribute the whole palette is undefined, no background, no
 * text colour, which renders as unreadable dark-on-dark.
 *
 * Light is the default. The design system proposes dark, and the OS preference is available through
 * `prefers-color-scheme`, but neither decides it here: a lawyer showing her screen to a client wants
 * the same interface she saw this morning, not one that changed when Windows switched at sunset.
 * A real switch belongs in Réglages.
 */
export function applyTheme(theme: Theme = 'light'): void {
  document.documentElement.setAttribute('data-theme', theme)
}
