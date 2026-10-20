/**
 * Her stamp, when she gave an SVG.
 *
 * <p><b>Why the window does this and not the backend.</b> Drawing an SVG needs an SVG engine, and
 * adding one to the backend would mean a native graphics library on six platforms for one logo. The
 * window already contains a complete one. So Réglages reads the placeholder out of the live
 * document, takes it out, renders what is left to a transparent PNG, and hands the backend the same
 * kind of image it has always stamped with. Nothing downstream changed.</p>
 *
 * <p>The part she actually asked for is the placeholder: writing {{numero}} where the number goes,
 * instead of nudging four percentages until it lands on the dotted line.</p>
 */

/** Big enough that no stamp outgrows it, small enough to sit in a settings blob. */
const RENDER_WIDTH = 1600

export interface DerivedStamp {
  /** The PNG that goes on a pièce: her drawing, with the placeholder taken out. */
  png: Blob
  /** Where the number goes, as the backend expresses it. */
  numberXPercent: number
  numberYPercent: number
  numberSizePoints: number
  numberColour: string
}

export class StampSvgError extends Error {}

/** « rgb(9, 49, 38) » as « #093126 ». Anything unreadable falls back to black. */
function hexOf(colour: string): string {
  const parts = /rgba?\(([^)]+)\)/.exec(colour)
  if (parts === null) return colour.startsWith('#') ? colour : '#000000'

  const [red, green, blue] = parts[1]!.split(',').map((piece) => Number(piece.trim()))

  return (
    '#'
    + [red, green, blue]
      .map((channel) => Math.max(0, Math.min(255, Math.round(channel || 0))).toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase()
  )
}

/**
 * The element carrying the placeholder.
 *
 * <p>The deepest one, because a tspan inside a text inside a group all report the same text and
 * only the innermost has the box the number should occupy.</p>
 */
function placeholderIn(svg: SVGSVGElement, marker: string): SVGGraphicsElement | null {
  const carrying = [...svg.querySelectorAll('*')].filter(
    (element) => (element.textContent ?? '').includes(marker),
  )

  const deepest = carrying.findLast(
    (element): element is SVGGraphicsElement =>
      typeof (element as SVGGraphicsElement).getBBox === 'function',
  )

  return deepest ?? null
}

/**
 * One element's box, expressed in the root SVG's own coordinates.
 *
 * <p><code>getBBox()</code> is pre-transform: it describes the element in the space the element
 * itself is drawn in. What we need is where it lands in the drawing, which is that box carried
 * through every transform between it and the root. <code>getScreenCTM()</code> gives each element's
 * path all the way out to the screen, so composing one with the inverse of the other gives exactly
 * the step between them, whatever sits in the middle.</p>
 *
 * <p>All four corners are mapped and the extremes kept, because a transform may rotate: a stamp
 * whose text runs up the side still has a box, and it is not the one two opposite corners describe.</p>
 *
 * <p><code>scale</code> is how much a vertical unit grows along the way, which is what the declared
 * font size has to be multiplied by to mean anything in the root's space.</p>
 */
function inRootSpace(svg: SVGSVGElement, element: SVGGraphicsElement) {
  const box = element.getBBox()
  const outer = svg.getScreenCTM()
  const inner = element.getScreenCTM()

  // No layout, no matrices. Nothing has been transformed either, so the raw box is the right answer
  // rather than a guess: this is the case of a flat SVG, which is also the case that always worked.
  if (outer === null || inner === null) {
    return { x: box.x, y: box.y, width: box.width, height: box.height, scale: 1 }
  }

  const matrix = outer.inverse().multiply(inner)

  const corners = [
    new DOMPoint(box.x, box.y),
    new DOMPoint(box.x + box.width, box.y),
    new DOMPoint(box.x, box.y + box.height),
    new DOMPoint(box.x + box.width, box.y + box.height),
  ].map((corner) => corner.matrixTransform(matrix))

  const xs = corners.map((corner) => corner.x)
  const ys = corners.map((corner) => corner.y)

  const x = Math.min(...xs)
  const y = Math.min(...ys)

  return {
    x,
    y,
    width: Math.max(...xs) - x,
    height: Math.max(...ys) - y,
    // The length a unit vector pointing down becomes. Uniform scaling is the normal case; a sheared
    // stamp is not something to be clever about.
    scale: Math.hypot(matrix.c, matrix.d) || 1,
  }
}

/**
 * Reads the placeholder's place, removes it, and renders the rest.
 *
 * <p>Both halves have to happen on the same document: the box is only meaningful against the
 * drawing it was measured in.</p>
 */
