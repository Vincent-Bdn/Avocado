import type { ReactNode } from 'react'
import { PageHeader } from '../../components/ui/page-header.js'
import { Panel } from '../../components/ui/panel.js'

/**
 * Connecter Google Agenda.
 *
 * <p>Le seul écran d'Avocado qui demande à l'avocate de créer quelque chose ailleurs. C'est une
 * demi-heure, une fois, et la page existe parce que sans elle le réglage dit « identifiant client »
 * à quelqu'un qui n'a aucune raison de savoir ce que c'est.</p>
 */
export function AgendaDoc() {
  return (
    <Panel>
      <PageHeader
        title="Agenda Google"
        meta={<span>Vos échéances dans votre agenda</span>}
      />

      <div className="flex-1 overflow-y-auto">
        <section>
          <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
            <h2 className="m-0 type-label text-ink-secondary">Ce qui est envoyé</h2>
            <p className="m-0 text-[11.5px] leading-4 text-muted">Dans un sens, et dans un seul</p>
          </header>

          <div className="grid gap-3.5 px-4 py-3.5">
            <Rule title="Un agenda à part">
              Avocado crée son propre agenda, «&nbsp;Avocado · Échéances&nbsp;», et n’écrit que
              dedans. Vos autres agendas ne sont ni lus, ni modifiés, ni même visibles pour lui :
              l’autorisation demandée à Google ne porte que sur les agendas qu’il a créés lui-même.
            </Rule>

            <Rule title="Les échéances de vos dossiers en cours">
              Chaque échéance devient un événement : l’intitulé et le dossier en titre, la référence
              en dessous, votre rappel en rappel. Un délai sans heure occupe la journée, une audience
              à 9 h occupe une heure, que vous pouvez étirer.
            </Rule>

            <Rule title="Rien ne revient">
              Ce que vous modifiez dans Google est écrasé à la synchronisation suivante : l’échéance
              du dossier est la référence. Si vous supprimez l’événement, il revient. Si vous
              supprimez l’échéance dans Avocado, l’événement part.
            </Rule>

            <Rule title="Clôturer un dossier n’efface rien">
              Ses échéances cessent d’être mises à jour et restent dans votre agenda : c’est le
              compte rendu de là où vous étiez. Déconnecter laisse également tout en place.
            </Rule>
          </div>
        </section>

        <section className="border-t border-line-strong">
          <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
            <h2 className="m-0 type-label text-ink-secondary">La connexion</h2>
            <p className="m-0 text-[11.5px] leading-4 text-muted">Une demi-heure, une fois</p>
          </header>

          <div className="grid gap-3.5 px-4 py-3.5">
            <p className="m-0 max-w-[68ch] text-[12.5px] leading-[19px] text-ink-secondary">
              Avocado ne fournit pas d’identifiant Google. Son code est public, donc un secret livré
              avec serait un secret publié ; et l’écran d’autorisation porte le nom du titulaire du
              projet Google Cloud, qu’un cabinet est en droit de connaître. Vous créez donc le vôtre,
              au nom du cabinet.
            </p>

            <Step n={1} title="Créer un projet">
              Sur <Mono>console.cloud.google.com</Mono>, créez un projet. Son nom n’a pas
              d’importance.
            </Step>

            <Step n={2} title="Activer l’API Google Agenda">
              Dans «&nbsp;API et services&nbsp;», activez <Mono>Google Calendar API</Mono>.
            </Step>

            <Step n={3} title="Remplir l’écran d’autorisation">
              Type «&nbsp;Externe&nbsp;». C’est le nom que vous mettez là qui s’affichera quand vous
              autoriserez l’accès. Ajoutez votre adresse comme utilisateur de test : tant que le
              projet n’est pas vérifié par Google, seules les adresses listées peuvent s’en servir,
              et un cabinet n’a besoin que de la sienne.
            </Step>

            <Step n={4} title="Créer un identifiant">
              «&nbsp;Identifiants&nbsp;», puis «&nbsp;ID client OAuth&nbsp;», type
              «&nbsp;Application de bureau&nbsp;». Recopiez l’identifiant et le secret dans Réglages,
              section Agenda Google.
            </Step>

            <p className="m-0 max-w-[68ch] text-[12.5px] leading-[19px] text-muted">
              Google affichera un avertissement «&nbsp;application non validée&nbsp;» au moment
              d’autoriser : c’est normal pour un projet qui ne sert qu’à vous, et vous passez outre
              par «&nbsp;Paramètres avancés&nbsp;». Une synchronisation a lieu tous les quarts
              d’heure ; en cas d’échec répété Avocado s’arrête après cinq tentatives et l’écrit dans
              Réglages, où un bouton relance.
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

const Step = ({ n, title, children }: { n: number; title: string; children: ReactNode }) => (
  <div className="flex gap-2.5">
    <span className="mt-px grid h-[18px] w-[18px] shrink-0 place-items-center rounded-full bg-sunken font-mono text-[10.5px] text-ink-secondary">
      {n}
    </span>

    <div>
      <h3 className="m-0 text-[12.5px] leading-[18px] font-semibold">{title}</h3>
      <p className="m-0 mt-0.5 max-w-[66ch] text-[12.5px] leading-[19px] text-ink-secondary">
        {children}
      </p>
    </div>
  </div>
)

const Mono = ({ children }: { children: ReactNode }) => (
  <span className="font-mono text-[11.5px] text-ink">{children}</span>
)
