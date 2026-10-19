# 0009 — Trois projets aux frontières vérifiées

- **Statut** : acceptée.
- **Date** : 19 octobre 2026.

## Contexte

Les règles « le Core ne connaît ni WPF ni Win32 » et « les appels Win32 restent dans quelques fichiers » étaient des
conventions tenues par revue de code. Un seul projet ne permettait pas au compilateur de les faire respecter ; trois
dépendances cachées l'avaient d'ailleurs déjà violée (validation des raccourcis, fond par défaut, noms d'images).

## Décision

- **`Core/`** (`TouchDeck.Core`, `net9.0`) : modèle, document, configuration, moteur d'actions, interfaces de plateforme.
- **`Platform/Windows/`** (`TouchDeck.Platform.Windows`, `net9.0-windows`, sans WPF) : `SendInput`, fenêtres Win32, Core
  Audio, registre. Seul `WindowsPlatform` (et `AutoStart`) est public, le reste est interne.
- **Application WPF** à la racine : vues, ViewModels, services, composition.
- **Dépendances levées** : syntaxe des raccourcis dans `HotkeySyntax` (la plateforme valide ensuite les caractères sur le
  clavier courant via `IKeyboardService.TryValidateHotkey`), noms d'images dans `IconNames`, fond par défaut dans
  `ConfigNormalizer.DefaultBackground`, programmes ouverts via `IWindowService.OpenProcessNames`.
- **Vérification.** `ArchitectureTests` lit les assemblies compilés : le Core ne référence ni UI, ni Windows, ni
  injection, ni Serilog, et n'a aucun appel natif ; la plateforme ne référence aucun framework d'interface ; l'interface
  n'appelle Win32 que depuis `MainWindow`.

## Conséquences

- Une violation fait échouer la compilation (types internes) ou les tests (références d'assemblies).
- Une plateforme Linux ou une interface Avalonia ajoute un projet sans toucher au Core.
- L'application dépend de deux projets de plus à compiler : coût de compilation négligeable.
- `AGENTS.md` et `CLAUDE.md` décrivent le nouveau découpage.