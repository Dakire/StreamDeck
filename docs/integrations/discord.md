# Discord

Deux façons de piloter Discord depuis le deck. **L'intégration Discord (RPC local) est la voie recommandée** : elle ne dépend ni du focus, ni du clavier.

| | Intégration Discord (RPC local) | Boutons « raccourci » |
|---|---|---|
| Discord au premier plan, en arrière-plan, réduit | Oui | Oui, si le raccourci est **global** dans Discord |
| Pendant un jeu lancé en administrateur | Oui | **Non** : Windows bloque les touches simulées (UIPI) |
| État réel sur la touche (voyant micro coupé, sourdine, salon) | Oui, en direct | Non |
| Volumes d'entrée et de sortie de Discord | Oui (commandes et widgets) | Non |
| Réglage | Une application Discord à toi (5 minutes, une fois) | Un raccourci global à déclarer dans Discord |

## Pourquoi les boutons Discord ne marchaient pas en arrière-plan

Constat dans le code (corrigé) :

1. Les boutons prêts à l'emploi envoyaient **Ctrl+Maj+M** et **Ctrl+Maj+D**. Ce sont les raccourcis **locaux** de Discord : ils n'agissent que si la fenêtre de
   Discord a le focus. Tant qu'aucun raccourci global n'est déclaré dans Discord, ces boutons ne marchent donc qu'avec Discord au premier plan.
2. **Ctrl+Maj+M est aussi le raccourci micro de Teams** : envoyé à l'application au premier plan, il coupait aussi le micro de Teams.
3. Les touches simulées (`SendInput`) partaient **sans code de balayage** et étaient appuyées et relâchées dans la même microseconde : certains crochets clavier
   globaux et les jeux en entrée brute les ignorent.
4. Quand l'application au premier plan tourne **en administrateur** (certains jeux, anti-triche), Windows refuse les touches simulées d'une application normale
   (UIPI). L'échec n'était que journalisé : la touche paraissait réussir.

Corrections : intégration RPC (ci-dessous) ; touches avec code de balayage et maintien de 30 ms, envoyées hors du thread d'interface ; refus de Windows affiché
sur la touche avec sa cause ; boutons prêts à l'emploi sur **Ctrl+Alt+Maj+M / D** ; conflits de raccourcis signalés (voir plus bas) ; second appui ignoré pendant
150 ms ou tant que la touche s'exécute (pas de double bascule par rebond du doigt).

## Intégration Discord (RPC local)

