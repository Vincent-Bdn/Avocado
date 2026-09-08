/**
 * What she types in the durée field, in minutes, or null when it means nothing.
 *
 * <p>The field used to be two boxes, heures and minutes, and the comment beside them said parsing
 * prose was how a duration silently lands wrong. That is still true, and it is why this accepts a
 * short, closed list of shapes and refuses everything else rather than guessing. What changed is the
 * evidence: lawyers record time in decimal hours because that is how a convention d'honoraires is
 * written and how every other tool in the office asks for it. « 0,25 » is a quarter of an hour to
 * them before it is fifteen minutes.</p>
 *
 * <p><b>A bare number is hours.</b> « 3 » is three hours, not three minutes, because that is the
 * convention she asked for and half-measures between the two would be worse than either. « 30 » is
 * therefore thirty hours, which looks alarming written down and is why the form echoes back what it
 * understood, in words, before anything is saved. Reading « 30 h » under the field is what catches
 * it; a parser that tried to be clever about which one she meant would not.</p>
 */
export function parseDuration(input: string): number | null {
  const text = input.trim().toLowerCase().replace(/\s+/g, '')

  if (!text) return null

  // « 1h30 », « 1h », « 1:30 », « h30 » is not offered: the hour has to be written.
  const composite = /^(\d{1,3})[h:](\d{0,2})$/.exec(text)
  if (composite) {
    const minutes = composite[2] === '' ? 0 : Number(composite[2])

    // « 1h75 » is a typo, not 2 h 15. Refusing is how she finds out now rather than on the facture.
    if (minutes > 59) return null

    return Number(composite[1]) * 60 + minutes
  }

  // « 45min », « 45mn », « 45m ». Explicit, so it cannot collide with the bare-number rule.
  const explicit = /^(\d{1,4})(?:min|mn|m)$/.exec(text)
  if (explicit) {
    return Number(explicit[1])
  }

  // « 0,25 », « 0.5 », « 2 », « 20,25 ». Hours, decimal, comma or point.
  const decimal = /^(\d{1,3})(?:[.,](\d{1,2}))?$/.exec(text)
  if (decimal) {
    const hours = Number(`${decimal[1]}.${decimal[2] ?? '0'}`)

    // Rounded to the minute, never floored: 0,33 h is 19,8 minutes and she meant twenty.
    return Math.round(hours * 60)
  }

  return null
}

/**
 * The other direction, for the field's initial value when correcting an entry.
 *
 * <p>Decimal hours rather than « 1 h 30 », so that what she sees in the box is something she could
 * have typed herself, and editing it behaves the way editing a number behaves.</p>
 */
export function toDurationInput(minutes: number): string {
  if (minutes <= 0) return ''

  const hours = minutes / 60

  // Two decimals is enough for every quarter and every sixth of an hour, and trailing zeroes are
  // noise in a box she is about to type over.
  return String(Number(hours.toFixed(2))).replace('.', ',')
}
