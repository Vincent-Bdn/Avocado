import type { ReactNode } from 'react'
import { PageHeader } from '../../components/ui/page-header.js'
import { Panel } from '../../components/ui/panel.js'

/**
 * Comment Avocado protège les données du cabinet.
 *
 * <p>Cette page porte les explications qui étaient dans Réglages, sous les boutons. Elles y étaient
 * lues une fois, à la mise en place, puis relues tous les jours par-dessus l'épaule de ce qu'on était
 * venu faire. Un écran de réglages dit ce qui est réglé ; ce qui se passe et pourquoi est ici.</p>
 */
export function SauvegardeDoc() {
  return (
    <Panel>
      <PageHeader
        title="Sauvegarde"
        meta={<span>Ce qui est copié, où, et ce qu’il faut garder à part</span>}
      />

      <div className="flex-1 overflow-y-auto">
        <Group title="Deux choses, pas une" summary="Et elles ne se protègent pas pareil">
          <Rule title="Le coffre, chiffré">
            Journal, tiers, facturation, temps passé, échéances, modèles, et une copie de votre clé.
            C’est une base de données chiffrée : personne ne l’ouvre sans votre clé de récupération,
            pas même celui qui héberge la copie.
          </Rule>

          <Rule title="Vos documents, en clair">
            Ils sont copiés tels quels, dans un dossier «&nbsp;Dossiers&nbsp;» que vous ouvrez et
            lisez directement, sans Avocado. C’est délibéré : vos documents vous appartiennent et
            vivent déjà dans vos propres répertoires. Une sauvegarde qui ne se relit qu’avec le
            logiciel qui l’a écrite n’est pas une sauvegarde, c’est une dépendance.
          </Rule>

          <Rule title="Avocado ne déplace rien">
            Vos fichiers restent là où vous les rangez. Avocado les lit, les recopie ailleurs, et ne
            les prend jamais en otage.
          </Rule>
        </Group>

        <Group title="Les copies locales" summary="Revenir en arrière, pas survivre à un disque">
          <Rule title="Des copies datées, dans le coffre">
            Toutes les demi-heures, Avocado garde une copie datée de la base. Les douze dernières,
            puis la dernière de chaque jour pendant deux mois. Elles pèsent peu : la base contient des
            écritures, pas des documents.
          </Rule>

          <Rule title="Elles répondent à « annuler cet après-midi »">
            Une erreur de saisie repérée trois semaines plus tard, un import malheureux : c’est à cela
            qu’elles servent, et elles le font bien.
          </Rule>

          <Rule title="Elles ne répondent à rien d’autre">
            Elles sont sur le même disque que le coffre. Disque mort, ordinateur volé, elles
            disparaissent avec. C’est la raison d’être de tout ce qui suit, et c’est la confusion que
            cette page existe pour éviter.
          </Rule>
        </Group>

        <Group title="Où copier" summary="Au moins un endroit qui n’est pas cet ordinateur">
          <Rule title="Un disque externe ou une clé USB">
            Le plus simple, et suffisant. Avocado dépose à la racine un petit fichier{' '}
            <Mono>.avocado-sink.json</Mono> : un identifiant et un nom, aucune donnée du cabinet. Une
            clé change de lettre selon ce qui est branché ; sans ce repère, Avocado écrirait un jour
            sur la clé d’un client. Vous pouvez le supprimer, le support ne sera simplement plus
            reconnu.
          </Rule>

          <Rule title="Un NAS, si le cabinet en a un">
            C’est ce que nous conseillons quand il y en a un : il est allumé, il est à demeure, il ne
            se débranche pas, et il n’a pas besoin qu’on y pense. Indiquez un sous-dossier plutôt que
            la racine : un NAS de cabinet est partagé et contient déjà des années d’archives.
          </Rule>

          <Rule title="Google Drive, Dropbox, OneDrive">
            Par leur logiciel, pas par leur API. Installez-le, créez un dossier qu’il synchronise, et
            ajoutez ce dossier ici : rien à autoriser, aucun mot de passe, rien à renouveler. Ils
            hébergent des fichiers qu’ils ne peuvent pas ouvrir.
          </Rule>

          <Rule title="Pas un dossier de cet ordinateur">
            Avocado l’accepte et le signale. Une copie sur le disque qui est en train de mourir
            réussit, se déclare réussie, et ne vaut rien. L’écran ne dit «&nbsp;vous ne perdriez
            rien&nbsp;» que pour les destinations qui sont ailleurs.
          </Rule>
        </Group>

        <Group title="À quel rythme" summary="Sans que vous ayez à y penser">
          <Rule title="Le coffre, en continu">
            À l’ouverture, toutes les demi-heures pendant que vous travaillez, à la fermeture, et dès
            qu’une destination réapparaît. Seul ce qui a changé est envoyé.
          </Rule>

          <Rule title="Les documents, une fois par nuit">
            À l’heure que vous choisissez, 22 h par défaut. Lire douze gigaoctets se sent : le disque
            travaille et un fichier ouvert dans Word est un fichier lu pendant qu’il est écrit. Si
            l’ordinateur était éteint, la nuit manquée est rattrapée au prochain passage, jamais
            sautée en silence.
          </Rule>

          <Rule title="Quand ça ne passe pas">
            Une clé débranchée est son état normal et ne mérite pas d’alerte. Un partage qui devrait
            répondre et ne répond pas, oui : après cinq tentatives, la destination dit ce qui se
            passe, et reprend d’elle-même dès qu’elle répond à nouveau.
          </Rule>
        </Group>

        <Group title="La copie des documents" summary="Dans un sens, et jamais destructrice">
          <Rule title="Elle n’efface jamais rien à l’arrivée">
            Du local vers la destination, et c’est tout. Votre portable contient ce qui est en cours,
            le NAS contient tout : un dossier absent du portable est l’état normal d’un vieux
            dossier, pas l’ordre de l’effacer.
          </Rule>

          <Rule title="Un dossier introuvable est signalé, pas supprimé">
            Si le répertoire indiqué pour un dossier n’existe plus, rien n’est touché à l’arrivée.
            Réglages vous propose de détacher ce répertoire du dossier, ce qui règle le message sans
            rien effacer nulle part.
          </Rule>

          <Rule title="Ce qui n’a pas pu être lu est compté">
            Un fichier verrouillé par Word, un accès refusé : le nombre et les premiers exemples sont
            à l’écran le lendemain matin. Un document que la sauvegarde ne contient pas est une chose
            que vous avez le droit d’apprendre un mardi, pas le jour où vous en avez besoin.
          </Rule>
        </Group>

        <Group title="La clé de récupération" summary="La seule chose qu’Avocado ne peut pas vous rendre">
          <Rule title="Ce qu’elle ouvre">
            Le coffre est chiffré avec elle. Une sauvegarde sans la clé est un fichier illisible, y
            compris pour nous, y compris pour celui qui héberge la copie. C’est ce qui rend
            acceptable de poser vos écritures sur un NAS partagé ou chez un hébergeur.
          </Rule>

          <Rule title="Où la garder">
            Ailleurs que sur l’ordinateur, et ailleurs que sur la sauvegarde. Imprimée dans un
            dossier du cabinet, dans un coffre, chez vous. Les deux ne doivent pas disparaître dans
            le même incendie ni dans le même vol.
          </Rule>

          <Rule title="La contrôler de temps en temps">
            Réglages propose de recopier deux groupes de votre fiche. Une clé que personne n’a jamais
            relue est une clé dont on découvre le jour venu qu’elle était illisible.
          </Rule>

          <Rule title="Le chiffrement du disque, en plus">
            BitLocker ou FileVault protègent l’ordinateur perdu ou volé. Avocado vous dit s’il est
            actif. Le coffre est chiffré de toute façon, mais vos documents, eux, sont des fichiers
            ordinaires sur votre disque.
          </Rule>
        </Group>

        <Group title="Restaurer" summary="Sur une machine neuve">
          <Rule title="Ce qu’il faut">
            La sauvegarde, et la clé de récupération. Installez Avocado, choisissez «&nbsp;Restaurer
            une sauvegarde&nbsp;», désignez la destination et saisissez la clé.
          </Rule>

          <Rule title="Les documents reviennent à part">
            Ils sont en clair dans «&nbsp;Dossiers&nbsp;» : vous pouvez les remettre vous-même, ou
            laisser Avocado les reposer dans un répertoire que vous choisissez. Il n’écrase jamais
            un fichier existant.
          </Rule>
        </Group>
      </div>
    </Panel>
  )
}

const Group = ({ title, summary, children }: {
  title: string
  summary: string
  children: ReactNode
}) => (
  <section className="border-b border-line-strong last:border-b-0">
    <header className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 bg-sunken px-4 py-2">
      <h2 className="m-0 type-label text-ink-secondary">{title}</h2>
      <p className="m-0 text-[11.5px] leading-4 text-muted">{summary}</p>
    </header>

    <div className="grid gap-3.5 px-4 py-3.5">{children}</div>
  </section>
)

const Rule = ({ title, children }: { title: string; children: ReactNode }) => (
  <div>
    <h3 className="m-0 text-[12.5px] leading-[18px] font-semibold">{title}</h3>
    <p className="m-0 mt-0.5 max-w-[68ch] text-[12.5px] leading-[19px] text-ink-secondary">
      {children}
    </p>
  </div>
)

const Mono = ({ children }: { children: ReactNode }) => (
  <span className="font-mono text-[11.5px] text-ink">{children}</span>
)
