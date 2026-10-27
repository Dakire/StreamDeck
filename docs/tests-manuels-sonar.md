# Tests manuels Sonar, audio par périphérique et roue de niveau

À faire sur le PC qui a **SteelSeries GG + Sonar**, le casque Arctis Pro Wireless et la Focusrite Scarlett Solo 3rd gen, et si possible un écran tactile (les tests automatiques ne les ont pas).
Utiliser `STREAMDAK_DATA_DIR=<dossier d'essai>` pour ne pas toucher à `%APPDATA%\StreamDak`. Journal : `streamdak.log` du dossier de configuration (lignes `[sonar]`).
Fonctionnement : [Sonar](integrations/sonar.md), ADR 0024. **L'API de Sonar n'a jamais été vérifiée** : le premier essai est le diagnostic.

Préparation : GG lancé, Sonar activé ; Paramètres › Intégrations › **SteelSeries Sonar** activé ; un bouton *Diagnostic Sonar* (catalogue › Sonar) posé sur le deck.

## A. Connexion et diagnostic (en premier)

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| A1 | Découverte | Activer le connecteur, attendre 5 s | État « connecté » dans Paramètres. Sinon lire l'erreur affichée et passer à A2 |
| A2 | Diagnostic | Toucher *Diagnostic Sonar* ; ouvrir `%APPDATA%\StreamDak\sonar-diagnostic.txt` | Le message liste les canaux et leurs niveaux. Le fichier montre `coreProps.json`, l'adresse de Sonar, les codes HTTP et le début de la réponse brute. **Envoyer ce fichier** (surtout s'il y a un échec ou un canal absent) |
| A3 | Niveaux réels | Noter les niveaux de Sonar (jeu, discussion, média, aux, micro, principal) et les comparer au diagnostic | Les pourcentages concordent (à 1 % près) |
| A4 | GG fermé | Quitter GG | L'intégration passe en erreur (« SteelSeries GG est introuvable ou fermé »), le deck reste fluide, rien ne plante. Relancer GG : reconnexion seule en moins d'une minute |
| A5 | Sonar éteint | Désactiver Sonar dans GG (si possible) | Message « Sonar est désactivé » ou « GG répond, mais Sonar n'y est pas » ; aucune exception |

## B. Actions Sonar

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| B1 | Volume absolu | Bouton `volume` avec `game=30`, le toucher | Le canal Jeu de Sonar passe à 30 (curseur de Sonar qui bouge) |
| B2 | Volume relatif | `chatRender=+10` puis `-10` | Le canal Discussion monte puis redescend de 10 |
| B3 | Coupure | `mute` puis `unmute` sur `media`, puis `togglemute` | Le canal est coupé / rétabli dans Sonar ; le voyant du bouton `togglemute` suit |
| B4 | Micro | `mute` sur `chatCapture` | Le micro de Sonar est coupé : vérifier dans une application de capture (Discord, enregistreur) |
| B5 | Modification dans GG | Changer un volume directement dans Sonar | Le titre `{sonar.game.volume}` et la roue suivent en 1 ou 2 s |
| B6 | Mode streamer | Si utilisé : régler `mode` sur `streamer` (et `slider`) | Mêmes essais ; sinon noter les routes réelles via le diagnostic |
| B7 | Canal inexistant | `volume` avec `xyz=10` | Message d'erreur lisible, rien ne casse |

## C. Audio Windows par périphérique

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| C1 | Lister | Étape *Audio : périphérique* › *Lister les périphériques* | Le message nomme les entrées et sorties : repérer le nom exact du micro Focusrite et des sorties Arctis (Game, Chat) |
| C2 | Couper le micro Focusrite | `toggle` avec `capture:Focusrite` | Le micro Focusrite est coupé (Paramètres Windows › Son › Entrée) ; le micro par défaut de Windows n'a pas changé |
| C3 | Niveau d'un casque | `volume` avec `render:Arctis=40` (nom selon C1) | Le niveau du périphérique passe à 40 dans le mélangeur de volume Windows ; la sortie par défaut ne change pas |
| C4 | Ambiguïté | `toggle` avec `Focusrite` (sans `capture:`) | Erreur qui liste les périphériques possibles, rien n'est modifié |

## D. Widget Niveau audio

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| D1 | Poser une roue | Catalogue › Widgets › Niveau audio ; propriétés : *Sonar : Jeu*, forme Roue, 2 × 2 | Une roue avec le niveau réel de Sonar au centre |
| D2 | Rotation au doigt | Poser le doigt sur l'anneau, tourner lentement puis vite, passer sous le centre, aller aux butées, revenir | Pas de saut au contact, niveau continu de 0 à 100, la roue repart dès qu'on revient d'une butée ; Sonar suit en temps réel (une valeur toutes les ~70 ms) |
| D3 | Coupure | Toucher le centre de la roue | « Muet » s'affiche, le canal Sonar est coupé ; retoucher : rétabli |
| D4 | Focus de la fenêtre | Pendant un geste sur la roue, regarder la fenêtre au premier plan (Bloc-notes par exemple) | Elle **garde le focus** : le deck n'en prend jamais ; un raccourci envoyé ensuite arrive toujours au Bloc-notes |
| D5 | Curseurs | Mêmes essais en curseur vertical (1 × 2) et horizontal (2 × 1) ; saisir la poignée, toucher ailleurs sur la piste | Poignée saisie sans saut ; ailleurs, elle va sous le doigt |
| D6 | Périphérique Windows | *Périphérique Windows*, pastille du micro Focusrite | La roue règle le niveau du micro Focusrite (hors gain matériel) ; le niveau suit les changements faits dans Windows en ~1 s |
| D7 | Sonar absent | Fermer GG | La tuile affiche l'erreur à la place du niveau, sans faux zéro ; elle revient quand GG revient |
| D8 | Taille tactile | Mesurer à la main une roue 1 × 1 | La roue se manipule au doigt sans difficulté (cible ≥ 48 px) |
| D9 | Thèmes | Passer en thème clair puis sombre | Contrastes lisibles (piste, remplissage, texte, coupure) |
| D10 | Appui long | Maintenir le doigt immobile sur la roue | Le menu de la case ne s'ouvre pas pendant le geste (modifier le widget se fait en mode édition) |
| D11 | Deck masqué | Réduire le deck 1 minute | Plus de requêtes vers GG (journal calme) ; au retour, la roue affiche le niveau réel |
| D12 | Écran de verrouillage et veille | Mettre en veille puis reprendre | Les niveaux se relisent, aucune erreur persistante |
