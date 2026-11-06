# 0037 — Toucher direct des pages web, focus clavier et pointeur verrouillé

- **Statut** : acceptée. Changement de la valeur par défaut de `WebInput` / `input` validé par l'utilisateur (6 novembre 2026).
  **Non vérifiée sur un écran tactile réel** : les tests rendent les fenêtres hors écran.
- **Date** : 6 novembre 2026.
- Remplace la décision 1 de l'ADR 0032 (mode par défaut `touch`). Le reste de l'ADR 0032 (bouclier, liseré, fins d'interaction) s'applique toujours, mais seulement au mode
  `touch`, désormais explicite. L'hébergement par composition et la transmission du doigt (ADR 0036 §3) sont inchangés.

## Contexte

Retour de l'utilisateur sur un écran tactile réel :

1. sur une page web, le premier toucher ne fait qu'activer la page (bouclier de l'ADR 0032) ; il faut toucher une seconde fois pour agir ;
2. le curseur de la souris semble « capturé » ou déplacé quand on utilise la page, alors que l'ADR 0036 annonçait le contraire sans l'avoir vérifié sur matériel.

## Diagnostic

### Premier toucher

Établi par la lecture du code. `WebInputModes.Default` valait `touch`. Au repos, le cadre est `IsEnabled = false` (lié à `IsInteracting`), donc `WebFrameAt` ne le trouve pas.
Le doigt passe par le test d'appui du deck, qui active le bouclier transparent (`InteractCommand`). Seuls les contacts suivants vont à la page. En mode `interactive`,
le premier toucher était déjà transmis tel quel.

### Curseur et focus

