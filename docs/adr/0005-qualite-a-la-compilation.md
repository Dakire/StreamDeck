# 0005 — Analyseurs, style et documentation vérifiés à la compilation

- **Statut** : acceptée.
- **Date** : 19 octobre 2026.

## Contexte

La compilation était sans avertissement, mais aucun analyseur n'était actif et aucune règle de style n'était vérifiée.
Les membres publics n'étaient documentés qu'en partie.

## Décision

- **Réglages communs.** `Directory.Build.props` s'applique à l'application et aux tests : `AnalysisLevel`
  `latest-recommended`, `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors` et `GenerateDocumentationFile`.
- **Style.** `.editorconfig` décrit le style du dépôt et les exceptions, chacune motivée par un commentaire.
- **Documentation XML (CS1591).** Elle est obligatoire sur toute API publique. Elle explique le pourquoi et les
  contraintes ; `<inheritdoc/>` sert pour les implémentations d'interface.
- **`ImplicitUsings` reste désactivé** (règle d'`AGENTS.md`) : les dépendances de chaque fichier restent visibles.

## Conséquences

- Tout nouvel avertissement bloque la compilation, donc la livraison.
- Ajouter un membre public sans commentaire XML fait échouer la compilation.
- Une règle trop bruyante se désactive dans `.editorconfig`, avec sa justification, jamais par `#pragma` dispersé.
