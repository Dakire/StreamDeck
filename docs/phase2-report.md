# Bilan de la phase 2 : architecture et fondations

Branche `refactor/architecture`, 19 octobre 2026, à la suite de la phase 1 (fusionnée dans `main`). Principe : aucun
changement de comportement visible. Chaque étape est un commit compilé sans avertissement et testé au vert.

## En bref

- **Trois projets** aux frontières vérifiées par le compilateur et par les tests : `StreamDak.Core` (`net9.0`, sans WPF ni
  Windows), `StreamDak.Platform.Windows` (Win32 interne, sans WPF) et l'application WPF.
- **Injection de dépendances** : le localisateur statique `AppServices` est supprimé. `AppHost.Build` construit un
  conteneur ; ViewModels et fenêtres reçoivent leurs dépendances par constructeur ; les tests construisent le même
  conteneur en simulation.
- **Journal Serilog** derrière une façade du Core ; `ILogger<T>` pour les services injectés.
- **Un gestionnaire par type d'action** (13 classes) dans un registre qui refuse les doublons : une intégration s'ajoute
  sans modifier le Core.
- **Dossiers trop profonds** : le contenu n'est plus ignoré (point validé en phase 1), il est remonté.
- **Tests** : 183 → 232.

## Ce qui a changé

| Commit | Étape |
|---|---|
| `ee15c76` | Dossiers au-delà de 8 niveaux : le contenu est remonté dans le dernier dossier autorisé, plus aucun bouton perdu |
| `11b2291` | Projet `StreamDak.Core`. Trois dépendances cachées levées : `HotkeySyntax`, `IconNames`, fond par défaut |
| `f8aacbb` | Conteneur d'injection, `ConfigPersistence`, `AppearanceService`, `IAppWindows`, `DisplayService` injecté, Serilog |
| `e55edc7` | Une classe par type d'action, `ActionRegistry` |
| `0a9e291` | Projet `StreamDak.Platform.Windows` (types internes), `ArchitectureTests`, `AGENTS.md` et `CLAUDE.md` à jour |
| `cd53bc5` | Conteneur seul au lieu de l'hôte générique : construction 18 ms → 5 ms |

Documentation : `docs/ARCHITECTURE.md` réécrit (trois projets, composition), ADR 0007 à 0010, `docs/DEVELOPMENT.md`,
`docs/configuration.md`, `README.md`, `CONTRIBUTING.md`, `CHANGELOG.md`.

## Mesures

Application réelle en mode `--simulate`, 32 touches, médianes de 3 lancements, versions mesurées en alternance
(deux séries, script hors dépôt).

| Mesure | Version initiale | Fin de phase 1 | Fin de phase 2 |
|---|---|---|---|
| Démarrage | ≈ 700 ms | 609 à 658 ms | 665 à 682 ms |
| Mémoire de travail | 172 Mo | 161 Mo | 164 à 165 Mo |
| Mémoire privée | 139,5 Mo | 129,5 Mo | 129,7 Mo |
| CPU au repos (10 s) | 0 à 31 ms | 16 à 31 ms | 16 ms |

**Régression par rapport à la phase 1** : environ 55 ms de démarrage et 3 Mo de mémoire de travail, pour le conteneur,
Serilog et l'injection par réflexion. Le résultat reste meilleur que la version initiale. Le journal du démarrage donne la
décomposition : services prêts en ≈ 100 ms, dont journal 8, déclarations 3, construction du conteneur 5. Le reste est du
code qui existait déjà (configuration, thème). Essayé sans effet : `TieredPGO` désactivé. Non essayé : compilation
ReadyToRun, qui ne s'applique qu'à la publication.

Hôte générique : la première version utilisait `Microsoft.Extensions.Hosting` ; son retrait a ramené la construction de
18 ms à 5 ms et fait passer le dossier de sortie de 37 à 15 DLL (ADR 0007).

## Qualité

