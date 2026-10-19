# Philips Hue

Connecteur `TouchDeck.Integrations.Hue`, par l'API locale CLIP v2 du pont (HTTPS, en-tête `hue-application-key`).

## Association

1. Dans TouchDeck : **Paramètres › Intégrations › Philips Hue**, saisir l'adresse IP du pont.
2. Appuyer sur le bouton du pont, puis cliquer sur **Associer au pont** dans les 30 secondes.
3. La clé obtenue est chiffrée pour ton compte Windows (`secrets.dat`) ; elle n'apparaît ni dans `config.json` ni dans le journal.

Le certificat du pont est auto-signé : il n'est accepté que pour l'adresse configurée.

## Actions

Les lumières, pièces et scènes se désignent par leur nom (insensible à la casse).

| Commande | Argument | Effet |
|---|---|---|
| `toggle` / `on` / `off` | nom de la lumière ou de la pièce | Bascule, allume, éteint |
| `brightness` | `Nom=valeur` (`Salon=40`, `Salon=+10`) | Règle ou ajuste la luminosité (0 à 100) |
| `scene` | nom de la scène | Active la scène |

## Valeurs pour les titres

`{hue.on.<nom>}`, `{hue.brightness.<nom>}`, relevées toutes les 2 s. Un code 403 invite à refaire l'association.

## Limites

Ponts ronds (v1) non pris en charge. Pas de flux d'événements SSE : interrogation toutes les 2 s.
