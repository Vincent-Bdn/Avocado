import { useEffect, useRef, useState, type ReactNode } from 'react'
// The legacy build, deliberately. The modern one calls Uint8Array.prototype.toHex, which arrived
// in Chrome 140; this application ships Electron 34, which is Chromium 132, so the modern build
// opens nothing and says « a.toHex is not a function ». The legacy build is the same renderer
// compiled for older engines, and it is what keeps the viewer working on whatever Electron the
// packaged app happens to carry.
import { GlobalWorkerOptions, getDocument, type PDFDocumentProxy } from 'pdfjs-dist/legacy/build/pdf.mjs'
import workerUrl from 'pdfjs-dist/legacy/build/pdf.worker.min.mjs?url'
import { Button } from './button.js'
import { cn } from '../../lib/utils.js'

// Bundled and served from the application itself, never from a CDN: the page's own policy is
// script-src 'self', and a viewer that needs the internet to open a pièce would be the one thing
// this application has promised it is not.
GlobalWorkerOptions.workerSrc = workerUrl

/**
 * A rectangle drawn over the page, in the page's own coordinates.
 *
 * <p>Points from the bottom left, which is how a PDF measures itself and how every library that
 * reads one reports what it found. Converting here rather than at each call site means the thing
 * that extracts a total from a facture can hand over the box it read it from, untranslated.</p>
 */
export interface PdfMark {
  page: number
  x: number
  y: number
  width: number
  height: number
  /** Drawn inside the box, top left. A short label, not a sentence. */
  label?: string
  tone?: 'found' | 'doubt'
  onClick?: () => void
}

/**
 * A PDF, on screen, with room to draw on top of it.
 *
 * <p><b>Why not Chromium's own viewer.</b> An &lt;embed&gt; would show the file for nothing, and
 * that is where it stops: the plugin is a closed surface, so nothing can be positioned over a word
 * in it. Showing her what Avocado read out of a facture, next to the figure it read, means owning
 * the rendering. PDF.js draws to a canvas we place ourselves, so a box in page coordinates lands on
 * the right word at any zoom.</p>
 *
 * <p>Pages are rendered at the screen's own pixel density, so the text is sharp on a Retina Mac
 * rather than the blur an unscaled canvas gives.</p>
 */
export function PdfViewer({ file, marks = [], height = 560, children }: {
  /** The document's bytes. A fresh copy each time: PDF.js takes ownership of what it is given. */
  file: Uint8Array | null
  marks?: PdfMark[]
  height?: number
  /** Shown in the toolbar, to the right of the page count. */
  children?: ReactNode
}) {
  const [document_, setDocument] = useState<PDFDocumentProxy | null>(null)
  const [pages, setPages] = useState<{ width: number; height: number }[]>([])
  const [zoom, setZoom] = useState(1)
  const [error, setError] = useState<string | null>(null)
  // Her zoom, once she has one. Until then the page is fitted to the width, and a reload for a
  // different number must not throw that away.
  const [chosen, setChosen] = useState(false)
  const surface = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (file === null) {
      setDocument(null)
      setPages([])
      return
    }

    let cancelled = false
    // PDF.js transfers the buffer it is handed and leaves the caller's copy detached, which turns a
    // second open of the same bytes into an empty document. The copy is cheaper than that bug.
    const task = getDocument({ data: new Uint8Array(file) })

    task.promise.then(
      async (opened) => {
        if (cancelled) {
          void opened.destroy()
          return
        }

        const sizes = []
        for (let number = 1; number <= opened.numPages; number += 1) {
          const page = await opened.getPage(number)
          const view = page.getViewport({ scale: 1 })
          sizes.push({ width: view.width, height: view.height })
        }

        if (cancelled) {
          void opened.destroy()
          return
        }

        setDocument(opened)
        setPages(sizes)
        setError(null)
      },
      (failure: unknown) => {
        if (!cancelled) setError(failure instanceof Error ? failure.message : String(failure))
      },
    )

    return () => {
      cancelled = true
      void task.destroy()
    }
  }, [file])

  // Opening at 100 % put an A4 a few pixels wider than the dialog and gave it a horizontal
  // scrollbar, which is the one thing a viewer should never greet you with.
  useEffect(() => {
    const box = surface.current
    const first = pages[0]

    if (chosen || box === null || first === undefined) return

    const room = box.clientWidth - 24
    if (room > 0) setZoom(Math.max(0.35, Math.min(2, Math.round((room / first.width) * 100) / 100)))
  }, [chosen, pages])

  const change = (by: number) => {
    setChosen(true)
    setZoom((current) => Math.max(0.35, Math.min(4, Math.round((current + by) * 100) / 100)))
  }

  if (error !== null) {
    return <p className="m-0 text-[12.5px] text-danger">Ce PDF n’a pas pu être affiché : {error}</p>
  }

  return (
    <div className="grid gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-1">
          <Button
            variant="secondary"
            size="sm"
            aria-label="Réduire"
            onClick={() => change(-0.25)}
          >
            −
          </Button>
          <span className="w-12 text-center font-mono text-[11.5px] tnum text-muted">
            {Math.round(zoom * 100)} %
          </span>
          <Button
            variant="secondary"
            size="sm"
            aria-label="Agrandir"
            onClick={() => change(0.25)}
          >
            +
          </Button>
        </div>

        {pages.length > 1 && (
          <span className="text-[11.5px] text-muted">
            {pages.length} pages
          </span>
        )}

        {children}
      </div>

      <div
        ref={surface}
        className="grid justify-items-center gap-3 overflow-auto rounded-md bg-sunken p-3"
        style={{ maxHeight: `${height}px` }}
      >
        {document_ !== null && pages.map((size, index) => (
          <Page
            key={index}
            document={document_}
            number={index + 1}
            size={size}
            zoom={zoom}
            marks={marks.filter((mark) => mark.page === index + 1)}
          />
        ))}
      </div>
    </div>
  )
}

