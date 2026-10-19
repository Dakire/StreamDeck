# 0010 — Un gestionnaire par type d'action, enregistrés dans un registre

- **Statut** : acceptée.
- **Date** : 19 octobre 2026.

## Contexte

Les treize types d'action étaient des lambdas dans une liste statique (`BuiltInActions`). Ajouter un type, notamment pour
une intégration, obligeait à modifier cette liste du Core, et un type en double aurait masqué l'autre ou déclenché une
erreur de dictionnaire peu lisible.

## Décision

- **Une classe par type**, dans `Core/Actions/Handlers/` : `HotkeyAction`, `MediaAction`, `TextAction`, `PageAction`,
  `ProfileAction`, `DelayAction`, `UrlAction`, `LaunchAction`, `ScriptAction`, `FocusAction`, `HttpAction`, `AudioAction`,
  `TimerAction`.
- **Trois bases** : `SyncActionHandler` (instantané), `BackgroundActionHandler` (peut bloquer : hors du thread d'interface,
  avec délai maximal et annulation), `ActionHandler` (asynchrone libre). Les délais sont dans `ActionTimeouts`.
- **Contrat inchangé** : `IActionHandler` (identifiant stable, délai maximal, exécution annulable). Messages, délais et
  comportements sont identiques à ceux des lambdas, vérifiés par les tests existants.
- **Registre.** `ActionRegistry` refuse deux gestionnaires pour un même type (sans distinction de casse).
- **Conteneur.** Chaque gestionnaire est enregistré comme `IActionHandler` ; le registre est construit à partir de tous.
  Une intégration ajoute les siens dans `AppHost.Build`.

## Conséquences

- Ajouter un type d'action = ajouter une classe et une ligne dans `BuiltInActions.All` (voir `docs/DEVELOPMENT.md`).
- `ActionTypes.All` reste la source des identifiants valides pour la configuration ; un test vérifie qu'ils correspondent
  exactement aux gestionnaires intégrés.
- Le nom `IActionHandler` est conservé plutôt que `IAction` : l'objet qui décrit une étape (`DeckAction`) s'appelle déjà
  « action », le gestionnaire est ce qui l'exécute.