export async function deriveFromSvg(source: string, widthPoints: number): Promise<DerivedStamp> {
  const parsed = new DOMParser().parseFromString(source, 'image/svg+xml')

  if (parsed.querySelector('parsererror') !== null) {
    throw new StampSvgError('Ce fichier SVG n’a pas pu être lu.')
  }

  const svg = parsed.documentElement as unknown as SVGSVGElement

  if (svg.tagName.toLowerCase() !== 'svg') {
    throw new StampSvgError('Ce fichier ne contient pas de dessin SVG.')
  }

  // getBBox only answers for a drawing that is laid out, so the document goes into the window,
  // off to one side. Not display:none, which would report zeroes.
  const stage = document.createElement('div')
  stage.setAttribute('style', 'position:fixed;left:-10000px;top:0;opacity:0;pointer-events:none')
  stage.appendChild(svg)
  document.body.appendChild(stage)

  try {
    const marker = '{{numero}}'
    const holder = placeholderIn(svg, marker)

    if (holder === null) {
      throw new StampSvgError(
        `Ce SVG ne contient pas ${marker}. Écrivez-le là où le numéro doit apparaître.`,
      )
    }

    const frame = svg.viewBox.baseVal
    const width = frame?.width || svg.getBBox().width || Number(svg.getAttribute('width')) || 0
    const height = frame?.height || svg.getBBox().height || Number(svg.getAttribute('height')) || 0

    if (width <= 0 || height <= 0) {
      throw new StampSvgError('Ce SVG n’a pas de dimensions utilisables.')
    }

    const left = frame?.x ?? 0
    const top = frame?.y ?? 0

    const style = window.getComputedStyle(holder)
    const colour = hexOf(style.fill || '#000000')

    // getBBox answers in the element's *own* user space and knows nothing about the transforms its
    // ancestors carry. Almost everything a cabinet hands over is wrapped in one — Illustrator and
    // Inkscape both emit <g transform="translate(…) scale(…)"> around the whole drawing — so the box
    // came back in coordinates that had nothing to do with the stamp, and the number landed beside
    // the dotted line instead of on it. « Le n° est mal positionné », and nothing about the stamp
    // itself was wrong.
    const box = inRootSpace(svg, holder)

    // The stamp is drawn widthPoints wide, so everything scales from that.
    const stampHeight = widthPoints * (height / width)

    // The font size is declared in the element's own space too, so it carries the same scale.
    const sizePoints =
      ((Number.parseFloat(style.fontSize) * box.scale) || box.height) * (stampHeight / height)

    const xPercent = ((box.x - left) / width) * 100

    // The backend puts the number's top at y + stampHeight * Y% − size, so the percentage carries
    // the size back out again. Doing it here keeps the backend's arithmetic untouched.
    const yPercent = (((box.y - top) / height) + (sizePoints / stampHeight)) * 100

    holder.remove()

    return {
      png: await rasterise(svg, width, height),
      numberXPercent: Math.round(xPercent * 10) / 10,
      numberYPercent: Math.round(yPercent * 10) / 10,
      numberSizePoints: Math.round(sizePoints * 10) / 10,
      numberColour: colour,
    }
  } finally {
    stage.remove()
  }
}

/** The drawing, minus the placeholder, as a transparent PNG. */
async function rasterise(svg: SVGSVGElement, width: number, height: number): Promise<Blob> {
  const clone = svg.cloneNode(true) as SVGSVGElement
  clone.setAttribute('width', String(width))
  clone.setAttribute('height', String(height))

  if (clone.getAttribute('viewBox') === null) {
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`)
  }

  const markup = new XMLSerializer().serializeToString(clone)
  const url = URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' }))

  try {
    const drawing = new Image()

    await new Promise<void>((resolve, reject) => {
      drawing.onload = () => resolve()
      drawing.onerror = () => reject(new StampSvgError('Ce SVG n’a pas pu être dessiné.'))
      drawing.src = url
    })

    const canvas = document.createElement('canvas')
    canvas.width = RENDER_WIDTH
    canvas.height = Math.max(1, Math.round(RENDER_WIDTH * (height / width)))

    const context = canvas.getContext('2d')
    if (context === null) throw new StampSvgError('Le rendu du tampon a échoué.')

    context.drawImage(drawing, 0, 0, canvas.width, canvas.height)

    return await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        (blob) => (blob === null ? reject(new StampSvgError('Le rendu du tampon a échoué.')) : resolve(blob)),
        'image/png',
      )
    })
  } finally {
    URL.revokeObjectURL(url)
  }
}
