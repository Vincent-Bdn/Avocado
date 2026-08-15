# Avocado

**Le suivi de dossiers pour les avocats.** Tout reste sur votre ordinateur, chiffré. Aucun serveur,
aucun compte, aucun abonnement.

### 👉 [Vous êtes avocat ? Le site, et le téléchargement, c'est ici.](https://vincent-bdn.github.io/Avocado/)

> Cette page-ci est le dépôt de code. Elle s'adresse à qui veut lire, vérifier ou modifier le
> logiciel. Pour l'installer et s'en servir,
> [le site](https://vincent-bdn.github.io/Avocado/) dit tout, sans une ligne de code.

---

## Ce que c'est

Un dossier réunit son client, le journal de ce qui s'y passe, ses documents, ses échéances, le temps
que vous y consacrez et ce qu'il vous reste à facturer. Le reste en découle.

- **Le journal** est le geste central, et le plus rapide : `⌘J` depuis n'importe où, deux lignes,
  `⌘⏎`, avec le temps passé dans le même geste. Ce qui n'est pas noté au moment où l'on raccroche ne
  se facturera jamais.
- **Les documents** se rangent chiffrés dans le coffre, s'ouvrent dans Word d'un double-clic, et
  chaque enregistrement y revient tout seul. Un document devient une **pièce** quand il reçoit un
  numéro et un libellé écrit pour le juge.
- **Le temps et la facturation** : Avocado n'émet aucune facture et ne calcule aucune TVA, le
  logiciel comptable le fait déjà. Il note ce qui est parti pour que ce qui reste soit connu, boni et
  mali compris, rétrocessions d'honoraires comprises.
- **Les tiers** portent un rôle en texte libre, propre à chaque dossier, et se remplissent depuis
  l'annuaire des entreprises.

La description complète, écrite pour un avocat plutôt que pour un développeur, est sur
[le site](https://vincent-bdn.github.io/Avocado/).

> Logiciel libre sous licence AGPL-3.0. Le code est lisible, vérifiable et réutilisable ;
> ce que vous en faites vous appartient.

---

## Le modèle, en trois phrases

**Un coffre est un dossier sur le disque.** Base de données, documents, modèles : tout y est chiffré
en permanence, y compris application fermée. Sauvegarder Avocado, c'est copier ce dossier.

**Deux façons de l'ouvrir.** Au quotidien aucune, la clé est gardée par le système d'exploitation et
liée à cette machine et à cette session. La **clé de récupération**, neuf groupes de six caractères,
est l'autre chemin et le seul qui traverse les machines. Il n'en existe aucune autre copie.

**Une seule requête sort de la machine**, la recherche dans l'annuaire des entreprises, et elle se
coupe d'un interrupteur. Pas de compte, pas de télémétrie, pas de synchronisation, pas de mise à jour
silencieuse.

Le détail, et ce que l'application refuse de faire, sont sur
[la page « Vos données »](https://vincent-bdn.github.io/Avocado/donnees.html) et dans
[docs/SECURITY.md](docs/SECURITY.md).

---

## Pour les développeurs

| Document | Ce qu'il couvre |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | La forme générale : les trois projets, comment ils s'assemblent, pourquoi |
| [docs/BACKEND.md](docs/BACKEND.md) | Le service C# : compilation, cycle de vie, API, permissions, comment une version est coupée |
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

Fabriquer les installeurs localement, ce que fait le job `desktop` de la CI :

```bash
dotnet publish src/Avocado.Server -c Release -r win-x64 -o artifacts/backend
cd app && npm ci && npm run build
npx electron-builder --win --x64 --publish never
```

### Ce que contient le dépôt

| Dossier | Ce qu'il y a dedans |
|---|---|
| `src/` | `Avocado.Vault` (chiffrement, coffre), `Avocado.Server` (l'API), `Avocado.Cli` (le coffre en ligne de commande) |
| `app/` | La coque Electron et l'interface React |
| `site/` | Le site public, du HTML et une feuille de style, sans étape de compilation. Publié par `.github/workflows/pages.yml` |
| `ds/` | Le design system et les maquettes d'écran |
| `docs/` | La documentation technique |

### Ce que publie une version

Pousser un tag `v*` déclenche tout. Une version publie :

- **Les installeurs**, `Avocado-<os>-<arch>.<ext>` : NSIS et zip sur Windows, `.dmg` et zip sur macOS,
  AppImage et tarball sur Linux x64. **Le numéro de version est volontairement absent de ces noms**,
  parce que les boutons du site pointent sur `/releases/latest/download/Avocado-win-x64.exe`, une
  adresse qui ne résout que si le nom ne change jamais.
- **Le coffre en ligne de commande**, `avocado-cli-<tag>-<rid>`, pour les six RID.
- `SHA256SUMS` sur l'ensemble.

Rien n'est signé numériquement : un certificat Windows coûte 200 à 400 € par an, le programme
développeur Apple 99 $ par an, et ce projet ne porte pas ce coût.
[La page d'installation](https://vincent-bdn.github.io/Avocado/installation.html#pourquoi-avertissement)
explique aux utilisateurs ce que SmartScreen et Gatekeeper leur diront, et pourquoi.

---

## Licence

**AGPL-3.0-only.** Vous pouvez lire, modifier et redistribuer ce logiciel ; si vous le proposez à
d'autres, y compris à travers un réseau, vous devez publier vos modifications sous la même licence.

Les données de votre cabinet ne sont couvertes par aucune licence : elles sont à vous, dans un format
ouvert, sur votre disque.
