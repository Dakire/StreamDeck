# Notion

Un connecteur à jeton personnel, sur une base commune (`RestIntegrationBase`) : requêtes JSON, erreurs traduites
(jeton refusé, limite de débit avec le délai à attendre, message du service), jeton jamais au journal ni dans `config.json`
(chiffré DPAPI). Pas de relevé périodique : ces services limitent les requêtes, l'état est vérifié à la connexion
(**Tester la connexion**) et les erreurs apparaissent à l'exécution. Protocole joué contre un faux serveur (`TaskServiceTests`).

| Service | Jeton | Réglages | Commandes |
|---|---|---|---|
| Notion (version 2022-06-28) | Intégration interne sur notion.so/my-integrations, **connectée à la base** (⋯ › Connexions) | Base de données, propriété du titre (`Name`, `Nom`…), propriété du statut | `page` (titre de la ligne), `status` (`idPage=statut`) |

## Précautions

- Notion limite à ≈ 3 requêtes/s (`429` + `Retry-After`) : le message indique combien attendre.
- Les noms de propriétés Notion dépendent de la langue de la base : à ajuster dans les réglages.

## Limites

Les identifiants (liste, tâche, page, élément) se lisent dans l'adresse web des services ; pas de recherche par nom pour l'instant.
