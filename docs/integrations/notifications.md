# Notifications (badges)

Connecteur `StreamDak.Integrations.Notifications` : un **badge rouge** avec le nombre de non lus (« 99+ » au-delà) sur les boutons « site web » de
**Gmail**, **Google Chat**, **Discord**, **Microsoft Teams** et **Slack**, et de tout service que tu décris toi-même. ADR 0020.

## Activer

1. Crée (ou garde) un bouton qui ouvre le site : catalogue › *Sites web*, ou glisse l'adresse sur une case. Le badge se pose sur tout bouton dont une
   étape « Lien web » porte l'adresse du service (`mail.google.com`, `chat.google.com`, `discord.com`, `teams.microsoft.com`, `slack.com`…).
2. **Connecte-toi une fois au service dans le deck** : ajoute une page web ou un widget web du même service (catalogue › Widgets › *Gmail*, *Google Chat*),
   et connecte-toi dans son cadre. Le suivi utilise la **même session** (profil WebView2 de StreamDak). Aucun mot de passe n'est lu ni enregistré.
3. **Paramètres › Intégrations › Notifications (badges)** : active le connecteur. Facultatif : *Services suivis* (`gmail, discord`) pour n'en suivre que
   quelques-uns.

Tant qu'un service n'est pas connecté, ou que la page affichée n'est pas la sienne (page de connexion), son bouton n'a pas de badge.

## Comment c'est lu

