# Glossaire, français ↔ code

Le code est en anglais, l'interface en français. Aucun terme français ne traverse l'API : le backend renvoie
des clés d'énumération (`IncomingLetter`), le front possède une table de libellés `fr.ts`.

Cette page sert à traduire un bug rapporté en français (« le bordereau ne marche pas ») vers le type
correspondant dans le code.

## Entités

| Français | Code | Ce que c'est |
|---|---|---|
| Dossier | `Matter` | Une affaire. Terme consacré dans les logiciels juridiques anglophones (Clio, MyCase). Pas `Case`. |
| Tiers | `Contact` | Toute personne physique ou morale du carnet d'adresses. |
| Partie au dossier | `MatterParty` | Le lien entre un `Contact` et un `Matter`, avec son rôle. |
| Journal / suivi | `Activity` | Un événement du dossier : appel, mail, courrier, RDV, note, audience. |
| Document | `Document` | N'importe quel fichier rattaché au dossier. |
| Pièce | `Document.ExhibitNumber` / `.ExhibitLabel` | Un document promu au rang de preuve, numéroté. |
| Échéance | `Deadline` | Audience ou délai. |
| Temps passé | `TimeEntry` | |
| Facture | `Invoice` | Suivi seulement, Avocado ne génère aucune facture. |
| Provision, débours, régularisation | `LedgerEntry` | Mouvement d'argent signé, hors facture. |

## Vocabulaire métier

| Terme | Anglais | Explication |
|---|---|---|
| **Pièce** | exhibit | Élément de preuve **communiqué à la partie adverse**, numéroté et cité dans les conclusions (« la pièce n°7 »). Son libellé est écrit pour le juge, « Contrat de travail de M. Dupont du 12 mars 2019 », pas `scan_003.pdf`. Les conclusions et la correspondance avec son propre client ne sont **jamais** des pièces. |
| **Bordereau de communication de pièces** | schedule of exhibits | Liste datée et numérotée des pièces communiquées, qui prouve *quoi* et *quand*. Hors périmètre v1. |
| **Conclusions** | pleadings / submissions | L'argumentaire écrit déposé au tribunal. Un `Document`, jamais une pièce. |
| **N° RG** (Répertoire Général) | docket number | Le numéro que le tribunal attribue à l'affaire. Il figure sur toute la correspondance : quand le greffe appelle, il donne le RG, pas le nom du client. D'où `CourtCaseNumber`, et son indexation dans la recherche. |
| **Provision sur honoraires** | retainer | Somme versée par le client **avant** la prestation. |
| **Débours** | disbursement | Frais avancés pour le compte du dossier (huissier, expert, greffe). |
| **Convention d'honoraires** | fee agreement | Obligatoire depuis 2015. Ici, un simple `Document`. |
| **Juridiction** | court | Tribunal judiciaire, conseil de prud'hommes, cour d'appel… Hors périmètre v1. |
| **Confrère / consœur** | opposing counsel | L'avocat d'en face. Un `Contact`, avec `MatterParty.Role` en texte libre. |
| **Commissaire de justice** | (ex-*huissier*) | Nouveau nom depuis 2022. |
| **Greffe** | court clerk's office | |
| **Secret professionnel** | legal professional privilege | La raison d'être du chiffrement. |
