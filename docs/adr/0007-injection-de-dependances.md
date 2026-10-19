# 0007 — Injection de dépendances par un conteneur seul

- **Statut** : acceptée.
- **Date** : 19 octobre 2026.

## Contexte

`AppServices` était une classe statique : document, moteur d'actions, minuteurs de sauvegarde, thème et ouverture des
fenêtres. Les ViewModels y puisaient directement. Conséquences : dépendances invisibles, tests qui initialisaient un état
global, impossibilité de brancher d'autres services (intégrations) sans modifier cette classe.

## Décision

- **Conteneur.** `AppHost.Build` (`Services/AppHost.cs`) est la racine de composition : un conteneur
  `Microsoft.Extensions.DependencyInjection`, sans l'hôte générique `Microsoft.Extensions.Hosting`.
- **Services.** Le document, la plateforme, le moteur d'actions, `ConfigPersistence`, `AppearanceService`,
  `DisplayService` et `IAppWindows` sont des singletons ; les ViewModels reçoivent leurs dépendances par constructeur.
- **Fenêtres.** Elles reçoivent l'`IServiceProvider` et construisent leur ViewModel avec `ActivatorUtilities`, en lui
  passant la fenêtre comme « shell » (interface de dialogues et de focus).
- **Tests.** Ils construisent **le même conteneur** en simulation, sur un dossier temporaire.
- **Statiques conservées.** `Notifier` (bus de messages), `Theme` et `Motion` (ressources globales de l'application WPF),
  `ConfigStore` et `IconStore` (fichiers), façade `Log`. Aucune ne porte d'état métier.

## Pourquoi pas l'hôte générique

L'application n'a ni service de fond, ni configuration externe, ni arrêt orchestré à gérer. Mesuré : la construction passe
de 18 ms à 5 ms sans l'hôte, avec moins d'assemblies chargés. Au total, le conteneur, Serilog et la résolution des
services coûtent environ 55 ms de démarrage par rapport à la phase 1 (≈ 670 ms contre ≈ 630 ms), soit moins que les 700 ms
de la version initiale. Si un jour une intégration a besoin de services de fond (reconnexion d'un client OBS), `IHostedService` pourra
être réintroduit sans toucher aux ViewModels.

## Conséquences

- Les dépendances de chaque classe se lisent dans son constructeur ; un test (`ArchitectureTests`) interdit le retour d'un
  localisateur statique ou d'un `IServiceProvider` dans un ViewModel.
- Une intégration ajoute ses services et ses gestionnaires d'action dans `AppHost.Build`.
- Les `ActivatorUtilities` résolvent les constructeurs par réflexion : un paramètre non enregistré échoue à l'ouverture de
  la fenêtre. `CompositionTests` et les tests d'interface couvrent chaque fenêtre.