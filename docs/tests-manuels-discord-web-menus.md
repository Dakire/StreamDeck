# Tests manuels : Discord en arrière-plan, pages web passives, paramètres, menus, mélangeur, nouveaux widgets

À dérouler sur le PC réel : les tests automatiques couvrent le protocole (faux client Discord, vrai WebView2 hors écran, harnais WPF), pas le vrai Discord,
un vrai écran tactile ni SteelSeries GG. Utiliser `STREAMDAK_DATA_DIR=<dossier d'essai>` pour ne pas toucher à `%APPDATA%\StreamDak`.
Journal : `streamdak.log` du dossier de configuration.

## A. Discord par l'intégration (RPC local)

Préparation : [Discord](integrations/discord.md) › Mise en route. Être dans un salon vocal. Poser trois boutons : *Micro : couper / rétablir*,
*Sourdine : activer / désactiver*, *Quitter le salon vocal* (catalogue › Discord), et un widget **Niveau audio** sur `Discord : Micro`.

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| A1 | Autorisation | « Autoriser dans Discord », accepter dans Discord | « Association réussie », état « Connecté » |
| A2 | Discord au premier plan | Toucher *Micro* | Le micro est coupé dans Discord, le voyant de la touche est plein ; re-toucher : rétabli |
| A3 | **Discord en arrière-plan** | Mettre le Bloc-notes au premier plan, toucher *Micro* et *Sourdine* | Discord réagit ; le Bloc-notes garde le focus et ne reçoit aucune touche |
| A4 | **Discord réduit** | Réduire Discord, recommencer A3 | Idem |
| A5 | Jeu en administrateur | Lancer une application en administrateur au premier plan (ex. Gestionnaire des tâches), toucher *Micro* | Discord réagit (pas de touche simulée) |
| A6 | Changement dans Discord | Couper le micro dans Discord lui-même | Le voyant de la touche et la pastille du widget suivent en moins d'une seconde |
| A7 | Volume | Glisser le widget Niveau audio | Le curseur « Volume d'entrée » de Discord (Paramètres › Voix et vidéo) suit |
| A8 | Quitter | Toucher *Quitter le salon vocal* | Tu quittes le salon ; le voyant s'éteint |
| A9 | Discord fermé | Quitter Discord (zone de notification › Quitter) | L'intégration passe en erreur « Discord a fermé la connexion », le deck reste fluide ; relancer Discord : reconnexion seule (60 s au plus) |
| A10 | Redémarrage de StreamDak | Fermer StreamDak, le relancer | Connexion sans nouvelle autorisation (jeton renouvelé et chiffré) |
| A11 | Rebond | Toucher deux fois *Micro* très vite (< 150 ms) | Une seule bascule |

## B. Discord par raccourci global (sans application Discord)

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| B1 | Déclaration | Discord › Raccourcis clavier › Ajouter › *Toggle Mute* › taper Ctrl+Alt+Maj+M | Raccourci enregistré |
| B2 | Arrière-plan | Bloc-notes au premier plan, toucher *Discord : micro (raccourci)* | Discord coupe le micro ; aucun caractère n'apparaît dans le Bloc-notes |
| B3 | Élévation | Une application en administrateur au premier plan, toucher le bouton | La touche passe en erreur : « Windows a bloqué les touches : l'application au premier plan est exécutée en administrateur… » |
| B4 | Conflit | Mode édition, un bouton avec la seule étape `ctrl+shift+m` | Avertissement : « … c'est aussi : Teams : micro ; Discord : micro (raccourci local…) » |

## C. Pages web : capture de la souris

