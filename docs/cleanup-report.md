# Bilan de la phase 1 : nettoyage, optimisation, documentation

Branche `chore/cleanup`, 19 octobre 2026. Principe suivi : aucun changement de comportement visible. Chaque étape est un
commit réversible, compilé sans avertissement et testé au vert.

## En bref

- **Défaut grave du harnais de tests.** Les tests d'interface tournaient sur la **vraie plateforme Windows** et ouvraient
  la **vraie fenêtre du deck**, contrairement à ce qu'annonçaient la documentation et les commentaires. Ils sont
  désormais réellement simulés, et un test le garantit.
- **Grille du deck.** Une modification de configuration ne reconstruit plus la grille : 135 ms → 12 ms pour 32 touches.
- **Démarrage et mémoire.** Le démarrage gagne environ 15 % (700 → 595 ms) et la mémoire de travail baisse de 10 Mo.
- **Qualité à la compilation.** Analyseurs .NET, style vérifié, avertissements bloquants, documentation XML obligatoire :
  650 membres documentés.
- **Tests.** 158 → 183. Ajouts : caractérisation de la navigation, robustesse, garde-fous du harnais et des ressources de
  thème, mesure de performance facultative.

## Mesures avant / après

### Application réelle

Mode `--simulate`, dossier de configuration jetable, fenêtre de 1100 × 650, 32 touches dont une horloge. Médianes de
3 lancements par série, les deux versions mesurées en alternance (script `measure.ps1`, hors dépôt).

| Mesure | Avant (`main`) | Après | Écart |
|---|---|---|---|
| Démarrage (lancement → fenêtre prête), série 1 | 701 ms | 598 ms | −15 % |
| Démarrage, série 2 | 698 ms | 594 ms | −15 % |
| Mémoire de travail | 172 Mo | 162 Mo | −10 Mo |
| Mémoire privée | 139,5 Mo | 129 Mo | −10,5 Mo |
| CPU au repos (10 s, horloge visible) | 0 à 31 ms | 16 à 31 ms | inchangé (bruit de mesure) |

Le gain vient très probablement de la réconciliation de la grille : au démarrage, plusieurs redimensionnements
successifs reconstruisaient les 32 touches à chaque fois. L'hypothèse n'a pas été mesurée isolément.

### Rendu hors écran

`BenchmarkTests`, `STREAMDAK_BENCH`, Release. Le rendu logiciel (`RenderTargetBitmap`) amplifie le coût des effets ; il
sert à comparer, pas à prédire les images par seconde réelles.

| Mesure | Avant (début de session) | Après `eb6f1e2` (même période) |
|---|---|---|
| Reconstruction de la grille après une modification | 132 à 149 ms | 12 ms |
| Rendu complet 1920 × 1080, touches animées (avec ombres) | 119 ms | 118 ms |
| Rendu complet, style statique (sans ombre de touche) | 45 à 47 ms | 48 ms |

Plus tard dans la session, la machine était plus chargée : le rendu animé mesurait environ 138 ms. Mesuré dans la même
minute, `eb6f1e2` et l'état final donnent le même rendu (140 et 138 ms) : aucune régression. N'ont donc été comparées
que des mesures prises dans les mêmes conditions.

### Compilation et tests

| | Avant | Après |
|---|---|---|
| Avertissements, application | 0 (aucun analyseur actif) | 0 avec `latest-recommended`, style et CS1591 bloquants |
| Avertissements, tests | 2 (xUnit1031) | 0 |
| Tests | 158, au vert | 183, au vert, 3 exécutions consécutives |
| Membres publics sans documentation XML | 650 | 0 (la compilation échoue sinon) |

## Corrigé

