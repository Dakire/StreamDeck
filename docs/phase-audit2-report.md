# Bilan de l'audit 2 : freezes, WebView2, fiabilité, sécurité

Branche `mission/fiabilisation-audit-2`, 2 novembre 2026. Point de départ : `d6ff0a4`. Constats : [AUDIT-2.md](AUDIT-2.md).
Compilation Release : 0 avertissement, 0 erreur. Tests : 1 359 au départ, tous au vert ; le nombre final et le résultat sont dans la dernière section.

## Ce qui est mesuré, et ce qui ne l'est pas

Aucune mesure de démarrage, de mémoire ou de CPU sur l'application réelle n'a été faite pendant ce travail : ces mesures demandent d'afficher le deck à l'écran de l'utilisateur et des comptes réels
(pages web, badges), ce que je n'ai pas fait. **Il n'y a donc pas de tableau avant / après chiffré** pour le démarrage, la RAM avec pages web, le CPU et le temps d'arrêt réel. Ce qui est vérifié :

| Quoi | Vérification | Résultat |
|---|---|---|
| Arrêt des intégrations | `ShutdownDisconnectsInParallelUnderOneGlobalDeadline` : 6 intégrations dont la déconnexion dure 20 s | moins de 5 s (en série : 6 × 2 s au moins, jusqu'à 44 s avec 11) |
| Charge | `LoadTests` : 1 000 changements de page, 20 ouvertures de paramètres, modifications de config | mémoire < +30 Mo, threads < +25, handles < +300, opération la plus lente < 250 ms |
| Relevé qui meurt en silence | `APollThatThrowsAnythingEndsInALostConnectionNotAFrozenOne` | l'intégration passe à « perdue » |
| Plugins | `FirstUseTrustsPluginsAlreadyThereThenRefusesNewOnesAndChangedOnes` | non listé ou modifié : refusé avant chargement |
| Journal | `RedactorMasksTheSecretParts` (13 cas) | cookies, clés, webhooks, guillemets, « mot de passe= » masqués |

Hypothèses de l'audit **confirmées par la lecture du code** : écriture de `config.json` sur l'UI, E/S et COM sur l'UI, arrêt en série, relevé qui meurt en silence, absence d'échec visible des touches d'intégration.
Hypothèse **infirmée** : le remplacement de `_fieldTimer` par `WebMessageReceived` créerait un pont page → application, il n'a pas été fait (ADR 0029). Hypothèse **corrigée** : l'environnement WebView2 est unique,
il n'y a pas un processus navigateur par cadre.

## Corrigé (références AUDIT-2.md)

| Lot | Contenu | Constats |
|---|---|---|
| Fluidité | Chien de garde 200 ms avec tâche et priorité, tâches longues, compteur de pauses ; écriture de config hors UI (ADR 0027) ; arrêt parallèle ; premier plan par événement ; audio COM, saisie, validation de chemins, images hors UI ; journal asynchrone | 1.1-1.14 |
| WebView2 | Refus de permission non mémorisé, pannes filtrées, cadre mort recréé, minuterie orpheline, badge rétabli après connexion, mémoire basse, ouverture échelonnée, relevé borné | 2.2-2.8, 2.10, 2.11 |
| Intégrations | Toute exception d'un relevé compte, gigue, attente réinitialisée après 30 s stables, commandes hors UI sous échéance, touche indisponible visible | 3.1-3.4 |
| Sécurité | Redirection de jeton par import fermée, `LogRedactor`, liste d'autorisation des plugins (ADR 0028), API locale, téléchargement borné, actions GitHub épinglées, Dependabot, DI 10.0.14 et WebView2 1.0.4314.35, SECURITY.md corrigé | 3.6, 3.8-3.14 |
| Interface | Poignées de panneaux repliables et mémorisées, taille des paramètres mémorisée, Diagnostic et copie sans secret, mode économie, textes qui reviennent à la ligne, curseur indisponible expliqué, cibles tactiles | 4.1-4.7 |

## Fonctionnalités ajoutées

Raccourcis clavier globaux (`GlobalHotkey`), en-têtes et méthode des requêtes web (`Headers`, `Method`), réordonnancement des profils. Chacune a des tests, une entrée de CHANGELOG et de la documentation.

## Écarté ou reporté, et pourquoi

| Point | Décision |
|---|---|
| Capture de pile du thread UI au moment d'un blocage | **Écarté** : .NET ne permet plus de suspendre un thread pour lire sa pile, et EventPipe demande un processus externe. Le nom de la tâche (`LongTask`) et la priorité de l'opération tiennent lieu |
| Mises à jour différentielles de la grille (`Rebuild`) | **Non fait** : non mesuré en conditions réelles. `BuildTiles` réconcilie déjà case par case (voir ARCHITECTURE.md), `Rebuild` est maintenant chronométré par `LongTask` pour décider sur des chiffres |
| Remplacer le relevé de champ par des messages web | **Refusé** (ADR 0029) |
| Suspendre les cadres rangés (`TrySuspendAsync`) | **Refusé** : leurs notifications doivent continuer d'arriver (ADR 0018) |
| Webhooks en clair dans `config.json` | **Reporté** : un champ secret change le format ; consigné dans SECURITY.md |
| Épingler la version d'Inno Setup | **Reporté** : version non vérifiée en ligne |
| `White` / `Black` en dur, cartes dans des cartes | **Reporté** : cosmétique, contraste en thème clair non vérifié |
| Boutons à états multiples, macros (répétitions, conditions, variables), palette de commandes, Spotify, Wave Link, Home Assistant, Voicemod, planification des profils, aperçu avant restauration, télécommande web locale, plugins à manifeste | **Non faits** dans cette passe. La télécommande web demande l'étude de risque et l'ADR prévus avant tout code |
| Mesures réelles (démarrage, RAM avec pages web, CPU deck masqué, 1 h simulée) | **Non faites** (voir plus haut) |

## À vérifier à la main sur votre écran tactile

1. **Raccourcis globaux** : poser `ctrl+alt+f1` sur un bouton, l'utiliser depuis le Bloc-notes ; essayer une combinaison déjà prise (message attendu) ; vérifier que `--simulate` ne réserve rien.
2. **Poignées de panneaux** (mode édition) : glisser au doigt, toucher pour replier, relancer pour vérifier les tailles mémorisées ; 100 / 125 / 150 / 200 % de DPI, portrait et 16:9.
3. **Programme au premier plan** : profil associé à une application, Alt+Tab rapide, retour au profil choisi.
4. **Badges** : se connecter à un service depuis le deck et vérifier que le badge apparaît sans redémarrer (au plus 2 minutes).
5. **Plugins** : si vous en avez, vérifier qu'ils se chargent encore au premier démarrage, puis qu'un plugin ajouté ensuite est refusé avec la ligne à ajouter dans `plugins\trusted.json`.
6. **Mode économie** : activer, constater animations coupées et relevés espacés, puis mesurer la RAM et le CPU dans Paramètres › Diagnostic.
7. **Arrêt** : fermer l'application avec OBS, Discord et Sonar connectés ; aucun processus `StreamDak` ne doit rester.
8. **Importer** une configuration dont `baseurl` pointe vers un hôte distant : le réglage doit être ignoré (message dans le journal).
