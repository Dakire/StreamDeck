# AGENTS.md

Instructions pour les agents de code (Claude Code, Codex, Copilot, etc.). Lire aussi `README.md`.

## Projet

TouchDeck : application WPF .NET 9 (`net9.0-windows`), tableau de bord de raccourcis pour écran tactile.
Namespace racine `TouchDeck`, nullable activé, `ImplicitUsings` désactivé (écrire les `using`).

## Commandes

```powershell
dotnet build -c Release      # compilation
dotnet run -c Release        # lancement
```

Il n'y a pas encore de tests automatisés. Vérifier un changement en lançant l'application.

## Règles à respecter

- **Fenêtre sans focus** : `WS_EX_NOACTIVATE` et `WM_MOUSEACTIVATE` dans `MainWindow.xaml.cs` ne doivent
  pas être retirés. Sans eux, les raccourcis envoyés ne parviennent plus à l'application active.
- **Cibles tactiles** : minimum 48 px de haut pour tout contrôle interactif.
- **Compatibilité de configuration** : `config.json` d'utilisateurs existants doit rester lisible.
  Ajouter des propriétés avec valeur par défaut, ne jamais renommer ni supprimer une clé sans migration.
- **Sécurité** : ne pas ajouter d'exécution de code issu d'une source distante. Les actions `script` et `launch`
  s'exécutent avec les droits de l'utilisateur.
- **Win32** : les appels P/Invoke restent dans `NativeInput.cs`, `WindowFocus.cs` et `MainWindow.xaml.cs`.
- **Pas de secrets** dans le dépôt, ni de binaires : `bin/` et `obj/` sont ignorés par git.

## Style de code

- C# moderne (file-scoped namespaces, `record`, pattern matching), indentation 4 espaces.
- Textes de l'interface et commentaires en français, identifiants en anglais.
- Changements ciblés : ne pas reformater ni renommer en dehors du sujet de la tâche.

## Direction UI en cours

Migration vers Fluent / Windows 11 : thème clair et sombre, Mica, coins arrondis, Segoe UI Variable,
icônes Segoe Fluent Icons, interface décrite en XAML plutôt que construite en code C#.
Voir `CLAUDE.md` pour le détail.

## Avant de livrer

1. `dotnet build -c Release` sans avertissement nouveau.
2. Lancer l'application et exercer le chemin modifié (clic, édition, rechargement de config).
3. Mettre à jour `README.md` si un comportement ou une clé de configuration change.
