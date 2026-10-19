# vMix

Connecteur `TouchDeck.Integrations.Vmix`, par l'API HTTP de vMix (`/api/`), sans dépendance externe.

## Prérequis

1. Dans vMix : **Paramètres › Web Controller**, activer le contrôleur Web (port 8088 par défaut).
2. Dans TouchDeck : **Paramètres › Intégrations › vMix**, activer, renseigner l'adresse et le port, puis **Tester la connexion**.

L'état est relevé toutes les secondes (XML de `/api/`). Après 3 échecs de suite, la connexion est déclarée perdue et rétablie automatiquement.

## Actions

| Commande | Argument | Effet |
|---|---|---|
| `cut` | | Coupe vers l'entrée en prévisualisation |
| `fade` | | Fondu vers l'entrée en prévisualisation |
| `preview` | numéro ou titre de l'entrée | Met l'entrée en prévisualisation |
| `active` | numéro ou titre de l'entrée | Envoie l'entrée directement à l'antenne |
| `mute` | numéro ou titre de l'entrée | Coupe ou rétablit l'audio de l'entrée |
| `record` | | Démarre ou arrête l'enregistrement |
| `stream` | | Démarre ou arrête le direct |
| `function` | `Fonction&Paramètre=valeur` | Toute fonction de l'API vMix |

## Valeurs pour les titres

`{vmix.active}`, `{vmix.preview}`, `{vmix.active.title}`, `{vmix.recording}`, `{vmix.streaming}`, `{vmix.mute.<n>}`.

## Limites

Licence vMix requise. Le protocole TCP (tally) n'est pas utilisé : l'interrogation HTTP suffit à 1 s.
