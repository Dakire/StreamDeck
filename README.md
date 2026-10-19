# TouchDeck

Tableau de bord de raccourcis façon Stream Deck pour Windows, pensé pour un écran tactile secondaire.
Usage visé : perso, pro IT et gaming.

Chaque bouton lance une ou plusieurs actions : raccourci clavier, saisie de texte, touche média,
programme, script, URL, changement de page ou de profil, délai, mise au premier plan d'une fenêtre.

## Fonctionnalités

**Le deck (écran tactile)**

- Grille de boutons adaptative (nombre de cases selon la résolution) ou format fixe par profil, avec sous-pages automatiques.
- Profils, pages en onglets, dossiers et sous-dossiers ; pages masquées accessibles par une action `page`.
- Fenêtre qui ne prend pas le focus : les raccourcis arrivent à l'application active.
- Plein écran sur l'écran secondaire ou mode fenêtre, sans masquer la barre des tâches (le deck occupe la zone de travail) ; le deck suit le branchement et le débranchement de l'écran, et se cale exactement même entre écrans de densités différentes.
- Boutons à fond dégradé discret (la couleur choisie en haut, assombrie en bas), palette moderne par défaut, survol et appui nets.
- Profil associé à une application : il s'affiche seul quand le programme est au premier plan.
- Mode édition tactile (menu ⚙ › *Modifier au toucher*) : toucher un bouton ouvre l'éditeur dessus.

**L'éditeur** (menu ⚙ › *Ouvrir l'éditeur*, ou `TouchDeck.exe --edit`)

- Trois panneaux inspirés d'Elgato Stream Deck : catalogue d'actions, grille du deck, propriétés du bouton.
- Catalogue : recherche instantanée, 8 catégories repliables, favoris, actions récentes, glisser vers une case.
- Grille fidèle au deck : sélection, case vide, glisser-déposer (déplacer, échanger, copier avec Ctrl, ranger dans un dossier,
  envoyer vers une autre page), formats 3×2, 5×3, 6×3, 5×4, 8×4 ou automatique.
- Propriétés : titre (position, affichage), icône Fluent / emoji / texte, image importée, couleurs, étapes multiples
  (ajout, ordre, suppression), aperçu en direct, validation en direct, réinitialisation de l'apparence.
- Menus contextuels (clic droit, appui long, touche Menu) sur les cases, profils, pages et catalogue.
- Annuler / rétablir (100 niveaux), presse-papiers interne, notifications avec bouton *Annuler*.
- Profils : créer, renommer, dupliquer, supprimer, exporter / importer (`.touchdeck`), définir comme profil actif,
  associer à une application.
- Paramètres : thème, animations, icônes, fond, plein écran, premier plan, grille, écran du deck, lancement avec Windows,
  sauvegardes (export, import, restauration), raccourcis.

Cliquer un bouton dans l'éditeur ne l'exécute jamais.

### Raccourcis de l'éditeur

