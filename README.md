# Avocado

**Le suivi de dossiers pour les avocats.** Tout reste sur votre ordinateur,
chiffré. Aucun serveur, aucun compte, aucun abonnement.

Avocado remplace les logiciels de gestion de cabinet dont on hérite en s'installant : ceux qui
demandent trois écrans pour noter un appel, et dont personne ne sait plus où sont les données. Ici,
un dossier réunit son client, le journal de ce qui s'y passe, ses documents, ses échéances, le temps
que vous y consacrez et ce qu'il vous reste à facturer. Le reste en découle.

> Logiciel libre sous licence AGPL-3.0. Le code est lisible, vérifiable et réutilisable ;
> ce que vous en faites vous appartient.

---

## Ce que ça fait

### Les dossiers

Un dossier porte une référence, un intitulé, un client, une date d'ouverture et un taux horaire.
Il est **en cours** tant qu'il n'a pas de date de clôture, et **clôturé** ensuite : il n'y a pas de
statut à tenir à jour. Vous le classez en *conseil* ou en *contentieux*, ou avec vos propres mots,
et un dossier contentieux porte en plus sa juridiction et son n° RG.

Les quelques dossiers du mois se mettent en favori et remontent en tête de liste.

### Le journal

Le geste central de l'application, et le plus rapide : `⌘J` depuis n'importe où, deux lignes, `⌘⏎`.
Un appel, un courrier, un rendez-vous, une audience. Et, dans le même geste, **le temps passé**.

C'est là que se joue l'essentiel : ce qui n'est pas noté au moment où l'on raccroche ne se facturera
jamais. Le temps saisi depuis le journal remonte automatiquement dans l'onglet *Temps passé* et dans
ce qui reste à facturer.

### Les documents

Chaque fichier qui arrive au dossier s'y range, chiffré, dans les dossiers de classement que vous
choisissez. Un document devient une **pièce** quand vous lui donnez un numéro et un libellé écrit
pour le juge ; les numéros retirés restent libres, parce qu'ils sont peut-être déjà cités dans des
conclusions déposées.

Un double-clic ouvre le fichier dans Word, ou dans ce que votre ordinateur utilise pour ce type de
fichier. **Chaque enregistrement revient tout seul dans le coffre**, sans rien exporter ni
réimporter.

### Les modèles

Écrivez votre lettre de mission une fois, dans Word, en laissant des repères là où le dossier doit
s'écrire : `{{client.nom}}`, `{{dossier.reference}}`, `{{dossier.tauxHoraire}}`. Depuis un dossier,
« Générer depuis un modèle » les remplit et dépose le résultat dans le coffre, où vous l'ouvrez et le
terminez.

### Les échéances

