# 0032 — Activation des pages web au toucher

- **Statut** : acceptée. **Non vérifiée sur un écran tactile réel** : les tests rendent les fenêtres hors écran et ne peuvent pas poser un doigt sur un cadre WebView2.
- **Date** : 5 novembre 2026.

## Contexte

Une page web (widget ou plein cadre) est un cadre WebView2, c'est-à-dire une **fenêtre Win32 enfant** (ADR 0017, 0025). Cette fenêtre reçoit seule la souris et le doigt
qui la touchent : le deck ne voit rien, la page « attrape » le pointeur et réagit dès le premier contact (défilement, liens, saisie).

Voulu : page **inactive par défaut** ; un premier toucher l'**active** (liseré d'accent) ; active, elle se comporte normalement (défilement au doigt, pincement, liens),
sans déplacer le curseur système ni envoyer de clic synthétique ; elle redevient inactive après un délai sans geste, par un toucher hors de la page, ou au changement de
page ou de dossier ; une sortie reste toujours possible.

## Pistes évaluées

| Piste | Principe | Verdict |
|---|---|---|
| **A. `WebView2CompositionControl` + `SendPointerInput`** | Le cadre est rendu par composition (plus de fenêtre Win32, donc plus d'airspace) ; l'application reçoit les `WM_POINTER` et les transmet à la page avec `CoreWebView2CompositionController.SendPointerInput`. | Rejetée. Contrôle et mode composition moins éprouvés (accessibilité, IME, focus, rendu sur écran à fort DPI) ; il faudrait transcrire soi-même chaque geste (défilement inertiel, pincement, appui long), ce qui reproduit mal le comportement natif de Chromium. Le coût et le risque de régression sont élevés, et le gain (superposer du WPF au cadre) ne sert pas ici. |
| **B. Calque d'activation sur un cadre désactivé** | Au repos, la fenêtre du cadre est désactivée (`EnableWindow(false)`) : Windows attribue le point à la fenêtre du deck ; un élément WPF transparent posé au-dessus capte le premier toucher et active le cadre. | **Retenue** (déjà en place depuis l'ADR 0025, complétée ici). |
| **C. `touch-action` (CSS)** | Limiter les gestes tactiles que la page traite. | Rejetée seule : elle dépend de la page (qu'on ne contrôle pas), n'empêche pas le premier contact d'atteindre la page, et ne permettrait d'injecter du code dans la page que pour une fonction de confort. |

Le « problème d'airspace » de la piste B se contourne parce que le calque n'est pas *au-dessus* du cadre pour le dessin, mais pour la **résolution du pointeur** : le cadre étant
désactivé, la fenêtre du deck (donc les éléments WPF) reçoit le toucher. Une fois actif, le cadre reçoit directement les `WM_POINTER` de Chromium : défilement, pincement et
liens sont natifs, et le deck n'envoie ni clic ni déplacement de curseur.

## Décision

1. Piste B conservée. Mode par défaut `touch` (toucher pour interagir), inchangé pour les configurations existantes.
2. **Liseré d'accent** (3 px) autour de la tuile tant que la page est activée par un toucher (`ShowActiveRim`). Il est dessiné par la bordure de la tuile, hors de la
   fenêtre du cadre, donc jamais recouvert. Pas de liseré en mode interactif permanent.
3. **Fin d'interaction** :
   - délai sans geste (30 s par défaut ; `WebWidgetViewModel.IdleExit`, mesuré par le script en lecture seule déjà en place) ;
   - **toucher hors de la tuile web** : tout toucher reçu par le deck (jamais sur le cadre actif, qui a sa fenêtre) hors de la tuile remet les pages au repos
     (`MainViewModel.EndWebInteractions`, appelé par `MainWindow.Touch.cs`) ;
   - changement de profil, de page, de dossier ou de sous-page (`RebuildCore`) ;
   - **geste de sortie toujours disponible** : la bande de commandes (48 px) est hors de la fenêtre du cadre, donc toujours touchable ; son bouton « Interagir / Terminer »
     termine l'interaction à tout moment. Le bouton clavier et un menu ouvert gardent leur comportement.
4. `WS_EX_NOACTIVATE` et `WM_MOUSEACTIVATE` ne sont pas touchés ; les droits https limités au même site (ADR 0018) et l'absence de pont page → application (ADR 0029) non plus.

## Conséquences

- Le délai d'inactivité est **réglable côté modèle** (`IdleExit`) mais **sans réglage persistant** : l'ajouter dans `config.json` est une modification de schéma, soumise à validation
  (proposition dans le compte rendu de livraison). Tant que ce n'est pas validé, la valeur est 30 s.
- Un toucher sur la bande de commandes de la tuile web ne termine pas l'interaction (elle fait partie de la tuile) ; un toucher partout ailleurs la termine.
- À vérifier sur matériel tactile : le pincement et le défilement inertiel dans le cadre actif, et l'absence de curseur système mobile.
