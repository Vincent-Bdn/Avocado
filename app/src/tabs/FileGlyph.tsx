import {
  File, FileArchive, FileSpreadsheet, FileText, FileType, Image as ImageIcon, Mail,
} from 'lucide-react'
import { cn } from '../lib/utils.js'

/**
 * What kind of file this is, at a glance.
 *
 * <p>Worth more than the mapping costs in a dossier that is half correspondence: 4 713 of the 13 917
 * documents in the real export arrived as courriels, and telling a .msg from the .pdf beside it is
 * most of what a name in a long list has to do. Lawyers reading the first version said every row
 * surfaced the same whatever the file was.</p>
 *
 * <p>Each kind carries a tint as well as a glyph, muted enough that fifty rows do not become a
 * rainbow, and the glyph alone still separates them in monochrome.</p>
 */
const KINDS: { extensions: string[]; icon: typeof Mail; tone: string; label: string }[] = [
  { extensions: ['msg', 'eml'], icon: Mail, tone: 'text-[#2B5578]', label: 'Courriel' },
  {
    extensions: ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'tif', 'tiff', 'heic'],
    icon: ImageIcon,
    tone: 'text-[#7A4E86]',
    label: 'Image',
  },
  {
    extensions: ['xls', 'xlsx', 'xlsm', 'csv', 'ods'],
    icon: FileSpreadsheet,
    tone: 'text-[#2F6B47]',
    label: 'Tableur',
  },
  { extensions: ['pdf'], icon: FileType, tone: 'text-[#A32A22]', label: 'PDF' },
  {
    extensions: ['doc', 'docx', 'odt', 'rtf', 'txt'],
    icon: FileText,
    tone: 'text-[#2C4A38]',
    label: 'Document',
  },
  { extensions: ['zip', '7z', 'rar'], icon: FileArchive, tone: 'text-[#8A5A10]', label: 'Archive' },
]

export function kindOf(fileName: string) {
  const extension = fileName.slice(fileName.lastIndexOf('.') + 1).toLowerCase()

  return KINDS.find((kind) => kind.extensions.includes(extension))
}

export function FileGlyph({ fileName, size = 14, className }: {
  fileName: string
  size?: number
  className?: string
}) {
  const kind = kindOf(fileName)
  const Glyph = kind?.icon ?? File

  return (
    <Glyph
      size={size}
      strokeWidth={1.75}
      aria-label={kind?.label}
      className={cn('shrink-0', kind?.tone ?? 'text-disabled', className)}
    />
  )
}
