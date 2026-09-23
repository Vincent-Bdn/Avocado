import { useState } from 'react'
import { Dialog } from '../components/ui/dialog.js'
import { Input } from '../components/ui/input.js'

/** A4 in points, which is what every measurement here is in. */
const PAGE = { width: 595, height: 842 }

export interface TesterOptions {
  corner: 'TopRight' | 'TopLeft' | 'BottomRight' | 'BottomLeft'
  widthPoints: number
  marginPoints: number
  numberXPercent: number
  numberYPercent: number
  numberSizePoints: number
  numberColour: string | null
}

/**
 * The stamp on a page, here, now.
 *
 * <p><b>Why this exists.</b> Checking the number used to mean opening a dossier, ticking a document,
 * versing it, finding the file and opening it in a reader, then deleting the pièce and the number it
 * consumed. Nobody does that twice, so nobody nudged the percentages, so the number sat wherever the
 * defaults left it.</p>
 *
 * <p>The geometry is the backend's, repeated: the stamp is widthPoints wide in the chosen corner at
 * marginPoints from the edges, and the number's top-left sits at numberXPercent across and
 * numberYPercent down, less its own size. If those two ever disagree this page is lying, so they are
 * written the same way round and read from the same options.</p>
 */
export function StampTester({ image, options, onClose }: {
  image: string
  options: TesterOptions
  onClose: () => void
}) {
  const [number, setNumber] = useState('7')
  const [ratio, setRatio] = useState(1)

  const stampHeight = options.widthPoints * ratio
  const left = options.corner === 'TopLeft' || options.corner === 'BottomLeft'
  const top = options.corner === 'TopLeft' || options.corner === 'TopRight'

  const x = left ? options.marginPoints : PAGE.width - options.widthPoints - options.marginPoints
  const y = top ? options.marginPoints : PAGE.height - stampHeight - options.marginPoints

  const percent = (value: number, of: number) => `${(value / of) * 100}%`

  // The page is drawn at a fixed width, so one point is this many pixels. Sizes have to go through
  // it rather than through a CSS percentage: a percentage on font-size means « of the parent's font
  // size », which is not a width and would silently draw the wrong number.
  const SHOWN_WIDTH = 360
  const perPoint = SHOWN_WIDTH / PAGE.width
  const numberPx = options.numberSizePoints * perPoint

  return (
    <Dialog title="Essayer le tampon" width={640} onClose={onClose}>
      <p className="m-0 max-w-[72ch] text-[12px] leading-[18px] text-muted">
        La page telle qu’elle sortira. Changez le numéro, ou les réglages derrière, et regardez.
      </p>

      <label className="flex items-center gap-2 text-[12.5px]">
        Numéro d’essai
        <Input
          inputSize="sm"
          className="w-16 text-right font-mono tnum"
          value={number}
          onChange={(event) => setNumber(event.target.value)}
        />
      </label>

      <div className="grid place-items-center rounded-md bg-sunken p-4">
        {/* A4 at a fixed width; everything inside is a percentage of the page, so the proportions
            hold whatever this box is sized to. */}
        <div
          className="relative shadow-sm ring-1 ring-line-strong"
          style={{
            width: `${SHOWN_WIDTH}px`,
            aspectRatio: `${PAGE.width} / ${PAGE.height}`,
            background: '#FFFFFF',
          }}
        >
          <div
            className="absolute"
            style={{
              left: percent(x, PAGE.width),
              top: percent(y, PAGE.height),
              width: percent(options.widthPoints, PAGE.width),
            }}
          >
            <img
              src={image}
              alt=""
              className="block w-full"
              onLoad={(event) => {
                const drawing = event.currentTarget
                if (drawing.naturalWidth > 0) {
                  setRatio(drawing.naturalHeight / drawing.naturalWidth)
                }
              }}
            />

            <span
              className="absolute font-semibold leading-none"
              style={{
                left: `${options.numberXPercent}%`,
                top: `calc(${options.numberYPercent}% - ${numberPx}px)`,
                fontSize: `${numberPx}px`,
                color: options.numberColour ?? '#000000',
              }}
            >
              {number}
            </span>
          </div>
        </div>
      </div>

      <p className="m-0 text-[11.5px] text-muted">
        L’aperçu utilise une police proche de celle du PDF. Le placement, lui, est le même.
      </p>
    </Dialog>
  )
}
