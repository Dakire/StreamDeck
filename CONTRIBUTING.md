# Contribuer à TouchDeck

Ce guide complète [AGENTS.md](AGENTS.md), dont les règles s'imposent à toute contribution, humaine ou automatisée :
- fenêtre sans focus ;
- cibles tactiles de 48 px ;
- compatibilité de `config.json` ;
- frontière Win32.

## Préparer son poste

Il faut Windows 10 ou 11 et le SDK .NET 10 (voir [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)).

```powershell
dotnet build -c Release              # aucun avertissement toléré (TreatWarningsAsErrors)
dotnet test tests/TouchDeck.Tests    # tous les tests, interface comprise (rendue hors écran)
dotnet run -c Release -- --simulate  # essai sans effet sur le système
```

Pour vos essais, utilisez un dossier de configuration jetable : `$env:TOUCHDECK_DATA_DIR = "$env:TEMP\touchdeck-essai"`.
Ne touchez pas à `%APPDATA%\TouchDeck`.

## Branches et commits

- **Branches.** Une branche par sujet : `feat/…`, `fix/…`, `docs/…`, `chore/…`, `refactor/…`, `perf/…`, `test/…`.
  `main` reste toujours compilable et vert.
- **Commits.** Ils sont petits et ciblés, une étape logique par commit. Le message suit Conventional Commits et est
  rédigé en français :

  ```
  perf: la grille du deck n'est plus reconstruite à chaque modification

  Pourquoi et comment, mesures avant / après le cas échéant.
  ```

- **Fichiers interdits.** Ne commitez jamais `bin/`, `obj/`, des secrets ni de fichiers de configuration personnels.

## Code

- **Style.** C# moderne, indentation de 4 espaces. Le style est vérifié à la compilation par `.editorconfig`.
  `ImplicitUsings` est désactivé : écrivez les `using`.
- **Langue.** Les identifiants sont en anglais ; les textes de l'interface et les commentaires, en français.
- **Commentaires XML.** Toute API publique en porte un (la compilation échoue sinon). Il explique le pourquoi et les
  contraintes, pas ce que le nom dit déjà. Utilisez `<inheritdoc/>` pour une implémentation d'interface.
- **Changements ciblés.** Ne reformatez ni ne renommez rien en dehors du sujet.
- **Exceptions.** Ne les avalez jamais en silence. Filtrez les exceptions attendues par type et journalisez-les avec
  `Log`. Laissez les autres remonter au gestionnaire global (`App.OnUnhandled`).
- **Couleurs.** Aucune couleur en dur dans les vues : publiez une ressource `Deck*` depuis `Theme` et consommez-la en
  `DynamicResource`. Un test vérifie que chaque ressource utilisée existe dans les deux thèmes.
- **Injection de dépendances.** Un service s'ajoute dans `AppHost.Build` ; un ViewModel ou une fenêtre le reçoit par son
  constructeur. Pas de localisateur statique (un test le vérifie) : les statiques restantes sont documentées dans
  `docs/ARCHITECTURE.md`.
- **Frontières.** `Core/` ne référence ni WPF, ni Windows, ni les autres projets ; `Platform/Windows/` ne référence pas WPF ;
  le Win32 de l'interface est limité à `MainWindow.xaml.cs`. `ArchitectureTests` le vérifie sur les assemblies compilés.
- **Clés de format.** Les identifiants d'action passent par `ActionTypes`, `PageTargets` et `AudioCommands`. Ce sont des
  clés de `config.json` : on ne les renomme pas.

## Tests

- **Logique.** Tout changement de logique (`Core/`, `Services/`, ViewModels) est livré avec un test.
- **Comportement figé.** Avant de remanier du code, écrivez un test de caractérisation qui fige le comportement actuel.
- **Interface.** Les tests d'interface passent par `WpfHost` (`tests/TouchDeck.Tests/UiHarness.cs`) : vraies fenêtres
  rendues hors écran, plateforme simulée, configuration temporaire. Ne pilotez jamais le vrai bureau (captures, clics
  simulés).
- **Captures.** `TOUCHDECK_SHOTS=<dossier>` garde les PNG rendus par les tests ; utile pour comparer un rendu avant et
  après.
- **Performances.** `TOUCHDECK_BENCH=<dossier>` lance la mesure de performance hors écran (`BenchmarkTests`) et ajoute
  ses résultats à `bench.md`. N'optimisez pas sans mesure avant / après.

## Avant de proposer une modification

1. `dotnet build -c Release` sans avertissement, `dotnet test tests/TouchDeck.Tests` au vert.
2. Lancez l'application avec `TOUCHDECK_DATA_DIR` et exercez le chemin modifié.
3. Mettez à jour la documentation concernée : `README.md`, `docs/`, `CHANGELOG.md`. Si le choix est structurant,
   ajoutez une décision dans `docs/adr/`.
4. Tout changement du format de `config.json` est validé avec le mainteneur avant d'être codé
   (voir [docs/configuration.md](docs/configuration.md)).