Préparation : un widget **Page web** (Google Chat ou un site avec un champ de saisie qui prend le focus) et une page web plein cadre.

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| C1 | Défaut (toucher pour interagir) | Survoler et faire défiler la molette sur la page | Rien ne se passe dans la page ; la bande dit « Toucher la page pour interagir » |
| C2 | Focus non volé | Bloc-notes au premier plan, attendre que la page charge (champ « autofocus ») | Le Bloc-notes garde le clavier |
| C3 | Toucher pour interagir | Toucher (ou cliquer) la page | Le bouton « Interagir » s'allume ; liens, défilement et saisie marchent |
| C4 | Fin automatique | Ne plus toucher la page 30 s | La page redevient passive (journal : « interaction terminée ») |
| C5 | Terminer | Pendant l'interaction, toucher le bouton « Interagir » | Retour à l'affichage seul |
| C6 | Mode passif | Propriétés › Interaction › *Passive*, puis toucher la page | Rien ne se passe ; appui long ou clic droit sur la page : menu de la case |
| C7 | Mode interactif | *Interactive* | Comportement d'avant : la page reçoit tout, en permanence |
| C8 | Page plein cadre | Mode édition › Page › *Interaction avec la page* | Les trois modes, cochés selon le choix ; dock et onglets restent utilisables |
| C9 | Tactile | Sur l'écran tactile, refaire C1, C3, C6 au doigt | Même comportement ; le pointeur de la souris ne bouge pas |

## D. Fermeture des paramètres

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| D1 | Fermeture | Ouvrir les paramètres, parcourir les sections, fermer | Fermeture immédiate. Journal : « Fermeture des paramètres : N ms (…) », N < 100 |
| D2 | Saisie d'un secret | Taper un mot de passe d'intégration puis fermer aussitôt | Le secret est enregistré (rouvrir : il est là) ; la connexion n'a pas été relancée à chaque caractère |

## E. Clic droit et menus

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| E1 | Mode normal | Clic droit / appui long sur un bouton, une case vide, un dossier, un widget | Menu instantané, adapté : *Ouvrir le dossier*, *Régler le widget…*, *Remplacer par*… |
| E2 | Mode édition | Clic droit successif sur plusieurs touches | Menu instantané ; aucune ligne « Menu de la case … ouvert en N ms » dans le journal |
| E3 | Sans clic droit | Mode édition, toucher une touche puis le bouton **Case** du dock | Le menu de la case s'ouvre |
| E4 | Remplacer | Menu d'un bouton › Remplacer par › une action | Le bouton est remplacé ; *Annuler* le rend |

## F. Mélangeur audio et Sonar

Voir aussi [Tests manuels Sonar](tests-manuels-sonar.md).

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| F1 | Mélangeur | Widget **Mélangeur audio** (3 × 2), GG lancé | Cinq lignes Sonar avec pastille verte, valeur exacte, couper, ↺ |
| F2 | Curseur | Glisser la ligne *Jeu* | Le canal Jeu de Sonar suit sans à-coups ; la valeur affichée = celle de GG |
| F3 | Synchronisation | Changer le volume dans GG | La ligne suit en 1 à 2 s |
| F4 | Réinitialisation | Toucher ↺ | Le canal revient au niveau réglé (100 % par défaut) |
| F5 | GG fermé | Quitter GG | Pastilles grises, message à la place du curseur, aucun faux zéro ; relancer GG : retour seul |
| F6 | Clarté | Lire le sous-titre de chaque ligne | « Mélangeur Sonar », « Volume Windows du périphérique », « Volume d'entrée de Discord » : jamais un volume Windows présenté comme Sonar |

## G. Nouveaux widgets et actions

| # | Scénario | Résultat attendu |
|---|---|---|
| G1 | *Verrouiller la session* | Windows se verrouille (l'ancien Win+L simulé ne le faisait pas) |
| G2 | *Chronomètre* : toucher, attendre, toucher | Le temps écoulé défile sur la touche ; à l'arrêt, « Chronomètre arrêté : m:ss » |
| G3 | *Batterie* (portable) / *Durée de fonctionnement* / *Périphériques audio actifs* / *Lecture en cours* (Spotify) | Valeurs réelles ; *Lecture en cours* demande d'activer « Système Windows » sinon |
| G4 | *Flux d'actualités* avec un flux RSS public | Les derniers titres ; adresse fausse : message lisible |
| G5 | *Script PowerShell* `exit 3` | La touche passe en erreur « Le script a échoué (code 3) » |
| G6 | *Terminal dans un dossier*, *Connexion SSH*, *Projet dans VS Code*, outils (*Services*, *Son : périphériques*…) | Le programme s'ouvre au bon endroit |
