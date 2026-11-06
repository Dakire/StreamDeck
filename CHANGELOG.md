# Journal des modifications

Format inspiré de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/). Les dates sont au format AAAA-MM-JJ.

## [Non publié]

### Corrigé
- **Souris « attrapée » par une page web (Google Sheets)** (ADR 0037) : un bouton de la souris relâché hors du cadre n'arrivait jamais à la page, qui le croyait toujours
  enfoncé (la sélection de cellules suivait la souris). Le cadre capture maintenant la souris du clic au relâchement, et la page reçoit le relâchement même si la capture est perdue.

## [1.5.0] — 2026-11-06

Changement de la valeur par défaut d'une clé existante (validé par l'utilisateur le 2026-11-06) : `WebInput` d'une page et `input` d'un widget web, absents, valent
désormais `interactive` (toucher direct) au lieu de `touch`. `touch` reste disponible et s'écrit explicitement. Aucune clé ajoutée, renommée ni supprimée.

### Modifié
- **Pages web : plus de premier toucher d'activation** (ADR 0037) : une page web (widget ou plein cadre) réagit dès le premier toucher. Le balayage ne change toujours pas de page
  sur elle, les onglets du dock et la bande de commandes du cadre restent touchables, et le mode édition, un menu ou un dialogue la bloquent toujours. Le mode
  *Toucher pour activer* (bouclier, liseré, fin après un délai) reste au choix dans les propriétés pour protéger une page du défilement accidentel.

### Corrigé
- **Pages web et clavier** : un clic de souris sur la page ne lui donne plus le focus clavier (le contrôle WebView2 le prenait à chaque clic, au risque d'activer le deck) ;
  un champ `autofocus` ne lance plus la saisie au clavier sans toucher ni clic dans la page.
- **Souris attrapée par une page web** : un verrouillage du pointeur (Pointer Lock API, jeux, visionneuses) est relâché aussitôt et journalisé ; Échap n'atteint jamais la page
  dans le deck, la souris restait donc bloquée.
- Diagnostic `STREAMDAK_TOUCHLOG=1` : position du doigt à côté de celle du curseur à chaque appui, et souris fabriquée par Windows depuis le doigt.

## [1.4.1] — 2026-11-05

La 1.4.0 n'a pas été publiée (test de performance en échec sur le CI, sans lien avec l'application) : la 1.4.1 contient tout ce qui suit, avec un test de la suite corrigé.

## [1.4.0] — 2026-11-05 (non publiée)

Clé facultative ajoutée à `config.json` (validée) : `SwipePagesDisabled` (absente = balayage activé). Aucune clé renommée ni supprimée.

### Ajouté
- **Balayer pour changer de page** (ADR 0036) : un balayage horizontal sur la grille passe à la page suivante (vers la gauche) ou précédente (vers la droite) ; la grille suit le doigt
  et rebondit au bout de la liste. Jamais en mode édition ni sur une page web ; un balayage n'exécute jamais la touche de départ. Désactivable dans Paramètres › Toucher.
  Il part de n'importe où sur la grille, pas seulement du bord. Dans un dossier, un balayage vers le bas revient au dossier parent.

### Corrigé
- **Mode édition** : la case visée pendant un glissé est entourée d'un contour en pointillés (la case entière, plus seulement le « + »).
- **Icônes d'applications et de sites** (ADR 0036) : elles ne passent plus derrière le titre (zone bornée au-dessus ou au-dessous) et sont nettes : icône de programme en 256 px au lieu
  de 32, décodage à la taille d'affichage selon l'échelle de l'écran, petite image montrée à sa taille ou à un multiple entier plutôt qu'étirée. Les anciennes icônes de 32 px sont
  remplacées toutes seules au démarrage ; une image introuvable est remplacée par une icône du type d'action.
- **Toucher une page web ne déplace plus le curseur** (ADR 0036) : la page est rendue par composition, sans fenêtre Win32 sous le doigt ; le deck lui transmet le doigt comme un vrai
  toucher (défilement, appui, pincement, appui long). La souris physique garde son comportement. Non vérifié sur un écran tactile réel.

## [1.3.0] — 2026-11-05

