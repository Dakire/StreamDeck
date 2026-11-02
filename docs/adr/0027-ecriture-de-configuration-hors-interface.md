# 0027 — Écriture de `config.json` hors du thread d'interface

- **Statut** : acceptée.
- **Date** : 2 novembre 2026.

## Contexte

`ConfigPersistence` sérialisait toute la configuration, relisait le fichier existant, écrivait un fichier temporaire, calculait un SHA-256 et remplaçait le fichier depuis un `DispatcherTimer`, donc sur le
thread d'interface (audit 2, constat 1.3). La surveillance du fichier relisait et hachait aussi le contenu sur l'UI, y compris pour ses propres écritures.

## Décision

- L'**instantané** est pris sur le thread d'interface : `ConfigStore.Serialize(config)` donne une chaîne immuable. `DeckConfig` est mutable et partagé, on ne l'envoie jamais à un autre thread.
- Un **écrivain unique** du pool de threads écrit l'instantané (`ConfigStore.SaveJson`, sérialisé par un verrou). Les instantanés demandés pendant une écriture **fusionnent** : seul le dernier est écrit.
- Chaque instantané porte un **numéro de version** ; une écriture plus ancienne que celle déjà faite est abandonnée (un `Flush` synchrone ne peut pas être écrasé par un écrivain en retard).
- `Flush()` (fermeture, tests) reste **synchrone** : il écrit l'instantané en attente et attend l'écriture en cours. `App.OnExit` l'appelle avant de libérer les intégrations.
- La surveillance relit et hache **hors UI** (`Task.Run(ConfigStore.IsOwnWrite)`) avant de recharger.
- Une panne d'écriture est notifiée **une fois** (pas un toast toutes les 300 ms) et réessayée depuis l'UI.
- Si des changements locaux sont en attente quand une modification externe est détectée, ils l'emportent et un message le dit (avant : écrasement silencieux de la modification externe).

## Alternatives écartées

- Cloner `DeckConfig` : il n'a pas de clone dédié, un aller-retour JSON coûterait autant que la sérialisation qu'on veut garder.
- Un fil dédié avec file : le pool suffit, l'écrivain n'existe que pendant les écritures.

## Conséquences

- Le thread d'interface ne fait plus que sérialiser (CPU) ; plus aucun accès disque.
- `ConfigStore._lastWrittenHash` est lu et écrit par `Volatile`.