| Constat | Cause | Correction | Commit |
|---|---|---|---|
| Tests d'interface sur la vraie plateforme Windows, vraie fenêtre du deck ouverte pendant les tests | Le constructeur d'`Application` programme `OnStartup` au premier tour du Dispatcher, même sans `Run()`. `OnStartup` rappelait `AppServices.Init(simulate: false)` et `StartupUri` ouvrait `MainWindow`. | `App.Headless` (positionné par le harnais) saute le démarrage normal. La fenêtre principale est créée dans `OnStartup` au lieu de `StartupUri`, à l'identique en production. Le test `TheHarnessNeverUsesTheRealPlatformNorShowsWindows` le vérifie. | `bd769c9` |
| Échec intermittent de `BackupsAreCreatedOnlyOnChangeAndPruned` | Les collections « ui » et « config » tournaient en parallèle alors que `ConfigStore.Dir` est global : des sauvegardes de l'interface atterrissaient dans le dossier du test. | Tests en série (durée inchangée : 14 s). | `6232f6d` |
| 13 exceptions avalées (`catch {}`) | Échecs « au mieux » sans trace : surveillance de `config.json`, sauvegardes, images, registre… | Exceptions attendues filtrées par type et consignées. La saisie de couleur invalide reste une validation silencieuse. Tests de robustesse ajoutés. | `ceb54f1` |
| Échecs Win32 invisibles | Valeurs de retour ignorées. | `WS_EX_NOACTIVATE` vérifié après application et journalisé s'il manque. `SendInput` partiel journalisé (application cible élevée). `GetWindowText` sans `StringBuilder`. | `b1a6420` |
| `FEATURE_PARITY.md` annonçait « réordonner les profils » | `DeckDocument.MoveProfile` n'avait aucun appelant : aucune commande dans l'éditeur. | Méthode retirée, matrice corrigée. | `cb1ee3c` |

## Nettoyé

- **Configuration de compilation.**
  - `Directory.Build.props` : `AnalysisLevel` `latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`,
    `GenerateDocumentationFile`.
  - `.editorconfig` : style du dépôt, nommage (préfixe `I`, PascalCase), exceptions motivées.
- **Avertissements des analyseurs corrigés.**
  - Cultures explicites dans les formatages.
  - `using` inutiles.
  - Valeurs par défaut explicites dans `Models.cs`.
  - Noms de paramètres alignés sur les interfaces.
  - Tableaux constants extraits en champs.
  - Types de retour précis.
  - Test asynchrone au lieu d'une attente bloquante.
- **Chaînes magiques.**
  - `ActionTypes`, `PageTargets` et `AudioCommands` (`Core/ActionTypes.cs`) remplacent plus de 180 littéraux dans le
    moteur, le validateur, le catalogue, la configuration par défaut et les ViewModels.
  - Les valeurs (clés de `config.json`) sont inchangées et figées par `FormatKeysTests`.