Clés facultatives ajoutées à `config.json` (ADR 0034, validé) : `ThemeOverrides`, `WebIdleExitSeconds`, `LiftDragDisabled`, `LiftDelayMs`, et la valeur `tixia` pour `Theme`. Aucune valeur par défaut n'est écrite ;
un ancien fichier se lit tel quel ; aucune clé existante n'est renommée ni supprimée.

### Ajouté
- **Thèmes** (Paramètres › Apparence) : cartes de thèmes prédéfinis avec aperçu (Suivre Windows, Sombre, Clair, AMOLED, **TIXIA** : orange, fonds bruns chauds), éditeur de six couleurs (accent, fond, surface, texte, succès, erreur)
  avec 22 couleurs en un toucher (48 px) et saisie hexadécimale, **Réinitialiser** par couleur et **Tout réinitialiser**, aperçu en direct sur de vraies touches, avertissement de contraste. S'applique à chaud par les brushes `Deck*`.
- **Pages web au toucher** (ADR 0032) : liseré d'accent autour de la tuile activée ; la page redevient passive par un toucher hors de la tuile, au changement de profil, de page, de dossier ou de sous-page, ou après un délai
  sans geste réglable (Paramètres › Toucher, 5 à 300 s, 30 s par défaut). Le mode **Toujours active (24 h/24)** garde un site actif en permanence, sans premier toucher.
- **Déplacer un bouton hors édition par appui long** (ADR 0033), **activé par défaut** : la touche se soulève (seuil réglable de 300 à 1500 ms, 500 par défaut), puis se dépose sur une case, un dossier, un onglet de page ou la corbeille ;
  Échap ou un relâchement hors de la grille annule. Relâchée sans glisser, la touche ouvre l'éditeur de sa case, comme l'ancien appui long. Désactivable dans Paramètres › Toucher.

### Corrigé
- **Release 1.2.1 en échec** : l'applicateur de mise à jour utilisait `Get-FileHash`, introuvable quand `powershell.exe` 5.1 hérite du `PSModulePath` de PowerShell 7 (CI, lancement depuis `pwsh`).
  Deux tests d'`UpdateApplier` échouaient donc sur le CI, et `build.ps1` (qui lance les tests d'abord) arrêtait la release. L'empreinte est maintenant recontrôlée avec .NET, sans module ; le contrôle reste identique.

## [1.2.1] — 2026-11-04

Correctifs de sécurité et de fiabilité issus de `SECURITY.md` (« Audit 3 »). Aucune clé de configuration ajoutée, renommée ni supprimée.

### Corrigé
- **Mise à jour** : l'applicateur recontrôle le SHA-256 de l'installateur juste avant de le lancer (un fichier remplacé après la vérification est refusé et l'ancienne version est relancée).
- **Plugins** : un `.deps.json` incomplet ou modifié après vérification est refusé avant d'atteindre le résolveur natif (qui plante sur un fichier incomplet).
- **API locale** : lecture du document sur le thread d'interface (plus d'erreur intermittente pendant une modification) ; la boucle d'écoute survit à toute erreur au lieu de s'arrêter en silence.
- **Publication** : Inno Setup épinglé à la version 6.7.1 dans le workflow de release.

## [1.2.0] — 2026-11-04

Audit 3 : sauvegardes réglables, accessibilité, durcissement des imports, fiabilité au démarrage. Voir `SECURITY.md` (« Audit 3 ») et les ADR 0030 et 0031. Les clés ajoutées sont facultatives ; aucune clé existante n'est renommée ni supprimée.

### Ajouté
- **Changement de page au bord** pendant un déplacement : un bouton soulevé tenu près d'un bord de la fenêtre passe à la page visible suivante ou précédente (500 ms, puis une page par 900 ms) ; les pages masquées et web sont sautées (ADR 0035).
- **Écartement des touches voisines** de la case visée pendant un glissé (mode édition et appui long), et **soulèvement animé** de la touche tenue (échelle avec rebond, ombre en fondu).
- **Animations** d'ouverture des menus et des boîtes de dialogue, et de la corbeille ; elles respectent le réglage d'animations et ne retardent jamais une action.
- **Barre du haut** : curseur de déplacement, filet et capsule de poignée avec six points. **Poignées des panneaux** (catalogue à droite, inspecteur en bas) : rail sur toute l'arête et zone de saisie élargie, pour redimensionner de n'importe quel point de l'arête.
- **Palettes** : hiérarchie de surfaces régulière en sombre, clair, AMOLED et TIXIA (marches ≥ 1,09 : 1 au lieu de 1,04–1,06 en clair et AMOLED), touches mieux détachées du fond, couches d'état survol et appui, conteneur de succès, rouge d'erreur plus profond en clair.
- **Sauvegardes** (Paramètres › Sauvegardes) : nombre de sauvegardes conservées réglable de 1 à 100 (clé facultative `BackupLimit`, 15 par défaut), bouton **Supprimer** par sauvegarde,
  **Supprimer l'excédent** et **Tout supprimer** (avec confirmation). La limite ne supprime rien pendant la saisie : elle s'applique à la sauvegarde suivante.
