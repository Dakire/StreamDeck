# Journal des modifications

Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les dates sont au format AAAA-MM-JJ.

## [Non publié]

### Ajouté (phase 9, plugins et API locale)
- **Plugins** : intégrations posées dans `plugins<Nom><Nom>.dll`, chargées dans leur propre contexte (`docs/plugins.md`, exemple `samples/HelloPlugin`, ADR 0011 et 0012).
- **API locale** par canal nommé réservé à l’utilisateur (`ping`, `status`, `values`, `profile`, `page`, `run`, `show`, `edit`, `settings`) : `docs/local-api.md`.
- **Instance unique** : relancer TouchDeck affiche le deck existant (ou l’éditeur avec `--edit`) au lieu d’ouvrir une seconde fenêtre.

### Ajouté (phase 8, connecteurs HTTP)
- **vMix**, **lampes Elgato** et **Philips Hue** (association par le bouton du pont, clé chiffrée DPAPI) ; socle commun
  `PolledHttpIntegration` (interrogation, perte après 3 échecs) et `IPairableIntegration` (bouton « Associer » des paramètres).
- Documentation : `docs/integrations/{vmix,elgato,hue}.md`.
- **Système Windows** : `{sys.cpu}`, `{sys.ram}`, média en cours (`{sys.media.title}`, `{sys.media.artist}`, `{sys.media.playing}`) et commandes lecture / suivant / précédent par SMTC, sans compte (`docs/integrations/windows.md`). Les projets de l’application et des tests passent à `net9.0-windows10.0.19041.0`.
- **X-Plane 12** par son API web locale : datarefs affichés dans les titres (`{xplane.gear}`), écriture d’un dataref et activation d’une commande (`docs/integrations/xplane.md`).
- **Microsoft Teams** par l’API locale « application tierce » (association, micro, caméra, main, flou, enregistrement, réactions, état de la réunion) : `docs/integrations/teams.md`.
- **Applications cibles** : 35 boutons prêts à l’emploi (Zoom, Teams, Discord, Premiere, Photoshop, DaVinci, Blender, Outlook, Word, Excel, Star Citizen) qui mettent l’application au premier plan puis envoient son raccourci (`docs/integrations/raccourcis-applications.md`).

### Ajouté (phase 3)
- **Thème AMOLED** (`Theme` = `"amoled"` : fond noir pur), choisi dans les paramètres et le menu du deck. Valeur ajoutée à
  une clé existante : un ancien fichier reste lisible, une valeur inconnue retombe sur « system ».
- **Jetons de design** (`Themes/Tokens.xaml`) : rayons 8 / 12 / 16 / 20, espacements sur une grille de 4, échelle
  typographique (titre 20, section 14, corps 13, légende 11), styles de texte. Les couleurs restent dans `Theme.cs`
  (palettes dont les contrastes sont testés, publiées à chaud) plutôt que dans un `Colors.xaml` statique.

### Modifié (phase 3, début)
- **Palette sombre** du cahier des charges : application `#0B0E17`, panneaux `#121624`, cartes `#1A1F30`, accent `#6C5CE7`
  (clair et sombre). Contrastes WCAG AA revérifiés par les tests ; fond des touches, dock, fond du deck et champs alignés
  sur la nouvelle palette. L'accent texte `#B0A8FF` est conservé : `#7C6CFF` n'atteint que 3,9:1 sur les cartes.

### Architecture (phase 2)
- **Trois projets.** `TouchDeck.Core` (`net9.0`, sans WPF ni Windows), `TouchDeck.Platform.Windows` (Win32 interne, sans
  WPF) et l'application WPF. Les frontières sont vérifiées par `ArchitectureTests`.
- **Injection de dépendances.** Le localisateur statique `AppServices` est supprimé. `AppHost.Build` construit le
  conteneur ; ViewModels et fenêtres reçoivent leurs dépendances par constructeur. Nouveaux services :
  `ConfigPersistence`, `AppearanceService`, `IAppWindows`, `DisplayService` injecté.
- **Journal Serilog.** Façade `Log` du Core branchée sur Serilog ; `ILogger<T>` pour les services injectés. Le fichier
  `touchdeck.log` change de rotation : 1 Mo par fichier, 3 fichiers (`touchdeck_001.log`…) au lieu de `.old`.
- **Gestionnaires d'action.** Une classe par type (`Core/Actions/Handlers/`), enregistrés dans un `ActionRegistry` qui refuse
  les doublons : une intégration s'ajoute sans modifier le Core.
- **Démarrage.** Environ 55 ms de plus qu'en fin de phase 1, mais moins que la version initiale (≈ 670 ms contre ≈ 700 ms).
  Le journal indique le temps de chaque étape.
- **Documentation.** `docs/ARCHITECTURE.md` réécrit, ADR 0007 à 0010, `docs/phase2-report.md`.

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
