# Twitch

Connecteur `TouchDeck.Integrations.Twitch`, par l'API Helix. TouchDeck n'embarque **aucune application Twitch** : tu crées la tienne,
ce qui évite toute dépendance à un secret partagé et à une procédure d'approbation.

## Préparation

1. Console développeur Twitch (dev.twitch.tv/console) › Enregistrer une application ; copie son **identifiant client** (public).
2. Génère un **jeton utilisateur** pour ton compte avec les droits utiles :
   `clips:edit` (clip), `channel:manage:broadcast` (marqueur, titre), `channel:edit:commercial` (publicité), `user:write:chat` (chat).
3. Dans TouchDeck : **Paramètres › Intégrations › Twitch**, renseigne l'identifiant client et le jeton, puis **Tester la connexion**.

Le jeton est chiffré (DPAPI) ; il n'est ni dans `config.json` ni dans le journal. Il expire : en cas de refus (« Twitch refuse le jeton »), génère-en un nouveau.

## Actions

| Commande | Argument | Endpoint Helix |
|---|---|---|
| `clip` | | `POST /clips` (le lien d'édition est affiché) |
| `marker` | description (facultatif, 140 caractères) | `POST /streams/markers` |
| `ad` | 30, 60, 90, 120, 150 ou 180 s (30 par défaut) | `POST /channels/commercial` |
| `title` | nouveau titre | `PATCH /channels` |
| `chat` | message | `POST /chat/messages` (un message écarté par Twitch est signalé) |

## Limites

- Pas de flux EventSub (abonnés, raids) ni de valeur d'état en direct : l'intégration est en commandes seulement.
- Pas d'assistant OAuth intégré : le jeton se fournit à la main. Un flux OAuth (PKCE, boucle locale) pourra l'ajouter sans changer les commandes.
- Les limites de débit Helix sont à vérifier dans la documentation Twitch ; un `429` affiche le délai à attendre.
