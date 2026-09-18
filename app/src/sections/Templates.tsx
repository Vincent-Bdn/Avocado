import { useEffect, useRef, useState } from 'react'
import { Check, FileText, Trash2, Upload } from 'lucide-react'
import { ApiError, api } from '../api.js'
import { Button } from '../components/ui/button.js'
import { Unavailable } from '../components/ui/unavailable.js'

interface TemplateItem {
  id: string
  name: string
  fileName: string
  sizeBytes: number
  updatedAt: string
}

interface Field {
  field: string
  example: string
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

/**
 * Le modèle du détail de facture.
 *
 * <p><b>This was a general system and is now one thing.</b> It let her upload any number of .docx
 * files, give each a free-text « type », and generate letters from them. Nobody used it, and trying
 * showed why: the generation had no button anywhere in the window, and the type, which is what
 * decides whether a template is ever used at all, could not be set while uploading. It had to be
 * typed afterwards into a rename field, spelled exactly right. A template uploaded the obvious way
 * sat there doing nothing, and the screen said nothing about it.</p>
 *
 * <p>So: one document, for the détail that goes out with a facture, used the moment it is dropped.
 * There is no type to get wrong and nothing to choose between.</p>
 */
export function Templates() {
  const [template, setTemplate] = useState<TemplateItem | null>(null)
  const [fields, setFields] = useState<Field[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const picker = useRef<HTMLInputElement>(null)

  const reload = () => {
    void api<TemplateItem[]>('/api/templates')
      .then((items) => { setTemplate(items[0] ?? null); setError(null) })
      .catch((failure: unknown) => setError(messageOf(failure)))

    void api<Field[]>('/api/templates/fields').then(setFields).catch(() => setFields(null))
  }

  useEffect(reload, [])

  async function upload(file: File) {
    if (!file.name.toLowerCase().endsWith('.docx')) {
      setError('Le modèle doit être un fichier .docx, enregistré depuis Word.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const body = new FormData()
      body.append('file', file)
      body.append('name', file.name.replace(/\.docx$/i, ''))

      await api('/api/templates', { method: 'POST', body })

      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
      reload()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (template === null) return

    try {
      await api(`/api/templates/${template.id}`, { method: 'DELETE' })
      reload()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  if (error !== null && template === null && fields === null) {
    return <Unavailable detail={error} onRetry={reload} />
  }

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Le détail des diligences que vous joignez à vos factures. Donnez à Avocado le document Word
        que vous envoyez déjà : votre en-tête, votre logo, vos colonnes et votre phrase de fin sont
        conservés tels quels. Sans modèle, Avocado produit une mise en page simple qui fait l’affaire.
      </p>

      <input
        ref={picker}
        type="file"
        accept=".docx"
        className="hidden"
        onChange={(event) => {
          const file = event.target.files?.[0]
          event.target.value = ''
          if (file) void upload(file)
        }}
      />

      {template === null ? (
        <div>
          <Button disabled={busy} onClick={() => picker.current?.click()}>
            <Upload size={14} strokeWidth={1.75} />
            Choisir mon modèle
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-2.5 rounded-md border border-line-subtle bg-app px-3 py-2.5">
          <FileText size={14} strokeWidth={1.75} className="shrink-0 text-ink-secondary" />

          <span className="grid min-w-0 flex-1">
            <span className="truncate text-[12.5px] font-medium">{template.fileName}</span>
            <span className="type-caption text-muted">
              Utilisé pour tous vos détails de facture.
            </span>
          </span>

          <Button variant="secondary" size="sm" disabled={busy} onClick={() => picker.current?.click()}>
            Remplacer
          </Button>

          <Button variant="secondary" size="icon" aria-label="Retirer le modèle" onClick={() => void remove()}>
            <Trash2 size={13} strokeWidth={1.75} />
          </Button>

          {saved && (
            <span className="flex items-center gap-1.5 text-[12.5px] text-success">
              <Check size={13} strokeWidth={2.5} />
              Enregistré
            </span>
          )}
        </div>
      )}

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {fields !== null && (
        <details className="rounded-md border border-line-subtle bg-app px-3.5 py-3">
          <summary className="cursor-pointer text-[12.5px] font-medium">
            Les repères à écrire dans votre document
          </summary>

          <p className="m-0 mt-2.5 max-w-[72ch] text-[11.5px] leading-[17px] text-muted">
            Écrivez-les là où la valeur doit apparaître. Les repères «&nbsp;ligne&nbsp;» vont dans
            <strong> une seule ligne de votre tableau</strong> : elle sera répétée pour chaque
            diligence. Un repère inconnu est laissé tel quel plutôt que vidé, parce qu’un document où
            il reste «&nbsp;{'{{total}}'}&nbsp;» est visiblement faux, là où un blanc silencieux a
            l’air terminé et ne l’est pas.
          </p>

          <div className="mt-2.5 grid gap-1 sm:grid-cols-2">
            {fields.map((field) => (
              <div key={field.field} className="flex items-baseline gap-2 text-[11.5px]">
                <code className="rounded-[3px] bg-sunken px-1.5 py-0.5 font-mono text-[11px]">
                  {`{{${field.field}}}`}
                </code>
                <span className="truncate text-muted">{field.example}</span>
              </div>
            ))}
          </div>
        </details>
      )}
    </>
  )
}
