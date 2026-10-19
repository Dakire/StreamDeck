# TouchDeck

Tableau de bord de raccourcis façon Stream Deck pour Windows, pensé pour un écran tactile secondaire.
Usage visé : perso, pro IT et gaming.

Chaque bouton lance une ou plusieurs actions : raccourci clavier, saisie de texte, touche média,
programme, script, URL, changement de page, délai, mise au premier plan d'une fenêtre.

## Fonctionnalités

- Grille de boutons adaptative (nombre de cases selon la résolution), avec sous-pages automatiques.
- Pages organisées en onglets, pages masquées accessibles par une action `page`.
- Fenêtre qui ne prend pas le focus : les raccourcis arrivent à l'application active.
- Plein écran sur l'écran secondaire, ou mode fenêtre.
- Mode édition tactile intégré, avec modèles de boutons prêts à l'emploi.
- Configuration JSON rechargée à chaud.

## Prérequis

- Windows 10 ou 11 (Windows 11 recommandé)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) pour compiler, runtime Desktop pour exécuter

## Démarrage rapide

```powershell
dotnet run -c Release
```

Pour produire l'exécutable :

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Configuration

Fichier : `%APPDATA%\TouchDeck\config.json`, créé au premier lancement.
Il est surveillé : toute modification enregistrée est appliquée sans redémarrer.

| Clé | Rôle | Défaut |
|---|---|---|
| `Monitor` | Index de l'écran, `-1` = premier écran secondaire | `-1` |
| `Fullscreen` | Plein écran sur l'écran cible | `true` |
| `AlwaysOnTop` | Reste au-dessus des autres fenêtres | `false` |
| `Columns` / `Rows` | Taille de la grille, `0` = automatique | `0` |
| `Spacing` | Espacement entre boutons (DIP) | `14` |
| `Background` | Couleur de fond (la valeur par défaut laisse voir l'effet Mica) | `#0E1015` |
| `Theme` | `system` (suit Windows), `dark` ou `light` | `system` |
| `IconStyle` | `fluent` : emojis connus affichés en icônes Fluent ; `emoji` : tels quels | `fluent` |
| `Pages` | Liste de pages, chacune avec ses `Buttons` | |

### Types d'actions

| `Type` | `Value` | `Args` |
|---|---|---|
| `hotkey` | Combinaison, ex. `ctrl+shift+esc` | |
| `text` | Texte à saisir | |
| `media` | `playpause`, `prev`, `next`, `mute`, `voldown`, `volup` | |
| `launch` | Programme à lancer | Arguments |
| `script` | Fichier `.ps1`, `.bat`, `.cmd` ou commande PowerShell | Arguments |
| `url` | Adresse à ouvrir | |
| `page` | Nom de page, `home` ou `back` | |
| `delay` | Durée en millisecondes | |
| `focus` | Titre ou processus de la fenêtre | Programme à lancer si absente |

## Structure du dépôt

| Fichier | Contenu |
|---|---|
| `MainWindow.xaml(.cs)` | Interface principale ; code-behind limité à Win32 et au placement multi-écran |
| `ViewModels/` | MainViewModel, éditeurs, mise en page adaptative |
| `Theme.cs`, `IconCatalog.cs` | Thème clair/sombre, icônes Fluent |
| `App.xaml` | Styles des boutons |
| `Models.cs` | Modèle de configuration |
| `ConfigStore.cs` | Lecture, écriture et configuration par défaut |
| `ActionRunner.cs` | Exécution des actions |
| `NativeInput.cs`, `WindowFocus.cs` | Appels Win32 (clavier, médias, focus) |
| `ButtonEditorWindow.xaml`, `PageEditorWindow.xaml` | Fenêtres d'édition |
| `Templates.cs` | Modèles de boutons (Chrome, Google, 3CX…) |

## Feuille de route

Interface Windows 11 (Fluent, Mica, clair/sombre) en place. Voir `CLAUDE.md` et `AGENTS.md` pour les conventions.

## Sécurité

Les actions `script` et `launch` exécutent du code avec vos droits. N'importez que des fichiers
`config.json` de confiance.
