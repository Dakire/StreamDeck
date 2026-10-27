# 0022 — Modes de badge, observateur de sélecteur et réglages par page

- **Statut** : acceptée (complète l'ADR 0020).
- **Date** : 27 octobre 2026.

## Contexte

L'ADR 0020 lit le nombre de non lus dans le titre de la page, avec un sélecteur CSS relu chaque minute pour les services décrits ainsi. Il manque : un
réglage par service (titre seul, sélecteur seul, aucun badge), un service dont seule l'icône change (Google Agenda…), une lecture du DOM sur événement plutôt
qu'à la minute, et deux réglages par page web du deck (afficher ou non son badge, l'exclure d'un total).

## Décision

**Titre** (`Core/Notifications`, pur) : `NotificationTitle.Parse` accepte `(N)` et `[N]` en tête de titre, entier de 0 à 999 999 ; tout le reste (négatif,
décimal, parenthèses mal assorties, nombre plus long, texte avant) donne « inconnu ». Les services Google Chat, Discord, Teams et Slack utilisent ce motif ;
Gmail garde son motif non ancré et Slack ses marqueurs `*` et `!` (« au moins un »). Texte du badge : plafond « 99+ » (`NotificationBadge.Cap`),
`Text(count, activity)` ajoute « • » pour une activité sans nombre. `NotificationBadge.Total` somme les compteurs en ignorant les exclus, les inconnus et
les négatifs, sans dépassement.

**Mode par service** (`BadgeMode`, défaut `Auto`) : `Auto` = le titre d'abord, puis le sélecteur s'il est configuré (pour Google Chat, les libellés d'accessibilité, ci-dessous), puis, **seulement si `favicon` est activé**, le favicon comme simple activité ;
`Title` = le titre seul ; `Selector` = le sélecteur seul ; `Off` = aucun suivi (aucune page n'est ouverte). Résolution pure : `NotificationBadge.Resolve`.
Stockage dans `notifications.json`, sans nouvelle clé de `config.json` : propriété facultative `badgeMode` d'un service défini (absente = `auto`, les
anciens fichiers se relisent à l'identique), et section `badges` pour régler un service livré (`{ "badges": { "gmail": { "badgeMode": "off" } } }`, avec
`selector` et `attribute` facultatifs). Un mode inconnu, un mode `selector` sans sélecteur ou un sélecteur trop long : l'entrée est ignorée avec un avertissement.

**Activité** : la page cachée écoute `FaviconChanged`. La première icône vue est la référence ; une autre icône signale une activité (`FaviconActivity`), le retour
à la référence l'efface. Heuristique, **désactivée par défaut** (révision du 27 octobre 2026) : la première icône vue n'était pas toujours « sans notification » et affichait un badge sur Google Chat avec zéro non lu. Elle ne s'affiche que si l'utilisateur pose `"favicon": true` (section `badges` ou entrée d'un service), en `Auto`, sans nombre lisible ; sinon le changement d'icône ne sert qu'au journal. Un compteur à 0 ou l'absence de nombre n'affichent jamais de badge. Le point est un disque (`Ellipse`), pas un `InfoBadge` vide (qui donnait un ovale) ; le rouge et le chiffre blanc de l'`InfoBadge` viennent de la palette (`Badge` / `OnBadge`, contraste testé). `INotificationWatcher` gagne un événement `ActivityChanged(id, bool)`
déclaré avec une implémentation par défaut sans effet : les implémentations antérieures compilent sans changement, `CountChanged` ne bouge pas. Le connecteur
publie la valeur `notify.<service>.activity` (« 1 » ou vide) en plus de `notify.<service>`.

**Observateur** : pour un service à sélecteur en mode `Auto` ou `Selector`, un script injecté avec `AddScriptToExecuteOnDocumentCreatedAsync` **dans la seule page
cachée** pose un `MutationObserver` (anti-rebond 500 ms) et envoie `{"type":"streamdak.badge","count":N}` par `window.chrome.webview.postMessage`. Le sélecteur y
est une chaîne JSON, jamais du code. `IsWebMessageEnabled` n'est activé que sur ce contrôleur et seulement si le script est demandé ; le cadre visible
garde `false`. `WebMessageReceived` n'accepte que : JSON de 256 caractères au plus, objet à exactement deux clés, `type` égal à `streamdak.badge`, `count` entier de
0 à 999 999, document émetteur (`Source`) de l'hôte du service (`NotificationMessage`). Le reste est ignoré sans trace. Permissions toujours refusées,
`AreHostObjectsAllowed = false`. Quand l'observateur est actif, la relecture à la minute disparaît ; s'il ne peut pas être installé, elle revient.

**Google Chat : libellés d'accessibilité** (révision du 27 octobre 2026) : le titre de la page vaut « Chat », sans `(N)`. Sans sélecteur, le fournisseur `gchat` utilise un observateur qui lit les
`aria-label` / `title` correspondant à `NotificationProvider.DefaultLabelPattern` (nombre associé à « non lu(s) » / « unread », FR et EN) : libellé de 120 caractères au plus, nombre de 1 à 6
chiffres, 200 candidats au plus, parent/enfant dédupliqués (le plus interne compte), somme bornée. Réglable par `labelPattern` (validé : 200 caractères, un groupe de capture, délai d'évaluation borné), `selector`
ou `favicon` dans `badges`. Le message gagne une clé facultative `candidates` (entier de 0 à 100 000, journal seulement) ; tout autre ajout reste refusé. Le journal consigne mode, candidats et nombre retenu, sans texte de page,
et avertit une fois quand l'icône change alors qu'aucun candidat n'existe. Limite : le DOM réel de Google Chat n'a pas pu être vérifié (tests sur une page locale).

**Pas de `window.Notification`, pas de `NotificationReceived`** : surcharger l'objet de la page est fragile (les sites le gardent ou le contournent) et pousserait à
autoriser les notifications système dans la page cachée, ce que l'ADR 0020 refuse. Le titre, le DOM et l'icône suffisent pour un badge.

**Robustesse** : page non initialisée, déchargée ou plantée : la dernière valeur connue est conservée (ou rien si aucune n'a été lue), sans exception. Les mises à jour
passent par le thread d'interface ; tous les événements du contrôleur sont désinscrits à la fermeture ou à la réouverture.

**Réglages par page** (`DeckPage`, `config.json`) : `BadgeEnabled` (absent = vrai) et `BadgeExcludeFromTotal` (absent = faux), écrits seulement quand ils
s'écartent du défaut ; clés ajoutées avec accord de l'utilisateur (relecture des anciens fichiers testée).

## Conséquences

- Aucune clé renommée ni supprimée ; un `notifications.json` antérieur donne les mêmes badges.
- Le point d'activité est une heuristique : une icône qui change sans rapport avec un message peut le déclencher ; `Title` ou `Off` la désactive.
- Un site hostile pourrait envoyer un faux nombre par `postMessage` depuis son propre domaine : seul le badge de ce service en pâtit, et le message ne porte
  qu'un entier borné.
