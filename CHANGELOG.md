# Journal des modifications

Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les dates sont au format AAAA-MM-JJ.

## [Non publié]

## [0.3.4-rc] — 2026-10-30

### Corrigé
- **Sonar : « a répondu 404 à /subApps »** : le certificat HTTPS auto-signé de GG était toujours refusé (le rappel de validation de `SocketsHttpHandler` reçoit le `SslStream`, pas la requête). La connexion retombait sur une adresse HTTP qui répond 404. Test avec un vrai serveur TLS local.

## [0.3.3-rc] — 2026-10-30

La 0.3.2-rc n'a pas été publiée (un test Discord de timing a échoué en CI) : cette version reprend son contenu. Discord : les canaux locaux inexistants sont écartés tout de suite et un canal existant a 1,5 s pour répondre (150 ms avant).

### Modifié
- **Curseurs Sonar prêts à poser** : la catégorie *Sonar* du catalogue propose en tête un **curseur vertical tactile 1 × 2 par canal** (Principal, Jeu, Discussion, Média, Aux, Micro) et un **Mélangeur Sonar**, au lieu des seules actions « +X / -X ».
- **Inspecteur** : une étape n'affiche plus que son type choisi (bouton *Changer* pour déplier les autres), pour laisser la place aux réglages.

## [0.3.1-rc.1] — 2026-10-30

Première release candidate. Elle reprend le contenu de la 0.3.0-alpha (non publiée : tests en échec sur le runner de CI) et de la 0.3.1-alpha.

### Modifié
- **Mode édition style Stream Deck** : le catalogue d'actions passe à droite (recherche, catégories repliables), l'inspecteur de la case sous la grille (aperçu et titre à gauche,
  actions et réglages à droite). Un seul accent bleu (couleur source `#2F7DF6`), neutres presque gris, contrastes toujours testés. Aucun changement du format de `config.json`. [ADR 0026](docs/adr/0026-disposition-style-stream-deck-et-palette-sobre.md).