| Raccourci | Effet |
|---|---|
| `Ctrl+Z`, `Ctrl+Y` / `Ctrl+Maj+Z` | Annuler, rétablir |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+D` | Copier, couper, coller, dupliquer la case sélectionnée |
| `Suppr` | Supprimer le bouton |
| `F2` | Modifier le titre |
| `Entrée` | Ouvrir le dossier / configurer le bouton |
| `Retour arrière` | Remonter d'un dossier |
| `Alt+flèches` | Déplacer le bouton (échange avec la voisine) |
| `Ctrl+F` | Chercher dans le catalogue |
| `Maj+F10`, touche Menu | Menu contextuel |
| `Échap` | Fermer un menu, annuler un glisser-déposer |

Ces raccourcis ne s'appliquent jamais pendant la saisie dans un champ de texte.

## Prérequis

- Windows 10 ou 11 (Windows 11 recommandé : effet Mica, repli en fond uni sinon)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) pour compiler, runtime Desktop pour exécuter

## Démarrage rapide

```powershell
dotnet run -c Release
dotnet test tests/TouchDeck.Tests    # tests automatisés
```

Pour produire l'exécutable :

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Configuration

Dossier : `%APPDATA%\TouchDeck` (ou le dossier de la variable d'environnement `TOUCHDECK_DATA_DIR`).

| Élément | Rôle |
|---|---|
| `config.json` | Configuration, créée au premier lancement. Surveillée : toute modification enregistrée est appliquée sans redémarrer. |
| `Icons\` | Images des boutons (PNG, nommées d'après leur contenu). |
| `Backups\` | Sauvegardes automatiques (15 conservées). |
| `config.v1.backup.json` | Copie intacte de l'ancien fichier, faite une seule fois lors du passage aux profils. |
| `error.log` | Erreurs inattendues de l'interface. |

L'éditeur enregistre à chaque modification (écriture atomique : un arrêt en cours d'écriture ne corrompt pas le fichier).
Un fichier `config.json` invalide n'est jamais écrasé sans copie : il est conservé sous `config.invalide-<date>.json`.

### Clés de `config.json`

| Clé | Rôle | Défaut |
|---|---|---|
| `Version` | Version du format (`2`) | `2` |
| `Monitor` | Index de l'écran, `-1` = premier écran secondaire | `-1` |
| `Fullscreen` | Plein écran sur l'écran cible | `true` |
| `AlwaysOnTop` | Reste au-dessus des autres fenêtres | `false` |
| `Columns` / `Rows` | Taille de la grille pour les profils en format automatique, `0` = selon l'écran | `0` |
| `Spacing` | Espacement entre boutons (DIP) | `14` |
| `Background` | Couleur de fond (la valeur par défaut laisse voir l'effet Mica) | `#0E1015` |
| `Theme` | `system` (suit Windows), `dark` ou `light` | `dark` à la création, sinon inchangé |
| `IconStyle` | `fluent` : emojis connus affichés en icônes Fluent ; `emoji` : tels quels | `fluent` |
| `StartWithWindows` | Reflet du lancement à l'ouverture de session (le registre fait foi) | |
| `ReduceMotion` | Supprime les animations | `false` |
| `ActiveProfile` | Profil affiché sur le deck | premier profil |
| `Favorites`, `Recents` | Identifiants d'actions du catalogue | `[]` |
| `Profiles` | Liste de profils : `Name`, `Columns`, `Rows`, `AppMatch`, `Pages` | |

Un profil contient des `Pages` ; une page, des `Buttons` dans l'ordre de la grille. Un bouton :

