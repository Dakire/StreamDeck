# CLAUDE.md

@AGENTS.md

## Contexte

Application WPF .NET 9 de type Stream Deck pour écran tactile (usage perso, IT, gaming).
Le projet s'appelle `TouchDeck` (assembly et namespace), le dépôt `StreamDeck`.

## Architecture actuelle

- **MVVM** avec CommunityToolkit.Mvvm. Un seul `DeckDocument` (`Core/DeckDocument.cs`, sans WPF) porte la configuration, l'historique
  annuler/rétablir et le presse-papiers ; deck, éditeur et paramètres le partagent via `AppServices.Document`.
  Toute modification de contenu passe par ses méthodes (annulables) ou `UpdateSettings` (réglages, hors historique).
- `AppServices` : chargement, sauvegarde automatique (300 ms, écriture atomique), rechargement à chaud (ignore nos propres écritures),
  thème / animations, ouverture de l'éditeur et des paramètres.
- Deck : `MainWindow.xaml` + `MainViewModel` (profil, page, dossier, sous-page, menu) + `LayoutMetrics`. `MainWindow.xaml.cs` ne garde que
  Win32 et le placement (NOACTIVATE, plein écran multi-écran) et implémente `IShell`.
- Éditeur : `EditorWindow` (fenêtre activable normale, séparée du deck qui ne prend jamais le focus) + `EditorViewModel`,
  `PropertiesViewModel`, `CatalogViewModel`, `ActionStepViewModel`. Les menus contextuels sont décrits en `MenuSpec` par les ViewModels
  et construits par `ContextMenuFactory`. `Controls/TileFace` dessine un bouton partout (deck, grille, aperçu).
- Format de config : `Version` 2, `Profiles[].Pages[].Buttons[]` ; l'ancien format (`Pages` racine) est migré par `ConfigNormalizer`.
  Les images vivent dans `Icons\` et ne sont référencées que par un nom de hachage validé (`IconStore`).
- Thème : `Theme.cs` (palettes `DarkPalette` / `LightPalette`, contrastes testés) publie les brushes `Deck*` ; le XAML les consomme en `DynamicResource`.
- Icônes de l'interface : `ui:SymbolIcon` (Fluent System Icons fournis par WPF-UI). Les icônes des boutons utilisateur : nom Fluent, emoji ou image.
- Tests : `tests/TouchDeck.Tests` (xUnit). `UiHarness` démarre une vraie application WPF sur un thread STA et rend les fenêtres hors écran
  (aucune fenêtre affichée, dossier de config temporaire) ; il capte aussi les erreurs de liaison de données. Définir `TOUCHDECK_SHOTS` pour garder les PNG.

## Chantier : modernisation Fluent / Windows 11

Décisions prises avec l'utilisateur : refonte complète, look moderne et simple d'usage, .NET 9.

Cible technique :

1. Ajouter le package **WPF-UI** (lepoco, `Wpf.Ui`) et ses `ThemesDictionary` / `ControlsDictionary` dans `App.xaml`.
2. Passer `MainWindow` en `ui:FluentWindow` avec `WindowBackdropType="Mica"` et thème clair, sombre ou système.
3. ~~Migrer l'interface (principale et éditeurs) en XAML + MVVM~~ (fait).
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
- Demander avant tout changement du format de `config.json` (le passage à la version 2 a été validé : clés ajoutées, `Pages` migré dans `Profiles`).
- Ne pas piloter le vrai bureau de l'utilisateur (captures d'écran, clics simulés) : utiliser le harnais de tests hors écran.
