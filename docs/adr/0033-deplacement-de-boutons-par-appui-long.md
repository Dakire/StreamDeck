# 0033 — Déplacer des boutons hors édition par appui long

- **Statut** : acceptée pour le code ; **geste désactivé par défaut** (`MainViewModel.LiftDragEnabled`) tant que le réglage qui le commande n'est pas validé (ADR 0034). **Non vérifié sur un écran tactile réel.**
- **Date** : 5 novembre 2026.

## Contexte

Le glisser-déposer de boutons n'existait qu'en mode édition (ADR 0016) : ghost sous le doigt, case visée surlignée, dépôt sur une case, un dossier, « Retour » ou un onglet.
Hors édition, un appui long sur une case ouvrait l'éditeur (`MainWindow.Touch.cs`). Voulu : déplacer ou échanger des boutons sans entrer en édition, par appui long.

## Décision

1. **Appui long (500 ms par défaut, 300 à 1500 ms)** sur un bouton : la touche se **soulève** (`DeckTileViewModel.IsLifted` : échelle 1,08, ombre, premier plan). Le seuil est porté par
   `TapRecognizer.LongPressThresholdMs` (remis à 550 ms à chaque appui, réglé par le deck seulement pour les candidats au soulèvement).
2. **Appui court = action ; glissé = jamais d'action.** Rien n'est exécuté tant qu'une touche est soulevée ; un appui long consommé n'est déjà plus un appui (`TapRecognizer`).
3. **Relâché sans avoir glissé** : l'éditeur de la case s'ouvre, comme avant (le comportement historique de l'appui long est conservé, seulement différé au relâchement).
4. **Glissé** : réutilise le glissement du mode édition (`BeginDrag`/`UpdateDrag`) ; `DragAllowed` autorise hors édition *uniquement* une touche soulevée. Dépôt :
   case vide (déplacement), bouton (échange), dossier (rangement), onglet de page (déplacement vers la page) — toutes les méthodes annulables de `DeckDocument` (`MoveButton`, `MoveIntoFolder`) ;
   **corbeille** (64 px de haut, `TrashZone`) → `ClearButton`, avec « Annuler » dans la notification.
5. **Annulation propre** : relâcher hors de la grille, Échap (`CancelTouchLift`), perte du doigt (`ResetTouchTracking`) ou menu/dialogue ouvert → `CancelLift`, rien n'est écrit.
6. Le traitement du doigt reste dans `MainWindow.Touch.cs` ; l'état et les règles dans `MainViewModel.Lift.cs` (testés sans fenêtre : `LiftDragTests`).

## Non fait (reste à faire)

- Changement de page ou défilement quand le doigt approche d'un bord (le bouton déplacé doit survivre à la reconstruction de la grille : il faut garder son emplacement d'origine plutôt que la tuile).
- Les autres tuiles qui s'écartent en animation pendant le glissement (le panneau de grille place les tuiles sans animation de décalage).
- Réglages « seuil » et « désactiver » dans les Paramètres : dépendent du schéma de `config.json` (ADR 0034).

## Conséquences

- Tant que le geste est désactivé, aucun comportement ne change.
- Une fois activé, un appui long sur un bouton ne l'édite plus immédiatement : l'éditeur s'ouvre au relâchement s'il n'y a pas eu de glissé.