- **Curseur vertical 1 × 2** (composant `VerticalSlider`) : l'icône du canal coupe au toucher, niveau en %, nom du canal ; grisé (« – » à la place du niveau) quand Sonar est indisponible.
- **`IAudioMixerProvider`** : abstraction des mélangeurs à canaux (Sonar aujourd'hui, Wave Link ou Voicemeeter plus tard), pourcentages de 0 à 100 côté application.

### Corrigé
- **Sonar : coupure d'un canal** : la route `…/isMuted/{valeur}` répond 404 en mode classic ; la vraie route est `…/Mute/{valeur}`. L'API (lecture, volume, coupure, canal Principal) est désormais
  vérifiée contre un GG réel en mode classic ; le mode streamer en écriture reste à vérifier.
- **Discord en arrière-plan** : les boutons Discord envoyaient Ctrl+Maj+M / D, raccourcis *locaux* de Discord (focus requis) et micro de Teams. Nouvelle
  **intégration Discord** par le RPC local (micro, sourdine, volumes, quitter le salon ; état en direct ; indépendante du focus et du clavier) avec l'application
  OAuth2 de l'utilisateur, jeton chiffré et renouvelé (`ISecretRotating`). Boutons « raccourci » sur Ctrl+Alt+Maj+M / D. [Discord](docs/integrations/discord.md).
- **Touches simulées** : code de balayage, maintien de 30 ms hors du thread d'interface, refus de Windows (application en administrateur) affiché sur la touche.
  Conflits de raccourcis signalés. Second appui ignoré pendant 150 ms ou tant que la touche s'exécute.
- **Gel de 6 s à la fermeture des paramètres** : parcours d'arbre exponentiel du nettoyage des barres de titre (44,8 millions de visites pour 2 552 éléments).
  Fermeture chronométrée dans le journal ; champs d'intégration enregistrés après la frappe, plus à chaque caractère.
- **Pages web qui capturaient la souris** : modes *passive*, *toucher pour interagir* (défaut) et *interactive* (`WebInput`, option `input`) ; hors interaction,
  la fenêtre du cadre est désactivée (le pointeur revient au deck) et la page ne vole plus le clavier.
- **Clic droit lent en édition** (144 à 233 ms : rechargement complet de l'éditeur à chaque menu) : simple sélection, 32 à 37 ms.
- **Verrouiller la session** : Win+L simulé ne verrouille pas ; `LockWorkStation`.
- **Scripts** : le code de sortie et la dernière ligne d'erreur s'affichent sur la touche (observation de 3 s, le script continue seul au-delà).

### Ajouté
- **Mélangeur audio** : un curseur précis par canal (Sonar, Discord, périphériques Windows) avec valeur, couper, réinitialiser, disponibilité ; chaque contrôle
  dit ce qu'il règle (mélangeur Sonar, volume Windows, volume Discord). Niveau de réinitialisation réglable.
- **Menus de case** selon le contenu : ouvrir le dossier, régler le widget, renommer, changer l'icône, déplacer, *Remplacer par*. Bouton **Case** dans le dock d'édition.
- **Widgets** : batterie, durée de fonctionnement, périphériques audio actifs, lecture en cours, flux RSS/Atom. **Chronomètre** (minuteur de durée 0).
- **Actions prêtes à l'emploi** : panneau Son, Gestionnaire de périphériques, Services, Observateur d'événements, Gestion de l'ordinateur et des disques,
  Connexions réseau, Planificateur de tâches, Moniteur de ressources, Informations système, terminal dans un dossier, SSH, projet dans VS Code.
  `%VARIABLES%` développées dans le programme et les arguments lancés. ADR 0025, [tests manuels](docs/tests-manuels-discord-web-menus.md).

### Ajouté (SteelSeries Sonar et audio par périphérique)
- **Connecteur SteelSeries Sonar** (`sonar`) : découverte locale par `coreProps.json`, volume et coupure des canaux (principal, jeu, discussion, média, aux, micro ; modes classic et streamer), valeurs
  `{sonar.game.volume}` et `{sonar.game.muted}`, commande **Diagnostic** en lecture seule qui écrit `sonar-diagnostic.txt`. Adresses locales seulement, routes et canaux réglables sans recompiler.
  **API non vérifiée contre un vrai GG** (testée contre un faux serveur local) : [Sonar](docs/integrations/sonar.md), [tests manuels](docs/tests-manuels-sonar.md).
- **Audio Windows par périphérique** (action `audiodevice`) : énumération Core Audio, désignation par nom (`capture:Focusrite`, `render:Arctis`), niveau absolu ou relatif, coupure, liste ; le périphérique par défaut n'est
  jamais changé. `IAudioService` hérite de `IAudioDevices`.
- **Widget Niveau audio** : roue tactile (rotation continue sans saut ni zone morte, niveau au centre, toucher le centre coupe) ou curseur vertical / horizontal, lié à un canal Sonar ou à un périphérique Windows ;
  choix de la cible et de la forme dans le panneau de propriétés. Réglages dans `WidgetOptions` (`source`, `shape`) : aucune nouvelle clé de premier niveau de `config.json`. ADR 0024.

### Ajouté (1.0.0, premières publications)
- **Installateur Windows** (Inno Setup) : `build.ps1` produit `StreamDak-<version>-self-contained-Setup.exe`, `…-framework-dependent-Setup.exe` et `checksums.txt` ; dossier au choix,
  raccourcis, lancement avec Windows, mise à jour sur place sans toucher à `%AppData%`, désinstallation propre, WebView2 vérifié ; script de signature `installer/sign.ps1`. `docs/INSTALLATION.md`.
- **Mises à jour par les releases GitHub** : bouton *Rechercher des mises à jour*, notes Markdown, *Installer maintenant / Plus tard / Ignorer*, canal bêta, vérification automatique
  quotidienne désactivable, point discret sur le menu du deck. Téléchargement HTTPS limité au dépôt configuré (`update.json`), reprise après coupure, SHA-256 de `checksums.txt` et signature
  Authenticode vérifiés, installation silencieuse avec sauvegarde et restauration. Workflow GitHub Actions sur tag `vX.Y.Z`. ADR 0021.
- **Widgets de données extensibles** (`IWidget`) : disque, réseau, ping, état des services (HTTP / port TCP), météo, prochain événement, compteur JSON ; tailles 1 × 1, 2 × 1, 2 × 2,
  intervalle réglable, arrêt hors page visible et deck masqué ; réglages générés dans le panneau de propriétés. ADR 0019.
- **Badges de notifications** (Gmail, Google Chat, Discord, Teams, Slack, services de `notifications.json`) sur les boutons « site web » : pages WebView2 cachées, lecture sur événement,
  session partagée, aucun identifiant enregistré. ADR 0020.
- **Badges : modes, onglets, barre des tâches** : mode par service (`auto`, `title`, `selector`, `off`) dans `notifications.json`, observateur de sélecteur dans la page cachée, point d'activité par
  icône de l'onglet ; badge d'onglet en `InfoBadge` (WPF-UI) ; pastille du **total de non lus** dans la barre des tâches (toutes les pages web de tous les profils) ; réglages par page web
  *Afficher le badge* et *Exclure du total* (clés `BadgeEnabled` et `BadgeExcludeFromTotal`, valeurs par défaut non écrites). ADR 0022 et 0023.
- `SECURITY.md` (audit), `.github/workflows/ci.yml` (compilation, tests, dépendances vulnérables), version unique dans `Directory.Build.props`, SDK épinglé (`global.json`).

### Sécurité
- **Scripts** : plus aucune ligne de commande assemblée (`ScriptCommand`, `ArgumentList`) ; les métacaractères de `cmd.exe` sont refusés pour `.bat` et `.cmd` ; politique PowerShell `RemoteSigned`
  (au lieu de `Bypass`) : **un script téléchargé et non signé est maintenant bloqué**.
- **Journal** : masquage des jetons, mots de passe et en-têtes `Authorization` (`LogRedactor`).
- **Secrets** : un secret retrouvé en clair dans `config.json` est déplacé dans le magasin chiffré et supprimé du fichier.
- Analyseurs de sécurité .NET en erreur (`AnalysisModeSecurity=All`).

### Performances
- Deck réduit ou masqué : minuteries arrêtées, connecteur « Système Windows » relevé toutes les 30 s au lieu d'1 s, pages de notifications mises en veille ; reprise après veille. CPU au repos,
  widget Processeur, deck réduit : **1 766 ms → 250 ms par 30 s (−86 %)**.

### Corrigé (interface)
- Pastille « Simulation » illisible en thème clair (contraste ≥ 4,5 vérifié par test) ; le mot « Modifier » d'une touche étroite recouvrait le badge d'étapes.
- Test de rendu des jauges qui dépendait de l'ordre d'exécution.

### Corrigé (dock et page web)
- **Un lien reçu dans Google Chat (Google Docs…) s'ouvrait dans le cadre, sans moyen d'en sortir** : un lien « nouvel onglet » vers un autre site
  s'ouvre maintenant dans le navigateur par défaut ; seuls les liens du même site et les pages de connexion (Google, Microsoft) restent dans le cadre.
  Nouveau bouton **Retour** dans la barre du cadre : page précédente, ou page de départ du cadre.
- Le bouton **+ (ajouter une page)** du mode édition disparaissait derrière les onglets quand les pages étaient nombreuses : il reste maintenant toujours visible ; *Nouvelle page…* est aussi dans le menu *Page*.
- Une page web restait **décalée** d'environ 35 px après un changement de page (l'animation de glissement ne déplace pas le cadre WebView2) : pas d'animation quand la page affiche un cadre web.

### Corrigé (cadre web : autorisations)
- **Google Agenda se croyait bloqué pour les notifications** : les versions précédentes refusaient tout et le profil du navigateur avait mémorisé ces refus
  (sans barre d'adresse, aucun moyen de les lever). Les refus mémorisés des notifications, du micro et de la caméra sont remis à « demander » à l'ouverture
  d'un cadre, et un refus n'est plus jamais mémorisé.

### Modifié (renommage : TouchDeck devient StreamDak)
- Projets, assemblies, espaces de noms, textes, documentation, `streamdak.log`, extension `.streamdak`, variable `STREAMDAK_DATA_DIR`.
- **Reprise des données existantes** : le dossier `%APPDATA%\TouchDeck` est déplacé vers `%APPDATA%\StreamDak` au premier lancement ; l'ancienne
  variable `TOUCHDECK_DATA_DIR`, les fichiers `.touchdeck` et la valeur de démarrage automatique `TouchDeck` sont repris ; les secrets déjà
  chiffrés restent lisibles (l'entropie DPAPI ne change pas).
- Les plugins compilés contre `TouchDeck.Core` sont à recompiler contre `StreamDak.Core`.
- Nouveau logo vectoriel (`Themes/Logo.xaml`, `Assets/logo.svg`) ; `logo.png` et `icon.ico` régénérés.

### Ajouté (pages web, widgets système)
- **Page web plein cadre** : une page peut afficher un site à la place de la grille (`DeckPage.Kind` = `web`, `Url`). Dock › *Page* › *Afficher une
  page web…*. Les boutons de la page sont conservés. Clés facultatives de `config.json`, anciens fichiers inchangés (ADR 0018).
- **Widgets Processeur et Mémoire** (catalogue › *Widgets*) : charge en %, Go utilisés sur le total, jauge d'alerte à 85 %, rafraîchis chaque seconde.
  Ils activent le connecteur *Système Windows*, qui publie en plus `{sys.ram.used}` et `{sys.ram.total}`.
- Barre du cadre web : bouton **Ouvrir dans le navigateur** ; si Google refuse la connexion dans un cadre intégré, la tuile l'explique et propose
  le widget Agenda ou le navigateur. L'agent utilisateur n'est pas modifié (mesures et raisons dans l'ADR 0018).

### Modifié (barre du haut : Windows 11)
- Le bouton plein écran affiche les **dispositions d'accrochage de Windows 11** au survol de la souris ; la fenêtre est accrochable (glisser vers un bord, le haut
  d'un écran, Win + flèches) et redimensionnable (taille minimale 640 × 420). Le plein écran du deck, l'absence de focus et le toucher sont inchangés ; en plein
  écran les bords ne redimensionnent pas. Au doigt, la barre fonctionne comme avant (ADR 0018).

### Ajouté (cadre web : autorisations, cadres conservés, zoom)
- **Notifications, micro et caméra** accordés aux pages https du même site que l'adresse saisie (par exemple `meet.google.com` pour une page `calendar.google.com`) ; tout autre droit reste refusé (ADR 0018, qui remplace la règle de l'ADR 0017).
- **La page ne se recharge plus quand on change de page du deck** : le cadre est rangé puis repris (6 cadres au plus, libérés à la fermeture). Les notifications continuent d'arriver page masquée.
- **Zoom** : boutons − / % / + dans la barre du cadre, pincement et Ctrl + molette, mémorisé (`DeckPage.Zoom`, option `zoom` du widget web).

### Corrigé
- **Cadre web : saisie au clavier automatique.** Il fallait toucher le petit bouton clavier avant de taper dans une page (la fenêtre du deck ne prend jamais le clavier). Désormais la saisie démarre dès qu'un champ de texte de la page a le focus et s'arrête ~1,2 s après en être sorti. Le bouton clavier reste disponible et prend la main quand on l'utilise.
- **Cadre web : un lien qui ouvre un nouvel onglet ne faisait rien** (par exemple *Sign in* sur la page d'accueil de Google Agenda). Il s'ouvre maintenant dans le cadre, sans jamais créer de fenêtre.
- La mémoire du connecteur *Système Windows* venait d'un relevé du ramasse-miettes mis à jour rarement : elle est maintenant lue à chaque relevé.
- Une emprise de widget ne peut plus dépasser le bas de la grille.

### Ajouté (édition : glissement, quitter, pages Google)
- **Glissement visible** : la touche glissée s'atténue, la case visée s'allume (cadre et voile d'accent) et une étiquette suit le pointeur avec l'effet du dépôt (déplacer, échanger, ranger dans le dossier), à la souris comme au doigt.
- **Bouton *Quitter*** dans le dock en édition : annule les modifications faites depuis l'ouverture du mode (confirmation s'il y en a ; annulable). *Terminer* les garde.
- **Pages Google** dans le catalogue *Widgets* : Google Chat, Google Agenda (page web), Gmail.
- `DeckDocument.Checkpoint`, `HasChangesSince`, `RevertTo`.

### Ajouté (widgets)
- **Widgets** : horloge, agenda Google et page web (WebView2 isolé), sur plusieurs cases (`TileGridPanel`, ADR 0017). Catégorie *Widgets* du catalogue, taille et réglages dans le panneau de propriétés.
- **Agenda Google** : l'intégration Google lit l'agenda principal toutes les 5 minutes (valeurs `calendar.events` et `calendar.error`).
- `config.json` : clés facultatives `Widget`, `WidgetOptions`, `ColSpan`, `RowSpan` (absentes d'un bouton ordinaire ; anciens fichiers inchangés).
- Dépendance : `Microsoft.Web.WebView2`.

### Modifié (édition sans fenêtre, stabilité du toucher)
- **Plus de fenêtre d'édition** : *Nouveau bouton…*, *Modifier…*, *Éditeur complet…*, l'icône de la zone de notification et `--edit` ouvrent le mode édition du deck (panneaux d'actions et de propriétés autour de la grille). Supprimés : `EditorWindow`, `QuickCreateWindow`, `QuickCreateViewModel`.
- **Options de page et de profil dans le deck** : bouton *Page* du dock (renommer, dupliquer, masquer, déplacer, supprimer) ; pastille du profil (changer de profil, format de la grille, renommer, exporter, importer, nouveau profil depuis un modèle, supprimer). En édition, un appui long sur une case ouvre le menu complet de l'éditeur (déplacer, dossier, réinitialiser…).
- **Boîtes de dialogue dans le deck** (`DeckDialogViewModel`) à la place des fenêtres modales : saisie d'un nom, confirmation, information.
- Le menu ⚙ propose *Modifier le deck* (ancien *Modifier au toucher*) ; *Ouvrir l'éditeur* disparaît.

### Corrigé (toucher, blocages)
- Un appui bref exécute désormais l'action **après** le message `WM_POINTERUP` (et non pendant) : une commande qui ouvre une boîte de dialogue ne fige plus le traitement du toucher.
- Le doigt qui ouvre le mode édition par appui long n'est plus gardé « posé » : tous les appuis suivants étaient ignorés. Un doigt suivi qui ne touche plus l'écran n'empêche plus un nouvel appui.
- **Chien de garde** (`UiWatchdog`) : une interface sans réponse pendant plus de 3 s est consignée dans `streamdak.log` (« Interface sans réponse depuis … ms »).

### Ajouté (édition intégrée et Material Design 3)
- **Édition dans le deck** : le mode édition (menu ⚙ › *Modifier au toucher*, ou *Modifier…* sur un bouton) affiche un panneau d'**actions à gauche** et les **propriétés à droite** de la grille, sans autre fenêtre. Toucher une touche la sélectionne (liseré d'accent), toucher une action la pose dans la case choisie, *Annuler* / *Rétablir* sont dans le dock. L'éditeur complet reste disponible en fenêtre séparée.
- **Thème Material Design 3 dynamique** : les thèmes sombre, AMOLED et clair sont générés depuis une couleur source par des palettes tonales (accent, conteneurs, surfaces, erreur). L'onglet de page actif suit la barre de navigation MD3.
- **Élévation tonale** : plus d'ombre portée (touches, dock, bandeau, menu), états de survol et d'appui MD3, touches plus arrondies. Rendu de 32 touches 177 → 17 ms hors écran, mémoire privée 105 → 94 Mo.
- Pendant l'édition seulement, la fenêtre du deck est activable (saisie au clavier, toucher WPF) ; en sortant, `WS_EX_NOACTIVATE` est rétabli et le focus est rendu à l'application précédente (ADR 0015).

### Ajouté (création rapide)
- **Menu des cases du deck** (clic droit, appui long) : case vide → *Nouveau bouton…*, *Parcourir les actions*, modèles en un clic, *Coller*, *Éditeur complet…* ; bouton → *Modifier…*, *Dupliquer*, *Copier*, *Couper*, *Supprimer*, *Éditeur complet…*. Décrit en `MenuItemSpec`, affiché dans la feuille du deck (lignes de 48 px) pour que le toucher reste traité par le deck (ADR 0013, ADR 0014).
- **Panneau de création rapide** : recherche dans tout le catalogue (« chrome », « vol »), titre et icône proposés, *Créer* (Entrée) / *Annuler* (Échap) ; une seule écriture, un seul pas d'annulation.
- **Glisser-déposer** de fichiers, `.lnk`, exécutables, dossiers, `.url` et adresses web sur une case vide : bouton *Lancer* ou *Ouvrir* avec l'icône de l'application.
- **Bouton *Annuler*** dans le bandeau d'état après une création, une duplication ou un dépôt ; **bouton *Modifier*** quand un bouton échoue.
- `DeckDocument.AddButton`, `AddButtonFirstFree`, `AddButtons` (lot = une annulation), `QuickCreate`, `DroppedItem`, `DropPayload`.

### Corrigé (performance)
- **Fuite des fenêtres fermées** : la barre de titre de WPF-UI restait abonnée à un événement statique de thème, donc éditeur, paramètres, panneau rapide et invites restaient en mémoire une fois fermés (42 Mo pour 50 ouvertures de l'éditeur, 0,7 Mo maintenant). Test de non-régression chiffré.
- **Démarrage** : la publication est précompilée (ReadyToRun), environ 80 ms de moins.

### Modifié (simplicité d'usage)
- Page ou dossier vide : la première case propose *Ajouter un bouton* ; un indice « appui long ou clic droit » s'affiche une fois par lancement (sans clé de configuration).
- Éditeur : l'action passe avant l'apparence ; position du titre, icône, image et couleurs sont dans un bloc replié par défaut.
- Messages d'échec : ils disent quoi faire (« Réessaie… », « choisis une autre action »). Le menu des cases de l'éditeur parle du « bouton ».
- `config.json` n'est plus réécrit quand son contenu n'a pas changé.
- Aucune clé de `config.json` ajoutée ni modifiée.

### Corrigé (tactile)
- **Le toucher ne déplace plus le curseur** : le deck traite lui-même les appuis du doigt (boutons, menu, barre du haut déplaçable, double appui = plein écran) au lieu de laisser Windows les convertir en souris. La souris reste indépendante. Diagnostic : `STREAMDAK_TOUCHLOG=1` (`docs/adr/0013-pile-pointeur-wpf.md`).

### Ajouté (phase 8, Google Workspace)
- **Google Workspace** avec ton projet Google Cloud (OAuth de bureau, PKCE, jeton chiffré) : ajouter une ligne ou écrire une cellule Sheets, ajouter du texte à un Docs, créer un événement Agenda (`docs/integrations/google.md`).

### Modifié (.NET 10)
- **Migration vers .NET 10 (LTS)** : tous les projets, paquets `Microsoft.Extensions.*`, `ProtectedData`, `PerformanceCounter` et Serilog.Extensions.Logging en 10.0.0. .NET 9 sort de support en novembre 2026. Aucun changement de comportement, 354 tests au vert.

### Ajouté (phase 8, Twitch)
- **Twitch** par l’API Helix avec ton propre identifiant client et un jeton utilisateur : clip, marqueur, publicité, titre, chat (`docs/integrations/twitch.md`).

### Ajouté (phase 8, gestionnaires de tâches)
- **ClickUp**, **Notion** et **monday.com** (jeton personnel chiffré, base `RestIntegrationBase`) : créer une tâche ou une ligne, changer un statut, minuteur ClickUp (`docs/integrations/gestionnaires-de-taches.md`).

### Ajouté (phase 7, modèles de profils)
- **Modèles de profils** (menu d’un profil › « Nouveau profil depuis un modèle ») : Réunions, Streaming OBS, Montage vidéo, Simulation de vol, Poste de travail. Annulable en une fois.
- **Icône de la zone de notification** : afficher le deck, l’éditeur, les paramètres, quitter.

### Ajouté (phase 9, plugins et API locale)
- **Plugins** : intégrations posées dans `plugins<Nom><Nom>.dll`, chargées dans leur propre contexte (`docs/plugins.md`, exemple `samples/HelloPlugin`, ADR 0011 et 0012).
- **API locale** par canal nommé réservé à l’utilisateur (`ping`, `status`, `values`, `profile`, `page`, `run`, `show`, `edit`, `settings`) : `docs/local-api.md`.
- **Instance unique** : relancer StreamDak affiche le deck existant (ou l’éditeur avec `--edit`) au lieu d’ouvrir une seconde fenêtre.

### Ajouté (phase 8, connecteurs HTTP)
- **vMix**, **lampes Elgato** et **Philips Hue** (association par le bouton du pont, clé chiffrée DPAPI) ; socle commun
  `PolledHttpIntegration` (interrogation, perte après 3 échecs) et `IPairableIntegration` (bouton « Associer » des paramètres).
- Documentation : `docs/integrations/{vmix,elgato,hue}.md`.
- **Système Windows** : `{sys.cpu}`, `{sys.ram}`, média en cours (`{sys.media.title}`, `{sys.media.artist}`, `{sys.media.playing}`) et commandes lecture / suivant / précédent par SMTC, sans compte (`docs/integrations/windows.md`). Les projets de l’application et des tests passent à `net9.0-windows10.0.19041.0`.
- **X-Plane 12** par son API web locale : datarefs affichés dans les titres (`{xplane.gear}`), écriture d’un dataref et activation d’une commande (`docs/integrations/xplane.md`).
- **Microsoft Teams** par l’API locale « application tierce » (association, micro, caméra, main, flou, enregistrement, réactions, état de la réunion) : `docs/integrations/teams.md`.
- **Applications cibles** : 35 boutons prêts à l’emploi (Zoom, Teams, Discord, Premiere, Photoshop, DaVinci, Blender, Outlook, Word, Excel, Star Citizen) qui mettent l’application au premier plan puis envoient son raccourci (`docs/integrations/raccourcis-applications.md`).

### Ajouté (phase 3)
- **Thème AMOLED** (`Theme` = `"amoled"` : fond noir pur), choisi dans les paramètres et le menu du deck. Valeur ajoutée à
  une clé existante : un ancien fichier reste lisible, une valeur inconnue retombe sur « system ».
- **Jetons de design** (`Themes/Tokens.xaml`) : rayons 8 / 12 / 16 / 20, espacements sur une grille de 4, échelle
  typographique (titre 20, section 14, corps 13, légende 11), styles de texte. Les couleurs restent dans `Theme.cs`
  (palettes dont les contrastes sont testés, publiées à chaud) plutôt que dans un `Colors.xaml` statique.

### Modifié (phase 3, début)
- **Palette sombre** du cahier des charges : application `#0B0E17`, panneaux `#121624`, cartes `#1A1F30`, accent `#6C5CE7`
  (clair et sombre). Contrastes WCAG AA revérifiés par les tests ; fond des touches, dock, fond du deck et champs alignés
  sur la nouvelle palette. L'accent texte `#B0A8FF` est conservé : `#7C6CFF` n'atteint que 3,9:1 sur les cartes.

### Architecture (phase 2)
- **Trois projets.** `StreamDak.Core` (`net9.0`, sans WPF ni Windows), `StreamDak.Platform.Windows` (Win32 interne, sans
  WPF) et l'application WPF. Les frontières sont vérifiées par `ArchitectureTests`.
- **Injection de dépendances.** Le localisateur statique `AppServices` est supprimé. `AppHost.Build` construit le
  conteneur ; ViewModels et fenêtres reçoivent leurs dépendances par constructeur. Nouveaux services :
  `ConfigPersistence`, `AppearanceService`, `IAppWindows`, `DisplayService` injecté.
- **Journal Serilog.** Façade `Log` du Core branchée sur Serilog ; `ILogger<T>` pour les services injectés. Le fichier
  `streamdak.log` change de rotation : 1 Mo par fichier, 3 fichiers (`streamdak_001.log`…) au lieu de `.old`.
- **Gestionnaires d'action.** Une classe par type (`Core/Actions/Handlers/`), enregistrés dans un `ActionRegistry` qui refuse
  les doublons : une intégration s'ajoute sans modifier le Core.
- **Démarrage.** Environ 55 ms de plus qu'en fin de phase 1, mais moins que la version initiale (≈ 670 ms contre ≈ 700 ms).
  Le journal indique le temps de chaque étape.
- **Documentation.** `docs/ARCHITECTURE.md` réécrit, ADR 0007 à 0010, `docs/phase2-report.md`.

### Corrigé (phase 2)
- **Dossiers trop profonds.** Au-delà de 8 niveaux, le contenu n'est plus ignoré au chargement : il est remonté dans le
  dernier dossier autorisé, avec un avertissement. Aucun bouton n'est perdu.

### Corrigé
- **Tests réellement simulés.** Les tests d'interface tournaient sur la vraie plateforme Windows et ouvraient la vraie
  fenêtre du deck. En cause : le constructeur d'`Application` lance `OnStartup` dès le premier tour du Dispatcher. Le
  harnais utilise désormais `App.Headless`, et un test le garantit.
- **Test instable.** Un test de sauvegarde échouait par intermittence. Les collections de tests s'exécutent maintenant
  en série, car le dossier de configuration est global au processus.
- **Erreurs consignées.** Les erreurs attendues ne sont plus avalées en silence : copie de migration, sauvegardes,
  images illisibles, lancement automatique, thème de Windows, surveillance de `config.json`, rechargement refusé.
- **Diagnostic Win32.** Le journal signale désormais un style `WS_EX_NOACTIVATE` non appliqué ou une saisie refusée par
  une application élevée (`SendInput` partiel).

### Performances
- **Grille du deck.** Une modification de configuration ne reconstruit plus toute la grille, seulement les cases
  concernées : 135 ms → 12 ms pour 32 touches (mesure hors écran).
- **Démarrage et mémoire.** Démarrage d'environ 700 ms à environ 595 ms ; mémoire de travail de 172 Mo à 162 Mo
  (mesures en mode simulation, 32 touches).

### Modifié
- **Qualité à la compilation.** Analyseurs .NET (`latest-recommended`), style vérifié à la compilation (`.editorconfig`),
  avertissements bloquants, documentation XML obligatoire sur toute API publique.
- **Constantes.** Les identifiants d'action, de page et de commande audio sont des constantes (`Core/ActionTypes.cs`).
  Leurs valeurs dans `config.json` ne changent pas.
- **Couleurs.** Les couleurs écrites en dur dans le XAML sont devenues des ressources de thème `Deck*`. Le rendu est
  identique au pixel près.
- **Journal.** `Log` est déplacé de `Services/` vers `Core/`.
- **Démarrage.** La fenêtre principale est créée par `App.OnStartup` au lieu de `StartupUri`.

### Supprimé
- Code sans appelant : `DeckDocument.MoveProfile`, `Place.WithProfile` et `Place.WithPage`, `TileFaceData.WithLabel`,
  `EditorViewModel.NeighborIndex`.

### Documentation
- `docs/configuration.md` : format, cycle de vie et migrations, avec diagrammes.
- Diagrammes Mermaid dans `docs/ARCHITECTURE.md`.
- Décisions d'architecture dans `docs/adr/`.
- `CONTRIBUTING.md` et ce journal.
- `docs/integrations-research.md` : étude des intégrations.
- `docs/cleanup-report.md` : bilan du nettoyage.

## 2026-10-19 — refonte Fluent et éditeur façon Stream Deck

Historique antérieur au journal, résumé d'après les commits.

- **Migration Fluent.** WPF-UI, Mica, thème clair et sombre. Interface principale et éditeurs en XAML + MVVM
  (CommunityToolkit.Mvvm), icônes Fluent.
- **Éditeur façon Stream Deck.** Éditeur à trois panneaux, profils, dossiers, annuler / rétablir, presse-papiers interne.
- **Moteur d'actions.** Découplé de Windows : délai maximal par étape, annulation, retour visuel sur les tuiles,
  mode `--simulate`.
- **Écran du deck.** Mémorisé par son identifiant (`MonitorDevice`), avec des écrans simulables dans les tests.
- **Refonte visuelle du deck.** Touches teintées à pastille, variantes, dock unifié, fond nuancé.
- **Documentation.** Architecture, développement, parité avec Stream Deck, rétro-ingénierie fonctionnelle.
