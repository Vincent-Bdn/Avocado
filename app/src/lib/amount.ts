/**
 * « 1 480,00 », « 1480.5 », « 1 480 € » all mean the same thing to a French keyboard, and none of
 * them survive `Number()` unaided. Returning null rather than NaN is what lets a form say what is
 * wrong instead of posting a null amount and collecting a bare 400.
 */
export function parseAmountToCents(input: string): number | null {
  const cleaned = input.replace(/[\s €]/g, '').replace(',', '.')
  if (cleaned === '') return null

  const value = Number(cleaned)
  if (!Number.isFinite(value) || value <= 0) return null

  return Math.round(value * 100)
}

/** The inverse, for filling an edit form from what is stored. */
export function centsToAmount(cents: number): string {
  return (Math.abs(cents) / 100).toFixed(2).replace('.', ',')
}
