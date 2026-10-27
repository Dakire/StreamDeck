# 0024 — SteelSeries Sonar, audio Windows par périphérique et contrôles de niveau tactiles

- **Statut** : acceptée, **API Sonar non vérifiée** (voir « Risques »).
- **Date** : 27 octobre 2026.

## Contexte

Le deck sert à piloter un poste de jeu : casque SteelSeries Arctis Pro Wireless, carte son Focusrite Scarlett Solo 3rd gen, mélangeur **Sonar** de SteelSeries GG.
Besoin : régler depuis le deck, au doigt, le volume et la coupure des canaux Sonar (jeu, discussion, média, aux, micro) et, en secours, ceux des périphériques audio Windows
choisis par leur nom (micro Focusrite, sorties Arctis), même s'ils ne sont pas le périphérique par défaut. Le gain analogique du Scarlett n'est pas pilotable : son micro est l'entrée de Sonar,
dont le niveau se règle dans Sonar. `IAudioService` ne gérait que les périphériques par défaut. Le PC de développement n'a **pas** SteelSeries GG.

## Décision

**1. Connecteur Sonar** (`Integrations/Sonar`, id `sonar`, ADR 0011). `SonarIntegration` dérive de `PolledHttpIntegration`.
- Découverte locale : `coreProps.json` (`%PROGRAMDATA%\SteelSeries\SteelSeries Engine 3`) donne les adresses de GG, la liste de ses sous-applications donne l'adresse du serveur web de Sonar.
- **Lecture et écriture** par routes HTTP (lecture `volumeSettings/classic`, écriture PUT `…/{channel}/Volume/{value}` de 0 à 1 et `…/isMuted/{value}`). Valeurs publiées comme valeurs d'intégration :
  `{sonar.<canal>.volume}` (0 à 100), `{sonar.<canal>.muted}`.
