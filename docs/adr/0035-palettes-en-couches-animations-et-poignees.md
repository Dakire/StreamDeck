# 0035 — Palettes en couches, animations d'ouverture, changement de page au bord, poignées visibles

- **Statut** : acceptée. **Non vérifiée sur un écran tactile réel.**
- **Date** : 5 novembre 2026.

## Décisions

### Palettes (`Theme.cs`)
Les tons Material 3 sont presque confondus en clair (marches de 1,05 : 1) et en AMOLED (1,04 : 1). `Theme.Layered` recalcule la hiérarchie des surfaces à partir du fond
(fond → panneau → surface → surface haute) avec des marches régulières **≥ 1,09 : 1** dans les trois thèmes (les tests exigent 1,06 au minimum, et 1,25 entre le fond et la surface haute) ; les touches se détachent du fond d'au moins 1,10 : 1.
En clair, le fond baisse d'un cran sous le blanc (les touches blanches s'en détachent) et le rouge d'erreur s'assombrit pour garder 4,5 : 1 sur les surfaces plus marquées.
`Palette` gagne trois champs **facultatifs** : `Hover` et `Pressed` (voiles de la couleur du texte : clair sur sombre, sombre sur clair) et `SuccessContainer`, publiés en `DeckHover`, `DeckPressed`, `DeckSuccessContainer`.
Le thème TIXIA les définit à la main. Les palettes personnalisées (ADR 0034) héritent de ces couches : changer le fond ou la surface les recalcule.
Seuil d'un ancien test relevé en connaissance de cause : la luminance du fond clair doit rester > 0,85 (et non > 0,9).

### Changement de page au bord pendant un déplacement (ADR 0033)
Un bouton soulevé tenu à moins de 72 DIP d'un bord de la fenêtre change de page après 500 ms, puis une page toutes les 900 ms (`MainWindow.Touch.cs`, `MainViewModel.FlipPageWhileLifting`).
Les pages **masquées** et les **pages web** sont sautées (rien ne s'y dépose). La tuile d'origine disparaît avec la page quittée : `MainViewModel` mémorise la **case d'origine** (`_liftSource`) au soulèvement ;
le dépôt, la corbeille et le dépôt sur un onglet partent de cette case. Un seul pas d'annulation, comme avant.

### Animations (respectent `Motion.Enabled`, donc le réglage d'animations et le mode économie)
- Ouverture d'un **menu** : voile en fondu (140 ms), feuille qui monte de 18 px et grossit de 94 % à 100 % (180 ms, depuis son coin).
- Ouverture d'une **boîte de dialogue** : voile en fondu, carte de 94 % à 100 %.
- **Corbeille** : monte de 48 px avec un léger rebond.
- Déjà en place et inchangées : appui sur une touche (50 ms), transitions de page et de dossier, fantôme du glissé.

**Règle** : une animation est purement visuelle. L'élément animé est déjà affiché, touchable et actif dès la première image ; aucune commande n'attend la fin d'une animation.

### Barre du haut et poignées de panneaux
- La barre du haut se présente comme la poignée de déplacement : curseur de déplacement (`SizeAll`), filet sous la barre, capsule centrale de 120 × 32 px avec six points qui s'éclaire au survol.
- Les poignées du catalogue (colonne de droite) et de l'inspecteur (panneau du bas) montrent un **rail sur toute l'arête** qui s'allume au survol et pendant le glissé, en plus du repère central.
  Leur zone de saisie déborde de 8 à 14 px sur le panneau (cibles de plus de 48 px) et passe au-dessus : on redimensionne depuis n'importe quel point de l'arête. Un simple toucher replie ou déploie, comme avant.

## Conséquences
- `config.json` : aucune clé ajoutée.
- Les couleurs de survol et d'appui des touches du deck restent celles de `App.xaml` (voile blanc 8 % / 12 %) ; `DeckHover` et `DeckPressed` sont disponibles pour les prochains composants.
