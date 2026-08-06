import { useCallback, useEffect, useRef, useState } from 'react'
import { Download, FileText, Pencil, Trash2, Upload, X } from 'lucide-react'
import { ApiError, api, download, upload } from '../api.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { Input } from '../components/ui/input.js'
import { Micro, Row, RowAction, RowMain } from '../tabs/shared.js'
import { formatSize } from '../lib/urgency.js'

interface TemplateItem {
  id: string
  name: string
  kind: string | null
  fileName: string
  sizeBytes: number
  updatedAt: string
}

interface TemplateField {
  field: string
  example: string
}

const messageOf = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : String(failure)

/**
 * Modèles. She writes the lettre de mission once, in Word, with the dossier's own words left as
 * {{placeholders}}, and Avocado fills them.
 *
 * The template is a real .docx rather than something authored in this application, because her
 * letterhead, her margins and her typography already exist in Word and no editor built here would be
 * as good. The template lives in the coffre like any other document: a lettre de mission carries the
 * cabinet's bank details.
 */
export function Templates() {
  const [items, setItems] = useState<TemplateItem[]>([])
  const [fields, setFields] = useState<TemplateField[]>([])
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const picker = useRef<HTMLInputElement>(null)

  const reload = useCallback(() => {
    api<TemplateItem[]>('/api/templates')
      .then(setItems)
      .catch((failure: unknown) => setError(messageOf(failure)))
  }, [])

  useEffect(reload, [reload])

  useEffect(() => {
    api<TemplateField[]>('/api/templates/fields').then(setFields).catch(() => setFields([]))
  }, [])

  async function add(files: FileList) {
    setBusy(true)
    setError(null)

    try {
      const form = new FormData()
      form.append('file', files[0]!)
      form.append('name', files[0]!.name.replace(/\.docx$/i, ''))

      await upload('/api/templates', form)
      setEditing(null)
      reload()
    } catch (failure) {
      setError(messageOf(failure))
    } finally {
      setBusy(false)
    }
  }

  async function remove(id: string) {
    setError(null)

    try {
      await api(`/api/templates/${id}`, { method: 'DELETE' })
      reload()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  return (
    <>
      <p className="m-0 max-w-[72ch] text-[12.5px] leading-[19px] text-muted">
        Écrivez la lettre une fois dans Word, en laissant des repères entre doubles accolades là où le
        dossier doit s’écrire. Depuis l’onglet Documents d’un dossier, « Générer depuis un modèle »
        remplit ces repères et dépose le résultat dans le coffre, où vous pouvez l’ouvrir et le
        terminer.
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <Button disabled={busy} onClick={() => picker.current?.click()}>
          <Upload size={14} strokeWidth={1.75} />
          {busy ? 'Ajout en cours…' : 'Ajouter un modèle .docx'}
        </Button>

        <input
          ref={picker}
          type="file"
          accept=".docx"
          hidden
          onChange={(event) => event.target.files?.length && void add(event.target.files)}
        />
      </div>

      {error && <p className="m-0 text-[11.5px] text-danger">{error}</p>}

      {items.length === 0 ? (
        <EmptyState icon={<FileText size={18} strokeWidth={1.8} />} title="Aucun modèle">
          Une lettre de mission, un courrier type, une convention d’honoraires : tout ce que vous
          réécrivez à chaque dossier gagne à devenir un modèle.
        </EmptyState>
      ) : (
        <div className="grid">
          {items.map((template) =>
            editing === template.id ? (
              <RenameRow
                key={template.id}
                template={template}
                onCancel={() => setEditing(null)}
                onSaved={() => { setEditing(null); reload() }}
              />
            ) : (
              <Row key={template.id} className="group">
                <FileText size={14} strokeWidth={1.75} className="w-[26px] shrink-0 text-disabled" />

                <RowMain>
                  <span>{template.name}</span>
                  <Micro className="font-mono">{template.fileName}</Micro>
                </RowMain>

                <Micro>{template.kind}</Micro>
                <Micro className="font-mono tnum">{formatSize(template.sizeBytes)}</Micro>

                <span className="flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                  <RowAction
                    label="Télécharger le modèle"
                    onClick={() => void download(`/api/templates/${template.id}/content`, template.fileName)}
                  >
                    <Download size={13} strokeWidth={1.75} />
                  </RowAction>
                  <RowAction label="Renommer" onClick={() => setEditing(template.id)}>
                    <Pencil size={13} strokeWidth={1.75} />
                  </RowAction>
                  <RowAction label="Supprimer" danger onClick={() => void remove(template.id)}>
                    <Trash2 size={13} strokeWidth={1.75} />
                  </RowAction>
                </span>
              </Row>
            ),
          )}
        </div>
      )}

      {/* The catalogue is the documentation. Nothing else tells her what she is allowed to type. */}
      <details className="rounded-md border border-line-subtle bg-app px-3.5 py-3">
        <summary className="cursor-pointer text-[12.5px] font-medium">
          Repères disponibles dans un modèle
        </summary>

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

        <p className="m-0 mt-2.5 text-[11px] leading-4 text-muted">
          Un repère inconnu est laissé tel quel dans la lettre plutôt que vidé : une lettre où il
          reste « {'{{client.siret}}'} » est visiblement fausse, une lettre avec un blanc silencieux a
          l’air terminée et ne l’est pas.
        </p>
      </details>
    </>
  )
}

function RenameRow({ template, onCancel, onSaved }: {
  template: TemplateItem
  onCancel: () => void
  onSaved: () => void
}) {
  const [name, setName] = useState(template.name)
  const [kind, setKind] = useState(template.kind ?? '')
  const [error, setError] = useState<string | null>(null)

  async function save() {
    try {
      await api(`/api/templates/${template.id}`, {
        method: 'PUT',
        body: JSON.stringify({ name: name.trim(), kind: kind.trim() || null }),
      })

      onSaved()
    } catch (failure) {
      setError(messageOf(failure))
    }
  }

  return (
    <div className="my-1.5 flex flex-wrap items-center gap-2 rounded-md border border-[var(--focus-ring)] px-3 py-2.5">
      <Input
        autoFocus
        className="flex-1 basis-[220px]"
        value={name}
        placeholder="Lettre de mission"
        onChange={(event) => setName(event.target.value)}
      />
      <Input
        className="w-[180px]"
        value={kind}
        placeholder="Type, ex. Courrier"
        onChange={(event) => setKind(event.target.value)}
      />

      <Button disabled={!name.trim()} onClick={() => void save()}>Enregistrer</Button>
      <Button variant="secondary" size="icon" aria-label="Annuler" onClick={onCancel}>
        <X size={13} strokeWidth={2} />
      </Button>

      {error && <span className="text-[11.5px] text-danger">{error}</span>}
    </div>
  )
}