- **Garde-fous** : seules les adresses locales (127.0.0.1, localhost, ::1) sont jointes, sans identifiants ; pas de redirection suivie ; délais de 2 à 3 s ; réponses limitées à 1 Mo ; le certificat
  auto-signé de GG n'est accepté que pour une adresse locale (la requête est refusée avant l'envoi sinon). Aucune exception ne remonte : l'état « erreur » dit pourquoi (GG fermé, Sonar éteint…),
  le gestionnaire reconnecte avec une attente croissante. Aucun secret (GG n'en demande pas).
- **Tout est réglable sans recompiler** (champs de l'intégration) : adresse de Sonar, chemin de `coreProps.json`, canaux suivis, mode (classic / streamer), routes de lecture, de volume et de coupure
  avec les variables `{mode}`, `{slider}`, `{channel}`, `{value}`. Ces réglages vivent dans le mécanisme existant `Integrations[id].Values` : **aucun changement du format de `config.json`**.
- **Diagnostic en lecture seule** (commande `diagnose`, marquée `Offline` : elle s'exécute même sans connexion, ajout facultatif `IntegrationCommand.Offline`). Elle liste les canaux et leurs niveaux et écrit
  `sonar-diagnostic.txt` (adresses locales, routes, codes HTTP, début de la réponse brute) dans le dossier de configuration ; le même rapport est écrit après un échec de connexion (au plus toutes les 5 min).

**2. Audio Windows par périphérique.** `IAudioService` hérite de la nouvelle interface `IAudioDevices` (`Endpoints`, `ReadEndpoint`, `SetEndpointVolume`, `SetEndpointMute`). Implémentation Windows :
`NativeAudio` (énumération `IMMDeviceEnumerator`, nom convivial `PKEY_Device_FriendlyName`, `IAudioEndpointVolume` par identifiant), toujours interne à `Platform/Windows` ; `SimulatedPlatform` a des
périphériques simulés. La désignation par nom est une fonction pure du Core (`AudioDeviceQuery` : sous-chaîne sans casse, `capture:` / `render:` pour lever un doute, nom exact prioritaire, ambiguïté signalée
au lieu d'être devinée). Nouveau type d'action **`audiodevice`** (`AudioDeviceAction`, commandes `toggle`, `mute`, `unmute`, `volume`, `list`), ajouté à `BuiltInActions.All` ; les types existants
(`audio`…) ne changent pas. La clé `audiodevice` est une nouvelle valeur de `Type`, figée par `FormatKeysTests`. Le périphérique par défaut de Windows n'est jamais modifié.

**3. Contrôles de niveau tactiles.** Un nouveau widget **`level`** (« Niveau audio », ADR 0017) : un bouton `Widget = "level"` dont `WidgetOptions` porte `source` (`sonar:game`, `windows:capture:Focusrite`)
et `shape` (`vertical`, `horizontal` ; absent = roue). **Aucune nouvelle clé de premier niveau**, un ancien fichier se relit tel quel (test). Mécanisme :
- Cœur sans WPF (`Core/Audio`) : `WheelGesture` (angle du doigt, rotation continue, 270°, course bornée sans zone morte), `SliderMath`, `LevelTarget`, `ILevelSource` avec `SonarLevelSource` et
  `WindowsLevelSource`, `LevelSender` (un envoi à la fois, toujours le dernier niveau).
- `Controls/LevelControl` (dérivé de `RangeBase`, dessiné par `OnRender`, pinceaux du thème) et `LevelWidgetViewModel` (niveau réel affiché, geste immédiat, relevés ignorés 2,5 s après un geste, pause deck masqué).
- **Toucher** : le deck ne reçoit pas les événements tactiles de WPF (ADR 0013). `MainWindow.Touch` route un doigt posé sur un `LevelControl` vers `PointerDown / PointerMove / PointerUp` jusqu'à son relâchement (second doigt
  ignoré, pas d'appui long, pas de test d'appui) ; la souris passe par les événements normaux. `WS_EX_NOACTIVATE` et le hook `WM_MOUSEACTIVATE` ne sont pas touchés : le geste n'active jamais la fenêtre.
- **Roue** : au contact la roue **ne saute pas** (le doigt devient le point de départ), la rotation suit les différences d'angle ramenées à ±180° (franchir le bas ne fait pas sauter), aux butées la position est bornée de sorte
  qu'un retour en arrière reparte tout de suite. Toucher le centre coupe ou rétablit. **Curseurs** : la poignée saisie ne saute pas, ailleurs elle va sous le doigt.
- Mode édition : un widget est une touche (inchangé) ; le panneau de propriétés propose le canal Sonar ou le périphérique Windows (nom saisi ou pastilles des périphériques détectés) et la forme (roue / curseur vertical / curseur horizontal).
- Accessibilité : pair d'automatisation `IRangeValueProvider` (0 à 100), nom accessible, clavier (flèches, Pg, Début, Fin, Espace), cible minimale 48 px.

## Conséquences

- Nouvelle dépendance : aucune. Nouveau projet `Integrations/Sonar` ; nouveaux fichiers `Core/Audio/*`, `Controls/LevelControl.cs`, `ViewModels/LevelWidgetViewModel.cs`, `ViewModels/PropertiesViewModel.Level.cs`.
- `IAudioService` gagne quatre membres (hérités de `IAudioDevices`) : toute autre implémentation de plateforme doit les fournir.
- Un contrôle de niveau Windows relit le périphérique chaque seconde tant que sa page est affichée et le deck visible ; Sonar relit en 1,5 s (30 s deck masqué) pour **tous** les canaux, quel que soit le nombre de tuiles.
- Les tests contre Sonar utilisent un **faux serveur HTTP local** qui imite les routes supposées : ils prouvent la logique de StreamDak, pas l'API réelle.

## Risques et ce qui n'est pas vérifié (pas de GG sur le PC de développement)

- Forme de `coreProps.json`, de `/subApps` et des routes de volume (noms, méthode PUT, plage 0 à 1, échelle), noms des canaux, mode streamer : **écrits de mémoire de l'API publique, jamais testés contre un vrai GG**.
  Atténuation : tout est réglable (routes, adresse, canaux) et le diagnostic montre la réponse brute à renvoyer pour corriger.
- Comportement de la roue au doigt sur un vrai écran tactile (les tests actionnent le contrôle par programme et vérifient le rendu hors écran).
- Le gain analogique de la carte son n'est pas pilotable (matériel) : à régler dans Sonar (canal micro).
- Les noms conviviaux des périphériques Windows dépendent des pilotes (ils sont listés par la commande `list` et les pastilles de l'éditeur).
