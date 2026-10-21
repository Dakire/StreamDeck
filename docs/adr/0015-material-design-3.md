# 0015 — Thème Material Design 3

- **Statut** : acceptée.
- **Date** : 21 octobre 2026.

## Contexte

Demande : rendre l'application plus moderne en s'inspirant de Material Design 3 (MD3).

## Décision

- **Palette dynamique.** Les trois thèmes (sombre, AMOLED, clair) sont calculés à partir d'une couleur source (`Theme.Seed`, #6C5CE7) par des
  palettes tonales (`MaterialColor`, `MaterialScheme` dans le Core, sans WPF) : accent « primary » et sa couleur de texte (`DeckOnAccent`),
  conteneurs primaire et secondaire, surfaces tonales, erreur, contours. La clarté est le L* de CIELAB (HCT utilise CAM16 : écart de
  teinte de quelques degrés, sans effet sur les contrastes). Un écart de ton garantit les contrastes, vérifiés par test pour chaque rôle
  et chaque surface.
- **Élévation tonale.** Plus d'ombre portée sur les touches, la pastille d'icône, le dock, le bandeau et le menu : l'élévation se lit par
  le ton. États MD3 (survol 8 %, appui 12 %), touches plus arrondies (16 à 30 px), onglet actif en conteneur secondaire (style barre de
  navigation).

## Essai écarté

Un mode édition intégré au deck (panneau d'actions à gauche, propriétés à droite) a été réalisé puis retiré à la demande de l'utilisateur :
il imposait de rendre la fenêtre du deck activable pendant l'édition, ce qui suspendait `WS_EX_NOACTIVATE`. La règle du focus est donc
restée sans exception. Le panneau de propriétés reste un contrôle réutilisable (`PropertiesPanel`) de l'éditeur.

## Conséquences

- Gain mesuré par la suppression des ombres : rendu complet de 32 touches 177 → 17 ms hors écran, mémoire privée de l'application 105 → 94 Mo.
- La couleur source est fixe : choisir une couleur d'accent demanderait une clé de `config.json` (non ajoutée).
- La typographie MD3 (échelle de tailles) n'est pas appliquée : `DesignSystemTests` fige l'échelle actuelle.
