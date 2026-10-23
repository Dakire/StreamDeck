# Étude des intégrations

Étude du 19 octobre 2026, avec l'état de réalisation du 20 octobre 2026 (section 0). Ce document recense, pour chaque logiciel cible, comment StreamDak (WPF / .NET 9, Windows)
peut s'y connecter. Il a servi à choisir les intégrations à réaliser ; la section 0 dit lesquelles le sont.

Règles de rédaction :

- Les affirmations reposent sur la documentation officielle quand elle existe. Sinon, la source est signalée comme
  tierce ou communautaire.
- Une information non confirmée est marquée **à vérifier**.
- WPF ne fonctionne que sous Windows : un logiciel réservé à macOS est **non réalisable en natif**.

Légende : effort **S** (quelques jours), **M** (une à deux semaines), **L** (plus, ou dépendance externe forte).

## 0. État de réalisation (20 octobre 2026)

| Intégration | État | Détail |
|---|---|---|
| OBS Studio | **Faite** | `docs/integrations/obs.md` — scènes, micro, direct, enregistrement, caméra virtuelle, sources, relecture ; voyants et valeurs |
| vMix | **Faite** | `docs/integrations/vmix.md` — API HTTP, entrées active et aperçu, enregistrement, diffusion |
| Lampes Elgato | **Faite** | `docs/integrations/elgato.md` |
| Philips Hue | **Faite** | `docs/integrations/hue.md` — CLIP v2, association par le bouton du pont |
| Windows (système, média, Spotify par SMTC) | **Faite** | `docs/integrations/windows.md` — processeur, mémoire, média en cours ; pas de GPU ni de températures (pilote noyau) |
| Raccourcis d'applications (Zoom, Teams, Discord, Adobe, DaVinci, Blender, Office, Star Citizen) | **Faite** | `docs/integrations/raccourcis-applications.md` — 35 boutons prêts à l'emploi |
| Microsoft Teams (API locale) | **Faite, à vérifier sur un vrai Teams** | `docs/integrations/teams.md` — testée contre un faux serveur |
| X-Plane 12 | **Faite, à vérifier sur un vrai simulateur** | `docs/integrations/xplane.md` |
| Twitch | **Faite** (commandes seulement) | `docs/integrations/twitch.md` — jeton fourni par l'utilisateur ; pas d'EventSub |
| ClickUp, Notion, monday.com | **Faites** | `docs/integrations/gestionnaires-de-taches.md` |
| Plugins locaux | **Faits** | `docs/plugins.md` — pour tout ce qui n'est pas livré |
| Streamlabs Desktop | Non faite | Protocole peu documenté (**à vérifier**) ; OBS couvre la plupart des usages |
| Google Workspace | **Faite** (Sheets, Docs, Agenda) | `docs/integrations/google.md` — ton projet Google Cloud, OAuth de bureau |
| YouTube | Non faite | Quota de 10 000 unités par jour et application à faire vérifier : à décider par toi |
| Microsoft 365 (Graph) | Non faite | Enregistrement d'une application Entra ID nécessaire |
| Discord RPC | Non faite | Approbation de Discord requise : les raccourcis globaux sont proposés à la place |
| Spotify Web API | Non faite | Premium et 5 utilisateurs maximum ; le média en cours passe déjà par SMTC |
| Voicemod | Non faite | Clé `clientKey` à demander à Voicemod |
| Wave Link, MSFS (SimConnect), LIFX, Adobe UXP, DaVinci scripting, Blender (add-on), cockpits | Non faites | Protocole non officiel, SDK à redistribuer, ou plugin externe à écrire : voir les sections suivantes ; réalisables en plugin |
| Ecamm Live, macOS | **Impossibles** | WPF ne fonctionne que sous Windows |

## 1. Synthèse

