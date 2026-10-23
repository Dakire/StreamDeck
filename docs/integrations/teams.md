# Microsoft Teams

Connecteur `StreamDak.Integrations.Teams`, par l'API locale « application tierce » de la nouvelle version de Teams
(WebSocket `ws://127.0.0.1:8124`, protocole 2.0.0). Étiquetée **à vérifier** : Microsoft fait évoluer cette API ; le protocole
est joué ici contre un faux serveur (`TeamsTests`), pas contre un vrai Teams.

## Association

1. Dans Teams : **Paramètres › Confidentialité › Gérer l'API › Activer l'API**.
2. Dans StreamDak : **Paramètres › Intégrations › Microsoft Teams › Associer à Teams**, puis accepter la demande dans Teams (30 s).
3. Le jeton reçu est chiffré pour ton compte Windows (`secrets.dat`) ; il n'est ni dans `config.json` ni dans le journal.

## Actions (pendant une réunion)

| Commande | Argument | Voyant |
|---|---|---|
| `mute` | | Plein quand le micro est coupé |
| `camera` | | Plein quand la caméra est allumée |
| `hand` | | Plein quand la main est levée |
| `blur` | | Flou d'arrière-plan actif |
| `record` | | Enregistrement en cours |
| `react` | `applause`, `laugh`, `like`, `love`, `wow` | |
| `leave` | | |

Valeurs : `{teams.inMeeting}`, `{teams.muted}`, `{teams.camera}`, `{teams.hand}`, `{teams.blur}`, `{teams.recording}`, `{teams.sharing}`.
Hors réunion, une commande répond « Aucune réunion Teams en cours » au lieu d'échouer en silence.

## Limites

Nouvelle version de Teams seulement. Si l'API n'est pas activée, ou si Teams est fermé, l'intégration réessaie avec une attente croissante.
Sans API (Teams classique, ou refus d'association), les boutons « Applications cibles › Teams » envoient les raccourcis clavier.
