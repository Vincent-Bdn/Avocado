import type { ReactNode } from 'react'
import { PageHeader } from '../../components/ui/page-header.js'
import { Panel } from '../../components/ui/panel.js'

/**
 * Ce qu'Avocado calcule en facturation.
 *
 * <p>Quatre règles, et rien d'autre. Une première version ajoutait un simulateur, des chiffres à
 * changer et les écrans qui se redessinaient en dessous. Montré à une avocate, il a soulevé plus de
 * questions qu'il n'en réglait : ce qui manquait n'était pas un bac à sable, c'était la phrase qui
 * dit ce qu'Avocado fait d'un temps passé.</p>
 */
export function FacturationDoc() {
  return (
    <Panel>
      <PageHeader title="Facturation" meta={<span>Ce qu’Avocado calcule, et pourquoi</span>} />

      <div className="flex-1 overflow-y-auto">
        <section>
          <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
            <h2 className="m-0 type-label text-ink-secondary">Les règles</h2>
            <p className="m-0 text-[11.5px] leading-4 text-muted">Quatre, et rien d’autre</p>
          </header>

          <div className="grid gap-3.5 px-4 py-3.5">
            <Rule title="Un temps passé porte un montant">
              Chaque ligne de temps passé a une durée. Son montant est durée × taux horaire : celui
              du dossier, ou celui de la ligne quand vous en avez fixé un autre. Tous les montants
              d’Avocado sont hors taxes.
            </Rule>

            <Rule title="Une facture est faite de temps passés">
              Vous l’établissez en cochant des lignes de temps passé. La somme de leurs montants
              donne le montant de la facture, que vous pouvez remplacer par un autre. L’écart est
              enregistré sur cette facture : un <Term>boni</Term> au-dessus, un <Term>mali</Term> en
              dessous. Les lignes cochées quittent le reste à facturer.
            </Rule>

            <Rule title="Gestion des forfaits">
              Un forfait est un montant convenu d’avance pour une prestation. La facture s’établit de
              la même manière, en cochant des temps passés, et le montant proposé est ce qui reste du
              forfait. Le reste à facturer devient «&nbsp;forfait moins déjà facturé&nbsp;» ; les
              temps passés continuent d’être saisis et donnent le boni ou le mali.
            </Rule>

            <Rule title="Les mouvements ne sont pas de la facturation">
              Une provision est de l’argent reçu : elle compte dans l’encaissé, jamais dans le reste
              à facturer. Un débours est une avance faite pour le client : sa propre ligne, à lui
              refacturer. Une rétrocession est ce que le dossier a coûté au cabinet, sans effet sur
              ce que le client doit.
            </Rule>

            <p className="m-0 text-[12px] leading-[18px] text-muted">
              Avocado n’émet aucune facture et ne calcule aucune TVA. Il note ce qui est parti, pour
              que ce qui reste soit connu.
            </p>
          </div>
        </section>
      </div>
    </Panel>
  )
}

const Rule = ({ title, children }: { title: string; children: ReactNode }) => (
  <div>
    <h3 className="m-0 text-[12.5px] leading-[18px] font-semibold">{title}</h3>
    <p className="m-0 mt-0.5 max-w-[68ch] text-[12.5px] leading-[19px] text-ink-secondary">
      {children}
    </p>
  </div>
)

const Term = ({ children }: { children: ReactNode }) => (
  <strong className="font-semibold text-ink">{children}</strong>
)
