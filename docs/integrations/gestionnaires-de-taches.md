# ClickUp, Notion, monday.com

Trois connecteurs à jeton personnel, sur une base commune (`RestIntegrationBase`) : requêtes JSON, erreurs traduites
(jeton refusé, limite de débit avec le délai à attendre, message du service), jeton jamais au journal ni dans `config.json`
(chiffré DPAPI). Pas de relevé périodique : ces services limitent les requêtes, l'état est vérifié à la connexion
(**Tester la connexion**) et les erreurs apparaissent à l'exécution. Protocoles joués contre un faux serveur (`TaskServiceTests`).

| Service | Jeton | Réglages | Commandes |
|---|---|---|---|
| ClickUp (API v2) | Avatar › Paramètres › Apps › Jeton d'API (`pk_…`, envoyé sans « Bearer ») | Liste par défaut, espace de travail (team) | `task` (titre), `status` (`idTâche=statut`), `timer-start` (idTâche), `timer-stop` |
| Notion (version 2022-06-28) | Intégration interne sur notion.so/my-integrations, **connectée à la base** (⋯ › Connexions) | Base de données, propriété du titre (`Name`, `Nom`…), propriété du statut | `page` (titre de la ligne), `status` (`idPage=statut`) |
| monday.com (API 2025-04, GraphQL) | Avatar › Développeurs › Mon jeton d'accès | Tableau par défaut, colonne de statut (`status`) | `item` (nom), `status` (`idÉlément=libellé`) |

## Précautions

- monday : les valeurs saisies passent par des **variables GraphQL**, jamais collées dans la requête (un titre hostile ne peut pas ajouter une mutation) ; un test le vérifie.
- monday répond `200` avec un champ `errors` quand une requête est refusée : ces erreurs sont relayées.
- Notion limite à ≈ 3 requêtes/s (`429` + `Retry-After`) : le message indique combien attendre.
- Les noms de propriétés Notion dépendent de la langue de la base : à ajuster dans les réglages.

## Limites

Les identifiants (liste, tâche, page, élément) se lisent dans l'adresse web des services ; pas de recherche par nom pour l'instant.
Les limites de débit exactes de ClickUp et de monday sont à vérifier dans leur documentation officielle.
