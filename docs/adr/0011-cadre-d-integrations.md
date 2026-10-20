# 0011 — Cadre d'intégrations : un projet par connecteur, aucune nouvelle action

- **Statut** : acceptée.
- **Date** : 20 octobre 2026.

## Contexte

TouchDeck doit piloter des logiciels et services variés (OBS, vMix, Hue, Teams, simulateurs…), afficher leur état sur les
touches, et ne jamais être bloqué ni déstabilisé par l'un d'eux. L'étude (`docs/integrations-research.md`) montre des
protocoles très différents (WebSocket, HTTP, canal nommé, WinRT) et des secrets à protéger.

## Décision

- **Un contrat** `IIntegration` (Core) : identifiant, réglages, commandes, état de connexion, valeurs lisibles, connexion et
  exécution. `IntegrationBase` fournit l'état et les valeurs ; `PolledHttpIntegration` factorise les appareils HTTP.
- **Un projet par connecteur** (`Integrations/<Nom>/`, `net10.0` sauf Système Windows pour WinRT) qui référence seulement le Core.
  Un connecteur en panne ou instable se retire sans toucher au reste ; les dépendances ne se mélangent pas.
- **Aucun nouveau format d'action.** Une étape de bouton est `{ Type = <id de l'intégration>, Value = <commande>, Args = <argument> }`.
  Un seul gestionnaire générique (`IntegrationAction`) relaie vers l'intégration. Les anciens fichiers restent lisibles.
- **`IntegrationManager`** : une boucle par intégration activée, délai de connexion de 10 s, reprise après 2, 4, 8, 16, 32 puis 60 s,
  redémarrage si les réglages changent, déconnexion propre à la désactivation. Une intégration en erreur n'a aucun effet sur le deck.
- **Secrets** hors de `config.json` : `ISecretStore` (DPAPI, portée utilisateur, `secrets.dat`). Jamais au journal.
- **Valeurs et voyants.** Chaque intégration publie des valeurs (`obs.scene`) utilisables dans les titres (`{obs.scene:repli}`) ;
  une commande peut désigner la valeur qui allume le voyant de la touche. Les touches ne se redessinent que si leur valeur change.
- **Catalogue.** Les commandes deviennent des actions du catalogue, dans une catégorie propre à l'intégration.
- **Test sans le vrai logiciel.** Chaque connecteur est testé contre un faux serveur local (HTTP, WebSocket) ; les écarts possibles
  avec le vrai logiciel sont signalés « à vérifier » dans sa documentation.

## Conséquences

- Une nouvelle intégration = un projet, une ligne dans `AppHost`, une page `docs/integrations/<nom>.md`, des tests contre un faux.
- Clé de configuration `Integrations` ajoutée (activation et réglages non secrets), validée par l'utilisateur.
- Le projet de l'application et celui des tests passent à `net10.0-windows10.0.19041.0` (API média WinRT du connecteur système).
