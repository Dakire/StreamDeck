# SteelSeries Sonar et audio par périphérique

Connecteur `StreamDak.Integrations.Sonar` (type d'action `sonar`), action `audiodevice` et widget **Niveau audio** (roue ou curseur). ADR 0024.

> **API vérifiée le 30 octobre 2026** contre SteelSeries GG et Sonar en mode classic (voir « Vérifié et non vérifié » plus bas). GG répond en HTTPS avec un certificat
> auto-signé, accepté uniquement pour une adresse locale ; la commande **Diagnostic** montre ce que GG répond vraiment.

## Ce que ça permet

| Besoin | Outil |
|---|---|
| Volume et coupure des canaux du mélangeur Sonar (jeu, discussion, média, aux, micro, principal) | Intégration **Sonar** : actions `volume`, `mute`, `unmute`, `togglemute` |
| Niveau et coupure d'un périphérique audio Windows choisi par son nom (micro Focusrite, casque Arctis…), même s'il n'est pas celui par défaut | Action **Audio : périphérique** (`audiodevice`) |
| Régler tout ça **au doigt**, comme un potentiomètre | Widget **Niveau audio** : roue, curseur vertical ou curseur horizontal |
| Plusieurs canaux d'un coup, avec la valeur exacte | Widget **Mélangeur audio** : un curseur horizontal par canal, couper, ↺ (réinitialiser), pastille de disponibilité |

## Volume Windows, mélangeur Sonar, gain : ce que règle chaque contrôle

| Réglage | Où il agit | Contrôle StreamDak |
|---|---|---|
| Volume d'un canal Sonar (jeu, discussion, média, aux) | Mélangeur de Sonar, avant le casque | Cible `sonar:<canal>` (sous-titre « Mélangeur Sonar ») |
| Micro dans Sonar (`chatCapture`) | Niveau du micro **dans Sonar** (traitement de Sonar) | Cible `sonar:chatCapture` (« Micro dans le mélangeur Sonar ») |
| Volume Windows d'un périphérique | Volume de l'endpoint Windows (Paramètres › Son) | Cible `windows:render:…` (« Volume Windows du périphérique ») |
| Niveau d'entrée Windows | Niveau de l'endpoint d'enregistrement | Cible `windows:capture:…` (« Niveau d'entrée Windows ») |
| Volume d'une application | Mélangeur de volume par application de Windows | Non piloté (pas d'API publique simple et stable par application) |
| Gain matériel d'une carte son | Le bouton de la Scarlett | **Aucun** : c'est du matériel, aucune API |
| Volumes de Discord | Réglages Voix et vidéo de Discord | Cibles `discord:input`, `discord:output` |

Aucun contrôle ne présente un volume Windows comme un gain Sonar : chaque tuile et chaque ligne du mélangeur l'écrivent sous leur nom. Au démarrage, rien
n'est réappliqué : la tuile lit le niveau réel ; le bouton ↺ ne fait que remettre le niveau choisi (réglage `reset`, 100 % par défaut, 50 % pour Windows).

## Prérequis

- **SteelSeries GG doit tourner sur le PC du deck**, avec **Sonar** activé. StreamDak parle à l'API locale de GG ; sans GG, le connecteur affiche « SteelSeries GG est introuvable ou fermé »
  et se reconnecte tout seul (2, 4, 8… 60 s) dès que GG est lancé.
- Le **gain analogique** d'une carte son (bouton de gain du Scarlett Solo) **ne se pilote pas** : c'est du matériel. Avec une Focusrite Scarlett Solo 3rd gen, son micro est
  l'entrée de Sonar : **le niveau du micro se règle dans Sonar**, donc avec le canal Sonar `chatCapture` (Micro). Le gain de la carte reste à régler à la main (ou
  garde-le fixe, puis tout se règle dans Sonar).
- Le connecteur n'envoie de requêtes qu'à `127.0.0.1` / `localhost` : toute autre adresse est refusée.

## Configuration (Paramètres › Intégrations › SteelSeries Sonar)

Activer l'intégration suffit dans le cas normal. Réglages (tous facultatifs) :