Le client de bureau Discord ouvre un canal local (`\\.\pipe\discord-ipc-0` à `-9`) documenté par Discord : [RPC](https://docs.discord.com/developers/topics/rpc).
StreamDak s'y connecte avec **ton** application Discord.

> **Limite de Discord, non contournable** : les droits `rpc`, `rpc.voice.read` et `rpc.voice.write` ne sont accordés qu'au **propriétaire** de l'application
> et à ses testeurs (50 au plus) tant que Discord ne l'a pas approuvée. StreamDak n'embarque donc aucune application : chacun crée la sienne, pour son compte.
> Discord indique que le RPC est en accès restreint et peut évoluer.

### Mise en route (une fois)

1. Sur [discord.com/developers/applications](https://discord.com/developers/applications) : **New Application** (nom au choix, par exemple « StreamDak »).
2. Onglet **OAuth2** : copie le **Client ID** ; **Reset Secret** puis copie le **Client Secret** ; dans **Redirects**, ajoute `http://localhost` et enregistre.
3. StreamDak › Paramètres › Intégrations › **Discord** : colle le Client ID et le Client Secret, active l'intégration.
4. Clique **Autoriser dans Discord** : une fenêtre s'ouvre **dans Discord** ; accepte (2 minutes). Le jeton obtenu est chiffré pour ton compte Windows.
5. L'état passe à « Connecté ». Les commandes sont dans le catalogue, catégorie **Discord**.

Le jeton de rafraîchissement est renouvelé par Discord à chaque usage : StreamDak réenregistre le nouveau (chiffré) sans couper la connexion. Pour retirer
l'autorisation : vide le champ « Jeton d'accès », ou retire l'application dans Discord › Paramètres › Applications autorisées.

### Commandes (type d'action `discord`)

| Commande | Argument | Effet | Voyant |
|---|---|---|---|
| `togglemute` | | Coupe ou rétablit ton micro | plein si coupé |
| `mute` / `unmute` | | Coupe / rétablit le micro | |
| `toggledeafen` | | Sourdine (son et micro) ou sortie de sourdine | plein en sourdine |
| `deafen` / `undeafen` | | Active / désactive la sourdine | |
| `inputvolume` | `0` à `100`, ou `+5` / `-5` | Volume d'entrée de Discord | |
| `outputvolume` | `0` à `200`, ou `+10` / `-10` | Volume de sortie de Discord | |
| `leave` | | Quitte le salon vocal | plein si dans un salon |

Valeurs pour les titres : `{discord.mute}`, `{discord.deaf}`, `{discord.input.volume}`, `{discord.output.volume}`, `{discord.voice}`, `{discord.channel}` (nom du salon vocal).
Elles suivent Discord en direct (événements `VOICE_SETTINGS_UPDATE` et `VOICE_CHANNEL_SELECT`) : un micro coupé dans Discord lui-même allume le voyant.

Widgets : **Niveau audio** et **Mélangeur audio** acceptent les cibles `discord:input` (micro) et `discord:output` (sortie). Le curseur couvre 0 à 100 % ;
la sortie de Discord monte jusqu'à 200 % : au-delà de 100, le curseur affiche 100.

### Ce que l'intégration ne fait pas

- Rejoindre un salon précis, partager l'écran, activer la caméra : non exposé de façon fiable par le RPC public ; pas d'API privée.
- Un autre programme qui modifie les réglages vocaux par RPC en même temps peut être refusé par Discord (un seul à la fois).

### Dépannage

| Message | Cause, solution |
|---|---|
| « Discord n'est pas lancé sur ce PC » | Le client de bureau n'est pas ouvert (le site web n'a pas de canal local). Reconnexion automatique (2, 4… 60 s) |
| « Discord refuse ce Client ID » | Client ID mal copié |
| « Discord refuse l'autorisation (…) » | Client Secret ou redirection `http://localhost` manquante dans OAuth2 › Redirects ; puis « Autoriser dans Discord » |
| « Pas de réponse dans Discord » | La fenêtre d'autorisation n'a pas été acceptée à temps : relancer |
| « droit manquant » | Autorisation donnée avant un changement de droits : « Autoriser dans Discord » de nouveau |

## Boutons « raccourci » (sans application Discord)

Catalogue › Applications cibles › **Discord : micro (raccourci)** et **Discord : sourdine (raccourci)** envoient **Ctrl+Alt+Maj+M** et **Ctrl+Alt+Maj+D**.
Déclare la même combinaison dans Discord › Paramètres › **Raccourcis clavier** › *Ajouter un raccourci* › action **Toggle Mute** (et **Toggle Deafen**).
Un raccourci déclaré là est global : il agit même quand Discord est réduit.

Limites : aucun état lisible (pas de voyant) ; inopérant quand l'application au premier plan est en administrateur (la touche affiche alors
« Windows a bloqué les touches… ») ; les anciens boutons sur Ctrl+Maj+M restent tels quels (configuration intacte) mais sont signalés.

### Conflits de raccourcis

StreamDak n'enregistre **aucun** raccourci global : il envoie des combinaisons à l'application au premier plan, et Discord les intercepte s'il les a déclarées.
Dans le panneau de propriétés (et en avertissement sur la touche, en mode édition), StreamDak signale :

- une combinaison envoyée sans mise au premier plan qui est aussi un raccourci connu d'une application cible (Ctrl+Maj+M : micro de Teams, raccourci local de Discord) ;
- une combinaison envoyée telle quelle par deux boutons de titres différents.

Une combinaison précédée d'une étape « Fenêtre au premier plan » vise une application précise : elle n'est pas concernée.
