import { useState } from 'react'
import type { ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import { Avatar } from '../components/ui/avatar.js'
import { Button } from '../components/ui/button.js'
import { PartyRole } from '../sections/Parties.js'
import { FileGlyph } from './FileGlyph.js'
import { TierBullet, distance } from '../lib/urgency.js'
import { urgencyLabels } from '../labels.js'
import { cn } from '../lib/utils.js'
import type { DeadlineUrgency, MatterDetail } from '../types.js'

/**
 * Aperçu: the dossier at rest, and the tab she lands on.
 *
 * <p>The journal is forty rows of what happened, newest first. It is the right screen for working and
 * the wrong one for arriving, so it stopped being first; and the first version of this was a flat
 * stack of bordered boxes each topped by a mono uppercase caption, which read as a form.</p>
 *
 * <p><b>One panel, hairlines instead of boxes.</b> Sections are separated by rules and by white space,
 * never by nested cards, so the eye descends the page instead of hopping between frames. The divider
 * between the two columns is a 1px grid track rather than a border, so it runs the full height however
 * unequal the columns are.</p>
 *
 * <p><b>No counter strip.</b> An earlier draft opened with four headline counters. The tab bar already
 * carries those volumes; repeating them filled the width without teaching anything. The counts sit
 * beside their section titles now.</p>
 *
 * <p><b>The sparse dossier is a requirement, not a demo.</b> One with no description, no juridiction,
 * one party and nothing billed must still look composed: every section explains its own emptiness in
 * place, so nothing reflows and nothing looks broken.</p>
 */
export function Overview({ matter, onOpen, onEdit, onChanged }: {
  matter: MatterDetail
  onOpen: (tab: 'parties' | 'journal' | 'documents' | 'deadlines' | 'time' | 'billing') => void
  onEdit: () => void
  onChanged: () => void
}) {
  const [editingParty, setEditingParty] = useState<string | null>(null)

  /**
   * Nothing has happened here yet.
   *
   * <p>Not « no description »: a dossier repris de Gestisoft arrives with two hundred journal entries,
   * eight hundred documents and no description at all, and telling it to note its first call was
   * absurd. What makes a dossier new is that nothing is in it.</p>
   */
  const isNew =
    matter.counts.activities === 0
    && matter.counts.documents === 0
    && matter.counts.timeEntries === 0
    && !matter.description

  const clients = matter.parties.filter((party) => party.isClient)
  const others = matter.parties.filter((party) => !party.isClient)
  const shown = others.slice(0, 5)

  return (
    <div className="grid min-h-0 grid-cols-[minmax(0,1fr)_1px_minmax(0,380px)] overflow-y-auto max-[1120px]:grid-cols-[minmax(0,1fr)_1px_minmax(0,320px)] max-[1000px]:grid-cols-[minmax(0,1fr)]">
      {/* The reading column. */}
      <div className="min-w-0 px-7 pt-6 pb-7 max-[1000px]:order-2">
        {/* The only sentence on the screen, and the only body text in the application set above 13px.
            It earns it. Absent, the block goes rather than leaving an empty paragraph. */}
        {matter.description && (
          <p className="m-0 max-w-[64ch] text-[14.5px] leading-6 text-ink">{matter.description}</p>
        )}

        {/* The top rule separates the register from the description above it. With no description
            it separates it from nothing, and a line hanging over the first row of a page reads as a
            mistake, which is what it was. */}
        <dl
          className={cn(
            'm-0 grid grid-cols-[132px_minmax(0,1fr)]',
            matter.description && 'mt-[22px] border-t border-line-subtle',
          )}
        >
          {/* The référence lives in the dossier header, so it is repeated here only when there is
              little else to read. An absent field removes its row: the register simply gets shorter. */}
          {!matter.description && <Entry term="Référence" value={matter.reference} mono />}
          <Entry term="Nature" value={matter.classification} />
          <Entry term="Juridiction" value={matter.court} />
          <Entry term="N° RG" value={matter.courtCaseNumber} mono />
          <Entry term="Ouvert le" value={date(matter.openedOn)} mono />
          {matter.closedOn && <Entry term="Clôturé le" value={date(matter.closedOn)} mono />}
          <Entry term="Taux horaire" value={`${euros(matter.hourlyRateCents)} / heure`} mono last />
        </dl>

        <SectionTitle
          title="Documents"
          count={matter.counts.documents}
          aside={matter.documents.length > 0 ? 'derniers modifiés' : undefined}
        />

        {matter.documents.length === 0 ? (
          <Empty
            title={matter.documentsFolder ? 'Ce répertoire est vide' : 'Aucun répertoire indiqué'}
            action={matter.documentsFolder ? 'Ouvrir le répertoire' : 'Indiquer le répertoire'}
            onAction={() => onOpen('documents')}
          >
            {matter.documentsFolder
              ? 'Vos documents vivent dans ce répertoire. Déposez-y des fichiers comme vous le faites déjà.'
              : 'Vos documents restent chez vous : indiquez à ce dossier le répertoire où vous travaillez déjà.'}
          </Empty>
        ) : (
          <div className="mt-2 border-t border-line-subtle">
            {matter.documents.map((document, index) => (
              <button
                key={document.relativePath}
                type="button"
                onClick={() => onOpen('documents')}
                className={cn(
                  'flex w-full items-center gap-2.5 px-1 py-2 text-left hover:bg-hover',
                  index < matter.documents.length - 1 && 'border-b border-[#F1F3EE]',
                )}
              >
                {/* A pièce carries its number; anything else carries the glyph of its kind. */}
                {document.exhibitNumber === null ? (
                  <span className="grid h-6 w-6 shrink-0 place-items-center rounded-sm border border-line-subtle bg-app">
                    <FileGlyph fileName={document.name} size={13} />
                  </span>
                ) : (
                  <span className="grid h-6 w-6 shrink-0 place-items-center rounded-sm border border-[#BFD3C5] bg-brand-subtle font-mono text-[9.5px] leading-none font-medium text-brand-on-subtle">
                    {document.exhibitNumber}
                  </span>
                )}

                <span className="grid min-w-0 flex-1">
                  <span title={document.name} className="truncate text-[12.5px] leading-[17px]">
                    {document.name}
                  </span>

                  {/* Where it sits in her folder, when that is not the folder itself. */}
                  {document.relativePath !== document.name && (
                    <span
                      title={document.relativePath}
                      className="truncate font-mono text-[10px] leading-[14px] text-muted"
                    >
                      {document.relativePath.slice(0, document.relativePath.lastIndexOf('/'))}
                    </span>
                  )}
                </span>

                <span className="w-[76px] shrink-0 text-right font-mono text-[11px] leading-[15px] text-ink-secondary tnum">
                  {touched(document.modifiedAt)}
                </span>
              </button>
            ))}

            <div className="mt-0.5 flex flex-wrap items-center gap-2.5 border-t border-line-subtle pt-2.5">
              <button
                type="button"
                onClick={() => onOpen('documents')}
                className="flex h-[26px] shrink-0 items-center gap-[7px] rounded-sm border border-line-strong px-2.5 text-[11.5px] font-medium hover:bg-hover"
              >
                Voir les {matter.counts.documents.toLocaleString('fr-FR')} documents
                <ChevronRight size={12} strokeWidth={1.75} className="text-ink-secondary" />
              </button>

              <span className="min-w-0 flex-1 text-[11px] leading-4 text-muted">
                Les documents numérotés sont versés comme pièces, avec leur libellé.
              </span>
            </div>
          </div>
        )}

        <SectionTitle
          title="Parties"
          count={matter.parties.length}
          action={matter.parties.length > 0 ? 'Gérer' : 'Ajouter une partie'}
          onAction={() => onOpen('parties')}
        />

        {/* The client is set apart, and every client is: a dossier repris de Gestisoft sometimes has
            three, a famille and its two SCI. This is the only coloured left border on the tab, so it
            reads as the anchor. */}
        {clients.map((party) => (
          <div
            key={party.id}
            className="mt-2 flex items-center gap-2.5 rounded-[5px] border border-[#BFD3C5] border-l-[3px] border-l-brand bg-[#F4F8F5] px-3 py-[9px]"
          >
            <Avatar name={party.displayName} type={party.contactType} client size={28} />

            <span className="grid min-w-0 flex-1">
              <span className="truncate text-[13px] leading-[18px] font-semibold">{party.displayName}</span>
              <span className="truncate text-[11.5px] leading-[15px] text-brand-on-subtle">
                {party.role ?? 'Client'}, {nature(party.contactType)}
              </span>
            </span>

            <span className="shrink-0 font-mono text-[10.5px] leading-[14px] text-brand-on-subtle">
              facturable
            </span>
          </div>
        ))}

        {matter.parties.length === 0 && (
          <Empty title="Aucune partie" action="Ajouter une partie" onAction={() => onOpen('parties')}>
            Le client, la partie adverse, son conseil : tout ce qui a un nom dans ce dossier se range
            ici et se retrouve ensuite dans le carnet.
          </Empty>
        )}

        <div className="mt-1">
          {shown.map((party, index) =>
            editingParty === party.id ? (
              <PartyRole
                key={party.id}
                party={party}
                onCancel={() => setEditingParty(null)}
                onSaved={() => { setEditingParty(null); onChanged() }}
              />
            ) : (
              <div
                key={party.id}
                className={cn(
                  'flex w-full items-center gap-2.5 px-1',
                  index < shown.length - 1 && 'border-b border-[#F1F3EE]',
                )}
              >
                {/* The row is a div and the name is the button: a control inside a control is not
                    valid, and « retirer » must not also be « modifier le rôle ». */}
                <button
                  type="button"
                  onClick={() => setEditingParty(party.id)}
                  title="Modifier le rôle de cette partie"
                  className="flex min-w-0 flex-1 items-center gap-2.5 py-2 text-left hover:bg-hover"
                >
                  {/* A dashed avatar marks an incomplete record without making it an error. */}
                  <Avatar
                    name={party.displayName}
                    type={party.contactType}
                    size={24}
                    className={party.role ? undefined : 'border border-dashed border-line bg-app text-muted'}
                  />

                  <span className="grid min-w-0 flex-1">
                    <span className="truncate text-[12.5px] leading-[17px] font-medium">{party.displayName}</span>
                    <span
                      title={party.role ?? 'Indiquer le rôle de cette partie'}
                      className={cn(
                        'truncate text-[11px] leading-[15px] text-muted',
                        party.role || 'italic',
                      )}
                    >
                      {party.role ?? 'rôle non précisé'}
                    </span>
                  </span>
                </button>

                <span className="shrink-0 text-[11px] leading-[15px] text-muted">
                  {nature(party.contactType)}
                </span>

              </div>
            ),
          )}


          {/* A link into the tab, not an expander. Expanding was one-way, and a dossier with
              twenty-five parties turned the page she lands on into a scroll. */}
          {others.length > shown.length && (
            <button
              type="button"
              onClick={() => onOpen('parties')}
              className="block px-1 pt-2 text-[11.5px] leading-4 text-brand-on-subtle underline-offset-2 hover:underline"
            >
              Voir les {others.length - shown.length} autres parties
            </button>
          )}
        </div>

        {/* Only for a dossier that is genuinely new. It keyed off the missing description alone, so a
            dossier repris de Gestisoft with two hundred entries in its journal was told to note its
            first call. An empty description on a full dossier is a gap, not a beginning: it gets the
            quiet line underneath instead. */}
        {isNew ? (
          <div className="mt-5 border-t border-line-subtle pt-[18px]">
            <p className="m-0 max-w-[56ch] text-[13px] leading-5 text-ink-secondary">
              Ce dossier vient d’être ouvert. Notez le premier appel dès que vous raccrochez, deux
              lignes suffisent, et l’aperçu se remplira de lui-même.
            </p>

            <div className="mt-3 flex flex-wrap gap-2">
              <Button size="lg" onClick={() => onOpen('journal')}>Première entrée au journal</Button>
              <Button variant="secondary" size="lg" onClick={onEdit}>Ajouter une description</Button>
            </div>
          </div>
        ) : !matter.description && (
          <button
            type="button"
            onClick={onEdit}
            className="mt-5 block border-t border-line-subtle pt-[18px] text-[11.5px] leading-4 text-brand-on-subtle underline-offset-2 hover:underline"
          >
            Ajouter une description
          </button>
        )}
      </div>

      {/* Part of the grid rather than a border, so it runs the full height whichever column is taller. */}
      <div className="bg-line-subtle max-[1000px]:hidden" />

      <div className="grid min-w-0 content-start gap-6 px-7 pt-6 pb-7 max-[1000px]:order-1 max-[1000px]:border-b max-[1000px]:border-line-subtle">
        <section>
          <div className="text-[12px] leading-4 font-medium text-ink-secondary">Reste à facturer</div>

          <div
            className={cn(
              'mt-0.5 font-mono text-[28px] leading-[34px] font-semibold tracking-[-0.02em] tnum',
              // A zero is a value and « n/d » is a fact, so neither may use the disabled grey: it is
              // 2.4:1 and reserved for controls that do nothing.
              matter.billing.leftToBillCents === 0 ? 'text-ink-secondary' : 'text-ink',
            )}
          >
            {euros(matter.billing.leftToBillCents)}
          </div>

          <div className="font-mono text-[11.5px] leading-4 text-muted tnum">
            {matter.billing.leftToBillCents < 0
              ? 'le client est créditeur'
              : matter.billing.billableMinutes === 0
                ? 'aucun temps saisi'
                : `${hours(matter.billing.billableMinutes)} facturables à ${euros(matter.hourlyRateCents)}/h`}
          </div>

          <div className="mt-3.5 border-t border-line-subtle">
            <Figure label="Déjà facturé" cents={matter.billing.invoicedCents} />
            {/* The ledger is signed: positive is money in, negative is money she advanced on the
                dossier. Labelling both « Déjà reçu » and showing the absolute value would state the
                opposite of what happened. */}
            <Figure
              label={matter.billing.ledgerCents < 0 ? 'Avancé pour le client' : 'Déjà reçu'}
              cents={matter.billing.ledgerCents}
            />
            <Figure
              label={matter.billing.varianceCents < 0 ? 'Mali' : 'Boni'}
              cents={matter.billing.varianceCents}
              last
            />
          </div>

          <button
            type="button"
            onClick={() => onOpen('billing')}
            className="mt-3 text-[11.5px] leading-4 text-brand-on-subtle underline-offset-2 hover:underline"
          >
            Ouvrir la facturation
          </button>
        </section>

        <section>
          <SectionTitle
            title="Échéances"
            count={matter.deadlines.length}
            action={matter.deadlines.length > 0 ? 'Tout voir' : undefined}
            onAction={() => onOpen('deadlines')}
            flush
          />

          {matter.deadlines.length === 0 ? (
            <Empty
              centred
              title="Aucune date à tenir"
              action="Ajouter une échéance"
              onAction={() => onOpen('deadlines')}
            >
              Ajoutez-en une dès qu’un délai est fixé, elle remontera sur l’accueil.
            </Empty>
          ) : (
            <div className="mt-2 grid gap-1.5">
              {matter.deadlines.map((deadline) => (
                <div key={deadline.id} className={cn('rounded-sm border border-l-[3px] px-2.5 py-2', tint[deadline.urgency])}>
                  <div className="flex items-center gap-[7px]">
                    <TierBullet urgency={deadline.urgency} className="h-1.5 w-1.5" />
                    <span className="min-w-0 flex-1 truncate text-[12.5px] leading-[17px] font-medium">
                      {deadline.label}
                    </span>
                  </div>

                  {/* Shape, colour and wording all carry it, so a monochrome printout still reads. */}
                  <div className="mt-0.5 flex justify-between gap-2 pl-[13px]">
                    <span className="text-[11px] leading-[15px]">{urgencyLabels[deadline.urgency]}</span>
                    <span className="font-mono text-[11px] leading-[15px] tnum">
                      {distance(deadline.date, deadline.time)}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

    </div>
  )
}

/** The four tints, lighter than --status-*-bg so several rows can sit together without a rainbow. */
const tint: Record<DeadlineUrgency, string> = {
  Overdue: 'border-[#EBC9C5] border-l-[#A32A22] bg-[#FDF4F3] text-[#8A211A]',
  Today: 'border-[#E8D5AE] border-l-[#8A5A10] bg-[#FDF8ED] text-[#6E4A0E]',
  ThisWeek: 'border-[#C7DAEB] border-l-[#2B5578] bg-[#F4F8FC] text-[#234B6B]',
  Later: 'border-line-subtle border-l-[#D2D7CB] bg-[#F8F9F6] text-ink',
}

function SectionTitle({ title, count, aside, action, onAction, flush }: {
  title: string
  count: number
  aside?: string
  action?: string
  onAction?: () => void
  flush?: boolean
}) {
  return (
    <div className={cn('flex items-baseline gap-[9px]', flush ? 'mt-0' : 'mt-7')}>
      <span className="text-[15px] leading-[21px] font-semibold tracking-[-0.01em]">{title}</span>
      <span className="font-mono text-[11px] leading-4 text-muted tnum">{count.toLocaleString('fr-FR')}</span>
      <span className="flex-1" />

      {aside && <span className="text-[11px] leading-4 text-muted">{aside}</span>}

      {action && (
        <button
          type="button"
          onClick={onAction}
          className="text-[11.5px] leading-4 text-brand-on-subtle underline-offset-2 hover:underline"
        >
          {action}
        </button>
      )}
    </div>
  )
}

/**
 * A section explaining its own emptiness, in place. Nothing reflows into a different position, so a
 * dossier opened this morning looks composed rather than broken.
 */
function Empty({ title, action, onAction, centred, children }: {
  title: string
  action: string
  onAction: () => void
  centred?: boolean
  children: ReactNode
}) {
  return (
    <div
      className={cn(
        'mt-2 rounded-[5px] border border-dashed border-line',
        centred ? 'p-3.5 text-center' : 'flex flex-wrap items-center gap-3 px-4 py-3.5',
      )}
    >
      <span className={cn('grid min-w-0', centred || 'flex-[1_1_240px]')}>
        <span className="text-[12.5px] leading-[18px] font-medium">{title}</span>
        <span className="mt-px text-[11.5px] leading-[17px] text-ink-secondary">{children}</span>
      </span>

      <button
        type="button"
        onClick={onAction}
        className={cn(
          'inline-flex h-[26px] shrink-0 items-center rounded-sm border border-line-strong px-2.5 text-[11.5px] font-medium hover:bg-hover',
          centred && 'mt-2.5',
        )}
      >
        {action}
      </button>
    </div>
  )
}

function Entry({ term, value, mono, last }: {
  term: string
  value: string | null
  mono?: boolean
  last?: boolean
}) {
  // An absent field removes its row entirely: no empty cell, no placeholder dash.
  if (value === null || value === '') {
    return null
  }

  return (
    <>
      <dt className={cn('py-[7px] text-[12px] leading-4 text-muted', last || 'border-b border-[#F1F3EE]')}>
        {term}
      </dt>
      <dd
        className={cn(
          'm-0 py-[7px] leading-4',
          last || 'border-b border-[#F1F3EE]',
          mono ? 'font-mono text-[12.5px] tnum' : 'text-[13px]',
        )}
      >
        {value}
      </dd>
    </>
  )
}

function Figure({ label, cents, last }: { label: string; cents: number; last?: boolean }) {
  return (
    <div className={cn('flex justify-between gap-2.5 py-1.5', last || 'border-b border-[#F1F3EE]')}>
      <span className="text-[12px] leading-4 text-muted">{label}</span>
      <span
        className={cn(
          'font-mono text-[12.5px] leading-4 tnum',
          Number.isFinite(cents) ? 'text-ink' : 'text-muted',
        )}
      >
        {euros(Math.abs(cents))}
      </span>
    </div>
  )
}

const nature = (type: MatterDetail['parties'][number]['contactType']) =>
  type === 'Organisation' ? 'personne morale' : 'personne physique'

/** JJ/MM/AAAA, sliced rather than parsed: the value is already a calendar date, not an instant. */
function date(iso: string) {
  const parts = iso.slice(0, 10).split('-')

  return parts.length === 3 ? `${parts[2]}/${parts[1]}/${parts[0]}` : iso
}

/** « hier 17:12 » while it is fresh, JJ/MM/AAAA once it is not. */
function touched(iso: string) {
  const at = new Date(iso)
  const midnight = new Date()
  midnight.setHours(0, 0, 0, 0)

  const days = Math.floor((midnight.getTime() - at.getTime()) / 86_400_000) + 1
  const time = at.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })

  if (days <= 0) return `aujourd’hui ${time}`
  if (days === 1) return `hier ${time}`

  return at.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

/** « 4 h 30 ». */
function hours(minutes: number) {
  const whole = Math.floor(minutes / 60)
  const rest = minutes % 60

  if (whole === 0) return `${rest} min`

  return rest === 0 ? `${whole} h` : `${whole} h ${String(rest).padStart(2, '0')}`
}

/**
 * « 1 234,56 € », with the non-breaking spaces French typography wants, and « n/d » for a figure that
 * never arrived. Not « 0,00 € »: a zero is a statement, and a false one where nothing is known.
 */
function euros(cents: number) {
  if (!Number.isFinite(cents)) return 'n/d'

  const sign = cents < 0 ? '− ' : ''
  const absolute = Math.abs(cents)
  const units = Math.floor(absolute / 100).toString()
  const grouped = units.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')

  return `${sign}${grouped},${String(absolute % 100).padStart(2, '0')} €`
}