| Réglage | Défaut | Rôle |
|---|---|---|
| Canaux suivis (`channels`) | `master,game,chatRender,media,aux,chatCapture` | Canaux lus et publiés. Surnoms : `jeu`, `chat`, `musique`, `micro` |
| Mode du mélangeur (`mode`) | `classic` | `classic` ou `streamer` |
| Curseur (`slider`) | `streaming` | Mode streamer : `streaming` ou `monitoring` |
| Adresse de Sonar (`baseurl`) | vide | `http://127.0.0.1:port` : saute la découverte |
| Chemin de `coreProps.json` (`corepropspath`) | `%PROGRAMDATA%\SteelSeries\SteelSeries Engine 3\coreProps.json` | Où GG publie son adresse |
| Route de lecture (`readroute`) | `volumeSettings/classic` | Lecture de tous les canaux |
| Route du volume (`volumeroute`) | `volumeSettings/classic/{channel}/Volume/{value}` | PUT, valeur de 0 à 1 |
| Route de la coupure (`muteroute`) | `volumeSettings/classic/{channel}/Mute/{value}` | PUT, `true` ou `false` |
| Route des sous-applications (`subappsroute`) | `subApps` | Liste des sous-applications de GG |

Variables de route : `{mode}` (classic / streamer), `{slider}`, `{channel}`, `{value}`. En mode streamer, les routes par défaut passent par `{slider}` :
`volumeSettings/streamer/{slider}/{channel}/Volume/{value}`.

### Découverte (ce que fait StreamDak)

1. Lit `coreProps.json` et prend les adresses **locales** de GG (`ggEncryptedAddress`, `encryptedAddress`, `address`).
2. Demande à GG sa liste de sous-applications (`/subApps`) et en tire l'adresse du serveur web de Sonar (`webServerAddress`).
3. Lit les volumes (`/volumeSettings/classic`) toutes les 1,5 s (30 s quand le deck est masqué).

Sécurité : pas de redirection suivie, délais de 2 à 3 s, réponses limitées à 1 Mo, certificat auto-signé de GG accepté **pour une adresse locale seulement**, aucun secret.

## Actions

Type `sonar`, `Value` = commande, `Args` = argument.

| Commande | Argument | Effet |
|---|---|---|
| `volume` | `canal=niveau` : `game=60`, `chatRender=+5`, `micro=-10` | Règle le volume (0 à 100, absolu ou relatif) |
| `mute` / `unmute` / `togglemute` | `canal` | Coupe, rétablit, bascule ; le voyant du bouton est plein quand le canal est coupé (**écrire le canal en nom Sonar** : `game`, pas `jeu`) |
| `diagnose` | | **Diagnostic en lecture seule** (voir plus bas) |

### Valeurs pour les titres

`{sonar.game.volume}`, `{sonar.game.muted}`, `{sonar.chatCapture.volume}`… (0 à 100 ; `true` / `false`). Vides tant que Sonar n'est pas connecté.

## Audio Windows par périphérique

Action `audiodevice` (catégorie Audio : *Micro ou sortie d'une carte son*, *Volume d'un périphérique*). `Value` = commande, `Args` = périphérique.

| Commande | Argument | Effet |
|---|---|---|
| `toggle` / `mute` / `unmute` | `capture:Focusrite` | Coupe, rétablit, bascule |
| `volume` | `render:Arctis=50`, `Focusrite=+5` | Règle le niveau (absolu ou relatif) |
| `list` | | Liste les périphériques actifs (pour connaître les noms) |

Le périphérique se désigne par **une partie de son nom**, sans tenir compte des majuscules. `capture:` (micro, entrée) ou `render:` (sortie) lève un doute quand le même nom existe
dans les deux sens (une Focusrite a une entrée et une sortie). Un nom exact l'emporte sur une simple sous-chaîne. Si plusieurs périphériques restent possibles, l'action échoue en les
listant : rien n'est deviné. **Le périphérique par défaut de Windows n'est jamais changé.**

## Widget Niveau audio

Catalogue › Widgets › **Niveau audio**. Mode édition, panneau de propriétés :

- **Ce que le contrôle règle** : un canal Sonar (Principal, Jeu, Discussion, Média, Aux, Micro) ou un **Périphérique Windows** (nom saisi, ou pastille parmi les périphériques détectés).
- **Forme** : **Roue**, **Curseur vertical** ou **Curseur horizontal** (au choix, par bouton).
- **Taille** : 1 × 1 à 3 × 3 (la roue est à l'aise en 2 × 2, le curseur vertical en 1 × 2 ou 1 × 3, l'horizontal en 2 × 1).

Réglages écrits dans `WidgetOptions` du bouton (**aucune nouvelle clé de premier niveau**) : `source` (`sonar:game`, `windows:capture:Focusrite`) et `shape` (`vertical`, `horizontal` ; absent = roue).

