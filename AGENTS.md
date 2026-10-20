# AGENTS.md

Instructions pour les agents de code (Claude Code, Codex, Copilot, etc.). Lire aussi `README.md`.

## Projet

TouchDeck : application WPF .NET 10 (`net10.0-windows`), tableau de bord de raccourcis pour écran tactile.
Namespace racine `TouchDeck`, nullable activé, `ImplicitUsings` désactivé (écrire les `using`).

## Commandes

```powershell
dotnet build -c Release                 # compilation
dotnet run -c Release                   # lancement (--edit ouvre aussi l'éditeur)
dotnet test tests/TouchDeck.Tests       # tests automatisés
```

Variable `TOUCHDECK_DATA_DIR` : utiliser un autre dossier de configuration pour les essais (ne pas toucher à `%APPDATA%\TouchDeck`).
Ajouter ou adapter un test pour tout changement de logique (`Core/`, `Services/`, ViewModels) ; les tests UI rendent les fenêtres hors écran.

## Règles à respecter

- **Fenêtre sans focus** : `WS_EX_NOACTIVATE` et `WM_MOUSEACTIVATE` dans `MainWindow.xaml.cs` ne doivent
  pas être retirés. Sans eux, les raccourcis envoyés ne parviennent plus à l'application active.
- **Cibles tactiles** : minimum 48 px de haut pour tout contrôle interactif (deck et éditeur).
- **Compatibilité de configuration** : `config.json` d'utilisateurs existants doit rester lisible.
  Ajouter des propriétés avec valeur par défaut (et `JsonIgnore(WhenWritingDefault/Null)` pour ne pas alourdir le fichier),
  ne jamais renommer ni supprimer une clé sans migration (`ConfigNormalizer`) ni test.
- **Sécurité** : ne pas ajouter d'exécution de code issu d'une source distante. Les actions `script` et `launch`
  s'exécutent avec les droits de l'utilisateur.
- **Win32** : les appels P/Invoke et COM restent dans le projet `Platform/Windows/` (`NativeInput.cs`, `WindowFocus.cs`, `NativeAudio.cs`,
  internes) et dans `MainWindow.xaml.cs` (placement de la fenêtre). Les actions et les ViewModels n'y accèdent qu'à travers les interfaces de
  `Core/Actions/Platform.cs` (implémentation : `Platform/Windows/WindowsPlatform.cs`) ; `Core/` ne référence ni WPF ni Win32 (`ArchitectureTests`).
- **Pas de secrets** dans le dépôt, ni de binaires : `bin/` et `obj/` sont ignorés par git.

## Injection de dépendances

- Un service s'ajoute dans `AppHost.Build` ; un ViewModel ou une fenêtre le reçoit par son constructeur. Pas de localisateur statique.
- Un nouveau type d'action = une classe dans `Core/Actions/Handlers/` (voir `HotkeyAction`), ajoutée à `BuiltInActions.All`.
- Journal : `Log` (Core) hors injection, `ILogger<T>` dans les services injectés. Ne jamais journaliser un secret.

## Style de code

- C# moderne (file-scoped namespaces, `record`, pattern matching), indentation 4 espaces.
- Textes de l'interface et commentaires en français, identifiants en anglais.
- Changements ciblés : ne pas reformater ni renommer en dehors du sujet de la tâche.

## Direction UI en cours

Migration vers Fluent / Windows 11 : thème clair et sombre, Mica, coins arrondis, Segoe UI Variable,
icônes Segoe Fluent Icons, interface décrite en XAML plutôt que construite en code C#.
Voir `CLAUDE.md` pour le détail.

## Avant de livrer

1. `dotnet build -c Release` sans avertissement nouveau, `dotnet test tests/TouchDeck.Tests` au vert.
2. Lancer l'application (avec `TOUCHDECK_DATA_DIR`) et exercer le chemin modifié (clic, édition, rechargement de config).
3. Mettre à jour `README.md` si un comportement ou une clé de configuration change.