| Intégration | Connexion | Authentification | Plateforme | Bibliothèque .NET | Actions et états lisibles | Contraintes | Effort / valeur |
|---|---|---|---|---|---|---|---|
| **OBS Studio** | obs-websocket v5 : JSON sur WebSocket. Port 4455 par défaut (**à vérifier** : le document de protocole ne l'indique pas) | Mot de passe facultatif : défi SHA-256 avec sel (`Hello` / `Identify`) | Windows, macOS, Linux | `obs-websocket-dotnet` 5.0.0.2 (la v5 est incompatible avec la v4.9) | Scène (`SetCurrentProgramScene`), micro (`ToggleInputMute`), sources (`SetSceneItemEnabled`), stream, enregistrement, caméra virtuelle, relecture. États : `CurrentProgramSceneChanged`, `InputMuteStateChanged`, `StreamStateChanged`, `RecordStateChanged` | Aucune ; mot de passe à protéger | S-M / **forte** |
| **Streamlabs Desktop** | API locale : JSON-RPC sur canal nommé, ou WebSocket SockJS (port 59650 selon des sources tierces, **à vérifier**) | Jeton (Paramètres → Remote Control) ; désactivée par défaut | Windows, macOS | Aucune bibliothèque maintenue : client JSON-RPC à écrire | Scène, sources visibles ou masquées, mute (démo officielle). Événements : **à vérifier** | Documentation officielle mince ; maintenance du dépôt incertaine | M / moyenne |
| **Twitch** | API REST Helix et EventSub par WebSocket | OAuth2, jeton utilisateur. `clips:edit` pour créer un clip ; scopes de chat et de modération **à vérifier** | Web | TwitchLib (`TwitchLib.EventSub.Websockets` 0.6.0 ; version de `TwitchLib.Api` **à vérifier**) | Créer un clip, lancer une publicité, changer le titre ou la catégorie, envoyer un message, modérer. États par EventSub : abonnements, raids, chat | 3 WebSocket par utilisateur (RFC, **à vérifier**) ; abonnements perdus à la déconnexion ; limite de débit Helix **à vérifier** | M-L / forte (streamers) |
| **YouTube** | Data API v3 et Live Streaming API | OAuth2 Google. Scopes sensibles : vérification de l'application | Web | `Google.Apis.YouTube.v3` | Chat : `liveChatMessages.list` et `streamList` (5 unités), `insert` (20), bannissement (`liveChatBans.insert`, 200), changement d'état du direct (`liveBroadcasts.transition`, 1). Création de clip : absente de l'API (**à vérifier**) | **10 000 unités par jour** et par projet. Plafond de 100 utilisateurs tant que l'application n'est pas vérifiée | L / moyenne |
| **vMix** | API HTTP (`/api/?Function=…`, port du serveur Web, 8088 par défaut) et API TCP (port 8099 : `FUNCTION`, `SUBSCRIBE TALLY` / `ACTS`) | Aucune, ou identifiants du serveur Web s'ils sont activés | Windows | Inutile : HTTP et TCP simples | Toutes les fonctions de raccourci de vMix (transitions, entrées, incrustations, enregistrement). États : tally, activateurs, XML d'état | Licence vMix | **S / forte (régie)** |
| **Ecamm Live** | — | — | **macOS uniquement** | — | — | **Non réalisable en natif.** Pilotage à distance par le réseau : aucune API publique trouvée (**à vérifier**) | — |
| **Discord** | (a) Raccourcis globaux réglés dans Discord. (b) RPC local par canal nommé `\\?\pipe\discord-ipc-{n}` | (b) OAuth2 avec le scope `rpc` ; liste de testeurs approuvés, 50 au maximum | Windows, macOS, Linux | Clients RPC communautaires, orientés Rich Presence | (b) `SET_VOICE_SETTINGS` (mute, sourdine), `SELECT_VOICE_CHANNEL`, `GET_SELECTED_VOICE_CHANNEL`, événement `VOICE_SETTINGS_UPDATE`. (a) Aucun retour d'état | RPC **restreint** : approbation requise, transport WebSocket obsolète. Un seul modificateur des réglages vocaux à la fois. Scopes `rpc.voice.*` **à vérifier** | (a) S / forte ; (b) L / forte mais risquée |
| **Spotify** | (a) Contrôles média de Windows (SMTC, `GlobalSystemMediaTransportControlsSessionManager`). (b) Spotify Web API | (a) Aucune. (b) OAuth2 avec PKCE et redirection loopback | (a) Windows. (b) Web | (a) WinRT, ou `Dubya.WindowsMediaController`. (b) `SpotifyAPI.Web` 7.4.2 | (a) Lecture, pause, piste suivante ; titre, artiste et pochette en temps réel, pour toute application média. (b) Listes de lecture, file d'attente, bibliothèque | (b) Depuis février 2026, le mode développement exige un compte **Premium** pour le propriétaire et se limite à **5 utilisateurs**. Le contrôle de lecture exige Premium. Le quota étendu est quasi inaccessible | (a) **S / forte** ; (b) M / faible |
| **Wave Link (Elgato)** | WebSocket JSON-RPC local `ws://127.0.0.1:1824`, **non officiel** (rétro-ingénierie) | Aucune connue | Windows, macOS | `ElgatoWaveSDK` (2022, antérieur à Wave Link 3) | Volume et mute par entrée et par mix ; événements de volume | Wave Link 3 est sorti le 3 mars 2026 : protocole **à vérifier**. Il peut changer sans préavis | M / moyenne, risque élevé |
| **Microsoft Teams** | API locale « appareils tiers » : WebSocket `localhost:8124`, avec appairage | Jeton et appairage pendant une réunion (fenêtre d'approbation dans Teams) | Windows ; prise en charge par le nouveau Teams **à vérifier** | `Teams.ThirdPartyAppApi`, `SvRooij.TeamsMonitor` (communautaires) | Micro, caméra, main levée, flou d'arrière-plan, réactions, quitter. **État de la réunion lisible** | Documentation officielle **à vérifier** ; comportement différent entre l'ancien et le nouveau client | M / forte |
| **Zoom** | Raccourcis clavier : Alt+A (micro), Alt+V (caméra), Alt+Y (main levée). Option « global » dans les réglages de Zoom | Aucune | Windows | — | Envoi seulement, **aucun état** | Touches modifiables par l'utilisateur ; sources secondaires (page de support Zoom **à vérifier**) | **S / forte** |
| **Voicemod** | Control API : WebSocket `ws://localhost:59129/v1/`, premier message `registerClient` | `clientKey` **fourni par Voicemod** sur demande (formulaire partenaire) | Windows | Aucune en .NET (clients TypeScript) | Choix de la voix, activer ou couper le changeur de voix, bruitages ; état de la voix | Obtenir une clé (délai, conditions) ; certaines fonctions sont payantes | M / moyenne |
| **Adobe Creative Cloud** | (a) Raccourcis. (b) Plugins UXP (officiels dans Premiere Pro depuis la 25.6) ; CEP et ExtendScript en fin de vie | (b) Plugin installé côté Adobe, qui dialogue en local avec StreamDak | Windows, macOS | — (plugin UXP en JavaScript) | (a) Outils, coupe, navigation dans la timeline, sans état. (b) Actions et états du projet | (b) Plugin JavaScript à développer et maintenir ; appels asynchrones ; API encore incomplète | (a) S / forte ; (b) L / moyenne |
| **DaVinci Resolve** | (a) Raccourcis. (b) Scripting externe Python ou Lua (`fusionscript.dll`) | Préférence « External scripting » : local ou réseau | Windows, macOS, Linux | Pas de .NET : passerelle Python | (b) Projets, timelines, rendu, changement de page | (b) Version **Studio** (payante) pour le scripting externe ; incompatibilités signalées avec Python 3.12 | (a) S / forte ; (b) L / moyenne |
| **Blender** | (a) Raccourcis. (b) Add-on Python avec socket local : réseau sur un thread, file `queue.Queue` vidée par `bpy.app.timers` | Jeton local partagé (à concevoir) | Windows, macOS, Linux | Pas de .NET | (b) Opérateurs, modes, outils ; états (mode, objet actif) | `bpy` n'est pas thread-safe ; add-on à distribuer | (a) S / forte ; (b) M / moyenne |
| **Microsoft 365** | (a) Raccourcis Outlook, Word, Excel. (b) Microsoft Graph | (b) MSAL.NET en client public avec le courtier WAM (aucune redirection) ; scopes délégués (`Mail.Send`, `Mail.ReadWrite`…) | Windows ; Graph : web | `Microsoft.Identity.Client` (+ Broker), `Microsoft.Graph` | (b) Messages non lus, prochaine réunion, présence Teams, envoi de modèles | Application Entra ID à enregistrer ; consentement administrateur possible en entreprise | (a) S / forte ; (b) M-L / moyenne |
| **Google Workspace** | (a) Raccourcis Docs et Sheets (navigateur). (b) API Google ou Apps Script | (b) OAuth « Desktop » avec loopback et PKCE ; scopes sensibles | Web | `Google.Apis.Sheets.v4`, `Google.Apis.Docs.v1` | (b) Écrire des cellules, insérer des modèles | Application non vérifiée : **100 utilisateurs sur toute la vie du projet** ; vérification de plusieurs mois | (a) S / moyenne ; (b) L / faible |
| **Notion** | API REST | Jeton d'intégration interne | Web | Clients communautaires ; `HttpClient` suffit | Créer une page, changer une propriété (statut), afficher un compteur issu d'une requête | 3 requêtes/s en moyenne par connexion ; HTTP 429 avec `Retry-After` | M / moyenne |
| **ClickUp** | API REST v2 | Jeton personnel `pk_…` (sans préfixe `Bearer`) ou OAuth | Web | `HttpClient` | Créer une tâche, changer son statut (`PUT /task/{id}`), suivi du temps | 100 requêtes/min par jeton (sources tierces, **à vérifier**) | M / moyenne |
| **monday.com** | API GraphQL | Jeton personnel ou OAuth | Web | `HttpClient` et requêtes GraphQL | `change_column_value` (statut), créer un élément | Budget de complexité par minute (valeurs **à vérifier** dans la documentation officielle) | M / faible à moyenne |
| **Philips Hue** | CLIP v2 : HTTPS local vers le pont ; flux SSE `/eventstream/clip/v2` | Clé d'application (appui sur le bouton du pont), en-tête `hue-application-key` ; certificat du pont à valider | Réseau local | `HueApi` 3.2.0 (CLIP v2) ; `Q42.HueApi` ne couvre que la v1 | Allumer, éteindre, couleur, scènes ; états en temps réel par SSE | Ponts ronds v1 et anciens micrologiciels non compatibles | M / forte |
| **LIFX** | Protocole LAN : UDP 56700, découverte par diffusion. Ou API HTTP cloud | LAN : aucune. Cloud : jeton | Réseau local, web | Bibliothèques communautaires (**à vérifier**) | Allumer, couleur, effets ; état par les messages `Get*` | Cloud : environ 60 requêtes/min (source tierce, **à vérifier**) | M / moyenne |
| **Elgato Control Center** | HTTP local, port 9123, `/elgato/lights` (GET / PUT). Découverte mDNS (**à vérifier**) | Aucune | Réseau local | `HttpClient` | Allumer, luminosité 0-100, température de couleur (en mireds) ; état lisible | API non officielle ; bizarreries de micrologiciel (HTTP/1.0 contre 1.1) ; pilotage des caméras Facecam **à vérifier** | **S / moyenne** |
| **Windows (système)** | Core Audio (déjà dans `NativeAudio.cs`), SMTC, `PerformanceCounter` / WMI, LibreHardwareMonitor | Aucune ; certains capteurs exigent les droits d'administrateur | Windows | `LibreHardwareMonitorLib` (MPL-2.0) | CPU, RAM, GPU, températures, volume par application, média en cours, verrouillage, veille | SMTC impose une cible `net9.0-windows10.0.19041` ou plus ; ne jamais demander l'élévation | **S-M / forte** |
| **macOS** | — | — | — | — | — | **Non réalisable** (WPF) | — |
| **Flight Simulator 2020 / 2024** | SimConnect (assembly managée du SDK) | Aucune | Windows | `Microsoft.FlightSimulator.SimConnect.dll`, fournie par le SDK (droits de redistribution **à vérifier**) | Variables de simulation (lecture, écriture), événements ; états du cockpit en temps réel | Documentation écrite pour .NET Framework 4.7 (compatibilité .NET 9 **à vérifier**) | M-L / forte (simulation) |
| **X-Plane 12** | API Web locale : REST et WebSocket, port 8086 ; v1 à partir de 12.1.1, commandes en v2 (12.1.4), v3 en 12.4 | Aucune ; le trafic entrant doit être autorisé | Windows, macOS, Linux | `HttpClient`, `ClientWebSocket` | Datarefs (lecture, écriture, flux), commandes | Désactivable par `--no_web_server` | M / forte (simulation) |
| **Star Citizen** | Raccourcis clavier uniquement ; lecture possible du fichier de liaisons du jeu | — | Windows | — | Envoi seulement | Aucune API ; position de CIG sur les macros **à vérifier** | S / moyenne |
| **Cockpits physiques** | Port série, HID, joystick virtuel (vJoy) | — | Windows | `System.IO.Ports` ; vJoy (pilote tiers) | Entrées et sorties matérielles | Pilote noyau tiers ; maintenance | L / faible |

## 2. Classement et ordre d'implémentation recommandé

### Niveau 1 : simple et à forte valeur

Ordre recommandé :

1. **Socle** : interface `IIntegration`, `IntegrationManager`, stockage des secrets (DPAPI), page « Intégrations ».
   C'est le prérequis de tout le reste.
2. **OBS Studio** : API officielle stable, états en temps réel, public large.
3. **Windows** : contrôles média SMTC (qui couvrent Spotify sans Premium), surveillance CPU / RAM, volume par
   application. Aucune dépendance externe pour SMTC et les compteurs.
4. **Raccourcis applicatifs** : Zoom, Discord, Adobe, DaVinci Resolve, Blender, Office, Star Citizen.
   - Une seule action générique « raccourci vers une application cible », avec détection de la fenêtre et des
     préréglages par logiciel.
   - Elle s'appuie sur les actions `hotkey` et `focus` existantes.
   - L'absence de retour d'état est affichée clairement dans l'interface.
5. **vMix** : HTTP et TCP sans authentification, tally en retour.
6. **Lampes Elgato** : HTTP local sans authentification.
7. **Philips Hue** : CLIP v2 et SSE, bibliothèque maintenue.

### Niveau 2 : faisable avec effort

- Twitch (OAuth, EventSub) ;
- Microsoft Teams (API locale, appairage) ;
- Streamlabs Desktop ;
- X-Plane 12 et Flight Simulator (SimConnect) ;
- LIFX (LAN) ;
- Notion, ClickUp, monday.com ;
- Voicemod, sous réserve d'obtenir la clé partenaire ;
- Microsoft Graph ;
- Blender (add-on).

### Niveau 3 : complexe, risqué ou non réalisable

- **YouTube** : quota de 10 000 unités par jour, vérification Google, pas de clips.
- **Discord RPC** : accès restreint aux applications approuvées. Les raccourcis restent possibles (niveau 1).
- **Spotify Web API** : Premium obligatoire et 5 utilisateurs au maximum. SMTC suffit pour l'usage courant.
- **Wave Link** : protocole non officiel, changé avec la version 3.
- **Adobe UXP** et **DaVinci Resolve par script** (version Studio) : chaque intégration demande son propre plugin ou sa
  propre passerelle.
- **Google Workspace par API** : plafond de 100 utilisateurs et vérification.
- **Cockpits physiques** (vJoy, HID) : pilotes tiers.
- **Ecamm Live et macOS** : **non réalisables** avec WPF.

### Décision (19 octobre 2026)

**Toutes les intégrations sont retenues.** Elles seront réalisées dans l'ordre des niveaux : 1, puis 2, puis 3, une à
la fois, chacune avec ses actions, ses états, ses tests et sa documentation.

Deux exceptions, faute de solution technique :

- **Ecamm Live** et **macOS** restent hors de portée tant que StreamDak est en WPF. Elles seront rouvertes si une API
  réseau publique apparaît, ou si une interface Avalonia voit le jour (voir `ARCHITECTURE.md`).

Certaines intégrations ont des prérequis à remplir **par l'utilisateur ou le mainteneur**. Sans eux, seule la variante
par raccourcis clavier sera livrée :

- **Voicemod** : demander une `clientKey` partenaire à Voicemod.
- **Discord RPC** : faire approuver l'application Discord. En attendant, seuls les testeurs inscrits y ont accès.
- **Spotify Web API** : un compte Premium pour le propriétaire de l'application ; 5 utilisateurs au maximum.
- **YouTube** et **Google Workspace** : un projet Google Cloud. Ils restent plafonnés à 100 utilisateurs tant que Google
  n'a pas vérifié l'application.
- **Microsoft Graph** : une application enregistrée dans Entra ID.
- **DaVinci Resolve par script** : DaVinci Resolve Studio.
- **Flight Simulator** : le SDK MSFS, dont la licence de redistribution de SimConnect est à vérifier.

## 3. Risques transverses

- **Secrets** :
  - Stocker jetons et mots de passe avec DPAPI (`ProtectedData`, portée utilisateur) ou dans le Gestionnaire
    d'identification Windows.
  - Ne jamais les écrire dans `config.json` ni dans `streamdak.log`. Masquer toute valeur sensible avant journalisation.
- **OAuth** :
  - Flux PKCE avec redirection loopback sur un port choisi au hasard.
  - Aucun secret client embarqué : le binaire est un client public.
  - Scopes minimaux, et révocation possible depuis l'interface.
- **Conditions d'utilisation** :
  - Ne jamais contourner une approbation ni une limite (Discord, Voicemod, YouTube, Spotify).
  - Quand l'API est réservée, s'en tenir aux raccourcis clavier.
- **API non officielles** (Wave Link, lampes Elgato, Teams) :
  - un connecteur isolé par logiciel ;
  - détection de version ;
  - dégradation propre (état « non disponible » plutôt qu'une erreur bloquante).
- **Évolution des API** :
  - Plusieurs ruptures récentes : obs-websocket v4 → v5, Spotify en février 2026, CEP → UXP chez Adobe, Wave Link 3.
  - Chaque connecteur doit être testé contre un faux serveur, déclarer la version minimale prise en charge et pouvoir
    être désactivé sans toucher au reste.
- **Sécurité locale** :
  - Les ports locaux (OBS, vMix, Voicemod, X-Plane) sont accessibles à tout processus de la machine.
  - StreamDak ne doit pas exposer sa propre API sur le réseau par défaut.
  - Les actions `script` et `launch` gardent les droits de l'utilisateur.
- **Robustesse** :
  - Délai maximal, annulation et reconnexion avec attente croissante.
  - Respect des quotas (file d'attente, prise en compte de `Retry-After`).
  - Une intégration en panne ne doit bloquer ni l'interface ni les autres intégrations. Le moteur actuel
    (`ActionEngine`, délai par étape et annulation) est la base à étendre.
- **Format de configuration** : les paramètres d'intégration (hôte, port, compte) impliquent une nouvelle clé dans
  `config.json`. Elle doit être validée avant implémentation, avec une valeur par défaut et `JsonIgnore` pour les
  configurations existantes.

## 4. Sources

Une source « communautaire » ou « tierce » n'émane pas de l'éditeur du logiciel.

- **OBS** : [protocole obs-websocket](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md),
  [obs-websocket-dotnet](https://www.nuget.org/packages/obs-websocket-dotnet)
- **Streamlabs** : [documentation de l'API](https://github.com/stream-labs/streamlabs-obs-api-docs),
  [référence Streamlabs](https://dev.streamlabs.com/reference/streamlabs-obs-api)
- **Twitch** : [scopes](https://dev.twitch.tv/docs/authentication/scopes/),
  [abonnements EventSub](https://dev.twitch.tv/docs/eventsub/manage-subscriptions/),
  [référence de l'API](https://dev.twitch.tv/docs/api/reference/),
  [RFC EventSub WebSockets](https://discuss.dev.twitch.com/t/rfc-0016-eventsub-websockets/32652)
- **YouTube** : [coût des requêtes](https://developers.google.com/youtube/v3/determine_quota_cost)
- **vMix** : [API HTTP](https://www.vmix.com/help29/DeveloperAPI.html), [API TCP](https://sdapi01.vmix.com/help/TCPAPI.html)
- **Ecamm Live** : [préférences de contrôle à distance](https://support.ecamm.com/en/articles/6931454-remote-control-preferences)
- **Discord** : [RPC](https://docs.discord.com/developers/topics/rpc)
- **Spotify** : [guide de migration de février 2026](https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide),
  [TechCrunch, février 2026](https://techcrunch.com/2026/02/06/spotify-changes-developer-mode-api-to-require-premium-accounts-limits-test-users/),
  [SpotifyAPI.Web](https://www.nuget.org/packages/SpotifyAPI.Web)
- **Contrôles média de Windows (SMTC)** : [Dubya.WindowsMediaController](https://www.nuget.org/packages/Dubya.WindowsMediaController)
- **Wave Link** : [notes de version de Wave Link 3.0](https://help.elgato.com/hc/en-us/articles/44408325264017-Elgato-Wave-Link-3-0-0-Windows-Release-Notes),
  [description du protocole (communautaire)](https://gist.github.com/ghostzero/34ddc050bda7850cca4e561d6244f056)
- **Microsoft Teams** : [Teams.ThirdPartyAppApi (communautaire)](https://github.com/ferenyl/Teams.ThirdPartyAppApi),
  [SvRooij.TeamsMonitor (communautaire)](https://www.nuget.org/packages/svrooij.teamsmonitor),
  [état de l'API en 2023](https://blog.thoughtstuff.co.uk/2023/03/microsoft-teams-client-api-coming-soon-not-ready-yet/)
- **Zoom** : [raccourcis, Université de Washington (secondaire)](https://www.washington.edu/accesstech/meetings/zoom/shortcuts/)
- **Voicemod** : [Control API](https://control-api.voicemod.net/), [kit développeur](https://gitlab.com/voicemod/kit/control-api)
- **Adobe** : [changelog UXP de Premiere Pro](https://developer.adobe.com/premiere-pro/uxp/changelog),
  [migration UXP pour les partenaires](https://www.adobevideopartner.com/customer-stories/extensibility-update/)
- **DaVinci Resolve** : [README de l'API de scripting (copie)](https://resolvedevdoc.readthedocs.io/en/latest/readme_resolveapi.html).
  L'original est livré avec Resolve (Aide → Documentation → Développeur).
- **Blender** : exemple `bpy.app.timers` de la documentation de l'API Python (file d'attente thread-safe)
- **Microsoft 365** : [MSAL.NET et le courtier WAM](https://devblogs.microsoft.com/identity/msal-net-wam/),
  [WithBroker](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.publicclientapplicationbuilder.withbroker)
- **Google** : [applications non vérifiées](https://support.google.com/cloud/answer/7454865)
- **Notion** : [limites de requêtes](https://developers.notion.com/reference/request-limits)
- **ClickUp** : sources tierces uniquement (documentation officielle **à vérifier**)
- **monday.com** : [migration vers la version 2025-04](https://developer.monday.com/api-reference/docs/migrating-to-version-2025-04)
- **Philips Hue** : [HueApi / Q42.HueApi](https://github.com/michielpost/Q42.HueApi),
  [binding openHAB, compatibilité CLIP v2](https://www.openhab.org/addons/bindings/hue/)
- **LIFX** : [protocole LAN](https://lan.developer.lifx.com/),
  [authentification de l'API HTTP](https://api.developer.lifx.com/docs/authentication)
- **Lampes Elgato** : [API de la Key Light Air (communautaire)](https://www.ntpro.nl/blog/archives/3613-Postman-and-the-Elgato-Key-Light-Air-API.html)
- **LibreHardwareMonitor** : [dépôt](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- **Flight Simulator** : [clients SimConnect en code managé](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/programming-simconnect-clients-using-managed-code/index.md)
- **X-Plane** : [API Web locale](https://developer.x-plane.com/article/x-plane-web-api/),
  [API Web v2](https://developer.x-plane.com/2025/01/sdk-update-web-api-v2/)
- **Star Citizen** : aucune source officielle trouvée (**à vérifier**)