| Piste | Constat | Statut |
|---|---|---|
| `DefWindowProc` promeut le `WM_POINTER` du doigt en souris système | Le deck retire ses `WM_POINTER` tactiles du traitement par défaut (`MainWindow.WndProc`). Depuis l'ADR 0036, aucune fenêtre Win32 n'est sous la page (mesuré : `WindowFromPoint`). | Écartée par le code. Que Windows ne fabrique pas de souris malgré tout n'est **pas mesuré** (voir Diagnostic ci-dessous). |
| Promotion du toucher en souris par WPF | Les souris WPF portant un `StylusDevice` sont absorbées par `DeckWebView` ; elles ne déplacent pas le curseur système (aucun appel Win32). | Écartée. |
| `CaptureMouse`, `SetCapture`, `ClipCursor`, `SetCursorPos` | Aucun dans nos sources (`ArchitectureTests`). **Aucun non plus dans `Microsoft.Web.WebView2.Wpf`** : vérifié en lisant l'IL de la bibliothèque. | Écartée pour le deck et le contrôle. |
| `SendPointerInput` côté composition | Le deck n'envoie que des pointeurs `PT_TOUCH`. Les tests vérifient qu'aucun événement `:mouse` n'atteint la page. | Écartée. |
| **Focus clavier sur clic de la vraie souris** | Le `OnMouseDown` de base de `WebView2CompositionControl` appelle `UIElement.Focus()`, puis `OnGotKeyboardFocus` et `CoreWebView2Controller.MoveFocus` (lu dans l'IL). Dans une fenêtre `WS_EX_NOACTIVATE`, le `SetFocus` de WPF peut activer la fenêtre. Le clic étant la dernière entrée, Windows l'autorise. | **Cause établie** pour le vol du clavier ; son effet sur matériel n'est pas mesuré. |
| **Pointer Lock API** (jeux, visionneuses) | WebView2 ne propose pas ce droit dans `PermissionRequested`. Le deck ne prend jamais le clavier, donc Échap, le geste de sortie du navigateur, n'atteint jamais la page : le pointeur reste verrouillé. | Cause **plausible** de la « souris attrapée », non observée ici. |
| **Bouton relâché hors du cadre** (ajout du 6 novembre, retour « Google Sheets attrape toujours la souris ») | `WebView2CompositionControl` envoie le bouton enfoncé à Chromium mais ne capture pas la souris : il ne gère ni `LostMouseCapture` ni la sortie du pointeur (IL). En mode composition, cette capture revient à l'hôte. Un relâchement hors du cadre n'arrive jamais : la page croit le bouton enfoncé et la sélection de Sheets suit la souris. | **Cause établie** par le code ; correspondance avec le symptôme sur matériel à confirmer. |
| Curseur de la page (`CursorChanged` → `Cursor`) | Le contrôle applique la forme demandée par la page. `cursor: none` masque le curseur au-dessus du cadre, mais ne le déplace pas. | Explique un curseur invisible, pas un curseur qui bouge. |
| `WM_MOUSEACTIVATE`, `WM_SETCURSOR`, script d'inactivité | Respectivement : renvoie `MA_NOACTIVATE` ; non géré par le deck ; lecture seule. | Écartées. |
| Saisie automatique | En mode permanent, un champ `autofocus` de la page lançait la saisie : fenêtre activable et focus pris. | **Établie** (devenue critique avec le toucher direct par défaut). |

**Doigt qui déplace le curseur : pas de cause trouvée dans le code.** Ce n'est pas une preuve qu'il ne bouge pas. Le diagnostic `STREAMDAK_TOUCHLOG=1` écrit maintenant, à
chaque appui et relâchement, la position du doigt à côté de celle du curseur, ainsi que les déplacements de souris que Windows fabrique depuis le doigt (signature
`0xFF515700`). Si ces lignes apparaissent, la promotion a lieu.

## Décision

1. **Toucher direct par défaut** : clé `WebInput` (page) ou `input` (widget) absente = `interactive`. La page reçoit le premier contact.
   - `touch` (*Toucher pour activer*) reste une option, désormais **écrite** dans `config.json`. Un `touch` déjà écrit à la main est conservé.
   - Les configurations sans clé (tous les utilisateurs, puisque `touch` n'était pas écrit) passent au toucher direct.
   - Aucune clé ajoutée, renommée ni supprimée, et aucune migration.
2. Garde-fous conservés, sans notion de « mode activé » :
   - le balayage de page ne part jamais d'un cadre web (`IsInsideWebTile`), et un doigt posé sur la page lui appartient jusqu'au relâchement ;
   - le dock et la bande de commandes du cadre (48 px, hors du cadre) restent touchables ;
   - mode édition, menu ou dialogue ouverts : rien n'est transmis, et le cadre est masqué ou passif.
3. **Focus clavier** : `DeckWebView` n'est focusable que pendant la saisie au clavier (`AllowKeyboard`, lié à `IsTyping`). Le clic de souris ne prend plus le clavier.
   Toute prise de focus du cadre est journalisée, avec l'état d'activation de la fenêtre.
4. **Saisie automatique** : elle ne démarre que si le champ a reçu le focus moins de 5 s après un geste dans la page. Le chargement de la page ne compte plus comme un geste.
5. **Pointer Lock** : le relevé en lecture seule (toutes les 400 ms) indique aussi `document.pointerLockElement`. Un verrou vu est relâché (`exitPointerLock`, script de
   l'application) et journalisé. Une page qui en dépend (jeu) ne peut plus verrouiller le pointeur dans le deck : c'est voulu.
6. **Bouton de la vraie souris** : `DeckWebView` capture la souris (capture WPF ordinaire) du premier bouton enfoncé au dernier relâché. Si la capture est perdue avant
   (autre fenêtre, menu du deck, cadre masqué ou désactivé), la page reçoit le relâchement de chaque bouton encore enfoncé, par le chemin du contrôle lui-même.
   Vérifié hors écran : capture prise, rendue au relâchement, et `mouseup` reçu par la page après une capture perdue.
7. Inchangés :
   - `WS_EX_NOACTIVATE` et `WM_MOUSEACTIVATE` ;
   - aucun `SetCursorPos`, aucune souris injectée (`ArchitectureTests`) ;
   - profil isolé, aucun pont page → application (ADR 0029) ;
   - droits caméra / micro / notifications limités au même site en https (ADR 0018).

## Conséquences

- Le délai de Paramètres › Toucher (`WebIdleExitSeconds`) ne concerne plus que les pages en mode *Toucher pour activer* ; libellé et infobulle le disent.
- La bande d'un cadre en toucher direct n'affiche plus le bouton *Interagir / Terminer*.
- Un doigt posé sur une page web pour faire défiler le deck fait défiler la page : c'est le compromis accepté. Le mode *Toucher pour activer* existe pour l'éviter.

## Vérifié hors écran

- Avec un vrai WebView2 et des contacts simulés comme le deck les lit : une page sans réglage reçoit le **premier** toucher ; un balayage de 400 px sur le cadre ne change pas de page ;
  menu ouvert, rien n'est transmis.
- Le cadre refuse `Focus()` hors saisie et l'accepte pendant la saisie.
- Un focus donné par script sans geste ne lance pas la saisie ; un focus qui suit un `pointerdown` la lance.
- Lecture du verrou dans le relevé.

## Non vérifié sur matériel tactile (à faire)

- Toucher immédiat, défilement inertiel, pincement, lien, appui long.
- Saisie au clavier (tactile et physique).
- Curseur immobile pendant tout cela (comparer `doigt=` et `curseur=` dans le journal `STREAMDAK_TOUCHLOG=1`).
- Souris physique sur la page (survol, clic, molette), y compris sans focus clavier : un site qui exige le focus du navigateur pour réagir au clic n'a pas été observé.
- Raccourci envoyé au Bloc-notes après avoir touché ou cliqué la page.
- Relâchement réel d'un verrouillage du pointeur.
