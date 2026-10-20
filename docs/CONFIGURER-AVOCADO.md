# Configurer Avocado dans un cabinet

**Pour l'assistant qui lit ce fichier.** Ce document est une marche à suivre pour la mise en route
d'Avocado dans un cabinet : les deux réparations à faire dans les données après cette mise à jour,
puis les trois choses qu'Avocado ne peut pas deviner seul — l'agenda Google, la sauvegarde sur le
NAS, et le tampon des pièces. Il est écrit pour être suivi avec l'avocate devant son écran, pas
exécuté à sa place.

---

## 1. Ce que tu peux faire, et ce que tu ne peux pas

**Tu ne peux pas piloter Avocado par son API.** Le service écoute sur 127.0.0.1, mais :

- le jeton d'authentification est tiré au hasard **à chaque lancement**, il est passé au service par
  variable d'environnement et **ne touche jamais le disque** ;
- le port est choisi par le système d'exploitation au démarrage, il change à chaque fois.

Aucun processus extérieur ne peut donc appeler cette API, ni aujourd'hui ni avec un connecteur. Ce
n'est pas un oubli : c'est la raison pour laquelle une page web ouverte dans son navigateur ne peut
pas lire son coffre. **N'essaie pas de contourner ça**, et ne propose pas de le faire.

**Ce que tu peux faire, par ordre d'utilité :**

1. **La console Google Cloud**, qui est un site web. C'est 80 % de la difficulté de ce document, et
   c'est entièrement à ta portée si tu as accès à son navigateur.
2. **Cliquer dans Avocado**, si tu as le contrôle de son écran. Il n'y a que des champs de texte et
   des boutons.
3. **La guider et diagnostiquer.** Les messages d'erreur de Google sont opaques ; la section
   « quand ça rate » plus bas les traduit.

**Ce à quoi tu ne touches jamais :**

- le coffre (`Avocado/vault`) : base chiffrée, clés liées à la machine. Ne lis pas, n'écris pas, ne
  copie pas, n'édite aucune base de données ;
- ses répertoires de dossiers : Avocado les lit et ne les déplace pas, et c'est un engagement du
  logiciel. Ne renomme rien, ne range rien ;
- les dossiers `Pièces` existants : les numéros de pièces sont cités dans des conclusions déjà
  déposées devant un tribunal. Un fichier renommé est une référence cassée chez un magistrat ;
- la clé de récupération : neuf groupes de six caractères, aucune autre copie n'existe.

---

## 2. À faire en premier : deux réparations dans les données

Cette version corrige deux défauts qui ont laissé des traces dans son coffre. Les correctifs
empêchent que ça se reproduise ; ils ne réparent pas le passé, parce que personne ne peut deviner
après coup quelles heures une facture couvrait. **Ces deux points d'abord, le reste ensuite.**

### 2.1 Les dossiers où une facture a été glissée puis soldée en mali

**Ce qui se passait.** Déposer le PDF d'une facture sur un dossier enregistrait la facture mais ne
soldait aucune ligne de temps passé. Les heures restaient dans « reste à facturer », la facture
n'affichait ni boni ni mali, et le réflexe était alors de solder ces heures en mali à la main — ce
qui faisait apparaître un mali sur un dossier qui n'en avait jamais accordé. **Les chiffres étaient
faux dans les deux sens à la fois.**

**Le dossier qu'elle a signalé est PIRBAY**, mais il peut y en avoir d'autres. Comment les trouver :
onglet Facturation d'un dossier, chercher une ligne **« Temps non facturé · Soldé en mali »** qui
côtoie une facture portant **0 ligne de temps**. C'est la signature.

**La réparation, dossier par dossier, dans l'application :**

1. Supprimer la ligne « Soldé en mali ». Les heures qu'elle avait consommées redeviennent à facturer.
2. Sur la facture déposée, cliquer **Modifier**… puis la supprimer et la ressaisir — ou, plus simple,
   la laisser et utiliser **« Facturer… » → « À partir du temps passé »** en cochant les lignes que
   cette facture couvrait, avec le montant de la facture. Le boni ou le mali réel apparaît alors.
