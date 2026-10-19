# 0002 — Un seul document, modifié par des opérations annulables

- **Statut** : acceptée.
- **Date** : 2026.

## Contexte

Le deck, l'éditeur et les paramètres affichent et modifient la même configuration, parfois en même temps. Avec une copie
par fenêtre, les fenêtres se désynchroniseraient, et une annulation ne pourrait pas défaire une modification faite dans
une autre fenêtre.

## Décision

- **Un document partagé.** Un seul `DeckDocument` (`Core/DeckDocument.cs`) porte la configuration, l'historique
  (100 niveaux) et le presse-papiers interne. Les trois fenêtres le partagent via `AppServices.Document`.
- **Contenu.** Toute modification de contenu (boutons, pages, profils) passe par une méthode du document. Elle est
  annulable et renvoie un `OpResult`.
- **Réglages.** Les réglages (thème, écran, favoris) passent par `UpdateSettings` et restent hors de l'historique.
- **Notification.** Le document publie `Changed` avec la nature du changement (`ChangeKind`) ; chaque vue décide de ce
  qu'elle redessine.

## Conséquences

- Annuler et rétablir fonctionnent partout. La sauvegarde automatique s'abonne au même événement.
- Le document ne référence pas WPF : il est entièrement testé (`DeckDocumentTests`).
- `AppServices` reste un point d'accès statique au document. Le remplacer par une injection de dépendances est l'objet
  de la phase 2.