- **Accessibilité** (ADR 0031) : annonces pour lecteurs d'écran (statut, succès ou échec d'une action, ouverture d'un dialogue), thème **contraste élevé** de Windows suivi en direct, dialogue nommé avec focus cyclique
  et deck désactivé derrière lui, échap ferme le menu, poignée de réordonnancement des intégrations en vrai bouton, noms d'étapes indexés, erreurs de saisie liées à leur champ, avertissement de contraste sur les couleurs libres d'un bouton.
  En mode édition seulement, touches, dock et menus prennent le focus clavier (le deck hors édition reste sans focus).
- **Sécurité** (ADR 0030) : droits caméra, micro et notifications par hôte exact ; un widget ou une page web importés sont interrogés par WebView2 sans mémorisation ; import de configuration : les adresses Hue et OBS modifiées
  désactivent l'intégration ; images contrôlées avant décodage ; les actions HTTP ne suivent plus les redirections ; empreinte des plugins unique, `deps.json` inclus.

### Corrigé
- Une erreur d'action reste affichée jusqu'à « Fermer » (elle disparaissait en 5 s).
- Appui long fantôme sur le deck quand Windows annule un toucher (l'éditeur s'ouvrait doigt levé).
- **Configuration invalide au démarrage** : reprise sur la dernière sauvegarde valide (au lieu d'un deck vide), les copies `config.invalide-*` ne s'accumulent plus (5 au plus, sans doublon).
- **Erreur fatale** : le journal est vidé avant la fin du processus (les dernières lignes étaient perdues avec l'écriture asynchrone).
- **Erreurs de l'interface** : un message identique n'est plus affiché plus d'une fois toutes les 5 s, et le gestionnaire ne peut plus lui-même faire tomber l'application.
- Échec au démarrage : le processus ne reste plus vivant avec le verrou d'instance ; un message d'erreur s'affiche et l'erreur est journalisée.
- Import de profil : la confirmation signale aussi les actions `text`, `hotkey` avec la touche Windows et `url` hors http, https et mailto (elles permettent d'exécuter du code).

## [1.1.1] — 2026-11-03

La 1.1.0 n'a pas été publiée (un test de la barre des tâches a dépassé son délai sur le serveur de construction) : cette version reprend son contenu.

Audit 2 (voir [docs/AUDIT-2.md](docs/AUDIT-2.md)) : freezes, WebView2, fiabilité des intégrations, sécurité. Aucune clé existante n'est renommée ni supprimée ; les clés ajoutées sont facultatives.

### Ajouté
- **Paramètres › Diagnostic** : mémoire de l'application et des processus WebView2 qu'elle a lancés, pauses de l'interface (nombre, plus longue, dernières avec leur contexte), tâches longues,
  état des intégrations, derniers avertissements ; **Copier le diagnostic** (passé par `LogRedactor`, sans secret), actualiser, remettre à zéro, ouvrir le journal.
- **Mode économie** (Paramètres › Apparence, clé facultative `EconomyMode`) : animations coupées, relevés des intégrations espacés par 10, widgets de données par 3, pages web en mémoire basse, vérification du chien de garde ralentie.
- **Panneaux redimensionnables** en mode édition : poignées de 32 DIP entre la grille et le catalogue / l'inspecteur, à glisser, un toucher replie ou déploie ; largeurs mémorisées (clés facultatives
  `CatalogWidth`, `InspectorHeight`, `CatalogCollapsed`, `InspectorCollapsed`). La fenêtre des paramètres mémorise sa taille (`SettingsWidth`, `SettingsHeight`).
- **Plugins : liste d'autorisation** `plugins\trusted.json` (nom du dossier → empreinte SHA-256 de ses bibliothèques), vérifiée **avant** tout chargement. Les plugins déjà posés sont autorisés au
  premier démarrage ; un plugin ajouté ou modifié ensuite est refusé tant que son empreinte (affichée dans le journal) n'est pas listée. ADR 0028.
- **Touche d'intégration indisponible** : anneau rouge en pointillés, touche atténuée et « indisponible » dans son nom accessible, avant l'appui. Le curseur vertical indisponible écrit la raison sous lui.
- Chien de garde de l'interface à **200 ms** (durée, tâche en cours, priorité de la dernière opération) et trace des **tâches longues** autour des minuteries, de `OnDocumentChanged` et de la reconstruction de la grille.
- `dependabot.yml` (actions GitHub et NuGet).
- **Raccourcis clavier globaux** : un bouton peut porter un raccourci (inspecteur › « Raccourci clavier global », clé facultative `GlobalHotkey`) qui le déclenche depuis n'importe quelle application,
  même deck masqué (`IHotkeyService`, `RegisterHotKey` sur un thread dédié dans `Platform/Windows`). Un modificateur est obligatoire, sauf F13 à F24 ; doublons, syntaxe invalide et combinaisons déjà prises par une autre
  application sont signalés (message et journal). Avec `--simulate`, aucun raccourci n'est réservé.
- **Requêtes web : méthode et en-têtes personnalisés** (clés facultatives `Method` et `Headers` de l'étape) : GET, POST, PUT, PATCH, DELETE ; un `Nom: valeur` par ligne, 20 au plus ; `Host`, `Content-Length` et consorts refusés,
  aucun saut de ligne dans une valeur. Validés en direct dans l'éditeur.
- **Profils réordonnables** : « Monter » / « Descendre » dans le menu du profil (annulable) ; ferme l'écart noté dans la matrice de parité.

### Modifié
- **Enregistrement de `config.json` hors du thread d'interface** : l'instantané est sérialisé sur l'UI, un écrivain unique l'écrit (les instantanés successifs fusionnent) ; la relecture et le hachage de la
  surveillance se font aussi hors UI. ADR 0027.
- **Arrêt** : la configuration est enregistrée d'abord, puis les intégrations se déconnectent **en parallèle** sous une échéance de 2 s (jusqu'à 44 s en série auparavant).
- **Programme au premier plan** : événement Windows (`SetWinEventHook`, `IWindowService.ForegroundChanged`) au lieu d'un relevé de 0,8 s ; le relevé de 5 s ne sert plus que de filet.
- **Audio COM, saisie de texte et validation des chemins hors du thread d'interface** (un chemin réseau injoignable ne bloque plus l'éditeur : délai de 250 ms et cache de 10 s).
- **Images** : décodées au démarrage hors de l'UI, plafonnées à 1024 px de large (une image de 8192 px coûtait 256 Mo), une image absente n'est plus gardée « absente » pour toujours.
- **Journal** Serilog asynchrone (file bornée).
- **Intégrations** : toute exception d'un relevé compte comme un échec (un JSON illisible laissait l'intégration « Connecté » avec des valeurs figées) ; l'attente de reconnexion ne repart de zéro qu'après
  30 s de connexion stable, avec une gigue de 20 % ; les commandes s'exécutent hors de l'UI sous une échéance garantie ; Hue et Elgato espacent leurs relevés deck masqué.