3. Vérifier que « reste à facturer » du dossier correspond à ce qu'elle pense pouvoir encore demander.

> **Ne fais pas ça en base.** Chaque étape a un effet sur trois tables et la bonne réponse dépend de
> ce que la facture couvrait, ce que seule elle sait.

**À partir de maintenant**, déposer une facture propose les lignes de temps passé, pré-cochées
jusqu'au montant lu sur le PDF, avec le boni ou le mali affiché en direct. Si le total coché dépasse
le montant de la facture, c'est un mali — et il est dit avant d'enregistrer.

### 2.2 Les deux dossiers FINANCIERE CN NIETO / FINANCIERE NIETO

Avocado sait désormais fusionner deux dossiers qui sont la même affaire. Dans la fiche du dossier **à
faire disparaître**, le bouton de fusion est à côté de « Modifier ».

1. Ouvrir **FINANCIERE NIETO** (celui qu'elle ne veut pas garder).
2. Bouton fusion → choisir **FINANCIERE CN (NIETO)** comme dossier conservé.
3. Lire la prévisualisation : elle compte les entrées de journal, les lignes de temps, les échéances,
   les factures et leur montant, les mouvements, les tiers. **Vérifier ces chiffres avec elle.**
4. Lire les avertissements. Si les deux dossiers ont des taux horaires différents, Avocado fige le
   taux d'origine sur les lignes reprises : les montants déjà constatés ne bougent pas. Si les deux
   ont un répertoire de documents, elle choisit lequel garder — **aucun fichier n'est déplacé**.
5. Réécrire le nom du dossier conservé pour confirmer.

> **C'est définitif.** Il n'y a pas d'annulation. Si la sauvegarde n'a pas tourné récemment, lancez-la
> avant (§4), parce que c'est le seul retour en arrière qui existe.

---

## 3. L'agenda Google

**Ce que ça fait, exactement.** Avocado crée dans son compte un agenda à lui, appelé
« Avocado · Échéances », et y écrit les échéances de ses dossiers. C'est **à sens unique** : Avocado
n'ouvre jamais ses autres agendas, et ne relit jamais celui-là. Si elle déplace un événement dans
Google, la passe suivante le remet où il était — l'échéance dans le dossier est le document de
référence, et deux systèmes autorisés à avoir raison en même temps est la façon dont un avocat se
retrouve au mauvais tribunal.

**Dis-le-lui avant de commencer**, parce que c'est souvent l'inverse de ce qu'on attend d'une
« connexion à l'agenda ».

### Pourquoi c'est à elle de créer l'identifiant

Avocado n'embarque pas d'identifiant Google. Son code est public, donc un secret livré avec serait un
secret publié ; et l'écran d'autorisation que Google affiche porte le nom du titulaire du projet, qui
doit être le sien. Il faut donc créer un identifiant OAuth une fois. Compter trente minutes la
première fois.

### Les quatre invariants

Les libellés de la console Google changent plusieurs fois par an. **Navigue et adapte-toi** plutôt que
de chercher mot pour mot ce qui suit. Ce qui ne change pas :

| | Valeur | Pourquoi |
|---|---|---|
| Type de client OAuth | **Application de bureau** | Voir l'encadré ci-dessous. C'est l'erreur n° 1. |
| API à activer | **Google Calendar API** | Sans elle, la connexion réussit et rien ne se synchronise. |
| Habilitation | `calendar.app.created` | Et **elle seule**. Elle laisse Avocado créer un agenda et gérer ce qu'il y met, sans aucun accès aux agendas existants. L'agenda d'une avocate contient le nom de ses clients. |
| État de publication | **En production** | Sinon Google révoque l'autorisation au bout de sept jours. |

L'habilitation complète s'écrit `https://www.googleapis.com/auth/calendar.app.created`.

> **« Application de bureau », jamais « Application Web ».** Avocado écoute sur un port tiré au
> hasard à chaque démarrage, donc l'adresse de retour est
> `http://127.0.0.1:<port-qui-change>/api/agenda/google/callback`. Un client « Application Web »
> exige la liste exacte des adresses de retour, port compris : il est donc impossible à faire
> fonctionner ici, et il échoue avec `redirect_uri_mismatch`. Un client « Application de bureau »
> accepte n'importe quel port sur l'adresse locale.

### Les étapes

1. **Projet.** console.cloud.google.com → créer un projet, par exemple « Avocado agenda ». Un projet
   dédié, pas un projet existant : il sera plus facile à supprimer si elle change d'avis.
2. **API.** Bibliothèque d'API → rechercher « Google Calendar API » → Activer.
3. **Écran de consentement OAuth.** Type **Externe** (ou **Interne** si le cabinet a un Google
   Workspace — c'est plus simple, et ça évite l'étape 6). Renseigner le nom de l'application et une
   adresse d'assistance : c'est ce qu'elle verra elle-même à la connexion.
4. **Habilitations.** Ajouter `calendar.app.created`, et rien d'autre. Si la console propose
   d'ajouter des habilitations plus larges sur les agendas, refuse.
5. **Utilisateurs de test.** Ajouter son adresse Google, celle de l'agenda qu'elle veut utiliser.
6. **Publier.** Passer l'écran de consentement en « En production ». À la connexion, Google affichera
   un avertissement « application non vérifiée » : c'est normal et attendu, elle est l'auteur et
   l'unique utilisatrice de cette application. **Ne pas sauter cette étape** — voir « quand ça rate ».
7. **Identifiant.** Identifiants → Créer des identifiants → ID client OAuth → type **Application de
   bureau**. Copier l'ID client (il finit par `apps.googleusercontent.com`) et le secret.
8. **Dans Avocado.** Réglages → *Le cabinet* → **Agenda Google** → coller l'identifiant et le secret
   → Enregistrer → **Connecter un compte Google**. Le navigateur s'ouvre, elle choisit son compte et
   accepte. L'onglet qui s'ouvre ensuite est à fermer, c'est tout ce qu'il demande.

### Vérifier que ça marche

- Dans Réglages, le panneau nomme le compte connecté.
- Dans Google Agenda, un agenda **« Avocado · Échéances »** est apparu.
- Une échéance existante y figure après la première passe. S'il n'y a aucune échéance dans aucun
  dossier, l'agenda sera vide et c'est correct : créer une échéance de test dans un dossier, vérifier
  qu'elle apparaît, puis la supprimer.

### Quand ça rate

| Ce que Google ou Avocado dit | Ce que c'est |
|---|---|
| `redirect_uri_mismatch` | Le client est de type « Application Web ». Il faut en recréer un en « Application de bureau » ; on ne peut pas changer le type d'un client existant. |
| `access_denied`, ou un blocage net | Son compte n'est pas dans les utilisateurs de test, ou ce n'est pas le compte qu'elle vient de choisir. |
| « Cette application n'est pas validée » | Normal. Continuer via « Paramètres avancés ». C'est son application, et elle en est la seule utilisatrice. |
| « Google n'a pas renvoyé d'autorisation durable » | Google ne délivre l'autorisation longue qu'à la première acceptation. Révoquer l'accès dans myaccount.google.com → Sécurité → Vos connexions à des applications tierces, puis reconnecter depuis Réglages. |
| Ça marchait, puis plus rien au bout d'une semaine | L'écran de consentement est resté en « Test » : Google révoque l'autorisation après sept jours. Publier (étape 6), puis reconnecter. **C'est la panne la plus fréquente et la plus déroutante.** |
| « La synchronisation est en pause » | Cinq échecs d'affilée : Avocado arrête d'essayer plutôt que de brûler le quota en silence. Corriger la cause, puis « Réessayer » dans Réglages. |

---

## 4. La sauvegarde sur le NAS

Avocado sait déjà faire ça, il faut juste le lui dire. La base du coffre est copiée en continu ; les
**documents** sont capturés une fois par jour, à 22 h par défaut, parce que lire douze gigaoctets se
sent sur une machine en train de travailler. Une nuit où le portable était fermé est rattrapée à
l'ouverture suivante, jamais sautée.

### Les étapes

1. Réglages → *Vos données* → **Sauvegarde** → ajouter une destination de type dossier.
2. Le sélecteur de dossiers de Windows s'ouvre. Désigner le NAS, au choix :
   - un chemin UNC tapé dans la barre d'adresse du sélecteur : `\\NAS\partage\...` ;
   - ou un lecteur réseau mappé (`Z:\...`). Les deux sont reconnus comme **« Emplacement réseau »**.
3. Renseigner le **sous-dossier**, par exemple `Sauvegardes/Avocado`. Le NAS est partagé, il contient
   déjà des années d'archives, et Avocado n'écrira que dans ce sous-dossier.
4. Choisir une heure de capture où le portable est allumé **et** le NAS joignable.

### Vérifier que ça marche

- L'écran annonce **« Emplacement réseau »**. S'il dit « Ce dossier est sur cet ordinateur… les deux
  copies partiraient ensemble », le chemin désigne vraiment le disque local : vérifie-le.
- « Lancer maintenant », puis l'écran date la dernière copie réussie.
- Aller regarder le sous-dossier sur le NAS : il contient quelque chose.

### Ce qu'il faut lui dire

Un NAS dans le même bureau que le portable protège d'une panne de disque, pas d'un incendie, pas d'un
vol, pas d'un rançongiciel qui monte le partage réseau. **Garder une seconde destination hors ligne**,
une clé USB rebranchée une fois par semaine. Avocado accepte plusieurs destinations exprès.

Et : une sauvegarde ne se restaure pas sans la **clé de récupération**. Si elle ne sait pas où elle
est, c'est plus urgent que tout le reste de ce document.

---

## 5. Le tampon des pièces

Réglages → *Le cabinet* → **Tampon des pièces**. La prévisualisation montre le tampon posé sur une
page qui ressemble à un document, et c'est sur elle qu'on règle — jamais sur un vrai versement.

- **Largeur.** Un cabinet qui n'a jamais touché ce réglage démarre maintenant à 85 pt, environ 3 cm.
  **Un cabinet qui avait déjà enregistré une largeur garde la sienne**, souvent 120 pt, soit 4,2 cm :
  si le tampon paraît grand, c'est ce chiffre qu'il faut baisser.
- **Marge.** Distance aux bords de la page.
- **Fond blanc.** Case à cocher, éteinte par défaut. Un logo transparent laisse passer le texte de la
  pièce à travers lui, ce qui rend le numéro difficile à lire sur les scans. À cocher si c'est le cas —
  en sachant qu'un rectangle blanc couvre ce qu'il recouvre, et que le coin haut droit d'une
  conclusion n'est pas toujours vide.
- **Position du numéro.** Deux réglages en pourcentage, « depuis la gauche » et « depuis le haut ».
  Si le tampon vient d'un SVG, Avocado déduit cette position du repère `{{numero}}` écrit dans le
  dessin, et cette déduction tient maintenant compte des transformations que les exports Illustrator
  et Inkscape ajoutent autour du dessin. Si le numéro n'est pas exactement sur les pointillés, ces
  deux réglages le rattrapent.
- **Couleur du numéro.** À vérifier si le tampon est foncé : un numéro noir sur un carré noir est
  dans la page, aux bonnes coordonnées, et invisible.

---

## 6. Ce qui a changé dans cette version, et qui se voit

Trois choses qu'elle va remarquer tout de suite. Rien à configurer, mais autant qu'elle sache où
regarder.

- **L'accueil s'ouvre sur la facturation**, en pleine largeur. Le tableau a une période à choisir —
  ce mois, le trimestre, l'exercice, ou deux dates libres — et trois chiffres en grand : facturé,
  encaissé, travaillé. **Cliquer une colonne du graphique** liste les dossiers de ce mois-là, triés
  par ce qui reste dû : c'est la liste de qui relancer.
- **Un onglet « Temps passé »** dans la barre de gauche : les jours en colonnes, les dossiers en
  lignes, les heures dans les cases. Les cases vides sont l'information — un dossier sur lequel elle
  était toute la semaine avec un mercredi blanc est une saisie oubliée.
- **Les pièces vont désormais dans un sous-dossier « Pièces Avocado »**, et non plus « Pièces ». Son
  propre dossier « Pièces », tenu à la main, n'est plus touché du tout. Avocado continue de **lire**
  les deux, donc la numérotation reprend là où elle en était : rien ne repart à 1. **Ne renomme ni ne
  fusionne ces dossiers.**
- Au versement, chaque ligne a un champ pour un suffixe : **« bis », « ter », « 1 »**. Une pièce qui
  en porte un reste sur le numéro de la ligne au-dessus, « 68, 68 bis, 68 ter, 69 », ce qui est la
  seule façon d'ajouter une pièce à un bordereau déjà envoyé. Le numéro de départ est pré-rempli avec
  le numéro où en est le dossier, 1 sur un dossier vierge.
- À l'enregistrement d'une facture, la case **« Antérieure » a disparu**. Elle ne décidait plus aucun
  calcul. Les factures qui la portaient déjà la gardent.
- La **nature du dossier** est maintenant une liste de trois : Contentieux, Conseil, Autre. Les
  natures déjà saisies dans son coffre restent proposées en bas de la liste, donc aucun dossier n'est
  reclassé.

---

## 7. Pendant que tu y es

Trois réglages qui ne sont pas des branchements, mais qui faussent des chiffres s'ils sont faux :

- **Taux horaire et exercice** (Réglages → *Le cabinet*). Le jour et le mois de début d'exercice
  pilotent le chiffre d'affaires par exercice, et les périodes « Exercice en cours » et « Exercice
  précédent » du nouvel écran de facturation. Par défaut ce n'est pas forcément le sien.
- **Emplacements** (Réglages → *Vos données*) : où sont ses dossiers en cours et ses dossiers
  clôturés.
- **Chiffrement du disque.** Si l'écran dit que BitLocker est inactif, ses documents ne sont chiffrés
  que dans les sauvegardes, pas sur la machine. C'est à régler dans Windows, pas dans Avocado, et ça
  vaut une phrase.

---

## 8. Rappels de vocabulaire

Tout est **hors taxes**, partout : le temps passé, le facturé, l'encaissé, les provisions. Avocado
n'émet aucune facture et ne calcule aucune TVA — son logiciel comptable le fait déjà. Si un montant
TTC entre quelque part, le chiffre est faux et personne ne le verra. Les champs de saisie le disent
maintenant explicitement, « € HT ».

- **Boni / mali** : l'écart entre ce qu'elle a facturé et ce que valaient les heures rattachées à
  cette facture. Facturer 6 000 € pour 7 200 € d'heures est un mali de 1 200 €, décidé exprès. Sur un
  **dossier au forfait**, une fois le forfait entièrement facturé, les heures ne sont plus facturables
  du tout : l'écart devient le prix contre le travail, et 4 000 € de temps sur un forfait de 3 000 €
  est un mali de 1 000 €. Cette version est la première à le calculer.
- **Reste à facturer** : le temps qu'aucune facture n'a encore consommé. Sur un dossier au forfait,
  c'est le forfait moins ce qui est déjà sorti.
- **Provision** : de l'argent reçu, pas du travail facturé. Elle ne diminue pas le reste à facturer.
- **Pièce** : un document numéroté, copié dans un sous-dossier du dossier, cité par son numéro devant
  un tribunal. Le numéro ne se réemploie jamais, même libéré — et un « 68 bis » revendique le 68 aussi
  fermement qu'un 68 ordinaire.
