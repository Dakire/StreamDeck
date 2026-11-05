# 0036 — Icônes bornées et nettes, balayage des pages, toucher des pages web par composition

- **Statut** : acceptée. Clé `SwipePagesDisabled` et hébergement par composition validés par l'utilisateur (5 novembre 2026). **Non vérifiée sur un écran tactile réel.**
- **Date** : 5 novembre 2026.
- Remplace la piste B de l'ADR 0032 (cadre désactivé par `EnableWindow`) pour la réception du toucher ; le reste de l'ADR 0032 (bouclier, liseré, fins d'interaction) est inchangé.

## 1. Icônes des applications et des sites

### Cause constatée

- `TileFace` peignait l'image d'un bouton sur **toute la touche** (`ImageBrush`, `Uniform` ou `UniformToFill`), et le titre était superposé en bas : l'icône
  passait derrière le titre.
- L'icône d'un programme venait de `Icon.ExtractAssociatedIcon`, qui ne rend que **32 × 32 px**. Étirée sur une touche de 150 à 300 px physiques, elle était floue.
  Un favicon de 16 ou 32 px aussi. Les grandes images étaient décodées entières (jusqu'à 1024 px) et réduites au rendu.

### Décision

- **Zone d'icône** (`Core/IconLayout`, logique pure) : un carré de 55 % de la hauteur utile avec titre (62 % sans), plafonné à 128 DIP et réduit si le titre mesuré
  (une ou deux lignes) en a besoin. L'ensemble icône + écart + titre est centré ; titre en haut (`top`) : icône dessous, sinon dessus. La zone ne recouvre jamais le titre.
  Concerne une image « entière » (`ImageFit: contain` : icône d'application, favicon, image importée par défaut) et toute image de moins de 128 px.
  « Remplir » (`cover`, ou `ImageFit` absent) garde le plein cadre pour une image d'au moins 128 px. Les icônes Fluent dont le titre est en haut ou en bas sont bornées de la même façon.
- **Netteté** :
  - décodage à la taille d'affichage en pixels physiques, échelle de l'écran du deck comprise (`IconStore.LoadForDisplay`, paliers de 32 à 512 px, jamais agrandie au décodage) ;
  - réduction lissée (`BitmapScalingMode.HighQuality`), `SnapsToDevicePixels` et `UseLayoutRounding` sur l'image ;
  - une image plus petite que sa zone n'est lissée que jusqu'à 1,25 × ; au-delà, elle est montrée **à sa taille ou à un multiple entier** (3 × au plus) sans lissage (`NearestNeighbor`) ;
  - icône d'un `.exe` ou d'un `.lnk` : liste d'images système « jumbo » (256 px, `SHGetImageList`, dans `Platform/Windows/NativeIcons`), marges transparentes recadrées
    quand le programme n'a qu'une petite icône ; repli sur l'ancien 32 px. Un `.ico` : sa plus grande image. Favicon : inchangé (icône tactile, puis plus grande taille annoncée).
- **Anciennes icônes** : au démarrage (`IconUpgrade`, hors simulation), une image `contain` de 48 px au plus dont le nom de hachage est **exactement** celui que l'ancien chemin
  produit pour le programme du bouton est remplacée par la version 256 px (réglage, hors historique). Une image importée par l'utilisateur n'est jamais touchée ; l'ancien fichier reste dans `Icons\`.
- **Icône de secours** : image introuvable → icône Fluent du type d'action (`Apps24`, `Globe24`, `Folder24`, `Image24`) ; l'échec est journalisé une fois par `IconStore`.

Vérifié : tests de mise en page sur de vraies `TileFace` à 100, 150 et 200 % (rectangles mesurés, image de 512 px, 16 px et 300 × 80, titre sur deux lignes), capture `tile-icons.png`.

## 2. Balayage pour changer de page

- **Reconnaissance** (`Core/SwipeGesture`, constantes regroupées) : rien n'est décidé sous 24 DIP (c'est encore un appui) ; au-delà, balayage si `|dx| > 1,5 × |dy|`, sinon rejet.
  Au relâchement, la page change si `|dx| ≥ max(80 DIP, 8 % de la largeur de la grille)` et si le geste est vif (≥ 0,35 DIP/ms sur les 100 dernières ms) ou long (≥ 35 % de la largeur).
  Un doigt immobile 450 ms est un appui long : plus de balayage.
- **Priorités** : contrôles de niveau (roue, curseurs) et poignées d'abord ; un balayage reconnu abandonne l'appui (ni action, ni appui long, ni menu, ni soulèvement).
  **Exclus** : mode édition (glisser-déposer, ADR 0035), cadre web (la page garde son défilement), menu ou dialogue ouvert, bouton soulevé, second doigt.
- **Navigation** : commande de l'onglet voisin (`MainViewModel.SwipePage`) : pages masquées sautées, pas de bouclage, depuis un dossier ou une sous-page c'est la page voisine du profil.
- **Animation** : la grille seule suit le doigt (barre et dock fixes), résiste au bout de la liste (30 %, 12 % de la largeur au plus) et revient avec un rebond élastique ; la page suivante entre
  depuis la distance parcourue. Sans animations (`Motion.Enabled`), rien ne bouge pendant le geste.
- **Réglage** : *Balayer pour changer de page* (Paramètres › Toucher), clé `SwipePagesDisabled` (absente = activé).
- **Page web plein cadre** : le balayage ne s'y déclenche pas ; on change de page par les **onglets du dock**, toujours affichés hors du cadre.

## 3. Toucher d'une page web

### Diagnostic

- Hébergement mesuré (vrai WebView2 156, fenêtre hors écran) : `WebView2` WPF est un `HwndHost` dont la fenêtre (`Static`, notre processus) contient `Chrome_WidgetWin_0`,
  puis, **dans le processus du navigateur**, `Chrome_WidgetWin_1` › `Chrome_RenderWidgetHostHWND` et `Intermediate D3D Window`. En interaction, le doigt est donné par Windows à ces fenêtres.
- Le deck n'évite le curseur mobile qu'en retirant ses propres `WM_POINTER` du traitement par défaut (ADR 0013). Il ne peut ni voir ni filtrer ceux d'une fenêtre d'un autre
  processus, ni ceux de l'hôte `HwndHost` (procédure de fenêtre de WPF). Tout `WM_POINTER` qui y finit dans `DefWindowProc` est promu en souris système : le curseur saute sous le doigt.
  La pile WPF (`EnablePointerSupport`) n'y change rien : elle ne concerne que les fenêtres de WPF.
- **Non observé directement** : quelle fenêtre de Chromium laisse passer le message (pas d'écran tactile ici, et piloter le vrai bureau est exclu). La cause est donc structurelle : tant
  que la page est une fenêtre enfant, le deck ne maîtrise pas le toucher qui l'atteint.

### Décision

- La page est hébergée par **composition** (`WebView2CompositionControl`, sous-classe `Controls/DeckWebView`) : aucune fenêtre Win32 sous la page (mesuré : `WindowFromPoint` au centre
  de la page = fenêtre du deck, au repos comme en interaction).
- Le doigt arrive donc au deck, qui avale le `WM_POINTER` (ADR 0013, inchangé) et, sur une page **en interaction**, le transmet à la page par `SendPointerInput` avec les données de
  Windows (identifiant, trame, drapeaux, horodatage, zone de contact, pression, surfaces du périphérique). Chaque doigt appartient au cadre jusqu'au relâchement ; un toucher
  annulé est transmis comme tel. Défilement, appui, pincement et appui long sont ceux de Chromium.
- Le chemin tactile propre du contrôle (événements WPF) est coupé pour ne pas doubler les contacts ; une souris fabriquée par WPF à partir du doigt est ignorée. La vraie souris
  garde le chemin du contrôle (survol, curseur de la page). Aucun `SetCursorPos`, aucun `SendInput` souris, aucun clic synthétique.
- Au repos, le bouclier transparent de l'ADR 0032 reçoit le premier toucher (le cadre est `IsEnabled = false`) ; `EnableWindow` n'est plus utilisé.
- Inchangé : profil isolé, aucun pont page → application, droits caméra / micro / notifications limités au même site en https (ADR 0018, 0029), règles de saisie au clavier (ADR 0018),
  `WS_EX_NOACTIVATE` et `WM_MOUSEACTIVATE`.

**Curseur système : non comparé par un test automatique.** Mesuré sur la machine de développement, il bouge de lui-même (254 déplacements en 25 s sans aucun test lancé : souris de
l'utilisateur), si bien qu'une comparaison avant / après donne un résultat aléatoire. Garde-fou statique à la place (`ArchitectureTests.NoSourceMovesTheSystemCursorOrInjectsAMouse`, vérifié en
échec sur un appel interdit) : aucune source n'appelle `SetCursorPos`, `mouse_event` ni n'injecte de souris. Que le doigt ne déplace plus le curseur **n'est pas démontré ici** ; c'est la première
vérification à faire sur matériel.

Vérifié hors écran (vrai WebView2, contacts simulés comme le deck les lit) : défilement de plus de 100 px, `click` de type `touch`, aucun pointeur de type souris, `contextmenu`
sur appui long, zoom par pincement à deux doigts, aucun contact transmis au repos, aucun changement de page sur un cadre web. Les événements souris *de compatibilité* qui
suivent un appui (`mousedown`, `mouseup`, `click`) viennent de la page elle-même, comme dans Chrome.

### Conséquences et risques

- Rendu par capture (Windows Graphics Capture) : un peu plus de processeur et de mémoire par page affichée ; contenu protégé (DRM) possiblement noir. **Non mesurés.**
- Le menu contextuel natif du navigateur reste coupé (ADR 0017) : un appui long donne à la page son événement `contextmenu` (menus propres aux sites) mais pas le menu d'Edge.
- Dépendance à un membre non public du contrôle (`SendPointerInput`) : s'il disparaît, le contrôle reprend son propre chemin tactile (avertissement journalisé si un envoi échoue).
  À revérifier à chaque mise à jour du paquet `Microsoft.Web.WebView2`.
- Horodatage : avec des horodatages incohérents (numéro de trame à la place de l'heure), la page n'a pas défilé dans les tests ; le deck transmet le `dwTime` de Windows. Un test de défilement
  a aussi échoué tant que la page n'avait pas rendu quelques images : il les attend maintenant (cause plausible, non isolée).

### À vérifier sur un écran tactile réel

Défilement au doigt et inertie, toucher d'un lien, pincement, appui long, saisie de texte (clavier tactile et physique), curseur immobile pendant tout cela, envoi d'un raccourci au
Bloc-notes après avoir touché la page (la fenêtre ne doit pas prendre le focus), souris physique sur la page (survol, curseur), vidéo plein écran et contenu protégé.

## Non retenu

- **Retour au dossier parent par geste** (balayage vers le bas ou depuis le bord gauche) : le bord gauche de l'écran ouvre le panneau des widgets de Windows 11, et un
  balayage vertical entre en conflit avec les widgets qui défilent (agenda, RSS). Le bouton *Retour* et la case « retour » restent le chemin.
- **Points d'indicateur de pages** sous la grille : les onglets du dock montrent déjà les pages (48 px, toujours touchables) et les points de sous-pages existent ; un troisième
  indicateur doublerait l'information.
- **Favicons récupérés automatiquement** : la récupération reste à la demande de l'utilisateur (*Icône du site*), aucun nouveau comportement réseau.
