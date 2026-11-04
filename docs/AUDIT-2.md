# Audit 2 : freezes, WebView2, fiabilité, sécurité, UX

Phase 0 de la mission « fiabiliser, optimiser, polir, enrichir ». Branche `mission/fiabilisation-audit-2`, 2 novembre 2026.
Quatre relectures du code indépendantes, **sans modification ni mesure** : les gravités sont déduites de la lecture.
Tout ce qui est marqué *à mesurer* sera chiffré en phase 1 avant d'être corrigé. Ce qui est déjà écarté par
l'ancien rapport de performance (retiré, voir l'historique git) (connecteurs paresseux, `TieredPGO`, debounce du catalogue, etc.) n'est pas rouvert.

Gravité : **C** critique, **H** haute, **M** moyenne, **B** basse.

## 1. Thread UI : freezes et micro-freezes

| # | Constat | Où | Gr. | Correctif |
|---|---|---|---|---|
| 1.1 | `UiWatchdog` : seuil 3 s, une ligne au début et à la fin, ni durée totale, ni pile, ni opération. Sonde en priorité Normal : une file Background ou Input saturée n'est pas vue. `TickCount64` : faux blocage possible au réveil de veille | `Services/UiWatchdog.cs:15,35,44,52` | M | Seuil bas 150-250 ms, durée au retour, pile du thread UI capturée par un thread surveillant, `Dispatcher.Hooks` pour la dernière opération et la priorité, compteur exposé |
| 1.2 | Aucune trace « long task » autour des timers et handlers de grille | `MainViewModel.cs:159,161,205,363,485,952` ; `ConfigPersistence.cs:29-30` ; `WebWidgetView.xaml.cs:65` ; `MainWindow.Touch.cs:78` | M | Utilitaire `LongTask.Measure` (Stopwatch, journal limité en débit) |
| 1.3 | `ConfigPersistence.Flush` sérialise, relit le fichier, écrit, hache et fait `File.Replace` **sur le thread UI** ; `IsOwnWrite` relit et hache aussi, y compris pour nos écritures | `ConfigPersistence.cs:35-40`, `ConfigStore.cs:129-152` | H | Instantané `ConfigStore.Serialize(Config)` (chaîne immuable) sur l'UI, écriture par un écrivain unique hors UI avec coalescence ; `_lastWrittenHash` sûr entre threads |
| 1.4 | Bug : `ReloadFromDisk` fait `Flush()` avant de relire, ce qui écrase la modification externe | `ConfigPersistence.cs:86` | M | Si le fichier a changé hors de nous, relire d'abord et proposer, sinon écrire |
| 1.5 | Sur erreur d'E/S : nouvel essai toutes les 300 ms avec une notification à chaque fois | `ConfigPersistence.cs:55` | B | Backoff et notification unique |
| 1.6 | `IconStore.Load` décode le PNG en synchrone sur l'UI, sans `DecodePixelWidth` ; un `null` reste en cache jusqu'au changement de thème | `IconStore.cs:119-133`, `TileFaceData.cs:116-125` | M | Cache avec décodage borné à la taille d'affichage, préchargement hors UI, `Freeze`, ne pas cacher l'absence |
| 1.7 | Audio COM sur l'UI : `LevelWidgetViewModel.Start`/`SetPaused(false)` appellent `Refresh` synchrone ; `ToggleMute` ; `AudioAction`/`AudioDeviceAction` sont des `SyncActionHandler` | `LevelWidgetViewModel.cs:118,144,183`, `LevelSources.cs:110-117`, `ActionHandler.cs:50` | H | `Task.Run` pour les relevés, actions audio en `BackgroundActionHandler` |
| 1.8 | `ActionValidator` fait `File.Exists`/`Directory.Exists` à chaque frappe et pour chaque case de la grille d'édition : un chemin UNC injoignable bloque plusieurs secondes | `ActionValidator.cs:76,81`, `ActionStepViewModel.cs:277`, `EditorViewModel.cs:439`, `PropertiesViewModel.cs:225,231` | H | Validation syntaxique immédiate, existence du chemin vérifiée hors UI avec délai et cache |
| 1.9 | DPAPI et lecture de fichier à chaque changement de réglages (`ApplyAll`/`SettingsFor`) ; `OpenProcessNames` (EnumWindows + `GetProcessById`) ; `AppIcon.Store`, `IconStore.Import`, `DropPayload.Read`, E/S des paramètres, constructeur d'`UpdateService` (`Directory.Delete` récursif) | voir audit | M | Déplacer hors UI, un par un, selon les mesures |
| 1.10 | `async void` : seul `Mount` hors événement, avec code hors `try`. Tâches sans observation : `LevelSender` (`OperationCanceledException` laisse `IsBusy` vrai pour toujours), `PolledHttpIntegration` (§3.1), `RestoreAsync`, `EditorViewModel.cs:612`, tâches abandonnées au délai dépassé | `WebWidgetView.xaml.cs:133`, `LevelTarget.cs:155`, `SettingsViewModel.cs:189`, `ActionHandler.cs:69`, `ScriptAction.cs:24` | M | `try/catch` + journal, helper `FireAndForget(log)` |
| 1.11 | Journal Serilog synchrone (`shared: true`, pas de sink asynchrone) : chaque `Log.*` sur l'UI est une écriture disque | `StreamDak.csproj:67-68` | M | Sink asynchrone borné |
| 1.12 | Arrêt : tout sur l'UI dans `App.OnExit` ; `IntegrationManager.Dispose` attend en série (2 s + 2 s par intégration, 44 s au pire avec 11), sans `ConfigureAwait(false)` donc des attentes qui capturent le contexte bloqué ; `ConfigPersistence` est libérée **après** (sauvegarde retardée) ; sockets OBS et Discord non fermés | `IntegrationManager.cs:296-300`, `AppHost.cs:194-195`, `App.xaml.cs:76-85` | H | `Flush()` d'abord, déconnexions en parallèle (`Task.WhenAll` sur le pool) avec échéance globale ≈ 2 s |
| 1.13 | Profil par application : `PollForeground` toutes les 800 ms sur l'UI, `Process.GetProcessById` sans `Dispose`. Un changement de profil appelle `Rebuild()` qui recrée toutes les tuiles | `MainViewModel.cs:268`, `WindowFocus.cs:81`, `MainViewModel.cs:429` | M | `ForegroundChanged` dans `IWindowService` via `SetWinEventHook` (Platform/Windows), nom résolu hors UI ; coût de `Rebuild` *à mesurer* |
| 1.14 | Audio : aucun `ReleaseComObject`, un énumérateur par relevé, minuterie sans garde de réentrance (threads du pool qui s'accumulent si un appel COM bloque) | `NativeAudio.cs:145-176`, `LevelWidgetViewModel.cs:143`, `LevelSources.cs:69` | M | Libération explicite, énumérateur gardé, garde de réentrance |
| 1.15 | Minuteries qui tournent deck masqué : `_fieldTimer`, `UiWatchdog` (voulu), boucles Hue/Elgato/OBS, et le rafraîchissement déclenché par les intégrations (ne teste pas `_active`) | `MainViewModel.cs:945-963` | M | Respecter `_active`, passer Hue/Elgato/OBS en `IBackgroundAware` |

**Infirmé** : aucun `.Result` bloquant sur l'UI (`Dialog.cs:32` est un `await`), `Thread.Sleep` du clavier tourne sur le pool, aucun `Dns`,
`Ping` et lancements de programmes hors UI, second démarrage borné à 3 s avant toute fenêtre, `WebNotificationWatcher.Dispose` non bloquant.

## 2. WebView2

| # | Constat | Où | Gr. | Correctif |
|---|---|---|---|---|
| 2.1 | **Un seul environnement** partagé (`CreateAsync(null, <config>\WebView2)`), donc un processus navigateur : les ADR 0018 et 0020 disent à tort « un processus par cadre ». *À mesurer* (processus de rendu par site) | `WebWidgetView.xaml.cs:123` | B | Corriger les ADR après mesure ; compter les processus `msedgewebview2` |
| 2.2 | La `Task` d'environnement est gardée en cache même en échec : jamais retentée | `WebWidgetView.xaml.cs:123` | M | Ne pas cacher un échec |
| 2.3 | Cadres rangés (6 au plus) actifs, sans `MemoryUsageTargetLevel` ; voulu pour les notifications (ADR 0018) donc **pas de `TrySuspendAsync`** | `WebFramePool.cs:30,50-59` | M | `MemoryUsageTargetLevel = Low` sur les cadres rangés et cachés, pris en compte par le mode économie |
| 2.4 | Cadres rangés jamais purgés au changement de profil ni à la suppression d'un widget (un Meet ou un micro peut rester actif) | `MainViewModel.cs:1101-1119`, `MainWindow.xaml.cs:82` | M | Purger les clés absentes de la configuration |
| 2.5 | `_fieldTimer` (400 ms) tourne deck réduit ; une page figée laisse `_polling` vrai pour toujours, donc la fin d'interaction après 30 s ne se déclenche jamais | `WebWidgetView.xaml.cs:53,149,277-306` | M | Arrêt sur `SetActive(false)`, `WaitAsync(2 s)`, intervalle adaptatif. **Pas de `WebMessageReceived`** (voir §2.9) |
| 2.6 | Badge : si le service n'est pas connecté au moment de l'ouverture, la page de connexion reste chargée et **on ne renavigue jamais** : la connexion faite ensuite dans le deck ne rétablit pas le badge avant un redémarrage | `WebNotificationWatcher.cs:165`, `NotificationProviders.cs:213` | H | Renaviguer vers `StartUrl` sur compteur nul, avec backoff, et au retour au premier plan |
| 2.7 | `ProcessFailed` : type de panne ignoré (une panne GPU affiche « cessé de répondre »), pas de journal, cadre rangé laissé mort, minuterie orpheline à chaque plantage (`ReopenLaterAsync` n'arrête pas l'ancienne), `Controller.Close()` sans `try/catch` | `WebWidgetView.xaml.cs:208`, `WebNotificationWatcher.cs:223-227,169,301` | M | Filtrer `ProcessFailedKind`, journaliser, marquer le cadre mort pour recréation, arrêter la minuterie |
| 2.8 | Course : `Mount` (`async void`) peut voir `CoreWebView2` nul si le cadre est évincé ; `Suspended=true` peut arriver après `Resume` ; observateur Google Chat : `querySelectorAll('[aria-label],[title]')` jusqu'à 5000 éléments, `MutationObserver` sans `attributeFilter` | `WebWidgetView.xaml.cs:156,163,173`, `WebNotificationWatcher.cs:418`, `NotificationProviders.cs:240,256` | M | Garde de nullité et `try`, état de veille ordonné, `attributeFilter`, plafond |
| 2.9 | **Invariant de sécurité** : remplacer `_fieldTimer` par `AddScriptToExecuteOnDocumentCreated` + `WebMessageReceived` demande `IsWebMessageEnabled=true` sur le cadre visible, donc un pont page → application. **Refusé.** | `WebWidgetView.xaml.cs:176` | – | Voir 2.5 |
| 2.10 | **Bug de sécurité** : le refus de permission d'une session cachée n'a pas `SavesInProfile=false` : il est mémorisé dans le profil partagé et bloque les notifications du cadre visible du même site (ADR 0018:103) | `WebNotificationWatcher.cs:216` | M | `a.SavesInProfile = false` |
| 2.11 | Documentation : SECURITY.md:96 et :108 affirment « aucun message page → application », or les sessions cachées ont `IsWebMessageEnabled=true` (ADR 0022:33), message validé par `NotificationMessage.TryRead` | `WebNotificationWatcher.cs:189` | M | Corriger SECURITY.md (exact, pas affaibli) |
| 2.12 | Au démarrage toutes les sessions s'ouvrent en même temps ; jusqu'à 15 sessions cachées | `WebNotificationWatcher.cs:137` | B | Ouverture échelonnée |

Protections inventoriées à ne pas affaiblir : `AreHostObjectsAllowed=false`, `IsWebMessageEnabled=false` (cadres visibles), DevTools/menus/raccourcis/saisie
automatique coupés, `NewWindowRequested` filtré, `DownloadStarting` annulé, `NavigationStarting` limité à http(s), permissions limitées au même site en https,
journal limité au nom d'hôte ; sessions : son coupé, permissions refusées, messages validés. Non gérés (comportement par défaut) : `FrameNavigationStarting`,
`LaunchingExternalUriScheme`, `ScriptDialogOpening`, `ServerCertificateErrorDetected`.

## 3. Fiabilité des intégrations et gestion des erreurs

| # | Constat | Où | Gr. | Correctif |
|---|---|---|---|---|
| 3.1 | **Un relevé peut mourir en silence** : `PollLoopAsync` n'attrape que `InvalidOperationException`. Un `JsonException` ou `FormatException` termine la tâche et l'intégration reste « Connecté » avec des valeurs figées | `PolledHttpIntegration.cs:39,61`, `ElgatoLightIntegration.cs:87`, `HueIntegration.cs:95` | H | Intercepter tout sauf l'annulation et appeler `ConnectionLost` |
| 3.2 | Pas de disjoncteur ni de gigue. Le compteur repart à zéro dès qu'une connexion réussit : un service qui tombe aussitôt est retenté toutes les 2 s sans fin. 401 et 429 traités comme des pannes réseau | `IntegrationManager.cs:186`, `RestIntegrationBase.cs:78` | M | Réinitialiser après N s de stabilité, gigue, arrêt sur 401, `Retry-After`, état « Suspendu » |
| 3.3 | Délais non garantis si l'implémentation ignore le jeton ; `IntegrationAction` démarre la commande sur l'UI | `ActionEngine.cs:112-116`, `IntegrationAction.cs:27` | M | `Task.Run(...).WaitAsync(timeout, token)` |
| 3.4 | Statut par intégration visible dans les paramètres seulement : une touche d'intégration déconnectée n'a **aucun** état visible (le voyant disparaît) | `IntegrationManager.cs:273-276`, `TileFace.xaml.cs:115` | M | Touche « indisponible » (icône + attenuation + texte), jamais par la couleur seule |
| 3.5 | Pas de « Copier le diagnostic », pas de section Diagnostic. `AppDomain.UnhandledException` ne fait que `Log.Error` ; rien n'est journalisé avant `AppHost.Build` | `App.xaml.cs:31-33,90-95`, `AppHost.cs:94-100` | M | Section Diagnostic (§6), journal d'amorçage, fichier de crash |
| 3.6 | `LogRedactor` laisse passer : `Cookie`/`Set-Cookie`, `X-Api-Key`/`api-key`, `Authorization` sans schéma ou `OAuth`, `hue-application-key`, webhooks Discord et Slack, clé Hue dans `/api/<clé>/`, `code=` seul, `password="a b"` (fuite de « b »), `secret: a,b` ; `"$1$2"` renvoie `$2` littéral. Ne couvre ni les toasts ni l'API locale | `Core/LogRedactor.cs:21` | M | Étendre les motifs, tests de non-régression, passer le diagnostic copié par le redacteur |
| 3.7 | Les URL de webhook sont en clair dans `config.json` et les exports | `HttpAction` | M | À consigner en risque (pas de changement de format sans accord) ; masque à l'export en option |
| 3.8 | **Redirection de jeton par import** : `baseurl` (REST) et `apiurl` (Google) viennent de `config.json`. Une configuration importée peut envoyer le jeton Notion ou Twitch, ou le secret et le jeton de renouvellement Google, vers un hôte choisi | `RestIntegrationBase.cs:40`, `GoogleIntegration.cs:86-95`, `TransferService.cs:113` | **H** | Ignorer ces clés hors tests (ou allowlist d'hôtes) et les retirer à l'import |
| 3.9 | API locale : `new NamedPipeServerStream` hors `try`, au-delà de 8 clients la boucle d'acceptation s'arrête jusqu'au redémarrage ; pas de délai d'inactivité | `LocalApiServer.cs:53` | M | `try`, relance, délai par client |
| 3.10 | Téléchargement de mise à jour sans délai global | `AppHost.cs:245` | M | Délai d'inactivité sur le flux |
| 3.11 | `TestAsync` peut courir avec la boucle et écraser `_http` | `IntegrationManager.cs:242` | B | Sérialiser |
| 3.12 | Plugins : empreinte calculée **après** le chargement, dépendances non hachées, `TypeLoadException` non interceptée, constructeur sur l'UI | `PluginLoader.cs:61-68`, `AppHost.cs:224` | M | Liste d'autorisation `plugins\trusted.json` vérifiée avant `Load` ; confiance au premier usage pour les plugins existants |
| 3.13 | CI : `actions/checkout@v4` et `setup-dotnet@v4` non épinglées, pas de Dependabot, `choco install innosetup` sans version, `${{ github.ref_name }}` interpolé dans un script | `ci.yml:20-21`, `release.yml:26,28` | M | Épingler par empreinte (à vérifier en ligne), Dependabot, version d'Inno Setup |
| 3.14 | Paquets : DI 10.0.0 (10.0.14 existe), WebView2 1.0.3967.48 (1.0.4314.35 existe) | csproj | M | Mettre à jour avec tests |
| 3.15 | Secrets dans le dépôt : aucun motif réel sur les fichiers suivis ; seules cinq valeurs factices de tests | – | OK | – |

**SECURITY.md inexact** : ligne 73 (« aucun appel de mise à jour automatique » alors qu'une vérification part 20 s après le démarrage, activée par défaut), ligne 94
(« téléchargement puis exécution : aucun » alors que l'installateur est lancé), ligne 93 (5 `Process.Start`, il y en a 8), ligne 81 (Discord, Sonar et favicons manquent),
lignes 96 et 108 (voir 2.11). À corriger dans le lot sécurité.

## 4. UI, UX, responsive

| # | Constat | Où | Gr. | Correctif |
|---|---|---|---|---|
| 4.1 | Aucun `GridSplitter` ; panneaux non repliables ; largeurs proportionnelles bornées | `MainViewModel.Editing.cs:24-34`, `MainWindow.xaml:755-796` | M | Splitters tactiles ≥ 36 px, panneaux repliables, clés facultatives de largeur |
| 4.2 | Fenêtre des paramètres : redimensionnable mais taille non mémorisée ; une seule colonne de 9 cartes, sans navigation ; pas de Diagnostic | `SettingsWindow.xaml:8,46`, `AppWindows.cs:44` | M | Taille mémorisée, section Diagnostic |
| 4.3 | Curseur vertical indisponible : seulement grisé, raison dans une info-bulle (invisible au doigt) ; le bouton couper reste actif | `VerticalSlider.xaml:28,33,44` | M | Raison affichée dans la tuile, couper désactivé |
| 4.4 | Widgets de données : pas de bouton « Réessayer » | `DataWidgetViewModel.cs:137-141` | B | Toucher pour réessayer |
| 4.5 | Textes qui ne reviennent jamais à la ligne : `TextWrapping` dans des `StackPanel` horizontaux (« Échec : … Nouvelle tentative » coupé) | `SettingsWindow.xaml:255-257,279-283,111-126` | M | `Grid`/`DockPanel`, `TextTrimming` + info-bulle |
| 4.6 | `TextTrimming` absent sur les titres des paramètres, de l'inspecteur et du catalogue ; un trimming sans effet | `EditorShared.xaml:28`, `PropertiesPanel.xaml:98`, `CatalogPanel.xaml:49`, `MainWindow.xaml:350` | B | Ajouter |
| 4.7 | Cibles sous la règle : poignée de redimensionnement 30×30, en-tête de section du catalogue 34, poignée des intégrations 28 de large, pastilles du haut 34 minimum, boutons de fenêtre 36 (règle des widgets du deck : 48) | `MainWindow.xaml:172`, `CatalogPanel.xaml:47`, `SettingsWindow.xaml:246`, `LayoutMetrics.cs:172,181` | M | Remonter à 36/48 selon la zone ; commentaire périmé `CatalogPanel.xaml:11-13` |
| 4.8 | `White`/`Black` en dur à 15 endroits ; contraste en thème clair non vérifié | `App.xaml:35-121,209-211`, `TileFace.xaml:48-68` | B | Jetons de thème, test de contraste |
| 4.9 | Cartes dans des cartes (bordures en trop) | `MainWindow.xaml:104-111`, `EditorShared.xaml:14-21` | B | Aplatir (look Elgato) |
| 4.10 | Taille des touches non réglable (déduite de l'écran, `MaxAspect 1.3`) ; Columns/Rows/Spacing déjà à chaud | `SettingsViewModel.cs:224-226`, `LayoutMetrics.cs:138` | B | Aperçu en direct de la grille |
| 4.11 | `Motion.Apply` couvre `DeckTileStyle` et `Items.Animate` ; 18 storyboards d'`App.xaml` non tous vérifiés sous ce contrôle | `Services/Motion.cs:15-20` | B | Point d'appui du mode économie |
| 4.12 | Documentation périmée : « actions à gauche, propriétés à droite » (CLAUDE.md, README.md:93,106, ARCHITECTURE.md:211) alors que l'ADR 0026 met le catalogue à droite et l'inspecteur en bas | – | B | Corriger |

## 5. Fonctionnalités : état

| Fonction | Existant | À faire |
|---|---|---|
| Raccourcis globaux | Rien (`HotkeyConflicts` ne vérifie que les combinaisons envoyées) | `IHotkeyService` dans `Core/Actions/Platform.cs`, `RegisterHotKey` dans `Platform/Windows`, `WM_HOTKEY` |
| États multiples | Voyant d'intégration (`StateKey`, `StateOf`) ; pas de `States` | Clé facultative `States` + normaliseur + test de migration |
| Macros | Séquence, délai, arrêt à la première erreur ; `focus` attend 1,5 s fixe ; `http` sans en-têtes | Répétition, conditions, variables, attente de fenêtre, en-têtes HTTP |
| Palette de commandes | Seulement Ctrl+F du catalogue en édition | Palette (clavier + tactile) |
| Intégrations | Spotify via médias Windows ; interface `IAudioMixerProvider` prévue pour Wave Link ; rien pour Home Assistant, Voicemod | Un projet `Integrations/<Nom>` chacun (modèle : Notion, Elgato) ; OAuth PKCE de Google à extraire pour Spotify |
| Profils | Pas de réordonnancement ; pas de planification ; sauvegarde/restauration manuelles, **sans aperçu** à la restauration | `MoveProfile`, planification, aperçu avant import et restauration |
| Télécommande web | Absente (API locale par canal nommé) | Étude de risque + ADR avant toute implémentation |
| Plugins étape 1 | Absents (`PluginLoader` charge des DLL) | Manifeste JSON d'actions `script` |

## 6. Plan de lots (phase 2)

| Lot | Contenu | Réf. |
|---|---|---|
| A | Instrumentation : watchdog bas seuil, long tasks, compteur, mesures de référence | 1.1, 1.2 |
| B | Arrêt parallèle borné, `Flush` d'abord | 1.12 |
| C | Écrivain de configuration hors UI | 1.3-1.5 |
| D | E/S et COM hors UI (validation, audio, icônes, journal asynchrone) | 1.6-1.11, 1.14 |
| E | Profil par application par événement | 1.13 |
| F | WebView2 : correctifs 2.2-2.8, 2.10, mémoire basse | §2 |
| G | Intégrations : relevés, disjoncteur, délais, redirection de jeton, statut visible | 3.1-3.4, 3.8 |
| H | Sécurité : `LogRedactor`, plugins, API locale, CI, paquets, SECURITY.md | 3.6, 3.9-3.14 |
| I | UX : panneaux, paramètres, Diagnostic, mode économie, textes et cibles | §4 |
| J+ | Fonctionnalités, une par une (§5) | §5 |

Choix à consigner en ADR : écrivain de configuration (lot C), disjoncteur (lot G), liste d'autorisation des plugins (lot H), télécommande web (lot J).

## 7. Base de référence

Compilation Release : 0 avertissement, 0 erreur. Les tests et les mesures de phase 1 sont consignés dans `docs/phase-X-report.md`.
