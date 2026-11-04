# CLAUDE.md

@AGENTS.md

## Contexte

Application WPF .NET 10 de type Stream Deck pour écran tactile (usage perso, IT, gaming).
Le projet s'appelle `StreamDak` (assembly et namespace), le dépôt `StreamDeck`.

## Architecture actuelle

- **Trois projets** : `Core/` (`StreamDak.Core`, net10.0 : modèle, document, configuration, moteur d'actions, sans WPF ni Win32),
  `Platform/Windows/` (`StreamDak.Platform.Windows` : SendInput, Core Audio, fenêtres Win32, registre ; seul `WindowsPlatform` est public),
  et l'application WPF à la racine. Les frontières sont vérifiées par `ArchitectureTests`.
- **MVVM** avec CommunityToolkit.Mvvm. Un seul `DeckDocument` (`Core/DeckDocument.cs`, sans WPF) porte la configuration, l'historique
  annuler/rétablir et le presse-papiers ; deck, éditeur et paramètres le reçoivent par injection de dépendances.
  Toute modification de contenu passe par ses méthodes (annulables) ou `UpdateSettings` (réglages, hors historique).
- **Intégrations** : `Core/Integrations/` (`IIntegration`, `IntegrationManager`, `IntegrationAction`), un projet par connecteur dans
  `Integrations/<Nom>/` ; une étape de bouton = `{Type = id, Value = commande, Args = argument}`. Secrets en DPAPI (`ISecretStore`).
  Plugins locaux (`Core/Plugins`) et API locale par canal nommé (`Core/Api`). Voir ADR 0011 et 0012, `docs/integrations/`.
- **Injection de dépendances** (conteneur `Microsoft.Extensions.DependencyInjection` seul, sans hôte générique, ADR 0007) : `AppHost.Build` est la racine de composition (les tests construisent le même
  conteneur en simulation). `ConfigPersistence` : sauvegarde automatique (300 ms, écriture atomique) et rechargement à chaud (ignore nos propres
  écritures) ; `AppearanceService` : thème / animations ; `IAppWindows` : éditeur et paramètres. Plus de localisateur statique.
- **Journal** : façade `Log` (Core) branchée sur Serilog (`streamdak.log`) ; `ILogger<T>` pour les services injectés.
- Deck : `MainWindow.xaml` + `MainViewModel` (profil, page, dossier, sous-page, menu) + `LayoutMetrics`. `MainWindow.xaml.cs` ne garde que
  Win32 et le placement (NOACTIVATE, plein écran multi-écran) et implémente `IShell`.
- Édition : plus de fenêtre d'édition (ADR 0016). Le mode édition du deck (`MainWindow.Edit.cs`, `MainViewModel.Editing.cs`) affiche `CatalogPanel` à droite et
  `PropertiesPanel` sous la grille (ADR 0026) ; moteur : `EditorViewModel`, `PropertiesViewModel`, `CatalogViewModel`, `ActionStepViewModel`. Dialogues :
  `DeckDialogViewModel` (dans le deck, jamais de fenêtre modale). Les menus contextuels sont décrits en `MenuSpec` par les ViewModels
  et affichés dans la feuille du deck. `Controls/TileFace` dessine un bouton partout (deck, grille, aperçu).
- Format de config : `Version` 2, `Profiles[].Pages[].Buttons[]` ; l'ancien format (`Pages` racine) est migré par `ConfigNormalizer`.
  Les images vivent dans `Icons\` et ne sont référencées que par un nom de hachage validé (`IconStore`).
- Thème : `Theme.cs` (palettes `DarkPalette` / `LightPalette`, contrastes testés) publie les brushes `Deck*` ; le XAML les consomme en `DynamicResource`.
- Icônes de l'interface : `ui:SymbolIcon` (Fluent System Icons fournis par WPF-UI). Les icônes des boutons utilisateur : nom Fluent, emoji ou image.
- Actions : `Core/Actions/ActionEngine` (délai par étape, annulation, `ActionResult`), un gestionnaire par type dans `Core/Actions/Handlers/`
  enregistrés dans `ActionRegistry` ; appels système via les interfaces de `Core/Actions/Platform.cs` (Windows : `Platform/Windows/`,
  tests et `--simulate` : `SimulatedPlatform`). Détail : `docs/ARCHITECTURE.md`.
- Deck : touches dessinées par `TileFace` selon `TileKind` (corps teinté, pastille, variantes) ; seule la tuile change pendant une exécution.
- Écran du deck : `MonitorDevice` (identifiant) prioritaire sur `Monitor` (index), résolu par `DisplayResolver`.
- Tests : `tests/StreamDak.Tests` (xUnit). `UiHarness` démarre une vraie application WPF sur un thread STA et rend les fenêtres hors écran
  (aucune fenêtre affichée, dossier de config temporaire) ; il capte aussi les erreurs de liaison de données. Définir `STREAMDAK_SHOTS` pour garder les PNG.

## État de la modernisation Fluent / Material

Fait : WPF-UI (`ThemesDictionary` / `ControlsDictionary`), `ui:FluentWindow` avec Mica (fond uni en repli hors Windows 11), thème clair, sombre ou AMOLED
(palettes Material Design 3 générées depuis `Theme.Seed`, ADR 0026), interface en XAML + MVVM, ressources `DynamicResource`, icônes Fluent.
Non retenu : `NavigationView` (la navigation se fait par onglets de pages et dock, adaptés au tactile).

Contraintes à ne pas casser :

- `WS_EX_NOACTIVATE` et le hook `WM_MOUSEACTIVATE` : cœur du produit. Tester avec un raccourci envoyé vers le Bloc-notes (seul le mode édition les suspend, ADR 0015).
- Plein écran multi-écran par `SetWindowPos` en pixels physiques (les agrandissements demandés par Windows 11, `SC_MAXIMIZE`, y passent : ADR 0018).
- Le mode édition, les sous-pages et le rechargement à chaud restent équivalents.
- Le toucher est traité par le deck lui-même (`MainWindow.Touch.cs`) : pas de déplacement du curseur système ni de clic synthétique.

## Conventions

- Réponses et messages de commit en français.
- Petits commits ciblés, un par étape de migration.
- Ne pas committer `bin/` ni `obj/`.
- Demander avant tout changement du format de `config.json` (le passage à la version 2 a été validé : clés ajoutées, `Pages` migré dans `Profiles`).
- Ne pas piloter le vrai bureau de l'utilisateur (captures d'écran, clics simulés) : utiliser le harnais de tests hors écran.
