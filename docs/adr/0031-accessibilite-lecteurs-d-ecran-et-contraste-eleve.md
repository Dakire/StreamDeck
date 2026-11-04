# 0031 — Accessibilité : annonces pour lecteurs d'écran, contraste élevé, erreurs persistantes

- **Statut** : acceptée.
- **Date** : 4 novembre 2026.

## Contexte

Un audit d'accessibilité a relevé des écarts que ni Lighthouse ni un test de nom de contrôle ne voient : le message d'état (`Border` + `LiveSetting`) n'était jamais annoncé
(un `Border` n'a pas de peer UI Automation), la boîte de dialogue n'avait ni nom ni rôle et laissait le clavier atteindre les panneaux placés derrière son voile, le mode contraste élevé
de Windows n'était pas suivi, les erreurs disparaissaient au bout de 5 s, et plusieurs poignées (redimensionnement, réordonnancement des intégrations) n'avaient pas de peer.

## Décision

- **Annonces** : `MainViewModel.Announced` (levé par `SetStatus` et par le succès d'une action) est transmis par `MainWindow` à UI Automation
  (`RaiseNotificationEvent`). Une erreur est annoncée en `ImportantMostRecent`, une information en `MostRecent`. L'ouverture d'une boîte de dialogue annonce son titre et son message.
- **Peers** : `Controls/AccessibleContent` est un `ContentControl` à peer propre (rôle réglable) ; il sert de boîte de dialogue (rôle fenêtre, nom lié au titre) et de poignée de
  redimensionnement (rôle « thumb »). La poignée de réordonnancement des intégrations devient un `Button` plat (nom, rôle, `HelpText`) ; glisser-déposer et Alt + flèches sont conservés.
- **Dialogue** : `KeyboardNavigation` en cycle dans la boîte ; tant qu'elle est ouverte, la barre, la grille (panneaux d'édition compris) et le dock sont désactivés (`IsEnabled`), donc hors d'atteinte du clavier et des lecteurs d'écran.
- **Contraste élevé** : si `SystemParameters.HighContrast`, `Theme` publie des brushes `Deck*` issues de `SystemColors` (`Theme.BuildHighContrast`, contrastes testés sur les jeux noir et blanc de Windows) et
  se réapplique quand le réglage change (`SystemParameters.StaticPropertyChanged`). Le réglage de thème de l'utilisateur est ignoré tant que le contraste élevé est actif.
- **Erreurs** : une erreur reste affichée jusqu'à « Fermer » (cible de 48 px) ou jusqu'au message suivant ; seules les informations s'effacent (WCAG 2.2.1).
- **Divers** : l'onglet de la page affichée est annoncé « page courante » ; un avertissement s'affiche si la couleur de texte libre d'un bouton a un contraste inférieur à 4,5 : 1 ; les erreurs de saisie de l'inspecteur
  sont liées au champ (`HelpText`, `ItemStatus`) ; les noms des boutons d'étape sont indexés (« Étape 2 : supprimer ») ; les pastilles de sous-page font au moins 28 px de large.

## Conséquences

- Aucun essai avec un vrai lecteur d'écran (NVDA, Narrateur) n'a été fait : les tests vérifient les peers, les noms, les rôles et les événements levés, pas ce qui est réellement prononcé.
- Le rôle « fenêtre » d'`AccessibleContent` pour la boîte de dialogue est un choix : UI Automation n'a pas de rôle « dialogue » ; son rendu varie selon le lecteur.
- Une erreur d'action reste à l'écran jusqu'à fermeture : sur un deck laissé sans surveillance, le dernier message d'erreur est donc encore visible.
