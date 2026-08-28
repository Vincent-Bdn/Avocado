import { useState } from 'react'
import { Plus, Trash2, UserPlus } from 'lucide-react'
import { api } from '../api.js'
import { Avatar } from '../components/ui/avatar.js'
import { Button } from '../components/ui/button.js'
import { EmptyState } from '../components/ui/empty-state.js'
import { AddParty, PartyRole } from '../sections/Parties.js'
import { RowAction } from './shared.js'
import { cn } from '../lib/utils.js'
import type { MatterDetail } from '../types.js'

/**
 * Parties: everyone this dossier concerns, and the only place they are managed.
 *
 * <p>They used to be managed in the 208px context panel, in a column too narrow to read a role in and
 * absent from the tab where they are most looked at. The aperçu then grew its own list and a « voir
 * les 7 autres » that only ever expanded, so a dossier with twenty-five parties turned the landing
 * page into a scroll. Both now lead here.</p>
 *
 * <p><b>Parties, not tiers.</b> A tiers is an entry in the carnet, one per person or company across
 * the whole practice; a partie is that tiers <em>on this dossier</em>, with the role it plays here.
 * The same avocat is a partie adverse in one and a confrère in the next, and keeping the two words
 * apart is what lets that be true.</p>
 */
export function Parties({ matter, isOpen, onChanged }: {
  matter: MatterDetail
  isOpen: boolean
  onChanged: () => void
}) {
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)

  const clients = matter.parties.filter((party) => party.isClient)
  const others = matter.parties.filter((party) => !party.isClient)

  async function detach(id: string) {
    await api(`/api/parties/${id}`, { method: 'DELETE' }).catch(() => undefined)
    onChanged()
  }

  const row = (party: MatterDetail['parties'][number], client: boolean) =>
    editing === party.id ? (
      <div key={party.id} className="py-1">
        <PartyRole
          party={party}
          onCancel={() => setEditing(null)}
          onSaved={() => { setEditing(null); onChanged() }}
        />
      </div>
    ) : (
      <div
        key={party.id}
        className={cn(
          'group flex items-center gap-3 border-t border-line-subtle px-1',
          client && 'border-l-[3px] border-l-brand bg-[#F4F8F5] pl-3',
        )}
      >
        <button
          type="button"
          onClick={() => setEditing(party.id)}
          title="Modifier le rôle de cette partie"
          disabled={!isOpen}
          className="flex min-w-0 flex-1 items-center gap-3 py-2.5 text-left disabled:cursor-default"
        >
          <Avatar
            name={party.displayName}
            type={party.contactType}
            client={client}
            size={28}
            className={party.role ? undefined : 'border border-dashed border-line bg-app text-muted'}
          />

          <span className="grid min-w-0 flex-1">
            <span className="truncate text-[13px] leading-[18px] font-medium">{party.displayName}</span>

            {/* Free text, and often long: « Avocat de la partie adverse, barreau de Bordeaux ». This
                column is wide enough to read it, which the 208px panel never was. */}
            <span
              title={party.role ?? 'Indiquer le rôle de cette partie'}
              className={cn(
                'truncate text-[11.5px] leading-[15px]',
                client ? 'text-brand-on-subtle' : 'text-muted',
                party.role || 'italic',
              )}
            >
              {party.role ?? 'rôle non précisé'}
            </span>
          </span>
        </button>

        <span className="shrink-0 text-[11px] leading-[15px] text-muted">
          {party.contactType === 'Organisation' ? 'personne morale' : 'personne physique'}
        </span>

        {client && (
          <span className="shrink-0 font-mono text-[10.5px] leading-[14px] text-brand-on-subtle">
            facturable
          </span>
        )}

        {isOpen && (
          <span className="flex shrink-0 gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
            <RowAction label="Retirer cette partie du dossier" danger onClick={() => void detach(party.id)}>
              <Trash2 size={13} strokeWidth={1.75} />
            </RowAction>
          </span>
        )}
      </div>
    )

  return (
    <div className="grid content-start gap-3 overflow-y-auto px-5 pt-4 pb-6">
      {isOpen && (
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => setAdding(true)}>
            <Plus size={14} strokeWidth={2} />
            Ajouter une partie
          </Button>

          <span className="text-[11.5px] leading-[17px] text-muted">
            Un tiers du carnet, et le rôle qu’il joue dans ce dossier. Le rôle est libre : écrivez-le
            comme vous le diriez.
          </span>
        </div>
      )}

      {matter.parties.length === 0 ? (
        <EmptyState icon={<UserPlus size={18} strokeWidth={1.8} />} title="Aucune partie">
          Le client, la partie adverse, son conseil, la juridiction : tout ce qui a un nom dans ce
          dossier se range ici, et se retrouve ensuite dans le carnet.
        </EmptyState>
      ) : (
        <div>
          {/* Clients first and set apart, all of them: a dossier repris de Gestisoft sometimes has
              three, a famille and ses deux SCI, and running them in with the partie adverse loses the
              one distinction that matters. */}
          {clients.map((party) => row(party, true))}
          {others.map((party) => row(party, false))}
          <div className="border-t border-line-subtle" />
        </div>
      )}

      {adding && (
        <AddParty
          matterId={matter.id}
          existing={matter.parties.map((party) => party.contactId)}
          onCancel={() => setAdding(false)}
          onAdded={() => { setAdding(false); onChanged() }}
        />
      )}
    </div>
  )
}
