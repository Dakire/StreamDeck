# 0016 — Plus de fenêtre d'édition, dialogues dans le deck, action du toucher hors du message

- **Statut** : acceptée.
- **Date** : 22 octobre 2026.

## Contexte

Le deck est une fenêtre sans focus (`WS_EX_NOACTIVATE`), au premier plan, parfois sur un écran secondaire. Les fenêtres secondaires
(éditeur complet, panneau de création rapide, boîtes `PromptWindow` modales) s'ouvraient séparément : derrière le deck ou sur un autre
écran, elles laissaient un deck désactivé qui semblait figé. Par ailleurs, une commande exécutée pendant le traitement de
`WM_POINTERUP` (ADR 0013) pouvait ouvrir une boucle modale à l'intérieur de ce message. Enfin, un appui long ouvrait l'édition avant
le relâchement du doigt, qui restait « suivi » et faisait ignorer tous les appuis suivants.

## Décision

- Toute l'édition se fait dans le deck (ADR 0015) : *Nouveau bouton…*, *Modifier…*, l'icône de la zone de notification et `--edit`
  entrent dans le mode édition. `EditorWindow`, `QuickCreateWindow` et `QuickCreateViewModel` sont supprimés.
- Les fonctions qui n'existaient que dans la fenêtre sont portées dans le deck : options de page (bouton *Page* du dock), options
  de profil et format de la grille (pastille du profil), menu complet d'une case en édition (`EditorViewModel.BuildSlotMenu`).
- Les dialogues d'édition (nom, confirmation, information) sont des superpositions du deck (`DeckDialogViewModel`,
  `MainViewModel.AskAsync` / `ConfirmAsync` / `InfoAsync`). Seuls les sélecteurs de fichiers de Windows et les paramètres restent des fenêtres.
- Dans `MainWindow.Touch.cs`, l'action d'un appui bref est postée sur le Dispatcher après le message pointeur.
- `SetEditing` oublie le doigt suivi, et un doigt suivi qui ne touche plus l'écran est écarté au prochain appui.
- `UiWatchdog` journalise les blocages de l'interface de plus de 3 s.

## Conséquences

- Plus de fenêtre perdue derrière le deck ; l'édition au doigt reste dans la fenêtre activable pendant l'édition seulement.
- Perte volontaire : le glisser-déposer de cases entre elles dans une grille d'édition séparée (le déplacement se fait par le menu
  de la case : à gauche, à droite, vers une page, vers un dossier).
- Le chien de garde ne trouve pas la cause d'un blocage, il en laisse la trace (durée, instant) dans `touchdeck.log`.