- **Paquets** : Microsoft.Extensions.DependencyInjection 10.0.14, Microsoft.Web.WebView2 1.0.4314.35, Serilog.Sinks.Async 2.1.0. Actions GitHub épinglées par empreinte de commit.
- Cibles tactiles : poignée de redimensionnement 44 DIP, pastilles du haut 36, boutons de fenêtre 48, poignée des intégrations 36.

### Corrigé
- **Sécurité** : un `baseurl` ou `apiurl` d'une configuration importée ne peut plus rediriger le jeton d'une intégration vers un autre hôte que la machine locale.
- **Sécurité** : le refus de permission d'une page cachée n'est plus mémorisé dans le profil WebView2 partagé (il bloquait les notifications du cadre visible du même site).
- **`LogRedactor`** masque désormais les cookies, `X-Api-Key`, `Authorization` sans schéma, la clé d'application Hue, les webhooks Discord et Slack, les codes OAuth, les valeurs entre guillemets ; il laissait un `$2` littéral.
- **Badges** : un service dont la page de connexion restait chargée ne rétablissait pas son badge après la connexion faite dans le deck ; l'adresse de départ est retentée (2 min, doublant jusqu'à 30 min).
- **WebView2** : panne de processus filtrée par type (une panne GPU n'affiche plus « a cessé de répondre »), cadre mort recréé, minuterie orpheline arrêtée, environnement en échec non gardé, ouverture des
  sessions cachées échelonnée, course veille / reprise, observateur Google Chat limité aux attributs utiles, relevé de champ borné à 2 s et coupé deck masqué.
- **API locale** : l'écoute ne s'arrête plus définitivement au-delà de 8 clients ; un client muet est lâché après 2 min.
- **Niveau audio** : un délai dépassé laissait la tuile « occupée » pour toujours.
- Texte qui ne revenait pas à la ligne dans les paramètres (état d'une intégration, périphérique, résultat du test).

## [1.0.0] — 2026-10-31

Première version stable : reprend toutes les évolutions des versions 0.3.x (voir ci-dessous).

## [0.3.6-rc] — 2026-10-30

La 0.3.5-rc n'a pas été publiée (tests Discord instables en CI : le faux serveur créait son canal après le début du test) : cette version reprend son contenu. Discord : la liste des canaux locaux est lue sans s'y connecter.

### Modifié
- **Mode édition simplifié** : les boutons « Case » et « Page » du dock disparaissent. Le menu d'une case s'ouvre au **clic droit / appui long** et il est à plat (plus de sous-menus
  Ajouter, Déplacer, Propriétés) ; le menu d'une page s'ouvre au **clic droit / appui long sur son onglet** (renommer, dupliquer, masquer, page web, badge, exclure du total, supprimer).
  Une page change de rang en **glissant son onglet** (souris et doigt) ; « Nouvelle page » reste le bouton +.
- **Raccourcis clavier du mode édition** : Suppr, Ctrl+C / X / V / D, F2 (titre), Entrée (ouvrir le dossier), Alt+flèches (déplacer), Ctrl+Z / Y.
- **Redimensionner un widget depuis la grille** : un widget sélectionné en mode édition a une poignée de coin ; on la glisse (souris ou doigt) et la taille suit, bornée à la page et aux cases libres, en une seule annulation.
- **Inspecteur plus compact** : aperçu plus petit, titre et interrupteur sur la même carte, plus de place pour les réglages.
- **Ordre des intégrations** : dans les paramètres, on glisse la poignée d'une carte sur une autre (ou Alt + flèches) ; l'ordre est enregistré (clé facultative `IntegrationOrder`, absente tant qu'on n'a rien réordonné)
  et les catégories du catalogue le suivent au démarrage suivant.
- **Icônes automatiques** : « Icône du site » (favicon de l'étape Lien web, sur demande) et « Icône de l'appli » (programme ou .ico, plus grande taille) dans l'inspecteur.
- **Glisser-déposer animé** : touche atténuée, case visée et onglet survolé qui s'allument, étiquette qui apparaît en fondu ; une touche lâchée sur un onglet change de page, sur « Retour » sort du dossier ;
  l'ouverture d'un dossier joue un court zoom (sauf animations coupées).
- **Interface plus dense** : cibles de 36 px (menus du deck, clic droit à 32 px, catalogue, inspecteur, paramètres), police et légendes plus petites ; dock, dialogues et widgets du deck gardent 48 px.
- **Espacements plus compacts** : cartes, pastilles, légendes et champs de l'inspecteur, lignes du catalogue et des menus du deck, cartes des paramètres (cibles tactiles toujours de 48 px).

### Supprimé
- **Intégrations vMix, X-Plane, ClickUp et monday.com**, leurs tests, leur documentation et le modèle de profil « Simulation de vol ». Un `config.json` qui les mentionne reste lisible : leurs étapes sont signalées comme type inconnu.

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
