# Décisions d'architecture

Chaque fichier consigne une décision : son contexte, le choix fait et ses conséquences. Une décision remplacée n'est pas
effacée : elle passe au statut « Remplacée par … ».

| N° | Décision | Statut |
|---|---|---|
| [0001](0001-wpf-conserve.md) | Garder WPF plutôt que migrer vers Avalonia ou WinUI 3 | Acceptée |
| [0002](0002-document-unique.md) | Un seul document, modifié par des opérations annulables | Acceptée |
| [0003](0003-plateforme-abstraite.md) | Appels système derrière des interfaces, plateforme simulée | Acceptée |
| [0004](0004-format-json-versionne.md) | Configuration JSON versionnée, migration sans perte | Acceptée |
| [0005](0005-qualite-a-la-compilation.md) | Analyseurs, style et documentation vérifiés à la compilation | Acceptée |
| [0006](0006-journal-maison.md) | Journal maison plutôt qu'une bibliothèque de journalisation | Remplacée par 0008 |
| [0007](0007-injection-de-dependances.md) | Injection de dépendances par un conteneur seul, sans hôte générique | Acceptée |
| [0008](0008-journal-serilog.md) | Journal Serilog derrière une façade du Core | Acceptée |
| [0009](0009-trois-projets.md) | Trois projets (Core, plateforme Windows, application) aux frontières vérifiées | Acceptée |
| [0010](0010-gestionnaires-d-action.md) | Un gestionnaire par type d'action, enregistrés dans un registre | Acceptée |
| [0011](0011-cadre-d-integrations.md) | Cadre d'intégrations : un projet par connecteur, aucune nouvelle action | Acceptée |
| [0012](0012-plugins-et-api-locale.md) | Plugins locaux et API locale par canal nommé | Acceptée |
| [0013](0013-pile-pointeur-wpf.md) | Toucher du deck sans souris système | Acceptée |
| [0014](0014-creation-rapide.md) | Création rapide d'un bouton depuis le deck | Acceptée |
| [0015](0015-edition-integree-et-material-design-3.md) | Édition intégrée au deck et thème Material Design 3 | Acceptée |

Modèle d'une nouvelle décision : contexte, décision, conséquences, statut et date.
