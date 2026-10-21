# 0014 — Création rapide d'un bouton depuis le deck

- **Statut** : acceptée.
- **Date** : 21 octobre 2026.

## Contexte

Un clic droit ou un appui long sur une case du deck ouvrait l'éditeur (commit d892c27) : trop lourd pour créer un bouton.
Trois contraintes pèsent sur la solution :

- le deck ne prend jamais le focus (`WS_EX_NOACTIVATE`), donc il ne reçoit aucune saisie clavier ;
- le toucher n'est traité que dans la fenêtre du deck (ADR 0013) : un `ContextMenu` WPF est une fenêtre popup séparée,
  où le doigt redevient souris système et déplace le curseur ;
- toute modification passe par `DeckDocument` (ADR 0002) : une création doit rester **un seul pas d'annulation**.

## Décision

- **Menu des cases décrit en `MenuItemSpec`**, comme les menus de l'éditeur, mais **affiché dans la feuille du deck**
  (surcouche de `MainWindow`, lignes de 48 px, défilement tactile, sous-menus par navigation « Retour »).
  `ContextMenuFactory` reste utilisé par l'éditeur uniquement. Le geste (clic droit, appui long) ouvre ce menu :
  `IEditableTile.Edit` signifie désormais « ouvrir le menu de la case ».
- **Modèles en un clic** (`QuickCreate.Templates`) : des entrées du catalogue d'actions, pas une seconde liste. Un modèle qui demande un
  réglage (raccourci, adresse) est posé par l'éditeur rapide (`IAppWindows.OpenEditor(..., addCatalogId)`), où *Annuler* rend la case vide.
- **Panneau de création rapide** (`QuickCreateWindow`) : une petite fenêtre activable, propriétaire du deck, ouverte à la demande,
  pour la seule saisie de texte. Le deck lui-même reste sans focus. `QuickCreateViewModel` tient un **brouillon** ; `DeckDocument.AddButton`
  est appelé **une fois**, à *Créer*. Aucune écriture à la frappe.
- **Une écriture, un pas d'annulation** : `AddButton`, `AddButtonFirstFree` et `AddButtons` (lot) passent par `DeckDocument.Edit`. Le
  glisser-déposer de plusieurs fichiers est donc annulé en une fois.
- **Glisser-déposer** : `DroppedItem` (Core, pur) transforme un chemin ou une adresse en bouton ; `DropPayload` (application) lit l'objet
  OLE et ajoute l'icône de l'application. Seule une case vide accepte le dépôt.
- **Annulation au toucher** : le bandeau d'état du deck accepte un bouton (*Annuler*, *Modifier*) ; les confirmations passent par `Notifier`
  comme dans l'éditeur.
- **Aucune clé de `config.json`** : l'indice de découverte est mémorisé pour la session seulement.

## Conséquences

- Le geste « clic droit / appui long » sur un bouton ouvre un menu au lieu de l'éditeur : un toucher de plus pour modifier
  (*Modifier…*), en échange de *Dupliquer*, *Copier*, *Couper* et *Supprimer* au même endroit.
- La touche Menu n'ouvre rien : le deck n'a jamais le focus clavier.
- Le panneau rapide a besoin d'un clavier (physique ou tactile de Windows) pour la recherche ; les modèles et *Parcourir les actions*
  fonctionnent sans clavier. Rendre le focus à l'application précédente à la fermeture repose sur le comportement de Windows pour les
  fenêtres `WS_EX_NOACTIVATE` (la fermeture d'une fenêtre ne met pas le deck au premier plan) ; il n'a pas pu être vérifié sur le bureau réel.
- « Page pleine » : le document n'a pas de plafond (sous-pages automatiques). Une création sur une grille pleine réussit, dans la sous-page suivante.
