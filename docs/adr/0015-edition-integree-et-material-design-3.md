# 0015 — Édition intégrée au deck et thème Material Design 3

- **Statut** : acceptée.
- **Date** : 21 octobre 2026.

## Contexte

Modifier un bouton ouvrait l'éditeur dans une fenêtre séparée. Demande : afficher les actions à gauche et les propriétés à droite
de la grille, dans le deck, et rendre l'application plus moderne (Material Design 3).

Le deck ne prend jamais le focus (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE`) et traite lui-même le toucher (ADR 0013) : sans cela, les
raccourcis envoyés n'arrivent plus à l'application active. Or des panneaux d'édition contiennent des champs de texte, qui exigent
le focus clavier et un toucher traité par WPF.

## Décision

- **Édition intégrée.** En mode édition, `MainViewModel` héberge un `EditorViewModel` (catalogue, sélection, brouillon, annuler / rétablir,
  déjà complets) sans sa fenêtre. `PropertiesPanel` et `CatalogPanel` sont des contrôles partagés avec l'éditeur séparé
  (`Themes/EditorShared.xaml`). La grille garde le nombre de cases de l'usage normal et rétrécit. `Modifier…`, l'échec d'un bouton et une
  création qui demande un réglage mènent à ce mode ; l'éditeur complet reste une fenêtre à part.
- **Exception encadrée à la règle du focus.** Pendant le mode édition **seulement** : `WS_EX_NOACTIVATE` est retiré, le filtre
  `WM_MOUSEACTIVATE` et l'interception du toucher sont suspendus (`MainWindow.SetEditing`). À la sortie : `WS_EX_NOACTIVATE` est rétabli
  (et vérifié, avec un message d'erreur journalisé sinon) et le focus est rendu à l'application qui l'avait avant l'édition, de sorte que
  les raccourcis suivants ne partent pas vers le deck. Fermer la fenêtre en pleine édition réinitialise l'état. Test : le style est
  présent hors édition, absent en édition, rétabli ensuite.
- **Thème MD3 dynamique.** Les trois palettes (sombre, AMOLED, clair) sont calculées à partir d'une couleur source (`Theme.Seed`,
  #6C5CE7) par des palettes tonales (`MaterialColor`, `MaterialScheme` dans le Core, sans WPF) : accent « primary » et sa couleur de texte,
  conteneurs, surfaces tonales, erreur, contours. La clarté est le L* de CIELAB (HCT utilise CAM16 : écart de teinte de quelques degrés,
  sans effet sur les contrastes). Un écart de ton garantit les contrastes, vérifiés par test pour chaque rôle et chaque surface.
- **Élévation tonale.** Plus d'ombre portée sur les touches, la pastille d'icône, le dock, le bandeau et le menu : l'élévation se lit par
  le ton. États MD3 (survol 8 %, appui 12 %), formes plus arrondies, onglet actif en conteneur secondaire.

## Conséquences

- En mode édition, le toucher redevient celui de WPF : le curseur de la souris bouge. Acceptable pour l'édition, jamais en usage normal.
- Pendant l'édition, les raccourcis envoyés par le deck ne sont pas utilisés (on édite, on n'exécute pas) ; le focus est rendu à la sortie.
  La restitution du focus (`SetForegroundWindow`) n'a pas pu être vérifiée sur le bureau réel.
- La couleur source est fixe : choisir une couleur d'accent demanderait une clé de `config.json` (non ajoutée, voir le rapport).
- Gain mesuré par la suppression des ombres : rendu complet de 32 touches 177 → 17 ms hors écran, mémoire privée de l'application 105 → 94 Mo.
- La typographie MD3 (échelle de tailles) n'est pas appliquée : `DesignSystemTests` fige l'échelle actuelle.
