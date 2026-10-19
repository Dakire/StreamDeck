# Développement

## Prérequis

- Windows 10 ou 11.
- [SDK .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0). Pour exécuter seulement : runtime **Desktop** .NET 9.
  Sans installation système, le script officiel installe le SDK pour l'utilisateur courant :
  ```powershell
  & ([scriptblock]::Create((iwr https://dot.net/v1/dotnet-install.ps1))) -Channel 9.0 -InstallDir "$env:USERPROFILE\.dotnet" -NoPath
  $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
  ```

## Commandes

```powershell
dotnet build -c Release                       # doit rester sans avertissement
dotnet test tests/TouchDeck.Tests             # tous les tests (logique + rendu hors écran)
dotnet run -c Release -- --simulate           # deck sans effet sur le système
dotnet run -c Release -- --edit               # ouvre aussi l'éditeur
dotnet publish -c Release -r win-x64 --self-contained false
```

| Variable / option | Effet |
|---|---|
| `TOUCHDECK_DATA_DIR` | Dossier de configuration à utiliser (essais) au lieu de `%APPDATA%\TouchDeck`. |
| `TOUCHDECK_SHOTS` | Dossier où les tests UI gardent leurs captures PNG (deck sombre / clair, 1080p, portrait, éditeur…). Utile pour comparer un rendu avant / après au pixel près. |
| `TOUCHDECK_BENCH` | Active `BenchmarkTests` (désactivé sinon) : création du deck, rendu d'une grille de 32 touches, reconstruction, mémoire, CPU au repos. Résultats ajoutés à `bench.md` dans ce dossier. À lancer en `-c Release`. |
| `--simulate` | Plateforme simulée : chaque action est affichée (« Simulation : hotkey ctrl+c ») au lieu d'être exécutée. Un badge « Simulation » s'affiche. |

## Tests

- `ActionEngineTests` : séquences, erreurs, délai maximal, annulation, HTTP (serveur factice), audio, minuteur.
- `DisplayTests` : écran mémorisé, renumérotation, débranchement (écrans simulés par `FakeDisplays`).
- `UiTests` : vraies fenêtres rendues hors écran par `UiHarness`. Aucune fenêtre n'est affichée, la configuration est temporaire et la plateforme simulée (`App.Headless` empêche le démarrage normal ; `TheHarnessNeverUsesTheRealPlatformNorShowsWindows` le vérifie). Les erreurs de liaison de données font échouer les tests.
- `DeckNavigationTests` : caractérisation du deck (onglets, pages masquées, `back` / `home`, dossiers, sous-pages, profils, profil par application, exécution), réconciliation de la grille, ressources de thème présentes dans les deux thèmes.
- `PackageAndThemeTests` : import / export, contrastes WCAG (y compris titre sur corps teinté des touches).
- `RobustnessTests` : saisies et fichiers invalides consignés sans exception ; valeurs figées des clés du format (`ActionTypes`).
- `BenchmarkTests` : mesures de performance, seulement si `TOUCHDECK_BENCH` est défini.

Les collections de tests s'exécutent en série : `ConfigStore.Dir` est global au processus.

Ne jamais piloter le vrai bureau (captures, clics simulés) : utiliser le harnais.

## Vérifications manuelles (matériel requis)

1. **Fenêtre sans focus** : ouvrir le Bloc-notes, toucher une touche « Coller » (ou « Saisie de texte ») du deck. Le texte doit arriver dans le Bloc-notes.
2. **Écran secondaire** : choisir l'écran tactile dans Paramètres. Débrancher le câble : le deck revient en fenêtre sur l'écran principal. Rebrancher : il repasse en plein écran sur l'écran tactile, même si Windows l'a renuméroté.
3. **Tactile** : le HDMI ne transporte que l'image ; le toucher passe généralement par un câble USB. Vérifier dans Paramètres Windows › Affichage › « Étalonner l'écran pour la saisie tactile » que le toucher est associé au bon écran.
4. **Audio** : « Micro » doit couper le micro de communication par défaut (vérifier dans Paramètres › Son).

## Ajouter un type d'action

1. Ajouter une classe dans `Core/Actions/Handlers/` (voir `HotkeyAction`) qui dérive de `SyncActionHandler` (instantané),
   `BackgroundActionHandler` (peut bloquer : hors du thread d'interface, avec délai maximal) ou `ActionHandler` (asynchrone),
   puis l'ajouter à `BuiltInActions.All`. Les appels système passent par une interface de `Platform.cs`.
2. Déclarer le type dans `ActionValidator.KnownTypes` et sa validation (le test `EveryKnownTypeHasAHandler` vérifie l'accord).
3. L'exposer dans l'éditeur (`ActionStepViewModel.Types`, libellés) et, si utile, dans `ActionCatalog`.
4. Ajouter des tests avec `SimulatedPlatform` et documenter `Value` / `Args` dans le README.

## Système de design (jetons)

| Jeton | Où | Valeur |
|---|---|---|
| Fond | `DeckBackdrop` (Theme.cs) | Dégradé radial `#161B2D` → `#0B0E17`, lueur en haut ; opaque |
| Surfaces | `DeckWindow`, `DeckPanel`, `DeckSurface`, `DeckSurfaceHi`, `DeckDock` | Thème sombre : application `#0B0E17`, panneaux `#121624`, cartes `#1A1F30`, cartes surélevées `#252B40` ; dock translucide |
| Accent | `DeckAccent` / `DeckAccentText` | Indigo `#6C5CE7` (texte blanc ≥ 4,5:1) |
| Touche | `Theme.TileFill` / `TileBorder` / `TileGradient` | Corps teinté à 26 % puis 16 %, liseré 40 %, pastille pleine |
| Espacement | `LayoutMetrics.Gap` | `Spacing` × échelle d'écran, 6 px minimum |
| Cible tactile | `LayoutMetrics.BarH` | 52 à 84 DIP (≥ 48) |
| Rayons | `TileRadius` | 16 % de la touche (12 à 34) ; pastille 28 % ; dock en pilule |
| Proportion | `LayoutMetrics.MaxAspect` | 1,3 maximum |
| Mouvement | `App.xaml`, `MainWindow.xaml.cs` | Appui 50 ms, retour 220 ms, transition de page 180 ms ; aucun si `Motion.Enabled` est faux |
| Icônes | WPF-UI `SymbolRegular` | Fluent System Icons (MIT), un seul style |
