# OBS Studio

Connecteur `StreamDak.Integrations.Obs`, par obs-websocket v5 ([protocole](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md)),
intégré à OBS Studio 28 et plus. Aucune dépendance externe : le client est écrit sur `ClientWebSocket`.

## Prérequis

1. Dans OBS : **Outils › Réglages du serveur WebSocket › Activer le serveur WebSocket**.
2. Noter le port (4455 par défaut) et, si l'authentification est activée, le mot de passe.
3. Dans StreamDak : **Paramètres › Intégrations › OBS Studio**, activer, renseigner l'adresse (`localhost` si OBS tourne sur ce PC),
   le port et le mot de passe, puis **Tester la connexion**.

Le mot de passe est chiffré pour ton compte Windows (`secrets.dat`) : il n'est jamais écrit dans `config.json`, ni dans le journal.
StreamDak se reconnecte tout seul (2 s, 4 s… 60 s) quand OBS est fermé puis relancé.

## Actions

Une étape OBS s'écrit `{ "Type": "obs", "Value": "<commande>", "Args": "<argument>" }`. Elles sont toutes dans le catalogue, catégorie « OBS Studio ».

| Commande | Argument | Effet | Voyant de la touche |
|---|---|---|---|
| `scene` | nom de la scène (obligatoire) | Affiche la scène en sortie | Plein si c'est la scène courante |
| `mute` | nom de la source audio (obligatoire) | Coupe ou rétablit le micro | Plein quand elle est coupée |
| `stream` | | Démarre ou arrête le direct | Plein pendant le direct |
| `record` | | Démarre ou arrête l'enregistrement | Plein pendant l'enregistrement |
| `virtualcam` | | Bascule la caméra virtuelle | Plein quand elle est active |
| `source` | nom de la source (obligatoire) | Affiche ou masque une source de la scène courante | |
| `replay` | | Sauvegarde le tampon de relecture | |

## Valeurs pour les titres

Un titre de bouton peut afficher l'état d'OBS : `{obs.scene}`, `{obs.fps}`, `{obs.cpu}`, `{obs.streaming}`, `{obs.recording}`.
Exemple : `Scène : {obs.scene:hors ligne}` affiche « hors ligne » tant qu'OBS n'est pas connecté.
Les états arrivent par les événements d'OBS (aucune interrogation), sauf `fps` et `cpu` relevés toutes les 2 s.

## Limites

- OBS 27 et antérieur : le plugin obs-websocket 5 doit être installé à part.
- Streamlabs Desktop n'utilise pas ce protocole (voir l'étude des intégrations).
- Les niveaux sonores (`InputVolumeMeters`) ne sont volontairement pas écoutés : ils saturent la connexion.

## Tests

`ObsIntegrationTests` joue le protocole contre un faux serveur (`FakeObsServer`) : identification (avec le vecteur de la documentation du
protocole), mot de passe refusé ou manquant, OBS absent, état initial, commandes, événements, statistiques, perte de connexion et
reconnexion par le gestionnaire.