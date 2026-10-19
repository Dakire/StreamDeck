# CLAUDE.md

@AGENTS.md

## Contexte

Application WPF .NET 9 de type Stream Deck pour écran tactile (usage perso, IT, gaming).
Le projet s'appelle `TouchDeck` (assembly et namespace), le dépôt `StreamDeck`.

## Architecture actuelle

- **MVVM** avec CommunityToolkit.Mvvm. `MainWindow.xaml` décrit toute l'interface principale ; `ViewModels/MainViewModel.cs`
  porte l'état (pages, mode édition, menu, statut, rechargement de config) ; `ViewModels/LayoutMetrics.cs` calcule les
  dimensions à partir de la taille de la fenêtre ; `ViewModels/Items.cs` contient les éléments affichés (boutons, onglets, menu).
- `MainWindow.xaml.cs` ne garde que Win32 et le placement (NOACTIVATE, plein écran multi-écran, déplacement) et implémente
  `IShell` (`ViewModels/IShell.cs`), l'interface par laquelle le ViewModel demande ces actions et les boîtes de dialogue.
- Thème : `Theme.cs` (clair/sombre/système) publie les brushes `Deck*` comme ressources d'application ; le XAML les consomme en `DynamicResource`.
- Icônes de l'interface : `ui:SymbolIcon` (Fluent System Icons fournis par WPF-UI). Les icônes des boutons utilisateur restent des emojis.
- **Pas encore migré (encore construit en C#)** : `ButtonEditorWindow.cs` et `EditorWindows.cs` (fenêtres d'édition).
- Styles XAML dans `App.xaml` : `DeckButton`, `FlatButton`, `AddTileButton`.
- Configuration : `Models.cs`, `ConfigStore.cs` (JSON dans `%APPDATA%\TouchDeck`) ; actions : `ActionRunner.cs`.

## Chantier : modernisation Fluent / Windows 11

Décisions prises avec l'utilisateur : refonte complète, look moderne et simple d'usage, .NET 9.

Cible technique :

1. Ajouter le package **WPF-UI** (lepoco, `Wpf.Ui`) et ses `ThemesDictionary` / `ControlsDictionary` dans `App.xaml`.
2. Passer `MainWindow` en `ui:FluentWindow` avec `WindowBackdropType="Mica"` et thème clair, sombre ou système.
3. ~~Migrer l'interface principale en XAML + MVVM~~ (fait). Reste : les fenêtres d'édition.
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
