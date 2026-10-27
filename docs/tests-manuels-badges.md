# Tests manuels des badges de notifications

À faire sur une vraie machine (les tests automatiques n'ont ni compte ni bouton de barre des tâches). Utiliser `STREAMDAK_DATA_DIR=<dossier d'essai>` pour ne pas toucher à `%APPDATA%\StreamDak`.
Journal : `streamdak.log` du dossier de configuration. Fonctionnement : [Notifications](integrations/notifications.md), ADR 0020, 0022, 0023.

Préparation : Paramètres › Intégrations › Notifications activé ; une page web Google Chat (ou Gmail) dans le deck.

| # | Scénario | Étapes | Résultat attendu |
|---|---|---|---|
| 1 | Connexion Google | Ouvrir la page web Gmail du deck, se connecter, attendre un message non lu | Le badge de l'onglet apparaît avec le nombre ; il suit les lectures sans relance |
| 2 | Plusieurs comptes | Se connecter à un second compte Google dans le même cadre | Le badge reflète le compte de la session de StreamDak ; noter l'écart avec le navigateur habituel (limite connue) |
| 3 | Page en arrière-plan | Afficher une autre page que la page web | Le badge de l'onglet continue de se mettre à jour (page cachée) |
| 4 | Deck masqué ou suspendu | Réduire ou masquer le deck, recevoir un message, rouvrir | Au retour, le badge est à jour ; le journal montre la reprise du suivi |
| 5 | Redémarrage | Fermer puis relancer StreamDak | La session est conservée (pas de nouvelle connexion) ; le badge réapparaît sans action |
| 6 | Connecteur désactivé | Désactiver Notifications dans Paramètres › Intégrations | Plus aucun badge (onglets, touches), pastille de la barre des tâches retirée, plus de processus WebView2 caché |
| 7 | Hôte inconnu | Page web vers un site absent des services (ex. `https://example.org`) | Aucun badge, aucune erreur ; avec un hôte voisin `mail.google.com.exemple.net`, aucun badge non plus |
| 8 | Thème clair / sombre | Changer le thème (Paramètres) avec des non lus | `InfoBadge` de l'onglet lisible dans les deux thèmes, rouge conservé ; pastille de la barre des tâches redessinée |
| 9 | Plafond 99+ | Atteindre plus de 99 non lus (ou valeur simulée avec `--simulate`) | « 99+ » sur l'onglet et sur la pastille de la barre des tâches |
| 10 | Pastille de la barre des tâches | Non lus sur une page d'un autre profil que le profil affiché | La pastille affiche le total de tous les profils ; passe à rien à zéro ; **le bouton de la barre des tâches reste cliquable et le deck ne prend pas le focus** (envoyer un raccourci vers le Bloc-notes) |
| 11 | Écran à forte densité | Écran à 150 % ou 200 % | Pastille nette (icône 24 ou 32 px), chiffres lisibles, « 99+ » entier |
| 12 | Réglages par page | Mode édition › Options de la page › *Afficher le badge* décoché | Le badge de l'onglet disparaît, le total baisse ; *Annuler* le rétablit |
| 13 | Exclure du total | *Exclure du total* coché | Le badge de l'onglet reste, la pastille de la barre des tâches ne compte plus la page |
| 14 | Activité sans nombre | Service en mode `auto` dont seule l'icône change | Un simple point sur l'onglet, ni nombre ni pastille dans la barre des tâches |
| 15 | Lecteur d'écran | Narrateur sur un onglet avec badge | « Page …, 3 notifications non lues » ; coche des entrées du menu annoncée « activé » |
| 16 | Accessibilité tactile | Menu de la page au doigt | Entrées *Afficher le badge* et *Exclure du total* de 48 px de haut au moins |

## Badge absent : lire `streamdak.log`

1. Ouvrir `%APPDATA%\StreamDak\streamdak.log` (ou le dossier de `STREAMDAK_DATA_DIR`).
2. Chercher le nom du service (`gmail`, `gchat`, `discord`…) : la ligne donne le **titre lu** (masqué si sensible) et le **compteur** extrait.
3. Titre lu mais pas de compteur : le format a changé → décrire le service dans `notifications.json` (`titleRegex` ou `selector`), ou régler son `badgeMode`.
4. Aucune ligne pour le service : connecteur désactivé, service absent de *Services suivis*, mode `off`, ou aucun bouton / page web ne porte son adresse.
5. Compteur correct mais pas de badge : vérifier *Afficher le badge* de la page (menu de la page) ; la pastille de la barre des tâches ignore en plus les pages *Exclure du total*.
6. Avertissement « Pastille de la barre des tâches impossible » : l'overlay n'a pas pu être posé ; les badges d'onglet ne sont pas affectés.

## Non vérifié en réel à ce jour

- Titres réels de Gmail, Google Chat, Discord, Teams et Slack (formats connus, pas de compte de test).
- Overlay de la barre des tâches avec la `FluentWindow` et `WS_EX_NOACTIVATE`.
