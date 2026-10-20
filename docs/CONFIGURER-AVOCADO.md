# Configurer Avocado dans un cabinet

**Pour l'assistant qui lit ce fichier.** Ce document est une marche à suivre pour brancher les trois
choses qu'Avocado ne peut pas deviner seul : l'agenda Google, la sauvegarde sur le NAS, et le tampon
des pièces. Il est écrit pour être suivi avec l'avocate devant son écran, pas exécuté à sa place.

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
2. **Cliquer dans Réglages**, si tu as le contrôle de son écran. Il n'y a que des champs de texte et
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

## 2. L'agenda Google

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

## 3. La sauvegarde sur le NAS

Avocado sait déjà faire ça, il faut juste le lui dire. La base du coffre est copiée en continu ; les
**documents** sont capturés une fois par jour, à 22 h par défaut, parce que lire douze gigaoctets se
sent sur une machine en train de travailler. Une nuit où le portable était fermé est rattrapée à
l'ouverture suivante, jamais sautée.

### Les étapes

1. Réglages → *Vos données* → **Sauvegarde** → ajouter une destination de type dossier.
2. Le sélecteur de dossiers de Windows s'ouvre. Deux façons de désigner le NAS :
   - **un lecteur réseau mappé** (`Z:\...`) — **à préférer aujourd'hui**, voir la mise en garde ;
   - un chemin UNC tapé dans la barre d'adresse du sélecteur : `\\NAS\partage\...`.
3. Renseigner le **sous-dossier**, par exemple `Sauvegardes/Avocado`. Le NAS est partagé, il contient
   déjà des années d'archives, et Avocado n'écrira que dans ce sous-dossier.
4. Choisir une heure de capture où le portable est allumé **et** le NAS joignable.

> **Mise en garde, jusqu'au prochain correctif.** Avec un chemin UNC brut (`\\NAS\partage`), l'écran
> peut afficher à tort « Ce dossier est sur cet ordinateur… les deux copies partiraient ensemble ».
> C'est un défaut d'affichage connu : la sauvegarde fonctionne, mais le diagnostic est faux. Un
> lecteur mappé (`Z:`) est correctement reconnu comme « Emplacement réseau ». Préférer le lecteur
> mappé pour ne pas avoir à se demander si l'avertissement est vrai.

### Vérifier que ça marche

- « Lancer maintenant », puis l'écran date la dernière copie réussie.
- Aller regarder le sous-dossier sur le NAS : il contient quelque chose.
- Le NAS est annoncé comme **« Emplacement réseau »** (voir la mise en garde ci-dessus).

### Ce qu'il faut lui dire

Un NAS dans le même bureau que le portable protège d'une panne de disque, pas d'un incendie, pas d'un
vol, pas d'un rançongiciel qui monte le partage réseau. **Garder une seconde destination hors ligne**,
une clé USB rebranchée une fois par semaine. Avocado accepte plusieurs destinations exprès.

Et : une sauvegarde ne se restaure pas sans la **clé de récupération**. Si elle ne sait pas où elle
est, c'est plus urgent que tout le reste de ce document.

---

## 4. Le tampon des pièces

Réglages → *Le cabinet* → **Tampon des pièces**. La prévisualisation montre le tampon posé sur une
page qui ressemble à un document, et c'est sur elle qu'on règle — jamais sur un vrai versement.

- **Largeur.** 120 pt par défaut, soit environ 4,2 cm, ce qui est grand. Descendre vers 85 pt
  (environ 3 cm) et regarder la prévisualisation.
- **Marge.** Distance aux bords de la page.
- **Position du numéro.** Deux réglages en pourcentage, « depuis la gauche » et « depuis le haut ».
  Nudger jusqu'à ce que le numéro tombe sur les pointillés de son tampon.
- **Couleur du numéro.** À vérifier si le tampon est foncé : un numéro noir sur un carré noir est
  dans la page, aux bonnes coordonnées, et invisible.
- **Fond blanc.** Case à cocher — **seulement si la version installée la propose**. Si elle n'y est
  pas, cette version est antérieure au correctif ; ne pas chercher à la simuler.

> **Si son tampon vient d'un fichier SVG**, Avocado déduit la position du numéro du repère
> `{{numero}}` écrit dans le dessin. Cette déduction est fausse pour les SVG dont le dessin est
> enveloppé dans une transformation, ce qui est le cas de presque tout ce qui sort d'Illustrator ou
> d'Inkscape : le numéro atterrit à côté des pointillés. Correctif en cours. En attendant, rattraper
> à la main avec les deux réglages de position.

---

## 5. Pendant que tu y es

Trois réglages qui ne sont pas des branchements, mais qui faussent des chiffres s'ils sont faux :

- **Taux horaire et exercice** (Réglages → *Le cabinet*). Le jour et le mois de début d'exercice
  pilotent le chiffre d'affaires par exercice affiché sur l'accueil. Par défaut ce n'est pas
  forcément le sien.
- **Emplacements** (Réglages → *Vos données*) : où sont ses dossiers en cours et ses dossiers
  clôturés.
- **Chiffrement du disque.** Si l'écran dit que BitLocker est inactif, ses documents ne sont chiffrés
  que dans les sauvegardes, pas sur la machine. C'est à régler dans Windows, pas dans Avocado, et ça
  vaut une phrase.

---

## 6. Rappels de vocabulaire

Tout est **hors taxes**, partout : le temps passé, le facturé, l'encaissé, les provisions. Avocado
n'émet aucune facture et ne calcule aucune TVA — son logiciel comptable le fait déjà. Si un montant
TTC entre quelque part, le chiffre est faux et personne ne le verra.

- **Boni / mali** : l'écart entre ce qu'elle a facturé et ce que valaient les heures rattachées à
  cette facture. Facturer 6 000 € pour 7 200 € d'heures est un mali de 1 200 €, décidé exprès.
- **Reste à facturer** : le temps qu'aucune facture n'a encore consommé. Sur un dossier au forfait,
  c'est le forfait moins ce qui est déjà sorti.
- **Provision** : de l'argent reçu, pas du travail facturé. Elle ne diminue pas le reste à facturer.
- **Pièce** : un document numéroté, copié dans un sous-dossier du dossier, cité par son numéro devant
  un tribunal. Le numéro ne se réemploie jamais, même libéré.
