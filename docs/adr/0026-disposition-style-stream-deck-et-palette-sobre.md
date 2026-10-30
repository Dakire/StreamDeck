# 0026 — Disposition du mode édition « style Stream Deck », palette sobre, curseur vertical

- **Statut** : acceptée. Remplace la disposition de l'ADR 0015 et 0016 (catalogue à gauche, propriétés à droite) ; le reste de ces ADR est inchangé.
- **Date** : 30 octobre 2026.

## Contexte

Demande de l'utilisateur : une interface épurée proche du Stream Deck d'Elgato (grille centrée, catalogue d'actions à droite avec recherche et catégories repliables,
inspecteur de la touche en bas, profil en haut à gauche, pagination discrète sous la grille), un seul accent bleu, et des curseurs verticaux 1 × 2 pour les canaux Sonar.
Plusieurs éléments existaient déjà (recherche et sections repliables du catalogue, jetons `Themes/Tokens.xaml`, widget `level`, spans de widgets).

## Décision

- **Disposition** : en mode édition, le catalogue d'actions occupe une colonne à droite sur toute la hauteur (22 % de la largeur, entre 300 et 420 px) et l'inspecteur de la case
  se place sous la grille (40 % de la hauteur, entre 320 et 460 px). `MainViewModel.CatalogWidth` et `InspectorHeight` remplacent `LeftPanelWidth` et `RightPanelWidth`.
  La grille garde son nombre de cases et rétrécit pour laisser la place. L'inspecteur est large : l'aperçu et le titre sont à gauche, ce que fait le bouton (actions, widget, dossier,
  apparence repliée) est à droite.
- **Palette** : la couleur source du thème (`Theme.Seed`) passe du violet `#6C5CE7` au bleu `#2F7DF6`. Un seul accent bleu ; les surfaces et les textes restent des neutres presque gris
  (chroma 6), le succès, l'erreur et la simulation gardent leur couleur sémantique. Les contrastes des trois palettes (sombre, AMOLED, claire) restent vérifiés par les tests.
- **Aucun changement du format de `config.json`** : les clés de largeur et de hauteur de widget restent `ColSpan` et `RowSpan` (défaut 1 × 1, absentes du fichier tant qu'elles valent le défaut) ;
  elles n'ont pas été renommées en `ColumnSpan`, ce qui aurait exigé une migration.
- **Mélangeur** : `IAudioMixerProvider` (`Core/Audio/MixerProvider.cs`) décrit un mélangeur à canaux en pourcentages de 0 à 100 ; `SonarMixerProvider` en est l'implémentation, `MixerLevelSource`
  l'adapte à `ILevelSource`. Wave Link ou Voicemeeter n'ont qu'à implémenter cette interface.
- **Curseur vertical** : composant `Controls/VerticalSlider` (icône du canal qui coupe au toucher, niveau en %, nom), grisé et désactivé quand la source est indisponible.

## Conséquences

- Les tests de mode édition mesurent la position du catalogue (à droite) et de l'inspecteur (sous la grille).
- L'inspecteur partage la largeur de la grille : à très petit écran, ses deux colonnes se serrent (la colonne de droite a une largeur minimale de 320 px et le panneau défile).
