# 0020 — Badges de notifications sur les boutons « site web »

- **Statut** : acceptée.
- **Date** : 26 octobre 2026.

## Contexte

Un bouton qui ouvre Gmail, Discord ou Teams ne dit pas s'il y a quelque chose à lire. Demande : un badge rouge avec le nombre de non lus (« 99+ »),
sans stocker d'identifiants, avec une session persistante, une lecture économe (événements plutôt que relevés), un fournisseur par service
(`INotificationProvider`) et un fournisseur générique configurable.

## Décision

**Lecture** (`Core/Notifications`, pur et testé) : `INotificationProvider` donne l'adresse suivie, les hôtes reconnus et le nombre d'après le **titre** de
la page ; un script en **lecture seule** (sélecteur CSS) n'existe que pour les services décrits avec un sélecteur. `NotificationProvider` couvre Gmail,
Google Chat, Discord, Teams, Slack (motif de titre) et les services de `notifications.json` (expression régulière avec délai de 100 ms, sélecteur et
attribut bornés, passés à la page comme chaînes JSON). Un nombre n'est lu que sur une page de l'**hôte du service** : une page de connexion d'un autre
domaine, ou un hôte qui lui ressemble (`mail.google.com.evil.net`), donne « inconnu », jamais un badge.

**Suivi** (`Services/WebNotificationWatcher`) : une page WebView2 **cachée** par service qui a un bouton, créée dans l'environnement WebView2 des cadres
visibles : **même profil, donc même session** (se connecter dans une page web du deck suffit ; StreamDak ne lit ni ne stocke aucun identifiant). Pas de
fenêtre, son coupé, permissions refusées, téléchargements et nouvelles fenêtres bloqués, aucun objet hôte. Le compteur est relu sur `DocumentTitleChanged`,
fin de navigation et changement d'adresse ; **aucune minuterie** sauf pour un service à sélecteur (60 s). Deck masqué : `TrySuspendAsync`, reprise au retour.
Page plantée : nouvelle tentative après 1 minute.

**Connecteur** (`Integrations/Notifications`, id `notify`) : activé dans Paramètres › Intégrations, il lance le suivi et publie `notify.<service>` ; les
touches lisent ces valeurs comme n'importe quelle valeur d'intégration. Les réglages vivent dans `config.json` › `Integrations` (déjà existant) : **aucune
nouvelle clé de `config.json`**. Les services de l'utilisateur sont dans un fichier à part, `notifications.json`.

**Affichage** : `TileFaceData.Badge`, pastille rouge `#D92D20` à texte blanc (contraste 5,2 : 1 dans les deux thèmes), au coin haut droit ; le badge
d'étapes se décale à gauche. Nom accessible : « …, 3 notification(s) ».

## Pourquoi pas…

- **Une session par service avec connexion dédiée** : demanderait une fenêtre de connexion par service et des profils séparés. Le profil partagé suffit à
  l'usage et se règle par la page web que l'utilisateur a déjà. Conséquence : le suivi voit le compte connecté dans le deck, pas celui d'un autre navigateur.
- **Lire le favicon** : aucun des services ciblés n'y met de compteur fiable ; le titre est plus simple et change sur événement.
- **Les API officielles** (Gmail, Teams…) : comptes, consentement OAuth et secrets par service, très au-delà d'un badge.

## Conséquences

- Un processus de navigateur (≈ 100 Mo) par service suivi, seulement pour ceux qui ont un bouton ; zéro quand le connecteur est éteint.
- **Non vérifié ici** (pas de compte) : les titres réels des cinq services. Les formats sont ceux connus ; s'ils changent, le badge disparaît, et le
  fournisseur générique permet de le rétablir sans mise à jour.
- Sécurité : aucun secret, aucune exécution de code distant par StreamDak (la page s'exécute dans le navigateur isolé, comme les cadres des ADR 0017 et 0018) ;
  réseau seulement vers les services dont l'utilisateur a mis l'adresse sur un bouton.
