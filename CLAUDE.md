# CLAUDE.md

@AGENTS.md

## Contexte

Application WPF .NET 9 de type Stream Deck pour écran tactile (usage perso, IT, gaming).
Le projet s'appelle `TouchDeck` (assembly et namespace), le dépôt `StreamDeck`.

## Architecture actuelle

- `MainWindow.xaml` ne contient qu'une `Grid` vide. **Toute l'interface est construite en C#** dans
  `MainWindow.xaml.cs` (`Rebuild()`, `BuildTopBar`, `BuildButtons`, `BuildBar`, `BuildMenu`), avec des couleurs
  codées en dur (`Surface`, `Accent`…) et reconstruction complète à chaque changement d'état.
- `EditorWindows.cs` (`Ui`) et `ButtonEditorWindow.cs` construisent aussi leurs fenêtres en code.
- Styles XAML dans `App.xaml` : `DeckButton` (carte avec ombre et animation d'appui), `FlatButton`.
- Configuration : `Models.cs` (`DeckConfig`, `DeckPage`, `DeckButton`, `DeckAction`), `ConfigStore.cs`
  (JSON dans `%APPDATA%\TouchDeck`), rechargement via `FileSystemWatcher`.
- Actions : `ActionRunner.cs` dispatche sur `DeckAction.Type`.
- Pas de MVVM, pas de NuGet, `UseWindowsForms` activé uniquement pour `Screen`.

## Chantier : modernisation Fluent / Windows 11

Décisions prises avec l'utilisateur : refonte complète, look moderne et simple d'usage, .NET 9.

Cible technique :

1. Ajouter le package **WPF-UI** (lepoco, `Wpf.Ui`) et ses `ThemesDictionary` / `ControlsDictionary` dans `App.xaml`.
2. Passer `MainWindow` en `ui:FluentWindow` avec `WindowBackdropType="Mica"` et thème clair, sombre ou système.
3. Migrer l'interface construite en code vers du XAML : `ItemsControl` + `UniformGrid` pour les boutons,
   `DataTemplate` pour le bouton de deck, `ViewModel` avec `CommunityToolkit.Mvvm`.
4. Remplacer les couleurs en dur par des ressources de thème (`DynamicResource`).
5. Remplacer les emojis des chrome UI par `Segoe Fluent Icons` (les icônes **des boutons utilisateur** restent libres : emoji ou texte).
6. Navigation par pages : `NavigationView` à gauche, ou barre d'onglets en bas pour le mode tactile.

Contraintes spécifiques à ne pas casser pendant la migration :

- `ui:FluentWindow` : vérifier que `WS_EX_NOACTIVATE` et le hook `WM_MOUSEACTIVATE` fonctionnent toujours
  (c'est le cœur du produit). Tester avec un raccourci envoyé vers le Bloc-notes.
- Le plein écran multi-écran se fait par `SetWindowPos` en pixels physiques ; garder cette logique.
- Le mode édition, les sous-pages et le rechargement à chaud doivent rester équivalents.
- Mica exige Windows 11 : prévoir un fond uni en repli.

## Conventions

- Réponses et messages de commit en français.
- Petits commits ciblés, un par étape de migration.
- Ne pas committer `bin/` ni `obj/`.
- Demander avant tout changement du format de `config.json`.
