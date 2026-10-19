# Journal des modifications

Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les dates sont au format AAAA-MM-JJ.

## [Non publié]

### Corrigé (phase 2)
- **Dossiers trop profonds.** Au-delà de 8 niveaux, le contenu n'est plus ignoré au chargement : il est remonté dans le
  dernier dossier autorisé, avec un avertissement. Aucun bouton n'est perdu.

### Corrigé
- **Tests réellement simulés.** Les tests d'interface tournaient sur la vraie plateforme Windows et ouvraient la vraie
  fenêtre du deck. En cause : le constructeur d'`Application` lance `OnStartup` dès le premier tour du Dispatcher. Le
  harnais utilise désormais `App.Headless`, et un test le garantit.
- **Test instable.** Un test de sauvegarde échouait par intermittence. Les collections de tests s'exécutent maintenant
  en série, car le dossier de configuration est global au processus.
- **Erreurs consignées.** Les erreurs attendues ne sont plus avalées en silence : copie de migration, sauvegardes,
  images illisibles, lancement automatique, thème de Windows, surveillance de `config.json`, rechargement refusé.
- **Diagnostic Win32.** Le journal signale désormais un style `WS_EX_NOACTIVATE` non appliqué ou une saisie refusée par
  une application élevée (`SendInput` partiel).

### Performances
- **Grille du deck.** Une modification de configuration ne reconstruit plus toute la grille, seulement les cases
  concernées : 135 ms → 12 ms pour 32 touches (mesure hors écran).
- **Démarrage et mémoire.** Démarrage d'environ 700 ms à environ 595 ms ; mémoire de travail de 172 Mo à 162 Mo
  (mesures en mode simulation, 32 touches).

### Modifié
- **Qualité à la compilation.** Analyseurs .NET (`latest-recommended`), style vérifié à la compilation (`.editorconfig`),
  avertissements bloquants, documentation XML obligatoire sur toute API publique.
- **Constantes.** Les identifiants d'action, de page et de commande audio sont des constantes (`Core/ActionTypes.cs`).
  Leurs valeurs dans `config.json` ne changent pas.
- **Couleurs.** Les couleurs écrites en dur dans le XAML sont devenues des ressources de thème `Deck*`. Le rendu est
  identique au pixel près.
- **Journal.** `Log` est déplacé de `Services/` vers `Core/`.
- **Démarrage.** La fenêtre principale est créée par `App.OnStartup` au lieu de `StartupUri`.

### Supprimé
- Code sans appelant : `DeckDocument.MoveProfile`, `Place.WithProfile` et `Place.WithPage`, `TileFaceData.WithLabel`,
  `EditorViewModel.NeighborIndex`.

### Documentation
- `docs/configuration.md` : format, cycle de vie et migrations, avec diagrammes.
- Diagrammes Mermaid dans `docs/ARCHITECTURE.md`.
- Décisions d'architecture dans `docs/adr/`.
- `CONTRIBUTING.md` et ce journal.
- `docs/integrations-research.md` : étude des intégrations.
- `docs/cleanup-report.md` : bilan du nettoyage.

## 2026-10-19 — refonte Fluent et éditeur façon Stream Deck

Historique antérieur au journal, résumé d'après les commits.

- **Migration Fluent.** WPF-UI, Mica, thème clair et sombre. Interface principale et éditeurs en XAML + MVVM
  (CommunityToolkit.Mvvm), icônes Fluent.
- **Éditeur façon Stream Deck.** Éditeur à trois panneaux, profils, dossiers, annuler / rétablir, presse-papiers interne.
- **Moteur d'actions.** Découplé de Windows : délai maximal par étape, annulation, retour visuel sur les tuiles,
  mode `--simulate`.
- **Écran du deck.** Mémorisé par son identifiant (`MonitorDevice`), avec des écrans simulables dans les tests.
- **Refonte visuelle du deck.** Touches teintées à pastille, variantes, dock unifié, fond nuancé.
- **Documentation.** Architecture, développement, parité avec Stream Deck, rétro-ingénierie fonctionnelle.
