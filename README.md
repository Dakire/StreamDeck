# StreamDak

Tableau de bord de raccourcis façon Stream Deck pour Windows, pensé pour un écran tactile secondaire.
Usage visé : perso, pro IT et gaming.

Chaque bouton lance une ou plusieurs actions : raccourci clavier, saisie de texte, touche média,
programme, script, URL, requête web (webhook), micro et volume, minuteur, changement de page ou de profil, délai,
mise au premier plan d'une fenêtre. Un bouton peut aussi afficher l'heure ou la date.

![Le deck, thème sombre](docs/images/deck-dark.png)

| Thème clair | Éditeur | Paramètres |
|---|---|---|
| ![Deck, thème clair](docs/images/deck-light.png) | ![Éditeur à trois panneaux](docs/images/editeur-dark.png) | ![Paramètres](docs/images/parametres-dark.png) |

*Captures rendues hors écran par le harnais de tests, en mode simulation (d'où le badge « Simulation »).*

## Installer

Télécharge `StreamDak-<version>-self-contained-Setup.exe` (runtime .NET inclus) ou `…-framework-dependent-Setup.exe` (léger) dans les
[Releases](https://github.com/Dakire/StreamDeck/releases). Dossier au choix, raccourcis Bureau et menu Démarrer, lancement avec Windows en option, mise à jour sur place,
désinstallation propre ; **tes profils restent dans `%AppData%\StreamDak`**, jamais dans le dossier d'installation. Détails, choix d'Inno Setup, signature : [Installation](docs/INSTALLATION.md).
Paramètres › **Mises à jour** : *Rechercher des mises à jour*, notes de version, *Installer maintenant* / *Plus tard* / *Ignorer cette version*, canal bêta, vérification
automatique (une fois par jour, désactivable). Rien ne s'installe sans ton accord ; le fichier téléchargé est vérifié (SHA-256 de `checksums.txt`, signature) avant d'être lancé.

## Widgets de données et badges

- **Discord sans raccourci** (Paramètres › Intégrations › Discord) : micro, sourdine, volumes et « quitter le salon » par le **RPC local** de Discord, même quand Discord est
  réduit ou derrière un jeu en administrateur ; voyants en direct. Une application Discord à toi suffit (5 minutes, une fois) : [Discord](docs/integrations/discord.md).
- **Mélangeur audio** (catalogue › Widgets) : un **curseur précis par canal** (Sonar : jeu, discussion, média, aux, micro ; Discord ; périphériques Windows), avec la valeur exacte,
  *couper*, *revenir au niveau de réinitialisation* (↺) et une pastille de disponibilité. Chaque ligne dit ce qu'elle règle (mélangeur Sonar, volume Windows, volume Discord).
- **Niveau audio** (catalogue › Widgets) : une **roue** qu'on tourne au doigt (niveau au centre, toucher le centre coupe), ou un **curseur** vertical ou horizontal, lié à un canal **Sonar** ou à un
  **périphérique audio Windows** choisi par son nom (micro Focusrite, casque Arctis…, sans changer le périphérique par défaut). Choix du canal et de la forme dans les propriétés (mode édition).
  Le gain analogique d'une carte son ne se pilote pas (il se règle dans Sonar). [Sonar et audio par périphérique](docs/integrations/sonar.md), [tests manuels](docs/tests-manuels-sonar.md), [ADR 0024](docs/adr/0024-sonar-audio-par-peripherique-et-controles-de-niveau.md).
- **Widgets** (catalogue › Widgets) : horloge et date, processeur, mémoire, **disque**, **réseau**, **ping / latence** vers des hôtes, **état de services** (page web ou port TCP),
  **météo** (Open-Meteo, sans compte), **prochain événement** (Google Agenda), **compteur** lu dans une réponse JSON (tickets…), **batterie**, **durée de fonctionnement**,
  **périphériques audio actifs**, **lecture en cours** (Spotify, navigateur…), **flux d'actualités** (RSS / Atom). Tailles 1 × 1, 2 × 1, 2 × 2, intervalle réglable ;
  ils ne relèvent rien quand leur page n'est pas affichée ni quand le deck est masqué. Ajouter un widget : [ADR 0019](docs/adr/0019-widgets-de-donnees.md).
- **Badges de notifications** : un badge rouge (« 99+ ») sur les boutons « site web » de Gmail, Google Chat, Discord, Teams, Slack ou d'un service décrit dans `notifications.json`.
  Paramètres › Intégrations › Notifications ; session partagée avec les pages web du deck, aucun mot de passe enregistré : [Notifications](docs/integrations/notifications.md).
  - **Modes par service** (`auto`, `title`, `selector`, `off`) et services personnalisés dans `notifications.json` ; **Google Chat** lit le nombre de non lus dans les libellés d'accessibilité de la page (« 3 messages non lus », « 2 unread ») ; un compteur à 0 ou absent n'affiche **aucun** badge. Le point d'activité (icône de l'onglet) n'existe que sur demande (`"favicon": true` dans la section `badges`).
  - **Onglets** : badge `InfoBadge` (Fluent, thèmes clair et sombre, « 99+ »). **Barre des tâches** : pastille du total de non lus de toutes les pages web de tous les profils.
  - **Par page web** (mode édition › Options de la page) : *Afficher le badge* et *Exclure du total*.
  - Ajouter un service dans le code ou diagnostiquer un badge absent : [Notifications](docs/integrations/notifications.md) et [tests manuels](docs/tests-manuels-badges.md).

## Documentation

| Document | Contenu |
|---|---|
| [Installation](docs/INSTALLATION.md), [Publier une version](docs/RELEASING.md) | Installateurs, signature de code, mises à jour GitHub, workflow de release |
| [Sécurité](SECURITY.md) | Audit, secrets, lancement de scripts, import, réseau, dépendances, reste à traiter |
| [Architecture](docs/ARCHITECTURE.md) | Couches, exécution d'un bouton, mise à jour de la grille, choix de WPF (diagrammes) |
| [Format de configuration](docs/configuration.md) | `config.json` en détail, cycle de vie, migrations, exemple |
| [Développement](docs/DEVELOPMENT.md) | Prérequis, commandes, tests, vérifications manuelles, jetons de design |
| [Contribuer](CONTRIBUTING.md) | Conventions de code, branches, commits, tests |
| [Décisions d'architecture](docs/adr/README.md) | Pourquoi WPF, le document unique, la plateforme abstraite… |
| [Journal des modifications](CHANGELOG.md) | Ce qui a changé, version par version |
| [Parité avec Stream Deck](docs/FEATURE_PARITY.md), [rétro-ingénierie](docs/REVERSE_ENGINEERING.md) | Fonctions couvertes et écarts |
| [Étude des intégrations](docs/integrations-research.md) | OBS, Twitch, Discord, Hue… : faisabilité et ordre de réalisation |
| [Bilan du nettoyage](docs/cleanup-report.md) | Corrections, mesures avant / après, risques restants (phase 1) |
| [Bilan de l'architecture](docs/phase2-report.md) | Trois projets, injection de dépendances, Serilog, gestionnaires d'action (phase 2) |
| [Bilan des intégrations, plugins et API](docs/phase8-9-report.md) | Ce qui est fait, mesures, écarts avec la demande, décisions à prendre (phases 8 et 9) |
| [Intégrations](docs/integrations/) | OBS, vMix, Elgato, Hue, Windows, Teams, X-Plane, Twitch, ClickUp, Notion, monday, Sonar (et audio par périphérique), raccourcis d'applications |
| [Tests manuels Sonar](docs/tests-manuels-sonar.md) | À dérouler sur le PC avec SteelSeries GG : découverte, diagnostic, canaux, roue au doigt |
| [Discord](docs/integrations/discord.md), [tests manuels Discord, web, menus](docs/tests-manuels-discord-web-menus.md) | RPC local, raccourcis et leurs limites ; validation en arrière-plan, pages web passives, mélangeur, nouveaux widgets |
| [Plugins](docs/plugins.md), [API locale](docs/local-api.md) | Ajouter une intégration sans modifier StreamDak ; piloter le deck depuis un script |

## Fonctionnalités

**Le deck (écran tactile)**

- Grille de boutons adaptative (nombre de cases selon la résolution) ou format fixe par profil, avec sous-pages automatiques ;
  touches aux proportions bornées, grille centrée, emplacements libres dessinés « éteints ».
- Touches modernes : corps teinté par la couleur du bouton, couleur pleine sur une pastille d'icône ; variantes média (pastille ronde),
  navigation (contour + chevron), affichage (heure ou minuteur en grand), image plein cadre ; badge du nombre d'étapes.
- Retour d'exécution sur la touche : anneau si l'action dure, coche si elle réussit, alerte et message si elle échoue (étape en cause).
- Barre du haut : profil actif (toucher pour en changer), badges *Mode édition* et *Simulation*. Dock du bas : pages, sous-pages, menu.
- Profils, pages en onglets, dossiers et sous-dossiers ; pages masquées accessibles par une action `page`.
- Fenêtre qui ne prend pas le focus : les raccourcis arrivent à l'application active.
- Plein écran sur l'écran secondaire ou mode fenêtre, sans masquer la barre des tâches (le deck occupe la zone de travail) ; le deck suit le branchement et le débranchement de l'écran, et se cale exactement même entre écrans de densités différentes.
- Profil associé à une application : il s'affiche seul quand le programme est au premier plan.
- **Édition intégrée**, sans aucune fenêtre séparée (menu ⚙ › *Modifier le deck*, *Modifier…* ou *Nouveau bouton…* dans le menu d'une case, ou `StreamDak.exe --edit`) : un panneau d'**actions** apparaît à gauche et
  les **propriétés** du bouton choisi à droite, la grille garde le même nombre de cases. Toucher une touche la sélectionne, toucher une action
  la pose dans la case choisie ; *Annuler* / *Rétablir* sont dans le dock, *Terminer* quitte le mode. La fenêtre n'est activable que pendant
  l'édition (saisie au clavier) ; en sortant elle ne prend de nouveau jamais le focus et le rend à l'application précédente.
- Thème **Material Design 3** : palettes tonales (sombre, AMOLED, clair) générées depuis une couleur source, surfaces à élévation tonale, sans ombre portée.
- **Création rapide**, sans ouvrir l'éditeur : **clic droit** ou **appui long** (environ 0,55 s, doigt immobile) sur une **case vide**
  ouvre un menu : *Nouveau bouton…* (le panneau d'actions s'ouvre à gauche, avec sa recherche : « chrome » ou « vol »), *Parcourir les actions*
  (catégories, au toucher), des **modèles en un clic** (raccourci clavier, lancer une application, ouvrir un site, volume / muet,
  média, script, dossier, page) et *Coller*. Chaque création est une seule modification annulable, avec un bouton
  *Annuler* dans le bandeau d'état. Une page ou un dossier vide propose *Ajouter un bouton* sur sa première case.
- **Glisser-déposer** d'un fichier, d'un raccourci `.lnk`, d'un exécutable, d'un dossier, d'un raccourci Internet ou d'une adresse
  web sur une case vide : le bouton *Lancer* ou *Ouvrir* est créé sans dialogue, avec le bon titre et l'icône de l'application.
  Plusieurs éléments remplissent les cases libres suivantes (douze au plus), en une seule annulation.
- Sur un **bouton**, le même geste ouvre son menu : *Modifier…* (éditeur rapide : le titre est sélectionné), *Dupliquer* (dans la
  prochaine case libre), *Copier*, *Couper*, *Supprimer*. En mode édition, le même geste ouvre le menu complet de la case (déplacer, ranger dans un dossier, réinitialiser…). L'appui long n'exécute pas l'action ; un glissement du
  doigt l'annule ; le toucher ne déplace jamais le curseur. Un bouton qui échoue propose *Modifier* dans le bandeau d'état.

**Le mode édition** (il n'y a plus de fenêtre d'édition : tout se passe autour de la grille du deck)

- Panneau d'**actions** à gauche (recherche instantanée, 8 catégories repliables, favoris, récentes) et **propriétés** du bouton à droite,
  comme Elgato Stream Deck ou Corsair iCUE. Les boîtes de dialogue (nom d'une page, confirmation d'une suppression) s'affichent
  elles aussi dans le deck : plus de fenêtre qui s'ouvre derrière lui ou sur un autre écran.
- Dock en édition : annuler / rétablir (100 niveaux), *Page* (renommer, dupliquer, masquer, déplacer, supprimer la page), *Quitter* (sort en annulant tout ce qui a été fait depuis l'ouverture, après confirmation) et *Terminer* (garde les modifications).
- **Glisser une touche** (souris ou doigt) pour la déplacer : elle s'atténue, la case visée s'allume et une étiquette qui suit le pointeur dit ce que ferait le dépôt (déplacer, échanger, ranger dans le dossier).
  La pastille du profil ouvre ses options : changer de profil, format de la grille (3×2, 5×3, 6×3, 5×4, 8×4 ou automatique),
  renommer, dupliquer, exporter / importer (`.streamdak`), associer à une application, nouveau profil depuis un modèle, supprimer.
- Propriétés : aperçu, titre, étapes multiples (ajout, ordre, suppression), validation en direct ; position du titre, icône Fluent /
  emoji / texte, image importée et couleurs sont dans un bloc *Apparence* replié par défaut, avec réinitialisation de l'apparence.
  La saisie est un brouillon : l'aperçu suit chaque caractère, mais la configuration n'est écrite qu'à la validation, en une seule
  modification annulable. Échap abandonne la saisie en cours ; *Annuler* rend à la case son état d'avant.
- Paramètres (fenêtre séparée, menu ⚙ › *Paramètres*) : thème, animations, icônes, fond, plein écran, premier plan, grille, écran du
  deck, lancement avec Windows, sauvegardes (export, import, restauration), raccourcis.

Toucher un bouton en mode édition ne l'exécute jamais.

**Fenêtre (Windows 11)** : survoler le bouton plein écran de la barre du haut affiche les dispositions d'accrochage ; la fenêtre s'accroche en la glissant vers un
bord ou le haut d'un écran (plein écran), ou avec Win + flèches, et se redimensionne à la souris. Au doigt : glisser la barre pour déplacer, double-toucher pour le
plein écran, comme avant. Le deck ne prend jamais le focus.

**Widgets** (catalogue › *Widgets*, ou clic droit › *Parcourir les actions*) : des cases qui affichent une information au lieu de lancer une action,
sur plusieurs cases (de 1 × 1 à 5 × 3, réglable dans les propriétés ; les cases voisines doivent être vides).

- **Horloge** : l'heure en grand, la date, secondes et 12 heures en option.
- **Agenda** : les prochains événements de ton agenda Google (nombre et période réglables), mis à jour toutes les 5 minutes. Demande de connecter
  Google Workspace (Paramètres › Intégrations, voir `docs/integrations/google.md`) ; sans cela, la tuile dit quoi faire.
- **Google Chat, Google Agenda (page web) et Gmail** : trois pages prêtes à l'emploi (catalogue › *Widgets*), à connecter une fois à ton compte dans le cadre.
- **Processeur** et **Mémoire** : la charge du processeur (%) et la mémoire utilisée (Go sur le total) avec une jauge qui passe au rouge
  à partir de 85 %, rafraîchies chaque seconde. Ils activent d'eux-mêmes le connecteur *Système Windows* (aucun compte, aucun réglage).
- **Page web** : un site dans un cadre isolé (WebView2) : chat, tableau de bord, météo. Adresse `https://…` saisie dans les propriétés.
  La page ne peut rien faire sur StreamDak (pas de téléchargement ni de fenêtre). Un menu ou une boîte de dialogue
  masque temporairement la page ; la page n'est **pas rechargée** quand tu changes de page du deck (le cadre est gardé, 6 au plus) ; **zoom** avec − / + / « 100 % » dans la barre, au pincement
du doigt ou à Ctrl + molette, mémorisé. **Notifications, micro et caméra** sont autorisés aux pages `https` du même site que l'adresse saisie (rappels de
Google Agenda, Meet) ; tout autre droit est refusé, et Windows doit aussi autoriser micro et caméra aux applications de bureau.
**Interaction avec la souris et le doigt** (propriétés du widget, ou *Page* › *Interaction avec la page*) : *Toucher pour interagir* (défaut : la page s'affiche sans rien capter ; un
toucher ou un clic l'ouvre à l'interaction, qui se termine seule après 30 s sans geste ou par le bouton *Interagir*), *Passive* (affichage seul ; bouton *Interagir* pour s'en servir) ou
*Interactive* (la page reçoit tout, en permanence). Hors interaction, appui long et clic droit sur la page ouvrent le menu de la case ; en mode édition, la page ne capte jamais rien.
Pendant l'interaction, la saisie au clavier démarre toute seule quand un champ de texte de la page a le focus (et rend le focus en le quittant) ; le bouton *Saisie au clavier* de la tuile la force. Le bouton *Retour* de la barre du cadre revient à
  la page précédente (ou à la page de départ) ; un lien « nouvel onglet » vers un autre site (un document reçu dans Chat) s'ouvre dans le navigateur
  par défaut. Le bouton *Ouvrir dans le navigateur* de la barre du cadre ouvre l'adresse dans le navigateur par défaut ; si Google refuse la connexion dans un cadre intégré, la tuile le dit.

**Page web en plein cadre** : une page entière peut afficher un site à la place de la grille de boutons. En mode édition, bouton *Page* du dock ›
*Afficher une page web…* (« calendar.google.com » est complété en `https://…`), puis *Adresse de la page web…* pour la changer ou *Afficher une
grille de boutons* pour revenir (les boutons de la page sont conservés). Les règles du cadre sont celles du widget Page web.

**Intégrations** (Paramètres › Intégrations, chacune activée à la demande)

- Connecteurs livrés : **OBS Studio**, **vMix**, **lampes Elgato**, **Philips Hue**, **Système Windows** (processeur, mémoire, média en cours,
  donc Spotify sans compte), **Microsoft Teams**, **X-Plane 12**, **Twitch**, **ClickUp**, **Notion**, **monday.com**, **SteelSeries Sonar** (volume et coupure des canaux du mélangeur ; API non vérifiée, [Sonar](docs/integrations/sonar.md)). 35 boutons de
  raccourcis prêts à l'emploi pour Zoom, Teams, Discord, Premiere, Photoshop, DaVinci, Blender, Outlook, Word, Excel et Star Citizen. **Discord** par son RPC local
  ([Discord](docs/integrations/discord.md)). Outils d'administration (Services, Observateur d'événements, Gestion des disques, Connexions réseau, panneau Son…),
  terminal dans un dossier, SSH, projet dans VS Code, **chronomètre**.
- Les commandes sont des actions du catalogue (une catégorie par intégration). L'état s'affiche sur les touches : voyant plein / vide
  (micro coupé, direct en cours…) et valeurs dans les titres, par exemple `Scène : {obs.scene}`, `CPU {sys.cpu} %`.
- Une intégration en panne ne gêne jamais le deck : reconnexion automatique (2, 4, 8… 60 s), état lisible dans les paramètres.
- Mots de passe et jetons chiffrés pour ton compte Windows (`secrets.dat`), jamais dans `config.json` ni dans le journal.
- Modèles de profils (réunions, streaming OBS, montage, simulation de vol, poste de travail), API locale par canal nommé, plugins
  posés dans `plugins\`, instance unique et icône de la zone de notification.

### Raccourcis de l'éditeur

| Raccourci | Effet |
|---|---|
| `Ctrl+Z`, `Ctrl+Y` / `Ctrl+Maj+Z` | Annuler, rétablir |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+D` | Copier, couper, coller, dupliquer la case sélectionnée |
| `Suppr` | Supprimer le bouton |
| `F2` | Modifier le titre |
| `Entrée` | Ouvrir le dossier / configurer le bouton |
| `Retour arrière` | Remonter d'un dossier |
| `Alt+flèches` | Déplacer le bouton (échange avec la voisine) |
| `Ctrl+F` | Chercher dans le catalogue |
| `Maj+F10`, touche Menu | Menu contextuel |
| `Échap` | Fermer un menu, annuler un glisser-déposer |

Ces raccourcis ne s'appliquent jamais pendant la saisie dans un champ de texte.

## Prérequis

- Windows 10 ou 11 (Windows 11 recommandé : effet Mica dans l'éditeur et les paramètres ; le deck a son propre fond, opaque)
- Runtime Desktop .NET 10 pour exécuter
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) pour compiler, runtime Desktop pour exécuter

## Compiler, lancer, tester

Prérequis : Windows 10 ou 11, le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (version épinglée par `global.json`) et Git. Rien d'autre pour compiler ;
le runtime WebView2 (inclus dans Windows 11) est nécessaire pour les pages web et les badges.

```powershell
git clone git@github.com:Dakire/StreamDeck.git
cd StreamDeck
dotnet build -c Release              # compilation : aucun avertissement toléré (analyseurs, sécurité, style, documentation XML)
dotnet run -c Release                # lance le deck
dotnet run -c Release -- --simulate  # essai sans risque : les actions sont affichées, rien n'est envoyé au système
dotnet run -c Release -- --edit      # démarre en mode édition
dotnet test tests/StreamDak.Tests    # tests automatisés (≈ 1 000, 10 minutes ; l'interface est rendue hors écran, aucune fenêtre ne s'affiche)
```

Pour essayer sans toucher à ta configuration : `$env:STREAMDAK_DATA_DIR = "$env:TEMP\streamdak-essai"`.
Fermer l'application avant de recompiler (ses fichiers sont verrouillés tant qu'elle tourne). Une seule série de tests à la fois, par exemple
`dotnet test tests/StreamDak.Tests --filter "FullyQualifiedName~Notification"`. `STREAMDAK_SHOTS=<dossier>` garde les captures PNG rendues par les tests d'interface.

## Générer les installateurs

Prérequis supplémentaire : [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`). Tout passe par `build.ps1`, à la racine :

```powershell
./build.ps1                                       # tests, puis les deux installateurs + checksums.txt dans artifacts/installer
./build.ps1 -SkipTests                            # sans relancer les tests
./build.ps1 -Flavor self-contained                # une seule variante (self-contained ou framework-dependent)
./build.ps1 -Version 1.4.0                        # version imposée (sinon <Version> de Directory.Build.props)
./build.ps1 -Sign                                 # avec signature de code (certificat par variables d'environnement, voir installer/sign.ps1)
./build.ps1 -Iscc "D:\Outils\Inno Setup 6\ISCC.exe" # si Inno Setup n'est pas à un emplacement habituel
./build.ps1 -NoWebView2Redist                     # hors ligne : n'embarque pas le programme d'installation WebView2
```

Résultat dans `artifacts/installer` (ignoré par git) :

| Fichier | Contenu |
|---|---|
| `StreamDak-<version>-self-contained-Setup.exe` | runtime .NET embarqué (≈ 65 Mo) : variante par défaut |
| `StreamDak-<version>-framework-dependent-Setup.exe` | léger (≈ 11 Mo), demande le runtime « .NET Desktop 10 » |
| `checksums.txt` | sommes SHA-256 de ces fichiers (exigées par la mise à jour automatique) |

Publication seule, sans installateur : `dotnet publish StreamDak.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish`.
Publier une version sur GitHub (tag `vX.Y.Z`, le workflow construit et publie) : [Publier une version](docs/RELEASING.md). Détails des installateurs : [Installation](docs/INSTALLATION.md).

## Configuration

Dossier : `%APPDATA%\StreamDak` (ou le dossier de la variable d'environnement `STREAMDAK_DATA_DIR`).

| Élément | Rôle |
|---|---|
| `config.json` | Configuration, créée au premier lancement. Surveillée : toute modification enregistrée est appliquée sans redémarrer. |
| `Icons\` | Images des boutons (PNG, nommées d'après leur contenu). |
| `Backups\` | Sauvegardes automatiques (15 conservées). |
| `config.v1.backup.json` | Copie intacte de l'ancien fichier, faite une seule fois lors du passage aux profils. |
| `streamdak.log` | Journal : démarrage (avec le temps de chaque étape), erreurs, actions en échec, changements d'écran. 1 Mo par fichier, 3 fichiers conservés (`streamdak.log`, `streamdak_001.log`…). |

L'éditeur enregistre à chaque modification validée (écriture atomique : un arrêt en cours d'écriture ne corrompt pas le fichier).
Un fichier `config.json` invalide n'est jamais écrasé sans copie : il est conservé sous `config.invalide-<date>.json`.

Le format complet (modèle, cycle de vie, migrations, exemple) est décrit dans [docs/configuration.md](docs/configuration.md).
Les tableaux ci-dessous en donnent l'essentiel.

### Clés de `config.json`

| Clé | Rôle | Défaut |
|---|---|---|
| `Version` | Version du format (`2`) | `2` |
| `Monitor` | Index de l'écran, `-1` = premier écran secondaire | `-1` |
| `MonitorDevice` | Identifiant Windows de l'écran choisi (`\\.\DISPLAY2`), prioritaire sur `Monitor` dont l'index change au rebranchement ; facultatif | absent |
| `Fullscreen` | Plein écran sur l'écran cible | `true` |
| `AlwaysOnTop` | Reste au-dessus des autres fenêtres | `false` |
| `Columns` / `Rows` | Taille de la grille pour les profils en format automatique, `0` = selon l'écran | `0` |
| `Spacing` | Espacement entre boutons (DIP) | `14` |
| `Background` | Couleur de fond ; la valeur par défaut affiche le fond nuancé du thème | `#0E1015` |
| `Theme` | `system` (suit Windows), `dark` ou `light` | `dark` à la création, sinon inchangé |
| `IconStyle` | `fluent` : emojis connus affichés en icônes Fluent ; `emoji` : tels quels | `fluent` |
| `StartWithWindows` | Reflet du lancement à l'ouverture de session (le registre fait foi) | |
| `ReduceMotion` | Supprime les animations | `false` |
| `ActiveProfile` | Profil affiché sur le deck | premier profil |
| `Favorites`, `Recents` | Identifiants d'actions du catalogue | `[]` |
| `Profiles` | Liste de profils : `Name`, `Columns`, `Rows`, `AppMatch`, `Pages` | |
| `Integrations` | Par identifiant d'intégration : `Enabled` et `Values` (adresse, port…). Les secrets n'y sont jamais : `secrets.dat` | absent |

Un profil contient des `Pages` ; une page, des `Buttons` dans l'ordre de la grille. Un bouton :

| Clé | Rôle |
|---|---|
| `Label`, `Icon`, `Color`, `Actions` | Titre, icône (nom Fluent, emoji ou texte), couleur, étapes |
| `HideTitle`, `TitlePosition` | Masquer le titre ; `top` / `middle` / `bottom` (absent : sous l'icône) |
| `TextColor` | Couleur du texte et de l'icône |
| `ImagePath`, `ImageFit` | Image importée (nom dans `Icons\`), `cover` ou `contain` |
| `IsFolder`, `Children` | Dossier et son contenu |
| `Blank` | Case vide qui garde la position de ses voisines |

**Ancien format** : un `config.json` sans `Version`, avec `Pages` à la racine, se charge tel quel. Il est converti en un profil
« Principal » et réécrit au nouveau format, après copie dans `config.v1.backup.json`. Aucune clé n'est supprimée ni renommée,
sauf `Pages` qui descend dans `Profiles[].Pages`. Les nouvelles clés sont facultatives.

### Types d'actions

| `Type` | `Value` | `Args` |
|---|---|---|
| `hotkey` | Combinaison, ex. `ctrl+shift+esc` | |
| `text` | Texte à saisir | |
| `media` | `playpause`, `prev`, `next`, `stop`, `mute`, `voldown`, `volup` | |
| `launch` | Programme à lancer | Arguments |
| `script` | Fichier `.ps1`, `.bat`, `.cmd` ou commande PowerShell | Arguments |
| `url` | Adresse à ouvrir (`javascript:`, `vbscript:`, `data:` et `file:` sont refusés) | |
| `page` | Nom de page, `home` ou `back` | |
| `profile` | Nom du profil à afficher | |
| `delay` | Durée en millisecondes | |
| `focus` | Titre ou processus de la fenêtre | Programme à lancer si absente |
| `http` | Adresse `http://` ou `https://` (requête GET) | Corps facultatif : requête POST, en JSON s'il commence par `{` ou `[` |
| `audio` | `mic-toggle`, `mic-mute`, `mic-unmute`, `out-toggle`, `volume` | Niveau 0 à 100 pour `volume` |
| `audiodevice` | `toggle`, `mute`, `unmute`, `volume`, `list` | Périphérique audio par son nom : `capture:Focusrite`, `render:Arctis=50`, `Focusrite=+5` (le défaut de Windows n'est jamais changé) |
| `sonar` | `volume`, `mute`, `unmute`, `togglemute`, `diagnose` | Canal Sonar : `game=60`, `chatRender=+5`, `micro` (SteelSeries GG requis, API non vérifiée) |
| `timer` | Durée en secondes ; toucher à nouveau arrête le minuteur | |

Chaque étape a un délai maximal (5 s pour le clavier, 10 s pour `http` et `focus`, 15 s pour lancer un programme ;
aucun pour `delay` et `timer`). La première étape en échec arrête la séquence, et la touche affiche une alerte.

**Titres dynamiques** : `{time}` (heure), `{date}` (date), `{time:HH:mm:ss}`, `{date:dddd d MMMM}` (formats .NET, en français) et
`{timer}` (temps restant du minuteur du bouton). Le titre enregistré ne change pas ; seul l'affichage est calculé.
Un bouton sans action dont le titre contient `{time}` ou `{date}` sert d'horloge.

## Import et export

- **Profil** : éditeur › menu du profil › *Exporter…* / *Importer…*.
- **Configuration complète** : Paramètres › Sauvegardes.

Le format `.streamdak` est une archive zip (`profile.json` ou `config.json`, `icons/*.png`). L'import vérifie la taille,
le nombre d'éléments, les types d'actions et les noms d'images, ne lit jamais de chemin libre dans l'archive, demande
confirmation (en signalant les actions qui lancent des programmes ou des scripts), fait une sauvegarde puis peut être annulé.

## Structure du dépôt

| Dossier / fichier | Contenu |
|---|---|
| `Core/` (projet `StreamDak.Core`, `net10.0`) | Logique sans WPF ni Windows : `Models`, `DeckDocument` (opérations, historique), `ConfigStore`, `ConfigNormalizer` (migration), `ActionTypes` (clés du format), `ActionCatalog`, `QuickCreate` (modèles, suggestions), `DroppedItem` (éléments déposés), `ActionValidator`, `HotkeySyntax`, `Displays`, `LabelTemplate`, `Countdowns`, `Log` (façade) |
| `Core/Actions/` | Moteur d'actions (`ActionEngine`, `ActionRegistry`), un gestionnaire par type dans `Handlers/`, interfaces de plateforme, plateforme simulée |
| `Platform/Windows/` (projet `StreamDak.Platform.Windows`) | Implémentation Windows des interfaces de plateforme : `SendInput`, fenêtres Win32, Core Audio, registre. Seul `WindowsPlatform` est public |
| `Core/Integrations/`, `Core/Api/`, `Core/Plugins/` | Cadre d'intégrations (`IIntegration`, `IntegrationManager`, bases HTTP et REST), API locale, chargeur de plugins |
| `Integrations/<Nom>/` | Un projet par connecteur : Obs, Vmix, ElgatoLight, Hue, WindowsSystem, XPlane, Teams, Twitch, ClickUp, Notion, Monday, Sonar |
| `samples/HelloPlugin` | Exemple de plugin |
| `Services/` | `AppHost` (racine de composition, journal Serilog), `ConfigPersistence` (sauvegarde auto, rechargement à chaud), `AppearanceService`, `AppWindows`, `DisplayService`, `IconStore`, `DropPayload` (lecture d'un glisser-déposer), `ProfilePackage`, `TransferService`, `Notifier`, `Motion` |
| `ViewModels/` | `MainViewModel` (deck ; menu des cases, glisser-déposer et mode édition en classes partielles), `EditorViewModel`, `PropertiesViewModel`, `CatalogViewModel`, `DeckDialogViewModel`, `SettingsViewModel`, `MenuSpec`, `CatalogMenu` |
| `Controls/` | `TileFace` (rendu d'un bouton), `ContextMenuFactory`, `PromptWindow` (confirmations des paramètres), `PropertiesPanel` et `CatalogPanel` (panneaux d'édition du deck) |
| `MainWindow.xaml(.cs)` | Deck ; code-behind limité à Win32 et au placement multi-écran |
| `SettingsWindow.xaml(.cs)` | Paramètres (dialogues, focus, clavier) |
| `Theme.cs`, `Core/MaterialColor.cs`, `IconCatalog.cs`, `App.xaml` | Palettes Material Design 3 générées depuis une couleur source (contrastes vérifiés), icônes, styles |
| `docs/` | Architecture, configuration, développement, décisions (`adr/`), parité fonctionnelle, intégrations, bilans |
| `tests/StreamDak.Tests` | Tests xUnit : logique, migration, import, contrastes, navigation du deck, robustesse, composition (conteneur), architecture (frontières entre projets), rendu hors écran des fenêtres, mesures de performance (facultatives) |
| `Directory.Build.props`, `.editorconfig` | Analyseurs, style vérifié à la compilation, avertissements bloquants, documentation XML obligatoire |

## Accessibilité

Contraste des textes et des anneaux de focus vérifié par tests (WCAG 2.2 AA) en thèmes clair, sombre et AMOLED ; un test exige un nom accessible pour chaque contrôle interactif du deck, de l'éditeur, des paramètres et du panneau rapide ; focus clavier visible ;
état jamais porté par la couleur seule (icône + texte pour les erreurs, coche sur les couleurs, bordure + anneau pour la sélection) ;
noms accessibles et annonces polies (`LiveSetting`) pour les notifications et erreurs ; cibles de 48 px minimum ;
animations coupées par le réglage StreamDak ou celui de Windows.

## Limites connues

- Pas de périphérique matériel : StreamDak n'est pas un pilote de Stream Deck Elgato. Le « périphérique » est l'écran du deck.
- Plugins : seulement du code local posé par l'utilisateur dans `plugins\` (jamais téléchargé), exécuté avec ses droits (voir `docs/plugins.md`).
- Pas de traduction : l'interface est en français, il n'y a pas de sélecteur de langue.
- Les profils par application se basent sur le nom du programme au premier plan, relevé toutes les 0,8 s.
- Le glisser-déposer tactile dépend de la prise en charge de Windows ; le presse-papiers interne et `Alt+flèches` / le menu *Déplacer* offrent des alternatives.
- La touche Menu (ou Maj+F10) n'ouvre pas le menu d'une case : le deck ne prend jamais le focus clavier. Clic droit, appui long, ou bouton **Case** du dock en mode édition.
- Discord : le RPC n'est accordé qu'à une application dont tu es propriétaire (ou testeur) ; rejoindre un salon précis, la caméra et le partage d'écran ne sont pas pilotés.
- Audio : le périphérique par défaut de Windows ne se change pas depuis StreamDak (pas d'API publique) : le bouton *Son : périphériques* ouvre le panneau de Windows. Le volume par
  application et le gain matériel d'une carte son ne sont pas pilotés.
- Le panneau de création rapide est une petite fenêtre (le deck n'accepte pas la saisie) : la recherche demande un clavier ou le clavier tactile de Windows ; les modèles et *Parcourir les actions* se font sans clavier.
- Les images importées ne sont jamais supprimées automatiquement (les sauvegardes peuvent encore les référencer).

## FAQ

**Un raccourci n'arrive pas dans l'application.**
- Vérifiez d'abord que l'application au premier plan n'est pas lancée en administrateur. Windows refuse la saisie simulée vers une
  application élevée depuis une application qui ne l'est pas ; la touche passe en erreur avec ce message, et le journal le signale.
- Discord : utilisez l'intégration Discord (aucune touche simulée) ; un raccourci Ctrl+Maj+M n'est qu'un raccourci *local* de Discord.
- Sinon, placez une étape « Fenêtre au premier plan » avant le raccourci, suivie d'une pause d'environ 150 ms.

**Le toucher agit sur le mauvais écran.**
Le câble HDMI ne transporte que l'image. Associez le toucher au bon écran dans Paramètres Windows › Affichage ›
« Étalonner l'écran pour la saisie tactile ».

**StreamDak dit que ma configuration est invalide.**
Le fichier fautif est conservé sous `config.invalide-<date>.json` et StreamDak démarre sur une configuration par défaut.
Corrigez le JSON, ou restaurez une sauvegarde depuis Paramètres › Sauvegardes.

**Comment repartir de zéro ?**
Quittez StreamDak et renommez `%APPDATA%\StreamDak\config.json`. Une configuration par défaut est créée au lancement
suivant.

**Où trouver le journal ?**
Dans `%APPDATA%\StreamDak\streamdak.log`. Le bouton Paramètres › « Ouvrir le dossier de configuration » y mène directement.
Variables de diagnostic : `STREAMDAK_LOG_LEVEL=warning` (journal plus court ; `info` par défaut), `STREAMDAK_TOUCHLOG=1` (détail du toucher). Les étapes lentes
sont consignées d'elles-mêmes : fermeture des paramètres (durée par étape), menu de case de plus de 50 ms, touches refusées par Windows.

**Comment essayer sans risque ?**
Lancez `StreamDak.exe --simulate` : les actions s'affichent au lieu d'être exécutées. Ajoutez `STREAMDAK_DATA_DIR` pour
utiliser un dossier de configuration jetable.

## Sécurité

Les actions `script` et `launch` exécutent du code avec vos droits. N'importez que des fichiers de confiance : l'import liste ces actions et demande confirmation.
Les arguments d'un script ne sont jamais assemblés en ligne de commande, les secrets sont chiffrés (DPAPI) et masqués dans le journal, aucune télémétrie.
L'audit complet, ses résultats et ce qui reste à traiter sont dans [SECURITY.md](SECURITY.md).
