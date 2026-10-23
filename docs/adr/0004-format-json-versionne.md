# 0004 — Configuration JSON versionnée, migration sans perte

- **Statut** : acceptée.
- **Date** : 2026.

## Contexte

Le fichier `config.json` est le seul état durable de l'utilisateur. Il est souvent modifié à la main et doit survivre aux
mises à jour de StreamDak. Le passage aux profils a imposé un changement de structure : `Pages` à la racine est devenu
`Profiles[].Pages`.

## Décision

- **Version.** Un champ `Version` (actuellement 2) identifie le format. `ConfigNormalizer` migre l'ancien format au
  chargement, après une copie intacte (`config.v1.backup.json`), puis répare sans rien supprimer de valide.
- **Nouvelles clés.** Toute nouvelle clé a une valeur par défaut et `JsonIgnore(WhenWritingDefault/Null)`.
- **Renommage ou suppression.** Une clé n'est renommée ni supprimée sans migration ni test. Tout changement de format
  est validé avec le mainteneur.
- **Écriture.** L'écriture est atomique (fichier temporaire puis `File.Replace`). Les sauvegardes sont automatiques
  (15 conservées). Un fichier invalide est mis de côté, jamais écrasé.
- **Identifiants figés.** Les identifiants de type d'action sont des constantes (`ActionTypes`) dont les valeurs sont
  figées par un test.

## Conséquences

- Un fichier écrit par une ancienne version reste lisible ; un fichier écrit par la version actuelle reste léger.
- Le détail du format est dans [configuration.md](../configuration.md).
- Les réglages des futures intégrations (hôte, port, compte) demanderont une nouvelle clé, donc une validation préalable.
  Les secrets n'y seront jamais écrits (DPAPI ou Gestionnaire d'identification).