/** One page: a canvas, and an absolutely positioned layer the marks live in. */
function Page({ document: source, number, size, zoom, marks }: {
  document: PDFDocumentProxy
  number: number
  size: { width: number; height: number }
  zoom: number
  marks: PdfMark[]
}) {
  const canvas = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    let cancelled = false
    let task: { cancel: () => void } | null = null

    void source.getPage(number).then((page) => {
      const target = canvas.current
      if (cancelled || target === null) return

      // The screen's own density, so a Retina Mac gets a sharp page rather than a doubled blur.
      const density = Math.min(window.devicePixelRatio || 1, 3)
      const view = page.getViewport({ scale: zoom * density })

      target.width = Math.floor(view.width)
      target.height = Math.floor(view.height)
      target.style.width = `${size.width * zoom}px`
      target.style.height = `${size.height * zoom}px`

      const context = target.getContext('2d')
      if (context === null) return

      const render = page.render({ canvas: target, canvasContext: context, viewport: view })
      task = render
      void render.promise.catch(() => {
        // Cancelled by a zoom change or by the dialog closing. Not a failure worth showing.
      })
    })

    return () => {
      cancelled = true
      task?.cancel()
    }
  }, [source, number, size.width, size.height, zoom])

  return (
    <div
      className="relative shadow-sm ring-1 ring-line-strong"
      style={{ width: `${size.width * zoom}px`, height: `${size.height * zoom}px`, background: '#FFFFFF' }}
    >
      <canvas ref={canvas} className="block" />

      {marks.map((mark, index) => (
        <button
          key={index}
          type="button"
          onClick={mark.onClick}
          className={cn(
            'absolute cursor-default rounded-[2px] border text-left',
            mark.tone === 'doubt'
              ? 'border-[#E8D5AE] bg-[#FDF8ED]/55'
              : 'border-[#BFD3C5] bg-[#E7F0EA]/55',
            mark.onClick && 'cursor-pointer hover:bg-brand-subtle',
          )}
          style={{
            left: `${mark.x * zoom}px`,
            // Marks are given bottom-up, the way a PDF measures; the page is drawn top-down.
            top: `${(size.height - mark.y - mark.height) * zoom}px`,
            width: `${mark.width * zoom}px`,
            height: `${mark.height * zoom}px`,
          }}
        >
          {mark.label && (
            <span className="absolute -top-[15px] left-0 rounded-[2px] bg-ink px-1 text-[10px] leading-[14px] text-on-brand whitespace-nowrap">
              {mark.label}
            </span>
          )}
        </button>
      ))}
    </div>
  )
}
