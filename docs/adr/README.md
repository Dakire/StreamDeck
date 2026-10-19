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
| [0006](0006-journal-maison.md) | Journal maison plutôt qu'une bibliothèque de journalisation | Acceptée, à réexaminer en phase 2 |

Modèle d'une nouvelle décision : contexte, décision, conséquences, statut et date.