| | Avant | Après |
|---|---|---|
| Avertissements (compilation stricte) | 0 | 0 |
| Tests | 183 | 232 |
| Taille du dossier de sortie | — | 15 DLL (37 avec l'hôte générique) |

Nouveaux tests : composition (services, singletons, journal Serilog), registre d'actions, syntaxe des raccourcis et accord
avec la table Windows, noms d'images, architecture (références d'assemblies, appels natifs, API publique), dossiers trop
profonds, `DisplayService` injecté.

## Ce que je n'ai pas fait, et pourquoi

- **Code-behind des fenêtres.** `EditorWindow.xaml.cs` (≈ 400 lignes) ne contient que de la logique de vue : raccourcis
  clavier, glisser-déposer, menus contextuels, focus, boîtes de dialogue. Les décisions (`Drop`, `MoveSelection`,
  annulation, validation) sont dans les ViewModels et testées. Déplacer cette plomberie dans des comportements attachés
  serait un chantier de plus de 20 fichiers sans changement visible ; à reprendre si la fenêtre grossit.
- **Projet de tests séparé pour le Core.** La plupart des tests de logique n'ont pas besoin de WPF, mais partagent un
  harnais (`TempConfigDir`, `Samples`) qui en dépend. Le découpage est possible ; il n'apporte rien tant que le Core est
  vérifié par `ArchitectureTests`.
- **`ILogger<T>` partout.** Les classes statiques du Core (`ConfigStore`, `ActionValidator`) gardent la façade `Log` ;
  seuls les services injectés utilisent `ILogger<T>`.
- **Messages à l'utilisateur injectés.** `Notifier` reste un bus statique (56 usages dans l'éditeur) : il ne porte pas
  d'état métier, et le remplacer ne ferait que déplacer un paramètre de constructeur.
- **Migration automatique du schéma.** Elle existait déjà (`ConfigNormalizer`, version 2) ; aucun nouveau format.
- **Plateforme Linux.** Hors périmètre ; le découpage la rend possible (voir `docs/ARCHITECTURE.md`).

## Risques restants

- **Résolution par réflexion.** Les fenêtres construisent leur ViewModel avec `ActivatorUtilities` : un paramètre de
  constructeur non enregistré échoue à l'ouverture de la fenêtre, pas à la compilation. Les tests d'interface ouvrent
  chaque fenêtre, mais pas chaque combinaison de services.
- **Vérification manuelle à faire** sur ton écran tactile : fenêtre sans focus (raccourci vers le Bloc-notes), plein écran
  multi-écran, débranchement. L'application a été lancée et fermée normalement en simulation (journal « Démarrage » puis
  « Arrêt »), mais pas pilotée.
- **Format du journal.** `streamdak_001.log` remplace `streamdak.log.old`. Les anciens `.old` ne sont plus nettoyés.
- **Dossiers remontés.** Un fichier édité à la main avec plus de 8 niveaux de dossiers charge désormais tous ses boutons,
  dans le dernier dossier autorisé, avec un avertissement ; l'organisation d'origine n'est pas reconstituée.

## Comment vérifier

```powershell
dotnet build -c Release                       # 0 avertissement, 0 erreur, 3 projets
dotnet test tests/StreamDak.Tests             # 232 tests au vert
$env:STREAMDAK_DATA_DIR = "$env:TEMP\streamdak-essai"
dotnet run -c Release -- --simulate --edit    # deck et éditeur identiques à avant
Get-Content "$env:TEMP\streamdak-essai\streamdak.log"
```

Dans le journal : une ligne `Démarrage`, une ligne `Services prêts en N ms (…)`, puis `Arrêt` à la fermeture.
Points à observer : l'éditeur s'ouvre depuis le menu du deck, un titre modifié dans l'éditeur apparaît sur le deck, le
thème se change sans redémarrer, la configuration se recharge quand on modifie `config.json` à la main.
