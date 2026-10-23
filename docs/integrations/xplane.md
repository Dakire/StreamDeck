# X-Plane 12

Connecteur `StreamDak.Integrations.XPlane`, par l'API web locale d'X-Plane 12.1.1 et plus (REST, port 8086,
[documentation](https://developer.x-plane.com/article/x-plane-web-api/)). Sans authentification : seul le PC local est visé par défaut.

## Prérequis

X-Plane 12.1.1+ lancé. Le serveur web est actif par défaut (option de lancement `--no_web_server` pour le couper).
Les chemins `v1` (datarefs) et `v2` (commandes) suivent la documentation d'X-Plane ; **à vérifier sur ta version** si une commande est refusée.

## Réglages

- **Datarefs à afficher** : `nom=dataref`, séparés par des virgules. Exemple :
  `gear=sim/cockpit2/controls/gear_handle_down, alt=sim/cockpit2/gauges/indicators/altitude_ft_pilot`.
  Affichés par `{xplane.gear}` et `{xplane.alt}` (relevé toutes les 500 ms). Les noms se trouvent avec DataRefTool.

## Actions

| Commande | Argument | Effet |
|---|---|---|
| `command` | nom de la commande (`sim/lights/landing_lights_toggle`) | Active la commande du simulateur |
| `set` | `dataref=valeur` | Écrit un dataref accessible en écriture |

## Limites

Pas de flux WebSocket pour l'instant : relevé périodique (500 ms). Les datarefs en lecture seule refusent l'écriture.
Pas de tests contre un vrai simulateur : le protocole est joué contre un faux serveur (`XPlaneTests`).
