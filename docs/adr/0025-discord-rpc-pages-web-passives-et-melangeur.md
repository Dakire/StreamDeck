# 0025 — Discord par RPC local, pages web passives, menus sans rechargement, mélangeur audio

- **Statut** : acceptée. Intégration Discord **non vérifiée contre un vrai client Discord** (testée contre un faux client local qui parle le même protocole).
- **Date** : 27 octobre 2026.

## Contexte

Bugs signalés : boutons Discord inopérants quand Discord est en arrière-plan ; gel de plusieurs secondes à la fermeture des paramètres ; pages web qui
« capturent la souris » ; clic droit lent. Demandes : curseurs précis pour les canaux Sonar, widgets utiles, menus contextuels adaptés au toucher.

Causes trouvées (mesures et lectures du code) :

- **Discord** : les boutons envoyaient Ctrl+Maj+M / D, raccourcis *locaux* de Discord (focus requis) et micro de Teams ; `SendInput` sans code de balayage ni
  maintien ; refus de Windows (application élevée, UIPI) seulement journalisé.
- **Paramètres** : `WindowCleanup` parcourait arbre visuel et arbre logique sans mémoire des éléments vus. Un panneau ayant les mêmes enfants dans les deux arbres,
  chaque niveau doublait le parcours : 44,8 millions de visites pour 2 552 éléments, **6,6 s** sur le thread d'interface.
- **Pages web** : le cadre WebView2 est une fenêtre Win32 enfant ; WPF ne propage pas `IsEnabled` à la fenêtre hébergée (vérifié : `IsEnabled` faux, fenêtre
  toujours active). La saisie automatique rendait le deck activable dès qu'un champ de la page avait le focus (champ « autofocus »).
- **Clic droit en édition** : `editor.GoTo` rechargeait tout l'éditeur à chaque menu, **144 à 233 ms**.

## Décision

1. **Discord** : connecteur `Integrations/Discord` sur le RPC local documenté (canal `discord-ipc-N`, trames opcode + longueur + JSON, AUTHORIZE / AUTHENTICATE,
   GET/SET_VOICE_SETTINGS, SELECT_VOICE_CHANNEL, abonnement aux événements). OAuth2 avec l'application **de l'utilisateur** (Discord réserve les droits `rpc.voice.*`
   au propriétaire et aux testeurs ; aucune approbation contournée, aucune API privée). Jeton chiffré (DPAPI) ; nouveau contrat `ISecretRotating` : le jeton
   renouvelé à chaque usage est réenregistré par le gestionnaire sans relancer la connexion, et un réglage ancien ne réutilise jamais un jeton déjà échangé.
   Touches simulées : code de balayage, maintien de 30 ms hors du thread d'interface, exception lisible si Windows refuse. Garde anti-rebond de 150 ms par touche.
   Conflits de raccourcis (`HotkeyConflicts`) signalés dans les propriétés et la validation.
2. **Paramètres** : parcours dédoublonné ; fermeture chronométrée (journal), protégée contre une double exécution ; champs d'intégration enregistrés 600 ms
   après la dernière frappe ou à la fermeture.
3. **Pages web** : trois modes (`WebInputModes` : passif, toucher pour interagir par défaut, interactif), réglage `input` du widget et `WebInput` d'une page.
   Hors interaction, `EnableWindow(false)` sur la fenêtre du cadre (façade `WindowsPlatform.SetHostedWindowInput`, P/Invoke dans `Platform/Windows`) :
   Windows attribue le point sous le pointeur à la fenêtre du deck (vérifié par `WindowFromPoint` sur un vrai WebView2). Fin de l'interaction après 30 s sans
   geste (script en lecture seule qui note l'heure du dernier geste) ; saisie automatique seulement pendant l'interaction ; cadres toujours passifs en édition.
4. **Menus** : en édition, une case de la grille affichée est seulement sélectionnée ; menu du mode normal selon le contenu (dossier, widget, bouton) avec
   *Remplacer par* (un pas d'annulation) ; bouton **Case** du dock pour ouvrir le menu d'un toucher.
5. **Audio** : widget **Mélangeur audio** (un `LevelWidgetViewModel` par ligne : mêmes relevés, même envoi limité) ; `ILevelSource` dit ce qu'il règle
   (`Controls`), son icône et son niveau de réinitialisation ; sources Discord.
6. **Widgets** (`IWidget`) : batterie, durée de fonctionnement, périphériques audio actifs, lecture en cours, flux RSS/Atom (XML sans DTD ni résolveur).
   Chronomètre = minuteur de durée 0. Verrouillage par `LockWorkStation`. Scripts observés 3 s : code de sortie et dernière ligne d'erreur affichés.

## Conséquences

- `config.json` : clés **facultatives** ajoutées (`WebInput` d'une page ; `input`, `sources`, `reset` dans `WidgetOptions`), non écrites à leur valeur par défaut ;
  `Version` inchangée, aucune migration, anciens fichiers lus tels quels. Les anciens boutons Discord gardent leur combinaison (signalée en avertissement).
- Comportement changé par défaut : un cadre web ne capte plus la souris tant qu'on ne l'a pas touché (mode « toucher pour interagir »).
- Un script attend jusqu'à 3 s avant de rendre la main (le deck reste fluide : exécution hors du thread d'interface).
- Risques : RPC Discord en accès restreint, susceptible d'évoluer ; API Sonar toujours non vérifiée (ADR 0024).
