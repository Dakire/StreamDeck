# Lampes Elgato (Key Light, Ring Light)

Connecteur `StreamDak.Integrations.ElgatoLight`, par l'API HTTP locale des lampes (port 9123, `/elgato/lights`). API non officielle.

## Prérequis

Connaître l'adresse IP de la lampe (application Elgato Control Center ou routeur). Plusieurs lampes : adresses séparées par des
virgules ou des espaces ; les commandes agissent sur toutes.

## Actions

| Commande | Argument | Effet |
|---|---|---|
| `toggle` / `on` / `off` | | Bascule, allume, éteint |
| `brightness` | 0 à 100, ou `+10` / `-10` | Règle ou ajuste la luminosité |
| `temperature` | 2900 à 7000 (kelvins) | Règle la température de couleur (convertie en mireds, 143 à 344) |

## Valeurs pour les titres

`{elgato.on}`, `{elgato.brightness}`, `{elgato.temperature}` (état de la première lampe, relevé toutes les 3 s).

## Limites

API non documentée par Elgato, pouvant changer avec un micrologiciel. Pas de découverte automatique (mDNS) pour l'instant.