Une audience, un délai de procédure, un rendez-vous : ce qui a une date et ne doit pas être manqué.
Elles se regroupent par urgence (dépassées, aujourd'hui, cette semaine, plus tard) et chacune porte
la distance en toutes lettres, de sorte qu'une impression en noir et blanc reste lisible.

### Le temps et la facturation

Avocado **n'émet aucune facture** et ne calcule aucune TVA : votre logiciel comptable le fait déjà.
Il note ce qui est parti, pour que ce qui reste soit connu.

- Le temps se saisit en heures et minutes, facturable ou non, avec un taux dérogatoire si vous en
  avez accordé un.
- Pour facturer, vous **choisissez les lignes de temps**, vous voyez ce qu'elles valent, et vous
  décidez du montant. L'écart entre les deux est enregistré comme **boni** ou **mali** : c'est là que
  se lit où le cabinet a gagné de l'argent et où il en a laissé.
- Les provisions reçues et les frais avancés se notent comme *mouvements* et viennent en déduction.
- Quand une partie du travail est **sous-traitée** à un confrère, la rétrocession d'honoraires
  s'enregistre à part. Elle ne change rien à ce que le client doit : elle change ce qui vous reste,
  et le dossier affiche son montant net.
- Un bouton produit le **détail de facturation** en Excel, à joindre à la facture envoyée au client.

### Les tiers

Clients, parties adverses, confrères, experts. Le même tiers est client sur un dossier et partie
adverse sur un autre : le rôle est du texte libre, propre à chaque dossier, et *client* est le seul
que l'application interprète, c'est lui qui alimente la facturation.

Pour une société, tapez trois lettres de sa raison sociale ou son SIREN : Avocado interroge
l'**annuaire des entreprises** et remplit le SIREN, la forme juridique et l'adresse. Cette recherche
se coupe d'un interrupteur, et c'est la seule requête que l'application envoie sur Internet.

### L'accueil

Ce que vous voyez en ouvrant le matin : ce qui tombe, ce qui a été gagné et pas encore demandé, où
vous en étiez, et un graphique sur douze mois qui répond à une seule question, **est-ce que je
facture ce que je travaille ?**

### La recherche

`⌘K` ouvre la palette : dossiers, tiers, documents. `@` pour ne chercher que dans les tiers, `#` que
dans les documents.

---

## Comment ça marche

### Un coffre, un dossier sur votre disque

Toute votre pratique tient dans **un seul dossier**, que vous choisissez à l'installation. Base de
données, documents, modèles : tout y est chiffré en permanence, y compris quand l'application est
fermée. Personne ne peut le lire sans une clé, y compris nous.

Sauvegarder Avocado, c'est copier ce dossier. Le déplacer sur un autre ordinateur, c'est le copier et
saisir votre clé de récupération.

### Deux façons d'ouvrir le coffre

**Au quotidien, aucune.** La clé est gardée par votre système d'exploitation et liée à cette machine
et à votre session Windows. Vous ouvrez l'application, elle s'ouvre.

**La clé de récupération** est l'autre chemin, et le seul qui traverse les machines : neuf groupes de
six caractères, imprimés sur une feuille A4 avec un QR code au moment de l'installation. C'est elle
qui rouvrira vos dossiers sur un ordinateur neuf, après un vol ou une panne. **Il n'en existe aucune
autre copie** : ce n'est pas un mot de passe qu'on réinitialise. L'assistant de démarrage refuse de
vous laisser passer avant que vous l'ayez mise à l'abri.

Vous pouvez la contrôler à tout moment depuis les réglages, recopier deux groupes de votre feuille
suffit, et en éditer une nouvelle si la feuille est perdue.

### Ce qui ne quitte pas votre ordinateur

Tout, à une exception près : la recherche dans l'annuaire des entreprises, qui ne reçoit que ce que
vous tapez dans le champ « Raison sociale », et qui se désactive.

Pas de compte, pas de télémétrie, pas de synchronisation, pas de mise à jour silencieuse.

### Ce qu'Avocado refuse de faire

- **Installer le coffre dans un dossier synchronisé** (OneDrive, Dropbox, Google Drive). Une base de
  données vivante dans un dossier synchronisé finit corrompue. L'application vous propose le montage
  qui marche : le coffre en local, les sauvegardes dans le dossier synchronisé.
- **Vous laisser passer l'étape de la clé de récupération** sans l'avoir imprimée, copiée ou
  enregistrée sur une clé USB.
- **Migrer une base sans en faire une copie d'abord.** Chaque mise à jour du schéma prend un
  instantané avant de toucher à quoi que ce soit, et vous dit où il est.

---

## Installation

Une application de bureau pour **Windows, macOS et Linux**. Elle embarque tout ce dont elle a besoin :
il n'y a pas de runtime à installer d'abord, pas de base de données à administrer, pas de service à
démarrer.

Au premier lancement, un assistant vous demande deux choses : où vivront vos dossiers, et de mettre
votre clé de récupération à l'abri. Trois minutes, puis vous n'en entendez plus parler.

---

## Pour les développeurs

| Document | Ce qu'il couvre |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | La forme générale : les trois projets, comment ils s'assemblent, pourquoi |
| [docs/BACKEND.md](docs/BACKEND.md) | Le service C# : compilation, cycle de vie, API, permissions |
| [docs/FRONTEND.md](docs/FRONTEND.md) | La coque Electron et l'interface React |
| [docs/SECURITY.md](docs/SECURITY.md) | Le modèle de menace et tout ce qui en découle |
| [docs/GLOSSARY.md](docs/GLOSSARY.md) | Le vocabulaire français du métier et son équivalent dans le code |

Démarrage rapide :

```bash
dotnet build                    # le service et le coffre
cd app && npm install
npm run electron:dev            # compile le rendu, puis lance l'application
```

Les tests du coffre, chiffrement, clé de récupération, sauvegardes, sont dans
`tests/Avocado.Vault.Tests` :

```bash
dotnet test
```

---

## Licence

**AGPL-3.0-only.** Vous pouvez lire, modifier et redistribuer ce logiciel ; si vous le proposez à
d'autres, y compris à travers un réseau, vous devez publier vos modifications sous la même licence.

Les données de votre cabinet ne sont couvertes par aucune licence : elles sont à vous, dans un format
ouvert, sur votre disque.