Une page WebView2 **cachée** par service (jamais de fenêtre, son coupé, aucune autorisation, aucun téléchargement) reste ouverte. Le nombre est lu
**sur événement** : à chaque changement du titre de la page (« (3) Discord | #général ») ou fin de navigation ; aucun relevé périodique. Deck réduit ou
masqué, les pages sont mises en veille. Coût : un processus de navigateur (≈ 100 Mo) par service suivi, seulement pour ceux qui ont un bouton.

| Service | Titre lu | Remarque |
|---|---|---|
| Gmail | `(12)` n'importe où dans le titre | |
| Discord, Teams | `(3)` en début de titre | |
| Google Chat | `(3)` en début de titre, sinon **libellés d'accessibilité** `aria-label` / `title` de la page (« 3 messages non lus », « 2 unread ») | voir plus bas |
| Slack | `(3)` en début de titre, ou `*` / `!` (au moins un) | |

**Non vérifié sur le vrai Google Chat** (pas de compte ici) : les libellés réels de son DOM ; les tests couvrent des libellés français et anglais sur une page locale.

**Ces formats ne sont pas garantis** : les services changent leur page. Non vérifié ici (pas de compte) : le titre réel de chacun ; les tests couvrent les formats
ci-dessus avec de vraies pages WebView2 servies localement. Si un service ne suit plus ce format, son badge reste simplement absent : décris-le alors
toi-même (ci-dessous).

## Décrire son propre service : `notifications.json`

Fichier dans le dossier de configuration (`%APPDATA%\StreamDak`), relu au démarrage :

```json
{
  "providers": [
    {
      "id": "intranet",
      "name": "Intranet",
      "url": "https://intranet.exemple.fr/messages",
      "hosts": ["intranet.exemple.fr"],
      "titleRegex": "Messages \\((\\d+)\\)",
      "selector": "span.badge-unread",
      "attribute": "data-count"
    }
  ]
}
```

| Clé | Rôle |
|---|---|
| `id` | 1 à 32 caractères : minuscules, chiffres, tiret (pas ceux des services livrés) |
| `url` | page suivie (http ou https) |
| `hosts` | noms d'hôte du service (par défaut : celui de `url`) ; un hôte `exemple.fr` couvre `app.exemple.fr` |
| `titleRegex` | expression régulière du titre ; le premier groupe est le nombre |
| `selector` | sélecteur CSS des pastilles de la page : somme de leurs nombres (un élément sans nombre compte pour 1) ; relu toutes les minutes |
| `attribute` | attribut qui porte le nombre (sinon le texte de l'élément) |

Il faut `titleRegex` ou `selector`. Au plus 10 services, 64 Ko. Une entrée invalide est ignorée et signalée dans `streamdak.log`. Le sélecteur est
transmis à la page comme une **chaîne** (jamais collé dans du code), le script ne fait que lire le DOM, et une expression régulière trop lente est
interrompue après 100 ms.

## Mode de badge par service

Chaque service a un **mode** (`auto` par défaut) qui dit d'où vient le nombre. Réglage dans `notifications.json`, sans nouvelle clé de `config.json` (ADR 0022).

| Mode | Source du nombre |
|---|---|
| `auto` | le titre de la page d'abord ; à défaut le sélecteur CSS (ou, pour Google Chat, les libellés « non lus ») ; un **point** si l'icône (favicon) de l'onglet a changé **et seulement si `favicon` est activé** |
| `title` | le titre seul |
| `selector` | le sélecteur seul (observateur sur le DOM, sans relecture à la minute) |
| `off` | aucun suivi : aucune page cachée n'est ouverte pour ce service |

Un service que tu décris prend `"badgeMode": "title"` dans son entrée. Pour un service **livré**, la section `badges` change son mode (et, au besoin, son sélecteur) :

```json
{ "badges": { "gmail": { "badgeMode": "off" }, "slack": { "badgeMode": "selector", "selector": "span.p-channel_sidebar__badge", "attribute": "data-count" } } }
```

Un mode inconnu, un mode `selector` sans sélecteur ou un sélecteur trop long : l'entrée est ignorée, avec un avertissement dans `streamdak.log`. Un compteur lu à **0**, ou l'absence de nombre, n'affiche **aucun** badge. Le **point** d'activité par icône n'existe que **sur demande** : `"favicon": true` dans l'entrée du service
(défaut faux ; la première icône vue n'est pas toujours « sans notification », ce qui donnait de faux badges). Google Agenda n'a volontairement pas de badge.

### Google Chat : nombre de non lus (libellés d'accessibilité)

Google Chat n'affiche aucun `(N)` dans le titre. Son suivi lit donc, avec l'observateur (`MutationObserver`, anti-rebond 500 ms), les attributs `aria-label` et `title` de la
page cachée : un libellé contenant un nombre associé à « non lu(s/e/es) » ou « unread » (« 3 messages non lus », « 2 unread », « Non lus : 4 », « Unread (5) ») compte pour ce nombre.
Les classes CSS de Google sont obfusquées et instables ; les libellés d'accessibilité le sont beaucoup moins. Garde-fous : un libellé de plus de 120 caractères est ignoré (jamais un texte de
message), un nombre doit avoir 1 à 6 chiffres et être strictement positif, au plus 200 éléments candidats, un élément qui en contient un autre portant aussi un libellé n'est pas compté deux fois
(parent et enfant : seul le plus interne compte), somme bornée à 999 999. Le titre reste prioritaire s'il contient un nombre. Si tu poses un `selector`, il remplace cette lecture.

Ajuster sans recompiler, dans `notifications.json` :

```json
{ "badges": { "gchat": { "labelPattern": "([0-9]+) à lire", "favicon": false } } }
```

`labelPattern` : expression régulière (200 caractères, au moins un groupe de capture : le premier groupe qui capture est le nombre ; insensible à la casse) appliquée à chaque libellé ;
`selector` / `attribute` : sélecteur CSS et attribut à la place des libellés ; `favicon` : point d'activité par icône (défaut `false`). Valeurs invalides : entrée ignorée avec un avertissement.

**Journal** (`streamdak.log`, jamais de texte de conversation) : une ligne par changement, par exemple `Google Chat : mode Auto · libellés non lus · candidats 2 · retenu 5`, et, une fois, un
avertissement si l'icône de l'onglet a changé alors que **0 candidat** n'a été trouvé : le réglage (`labelPattern`, `selector`) ne correspond pas à la page, à ajuster.

## Ajouter un fournisseur pour un nouveau service

**Sans recompiler** : une entrée de `notifications.json` (voir plus haut) suffit pour un service dont le titre ou le DOM porte le nombre.

**Dans le code** : un service livré est une ligne de `NotificationProviders.BuiltIn` (`Core/Notifications/NotificationProviders.cs`) :
`new NotificationProvider("id", "Nom", "https://page-suivie/", new[] { "hote.exemple" }, motifDeTitre)`. Le motif rend le nombre dans le groupe `n`
(`LeadingCount` pour « (3) » en tête de titre ; le groupe `m` signifie « au moins un »). Un service très différent implémente `INotificationProvider`
(`Matches`, `CountFromTitle`, et au besoin `CountScript` / `ObserverScript`) et se passe à `NotificationProviders`. Règles :

1. un nombre n'est lu que sur une page de l'**hôte** du service (jamais un hôte qui lui ressemble) ;
2. aucun secret, aucun code distant exécuté : le script est en lecture seule et le sélecteur est une chaîne ;
3. ajouter des tests dans `tests/StreamDak.Tests/NotificationTests.cs` (titre réel, hôte voisin, mode) et mettre à jour ce document.

## Où le badge apparaît

- **Touche « site web »** : pastille rouge au coin haut droit (`TileFace`).
- **Onglet d'une page web** : `InfoBadge` de WPF-UI (rouge « critique » du thème, clair ou sombre), nombre plafonné « 99+ », **simple point rond** pour une activité sans nombre (seulement avec `favicon`),
  rien à zéro. Nom accessible : « 3 notifications non lues ».
- **Bouton du deck dans la barre des tâches** : pastille (*overlay*) avec le **total** des non lus de toutes les pages web de **tous les profils**, « 99+ » au-delà,
  retirée à zéro. Une activité (point) ne compte pas ; un service suivi par plusieurs pages n'est compté qu'une fois. ADR 0023.

## Réglages par page web

En mode édition, menu de la page (bouton *Options de la page* du dock) :

- **Afficher le badge** : décoché, la page n'a plus de badge et ne compte plus dans le total.
- **Exclure du total** : la page garde son badge mais n'entre pas dans la pastille de la barre des tâches.

Clés `BadgeEnabled` (absent = vrai) et `BadgeExcludeFromTotal` (absent = faux) de la page dans `config.json` ; modifications annulables.

## Diagnostiquer un badge absent

Consulte `%APPDATA%\StreamDak\streamdak.log` (le titre lu et le compteur de chaque service y sont consignés, jamais un secret). Liste de vérifications : [tests manuels des badges](../tests-manuels-badges.md).

## Limites

- Le badge ne compte que ce que voit la **session de StreamDak**. Si tu lis ta messagerie dans un autre navigateur avec un autre compte, les nombres peuvent différer.
- Sans WebView2 (Windows 11 l'inclut), le connecteur ne peut pas suivre les pages ; il le consigne et n'affiche rien.
- Commande *Actualiser les badges* : relit tout de suite chaque service.