**Au doigt**
- **Roue** : pose le doigt sur l'anneau et tourne. La roue **ne saute pas** au contact, suit en continu (de 0 à 100 sur 270°), sans zone morte aux butées (revenir en arrière la fait repartir tout de suite).
  Le niveau est écrit au centre. **Toucher le centre** coupe ou rétablit. Seule limite : près du centre exact, l'angle du doigt est instable et n'est pas suivi.
- **Curseur** : poser le doigt sur la poignée la saisit sans saut ; ailleurs, la poignée va sous le doigt.
- Le niveau affiché est le **niveau réel** (relu à chaque changement de Sonar, ou chaque seconde pour Windows). Après un geste, les relevés sont ignorés 2,5 s pour que la roue ne recule pas.
- Souris : glisser ; molette : ± 2 ; clavier : flèches ± 1, Pg ± 10, Début 0, Fin 100, Espace coupe. Lecteurs d'écran : curseur de 0 à 100 nommé.

Les relevés s'arrêtent quand la page n'est pas affichée ou que le deck est masqué. Sonar éteint ou GG fermé : la tuile le dit (« Active Sonar dans Paramètres › Intégrations », « SteelSeries GG est introuvable ou fermé »).

## Diagnostic (à lancer sur le PC avec GG)

Le diagnostic est **en lecture seule** (que des requêtes GET) :

- Action **Diagnostic Sonar** (catégorie Sonar du catalogue, commande `diagnose`) : à poser sur un bouton, ou à lancer depuis l'éditeur. Elle liste dans le message les canaux reconnus et leurs niveaux
  (`Canaux reconnus : game 60 %, chatRender 35 % (coupé), …`).
- Elle écrit aussi **`sonar-diagnostic.txt`** dans le dossier de configuration (`%APPDATA%\StreamDak`) et dans le journal (`streamdak.log`, lignes `[sonar]`) : chemin de `coreProps.json`,
  adresses locales trouvées, adresse de Sonar, routes utilisées, code HTTP et taille de chaque réponse, clés de premier niveau, **début de la réponse brute** (1 500 caractères), canaux reconnus et absents.
- Ce même fichier est écrit **automatiquement après un échec** de connexion (au plus toutes les 5 minutes), donc lisible même quand GG est fermé ou quand l'API a une autre forme.
- Aucun secret n'y figure (adresses locales, niveaux).

**Renvoyer le contenu de `sonar-diagnostic.txt`** pour ajuster les routes et la lecture si l'API réelle diffère.

La commande `diagnose` s'exécute **même quand le connecteur n'est pas connecté** (c'est la seule : elle sert justement à comprendre pourquoi il ne l'est pas) ; elle utilise les réglages enregistrés lors de la dernière tentative de connexion. Les commandes qui écrivent (`volume`, `mute`…) exigent la connexion.

## Vérifié et non vérifié

Vérifié le 30 octobre 2026 sur un PC avec SteelSeries GG et Sonar en mode classic (lectures, et écritures d'une valeur identique à l'état courant) :

- `coreProps.json` (`ggEncryptedAddress`, `address`), la réponse de `/subApps` (`subApps.sonar.metadata.webServerAddress`, `isReady`, `isRunning`) : adresse de Sonar en HTTP local.
- Lecture `volumeSettings/classic` : `masters.classic` et `devices.<canal>.classic` (`volume` de 0 à 1, `muted`) ; canaux `game`, `chatRender`, `chatCapture`, `media`, `aux`.
- Écriture PUT `volumeSettings/classic/{channel}/Volume/{value}` (HTTP 200) et `…/{channel}/Mute/{true|false}` (HTTP 200). **`isMuted` n'existe pas en mode classic (HTTP 404)** : la route était fausse dans les premières versions.
- Le canal `master` répond aux mêmes routes (`…/classic/master/Volume/…`, `…/master/Mute/…`).

Non vérifié :

- L'échelle du volume (linéaire ou non) entre 0 et 1.
- Le mode streamer en écriture : les routes `volumeSettings/streamer/{streaming|monitoring}/{channel}/Volume/{value}` existent (réponse « Cannot be called in current mode » en mode classic) ; la coupure y serait `…/isMuted/{value}` (la route `Mute` y répond 404).
- Le comportement au doigt de la roue sur un vrai écran tactile (les tests actionnent le contrôle par programme, pas avec un doigt).

Liste de contrôle à dérouler sur le PC avec GG : [Tests manuels Sonar](../tests-manuels-sonar.md).