| Clé | Rôle |
|---|---|
| `Label`, `Icon`, `Color`, `Actions` | Titre, icône (nom Fluent, emoji ou texte), couleur, étapes |
| `HideTitle`, `TitlePosition` | Masquer le titre ; `top` / `middle` / `bottom` (absent : sous l'icône) |
| `TextColor` | Couleur du texte et de l'icône |
| `ImagePath`, `ImageFit` | Image importée (nom dans `Icons\`), `cover` ou `contain` |
| `IsFolder`, `Children` | Dossier et son contenu |
| `Blank` | Case vide qui garde la position de ses voisines |

**Ancien format** : un `config.json` sans `Version`, avec `Pages` à la racine, se charge tel quel. Il est converti en un profil
« Principal » et réécrit au nouveau format, après copie dans `config.v1.backup.json`. Aucune clé n'est supprimée ni renommée,
sauf `Pages` qui descend dans `Profiles[].Pages`. Les nouvelles clés sont facultatives.

### Types d'actions

| `Type` | `Value` | `Args` |
|---|---|---|
| `hotkey` | Combinaison, ex. `ctrl+shift+esc` | |
| `text` | Texte à saisir | |
| `media` | `playpause`, `prev`, `next`, `stop`, `mute`, `voldown`, `volup` | |
| `launch` | Programme à lancer | Arguments |
| `script` | Fichier `.ps1`, `.bat`, `.cmd` ou commande PowerShell | Arguments |
| `url` | Adresse à ouvrir (`javascript:`, `vbscript:`, `data:` et `file:` sont refusés) | |
| `page` | Nom de page, `home` ou `back` | |
| `profile` | Nom du profil à afficher | |
| `delay` | Durée en millisecondes | |
| `focus` | Titre ou processus de la fenêtre | Programme à lancer si absente |

## Import et export

- **Profil** : éditeur › menu du profil › *Exporter…* / *Importer…*.
- **Configuration complète** : Paramètres › Sauvegardes.

Le format `.touchdeck` est une archive zip (`profile.json` ou `config.json`, `icons/*.png`). L'import vérifie la taille,
le nombre d'éléments, les types d'actions et les noms d'images, ne lit jamais de chemin libre dans l'archive, demande
confirmation (en signalant les actions qui lancent des programmes ou des scripts), fait une sauvegarde puis peut être annulé.

## Structure du dépôt

| Dossier / fichier | Contenu |
|---|---|
| `Core/` | Logique sans WPF : `DeckDocument` (opérations, historique), `ConfigStore`, `ConfigNormalizer` (migration), `ActionCatalog`, `ActionValidator` |
| `Services/` | `AppServices` (sauvegarde auto, rechargement à chaud), `IconStore`, `ProfilePackage`, `TransferService`, `DisplayService`, `AutoStart`, `Notifier`, `Motion` |
| `ViewModels/` | `MainViewModel` (deck), `EditorViewModel`, `PropertiesViewModel`, `CatalogViewModel`, `SettingsViewModel`, `MenuSpec` |
| `Controls/` | `TileFace` (rendu d'un bouton), `ContextMenuFactory`, `PromptWindow` |
| `MainWindow.xaml(.cs)` | Deck ; code-behind limité à Win32 et au placement multi-écran |
| `EditorWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)` | Éditeur et paramètres (dialogues, focus, clavier, glisser-déposer) |
| `Theme.cs`, `IconCatalog.cs`, `App.xaml` | Palettes claire / sombre (contrastes vérifiés), icônes, styles |
| `ActionRunner.cs`, `NativeInput.cs`, `WindowFocus.cs` | Exécution des actions, appels Win32 |
| `tests/TouchDeck.Tests` | Tests xUnit : logique, migration, import, contrastes, rendu hors écran des fenêtres |

## Accessibilité

Contraste des textes et des anneaux de focus vérifié par tests (WCAG 2.2 AA) en thème clair et sombre ; focus clavier visible ;
état jamais porté par la couleur seule (icône + texte pour les erreurs, coche sur les couleurs, bordure + anneau pour la sélection) ;
noms accessibles et annonces polies (`LiveSetting`) pour les notifications et erreurs ; cibles de 48 px minimum ;
animations coupées par le réglage TouchDeck ou celui de Windows.

## Limites connues

- Pas de périphérique matériel : TouchDeck n'est pas un pilote de Stream Deck Elgato. Le « périphérique » est l'écran du deck.
- Pas de modules externes (plugins) : le code distant n'est jamais exécuté. La catégorie n'existe donc pas dans le catalogue.
- Pas de traduction : l'interface est en français, il n'y a pas de sélecteur de langue.
- Les profils par application se basent sur le nom du programme au premier plan, relevé toutes les 0,8 s.
- Le glisser-déposer tactile dépend de la prise en charge de Windows ; le presse-papiers interne et `Alt+flèches` / le menu *Déplacer* offrent des alternatives.
- Les images importées ne sont jamais supprimées automatiquement (les sauvegardes peuvent encore les référencer).

## Sécurité

Les actions `script` et `launch` exécutent du code avec vos droits. N'importez que des fichiers de confiance.
