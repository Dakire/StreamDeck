# Matrice de parité fonctionnelle

État au 19 octobre 2026. **Fait** = implémenté et couvert par un test automatisé (sauf mention).
Les tests cités sont dans `tests/TouchDeck.Tests`.

## P0 — Version opérationnelle

| Fonction | Référence Stream Deck | TouchDeck | Écart / suite | Validation |
|---|---|---|---|---|
| Grille configurable | Fixée par l'appareil | **Fait** : par profil (3×2 … 8×4), réglage global ou automatique selon l'écran ; touches aux proportions bornées, grille centrée | — | `MainDeckWindowRendersProfileTilesAndImages` (1080p, 16:9, portrait) |
| Interface tactile | Matérielle | **Fait** : fenêtre sans focus (`WS_EX_NOACTIVATE`), cibles ≥ 48 px, appui instantané | Vérifier à la main avec un vrai écran tactile et le Bloc-notes | Manuel |
| Modes configuration / contrôle | Logiciel à part | **Fait** : éditeur séparé à trois panneaux + mode « Modifier au toucher » sur le deck | — | `UiTests` (éditeur) |
| Profils | Oui | **Fait** : créer, renommer, dupliquer, supprimer, actif | Réordonner les profils : non (aucune commande dans l'éditeur) | `DeckDocumentTests` |
| Personnalisation | Titre, image, couleur | **Fait** : titre et position, icône Fluent / emoji / texte, image, couleurs | — | `UiTests`, `PackageAndThemeTests` |
| Actions de base | Raccourci, texte, ouvrir, site, multimédia | **Fait** : `hotkey`, `text`, `launch`, `url`, `script`, `media`, `focus` | — | `ActionEngineTests` |
| Plein écran sur l'écran secondaire | Sans objet | **Fait** : zone de travail de l'écran choisi, mémorisé par identifiant (`MonitorDevice`), retour sur l'écran principal au débranchement, retour automatique au rebranchement | Test réel multi-écran à faire à la main | `DisplayTests` |
| Persistance fiable | Oui | **Fait** : écriture atomique, sauvegardes (15), fichier invalide conservé, rechargement à chaud | — | `ConfigStoreTests` |

## P1 — Utilisation avancée

| Fonction | TouchDeck | Écart / suite | Validation |
|---|---|---|---|
| Dossiers et pages | **Fait** : dossiers imbriqués (8 niveaux), pages masquées, sous-pages automatiques | — | `DeckDocumentTests` |
| Macros / séquences | **Fait** : étapes ordonnées, pause, arrêt à la première erreur (étape signalée), délai maximal par étape, annulation | Répétitions et conditions : non | `ActionEngineTests` |
| Retour d'exécution | **Fait** : anneau « en cours » après 250 ms, coche, alerte + message | — | `DeckTilesShowRunState…` |
| Contrôle audio | **Fait** : touches média, micro (couper / rétablir / bascule), volume précis (Core Audio) | Choix du périphérique : non | `ActionEngineTests` (simulé) ; manuel sur Windows |
| Contrôle multimédia | **Fait** (touches média) | — | `ActionEngineTests` |
| Requêtes HTTP / webhooks | **Fait** : GET ou POST (JSON / texte), 10 s maximum, erreur HTTP signalée | En-têtes personnalisés : non | `ActionEngineTests` (serveur factice) |
| Infos dynamiques | **Fait** : `{time}`, `{date}`, formats .NET, minuteur avec progression | État de services, indicateurs système, API : non | `LabelTemplateTests` |
| Profil automatique par application | **Fait** (nom du programme au premier plan, relevé toutes les 0,8 s) | — | Manuel |
| Raccourcis globaux (déclencher une touche depuis le clavier) | Non | `RegisterHotKey` derrière une interface `IHotkeyService` | — |
| Démarrage automatique | **Fait** (clé Run de l'utilisateur) | — | Manuel |
| Import / export | **Fait** : `.touchdeck` (profil ou configuration complète), contrôles de sécurité | Import de profils Elgato : non (format non documenté) | `PackageTests` |

## P2 — Extensibilité

| Fonction | TouchDeck | Approche envisagée |
|---|---|---|
| Plugins | Non | Étape 1 : actions « script » paramétrées, décrites par un manifeste JSON local (aucun code téléchargé). Étape 2 : processus séparés pilotés en JSON sur l'entrée/sortie standard, avec `IActionHandler` côté moteur. |
| États multiples (bascule) | Non | Clé facultative `States` sur le bouton (changement de format à valider). |
| Linux | Préparé (voir `ARCHITECTURE.md`) | Front Avalonia sur `Core/`, adaptateurs X11 / Wayland. |
| Mode simulation | **Fait** : `--simulate` | — |

## Défauts corrigés pendant l'audit

| Constat | Cause | Correction |
|---|---|---|
| Bouton « Avec image » à bandes horizontales | L'image **générée par le test** contenait des bandes (vert = `i % 255` sur le tableau d'octets) ; le rendu était correct | Vraie illustration de test |
| Couleurs des photos avec profil ICC faussées | `BitmapCreateOptions.IgnoreColorProfile` au chargement | Option retirée |
| Images réduites crénelées | Filtrage par défaut de l'`ImageBrush` | `BitmapScalingMode.HighQuality` |
| Écran du deck perdu au rebranchement | Mémorisé par index, que Windows renumérote | `MonitorDevice` + repli sur l'index |
| Moteur d'actions non testable, sans délai maximal ni annulation | Appels Win32 statiques dans un `switch` | `ActionEngine` + interfaces de plateforme |
