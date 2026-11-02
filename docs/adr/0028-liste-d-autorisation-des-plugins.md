# 0028 — Liste d'autorisation des plugins par empreinte

- **Statut** : acceptée.
- **Date** : 2 novembre 2026.

## Contexte

Un plugin est une DLL .NET exécutée avec les droits de l'utilisateur. Seule son empreinte SHA-256 était consignée, et **après** le chargement (donc après l'exécution de son code statique) ; ses dépendances
n'étaient pas hachées (audit 2, constat 3.12).

## Décision

- `plugins\trusted.json` : `{ "NomDuDossier": "EMPREINTE" }`. L'empreinte couvre **toutes les bibliothèques** du dossier du plugin (noms relatifs et contenus, dans l'ordre des noms).
- Elle est calculée et vérifiée **avant** `LoadFromAssemblyPath` : le code d'un plugin non autorisé ne s'exécute jamais.
- **Compatibilité** : au premier démarrage où le fichier n'existe pas, les plugins déjà posés sont autorisés (ils l'étaient jusque-là) et le fichier est créé. Un plugin posé ensuite est refusé ; le journal
  affiche la ligne exacte à ajouter. Le dossier `plugins` est créé à ce moment pour qu'un plugin déposé plus tard ne soit pas autorisé d'office.
- Fichier illisible : **aucun** plugin n'est chargé (on ne repart pas d'une liste vide en silence).
- Le fichier est à part de `config.json` : l'import d'une configuration remplace tout le document et pourrait sinon autoriser un plugin.

## Limite

La liste protège d'un ajout ou d'une modification **à l'insu** de l'utilisateur, pas d'un attaquant qui peut déjà écrire dans `%APPDATA%\StreamDak` (il pourrait aussi éditer `trusted.json`) : c'est hors du modèle de menace (SECURITY.md).