- **Couleurs en dur.**
  - Les 20 couleurs du XAML deviennent des ressources `Deck*` publiées par `Theme`, avec des valeurs identiques
    (`Theme.OnTile`).
  - Seules restent les couleurs noires des ombres (paramètres d'effet).
  - Rendu comparé pixel par pixel sur les 15 captures du harnais : identique. Seuls diffèrent un horodatage et une
    animation de WPF-UI capturée à un autre instant.
- **Code mort.** `MoveProfile`, `Place.WithProfile` et `Place.WithPage`, `TileFaceData.WithLabel` et sa copie,
  `EditorViewModel.NeighborIndex`, un champ et une variable de test. Aucun paquet NuGet, aucune ressource XAML et aucune
  image inutilisés.
- **Structure.** `Log` déplacé dans `Core/` (aucune dépendance WPF) pour que `ConfigStore` puisse journaliser.
- **Fichiers temporaires.** `*_wpftmp.csproj` ignorés par git : une compilation interrompue laissait ce fichier à la
  racine, ce qui cassait `dotnet build` sans argument.

## Mesuré, mais volontairement pas changé

| Sujet | Mesure | Pourquoi rien n'a changé |
|---|---|---|
| Jusqu'à 3 `DropShadowEffect` par touche (`App.xaml`, `TileFace.xaml`) | Rendu logiciel : 119 ms avec ombres, 46 ms sans. | Les remplacer (ombre pré-rendue, bordure en dégradé) modifie l'apparence, ce que la phase 1 exclut. À trancher en phase 3 ou 5. Seule l'ombre du titre sur image est désormais partagée et figée au lieu d'être recréée à chaque dessin. |
| Interrogation de la fenêtre active toutes les 0,8 s | Ne tourne que si un profil est associé à une application. Coût d'un appel `Process.GetProcessById` par relevé. | Un hook `SetWinEventHook` n'apporterait pas de gain mesurable et ajouterait un risque. |
| `UseWindowsForms` (pour `Screen` et `Cursor` seulement) | | Remplaçable par `EnumDisplayMonitors`, mais la zone (placement multi-écran) est sensible et le gain faible. |
| Découpage d'`EditorViewModel` (environ 1 000 lignes) et d'`EditorWindow.xaml.cs` | | Remaniement structurel, prévu en phase 2 avec l'injection de dépendances. |

## Écarts au cahier des charges, et pourquoi

- **`ImplicitUsings`** reste désactivé : c'est une règle d'`AGENTS.md`. `Nullable` était déjà activé.
- **Noms de fichiers.** Le cahier des charges demande `docs/architecture.md` : le fichier existant `docs/ARCHITECTURE.md`
  a été complété (diagrammes Mermaid) plutôt que dupliqué. Les noms ne diffèrent que par la casse, que Windows ignore.
- **Journal.** Pas de Serilog : le journal maison suffit pour l'instant (ADR 0006, à réexaminer en phase 2).
- **Tâches de fond.** Rien n'a été déplacé vers des tâches de fond. Les lectures de fichiers à l'ouverture (config,
  images en cache) sont rapides et mesurées dans le démarrage. Le lancement de programmes, les requêtes web et les pauses
  étaient déjà hors du thread d'interface et annulables.
- **Optimisations.** Seules ont été faites celles dont la mesure le justifiait : la réconciliation de la grille.
- **Captures du README.** Elles viennent du harnais de tests (rendu hors écran) et non du bureau, conformément à
  `CLAUDE.md`.

## Risques restants

- **Vérification manuelle à faire** sur un vrai écran tactile : fenêtre sans focus (raccourci vers le Bloc-notes),
  plein écran multi-écran, débranchement et rebranchement. Les tests ne couvrent pas le comportement Win32 réel.
- **Démarrage modifié.** La fenêtre principale est créée par code au lieu de `StartupUri`. La version finale a été
  lancée 9 fois pour les mesures, avec une fenêtre prête à chaque fois. L'édition, l'annulation et le changement de thème
  n'ont été exercés que dans le harnais : l'interface réelle n'a pas été pilotée, conformément à `CLAUDE.md`.
- **Exceptions filtrées par type.** Une exception d'un type imprévu, auparavant avalée, remonte désormais au
  gestionnaire global : elle est consignée et affichée, l'application continue. Les types retenus couvrent les cas
  documentés.
- **Dossiers trop profonds.** Corrigé en phase 2 : le contenu est remonté dans le dernier dossier autorisé au lieu
  d'être ignoré.
- **Interrupteur « Afficher le titre ».** Dans la capture de l'éditeur, il paraît décoché alors que le titre s'affiche.
  C'est probablement un artefact du rendu hors écran (l'animation de WPF-UI n'avance pas sans rendu réel). À confirmer à
  l'écran.

## Comment vérifier

```powershell
dotnet build -c Release                       # 0 avertissement, 0 erreur
dotnet test tests/StreamDak.Tests             # 183 tests au vert
$env:STREAMDAK_DATA_DIR = "$env:TEMP\streamdak-essai"
dotnet run -c Release -- --simulate --edit    # deck et éditeur : modifier un titre, annuler, changer de thème
```

Points à observer :
- le deck et l'éditeur ont exactement le même aspect qu'avant ;
- taper un titre dans l'éditeur met à jour la seule touche concernée, sans scintillement ;
- le journal (`streamdak.log`) ne contient aucune erreur au démarrage.

## Commits

| Commit | Étape |
|---|---|
| `15cd0c9` | docs : étude des intégrations et choix retenu |
| `bd769c9` | test : harnais réellement simulé, tests de caractérisation de la navigation |
| `eb6f1e2` | perf : la grille du deck n'est plus reconstruite à chaque modification |
| `b1a6420` | build : analyseurs, style vérifié à la compilation, avertissements bloquants |
| `ceb54f1` | fix : erreurs attendues filtrées et consignées |
| `6232f6d` | test : collections exécutées en série |
| `304c46f` | refactor : constantes `ActionTypes`, `PageTargets`, `AudioCommands` |
| `4fd6359` | refactor(theme) : couleurs du XAML en ressources de thème |
| `6b493b0` | docs : commentaires XML sur toutes les API publiques |
| `cb1ee3c` | chore : code mort retiré |
| (ce commit) | docs : README, configuration, architecture, ADR, CONTRIBUTING, CHANGELOG, ce bilan |
