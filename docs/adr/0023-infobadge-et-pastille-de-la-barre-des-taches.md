# 0023 — Badge d'onglet en InfoBadge, total de non lus et pastille de la barre des tâches

- **Statut** : acceptée (complète les ADR 0020 et 0022).
- **Date** : 27 octobre 2026.

## Contexte

Le badge des onglets de pages web était un `Border` rouge dessiné à la main. Il manquait un total de non lus visible quand le deck est réduit, et des réglages par page
accessibles sans toucher à `config.json`.

## Décision

- **Onglet** : `ui:InfoBadge` de WPF-UI 4.3 (`Severity="Critical"`, couleurs du thème, `Value` = nombre ou « 99+ », `Value` nul = simple point d'activité). Nom accessible
  « N notification(s) non lue(s) » (`NotificationBadge.Describe`, pluriel français).
- **Total** : `MainViewModel.TotalUnread`, calculé sur le thread d'interface pour **toutes** les pages web de **tous** les profils (`NotificationBadge.Total`), recalculé quand une valeur
  d'intégration ou le document change. Une page est ignorée si elle est exclue, sans badge, ou si le connecteur est éteint ; un service suivi par plusieurs pages n'est compté qu'une fois
  (même compteur) ; une activité sans nombre ne compte pas.
- **Barre des tâches** : `Services/TaskbarBadgeService` (application, pas Core) dessine une icône 16 DIP par `RenderTargetBitmap` (couleur `DeckTileNotify`, texte blanc, densité de l'écran)
  et la pose dans `Window.TaskbarItemInfo.Overlay` de la fenêtre du deck ; retirée à zéro ; suit le thème et les changements de DPI ; se détache à la fermeture de la fenêtre.
  `MainWindow` n'ajoute qu'un appel `Attach`.
- **Réglages par page** : deux entrées à coche du menu de la page en mode édition (*Afficher le badge*, *Exclure du total*), via `SetPageBadgeEnabled` / `SetPageBadgeExcluded` (annulables).
  Aucune nouvelle clé de `config.json` (celles de l'ADR 0022). Les pages n'ont pas de panneau de propriétés : le menu de la page est leur lieu de réglage.

## Conséquences

- **Non vérifié en réel** : le rendu de l'overlay avec la `FluentWindow` (WS_EX_NOACTIVATE, cadre WPF-UI) et le bouton de barre des tâches ; les tests hors écran ne créent pas de bouton
  de barre des tâches. `TaskbarItemInfo` n'ajoute ni style ni message sur la fenêtre (il utilise `ITaskbarList3` sur sa poignée) : le risque attendu est un overlay absent, pas une perte de focus.
- Un échec de pose de l'icône est journalisé (avertissement) et ignoré.
