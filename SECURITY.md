# Sécurité

Audit du 26 octobre 2026 (branche `main`, 662 tests au départ). Ce document décrit le modèle de menace, les résultats de l'audit,
ce qui a été corrigé et ce qui reste à surveiller. Pour signaler une faille : ouvrir une alerte de sécurité privée sur le dépôt GitHub
(Security › Report a vulnerability), pas une issue publique.

## Modèle de menace

StreamDak est une application de bureau à utilisateur unique. Elle s'exécute avec les droits de l'utilisateur et lui donne
volontairement le pouvoir de lancer des programmes et des scripts (c'est l'objet d'un deck). Les menaces prises en compte :

| Menace | Exemple | Réponse |
|---|---|---|
| Fichier importé piégé | profil `.streamdak` qui lance un script | validation bornée + confirmation « danger » listant les actions à risque |
| Injection dans une commande | chemin ou argument contenant `"`, `&`, `;` | aucune ligne de commande assemblée (`ScriptCommand`) |
| Fuite de secret | mot de passe dans `config.json`, le journal ou un export | DPAPI, masquage du journal, migration automatique |
| Page web hostile dans un cadre | lien vers un site tiers | WebView2 isolé, droits limités au site saisi (ADR 0017, 0018) |
| Autre processus ou utilisateur | écriture sur l'API locale | canal nommé `CurrentUserOnly` |

Hors périmètre : un autre processus qui tourne déjà avec les droits de l'utilisateur peut de toute façon lire ses fichiers et lancer
des programmes ; StreamDak ne prétend pas s'en protéger.

## Résultats de l'audit

### Secrets

- **Code et dépôt** : aucun mot de passe, jeton ou clé en clair. Recherche par motifs (clés GitHub, AWS, Google, Slack, OpenAI, clés privées,
  `client_secret`, affectations `password=`/`token=`) sur **tout l'historique git** (toutes branches) : une seule occurrence, la constante
  de test `pk_secret_de_test` (valeur factice d'un test ClickUp). Aucun fichier `.pfx`, `.key`, `.pem`, `.env` ni `secrets.*` n'a jamais été commité.
- **Binaires dans l'historique** : deux commits très anciens (`24d677f`, `2411db3`) contiennent un dossier `bin/Release/net9.0-windows/`
  (anciens binaires TouchDeck). Aucun secret, mais ils alourdissent le dépôt ; `bin/` est ignoré depuis. Les retirer demanderait de réécrire
  l'historique : non fait (décision à prendre par le propriétaire).
- **Stockage** : les champs de type « secret » des intégrations (mots de passe OBS/vMix, jetons ClickUp, Notion, Monday, Twitch, clé du pont Hue,
  secret client et jeton Google) vont dans `secrets.dat`, chiffré par **DPAPI portée utilisateur courant** avec une entropie propre à l'application
  (`DpapiSecretStore`). Le fichier copié sur un autre compte est illisible.
- **Migration automatique** (nouveau) : `IntegrationManager.MigratePlaintextSecrets`, appelée au démarrage, déplace dans le magasin chiffré tout
  secret retrouvé en clair dans `config.json` (ancienne version, édition à la main, fichier importé) et le supprime du fichier. Une valeur déjà
  chiffrée n'est jamais écrasée. Testé (`SecurityTests`).
- **Journal** (nouveau) : toutes les lignes de `streamdak.log` passent par `LogRedactor` (formateur Serilog `RedactingFormatter`) qui masque
  `user:motdepasse@hôte`, `?access_token=…`, `Authorization: Bearer …`, `password=…`, `"token": "…"`. C'est un filet : le code ne journalise déjà
  que des clés de secret, jamais des valeurs. Rotation : 1 Mo, 3 fichiers.
- **Registre** : seule la valeur `Run\StreamDak` (chemin de l'exécutable) est écrite, quand « Démarrer avec Windows » est activé. Aucun secret.
- **Profil WebView2** (`%APPDATA%\StreamDak\WebView2`) : cookies et sessions des sites ouverts dans les cadres, chiffrés par Chromium
  (DPAPI de l'utilisateur). StreamDak n'y lit rien. Supprimer ce dossier déconnecte tous les sites.

### Lancement de commandes et de scripts

| Point | Constat | Action |
|---|---|---|
| Action `script` (.ps1) | le chemin et les arguments étaient insérés dans une chaîne `-File "…" args` : un guillemet dans le chemin ou un `;` dans les arguments ajoutait des instructions | **corrigé** : `ScriptCommand` + `ProcessStartInfo.ArgumentList`, chaque argument transmis séparément |
| Action `script` (.bat/.cmd) | `cmd.exe /c ""chemin" args"` : métacaractères interprétés | **corrigé** : lancement direct, arguments séparés, et refus de `& \| < > ^ % "` (cmd.exe les interprète encore dans un .bat) |
| Action `script` (commande en ligne) | passée en un seul argument de `-Command` : c'est le code de l'utilisateur | conservé (c'est la fonction) ; les arguments supplémentaires sont refusés ; `-NonInteractive` ajouté |
| Politique d'exécution PowerShell | `-ExecutionPolicy Bypass` | **durci** : `RemoteSigned` (un script téléchargé et non signé est bloqué ; un script local s'exécute) |
| Action `launch` | `ShellExecute` sur la valeur saisie | inchangé : lancer un programme est la fonction ; déclaré risqué à l'import |
| Action `url` | `ShellExecute` | schémas `javascript`, `vbscript`, `data`, `file` refusés (déjà en place) |
| Action `http` | seulement `http`/`https`, avertissement en `http://` non local | déjà en place |
| Liste blanche | les types d'action connus sont une liste fermée (`ActionTypes`, `ActionRegistry` refuse les doublons) ; un type inconnu est refusé à l'import | déjà en place |
| Confirmation | l'import d'un profil ou d'une configuration qui contient des actions `launch` ou `script` affiche une confirmation en mode « danger » avec leur nombre | déjà en place |

**Décision** : pas de confirmation à chaque appui sur un bouton qui lance un script, ce qui rendrait le deck inutilisable ; la confiance
est donnée une fois, à l'import ou à la création. À reconsidérer si un jour des profils sont partagés par un catalogue en ligne.

### Import de profils et de configuration

`ProfilePackage` : archive zip, taille limitée (60 Mo), entrées limitées, JSON limité à 10 Mo, au plus 5000 boutons, 200 pages, 100 profils,
textes de 8192 caractères. Les entrées du zip ne sont jamais extraites par leur nom (pas de « zip slip ») ; les images sont acceptées
seulement avec un nom de hachage valide et une signature PNG. Types d'action inconnus refusés, `ConfigNormalizer` répare le reste.
**Désérialisation** : uniquement `System.Text.Json` sur des types concrets ; aucun `BinaryFormatter`, `TypeNameHandling`, `$type` ni
polymorphisme (recherche dans tout le code : aucune occurrence).

### Réseau

Aucune télémétrie, aucun suivi d'usage, aucun appel de mise à jour automatique (recherche de `telemetry`, `analytics`, `sentry`, `appcenter`,
`ApplicationInsights` : aucune occurrence). Les seuls appels sortants sont ceux que l'utilisateur configure :

- actions `http` du deck ;
- intégrations : OBS, vMix, Elgato, Hue (réseau local), Teams, ClickUp, Notion, Monday, Twitch, Google (OAuth « application de bureau » avec le
  projet Google Cloud de l'utilisateur) ;
- pages web affichées dans WebView2 (adresses saisies par l'utilisateur).

**Certificats** : seul le pont Hue accepte un certificat non public (autorité propre à Hue), **uniquement pour l'adresse du pont configurée**.
Aucun autre `ServerCertificateCustomValidationCallback`.

### Code chargé ou exécuté dynamiquement

| Point | Constat |
|---|---|
| `Assembly.Load*` | seulement dans `PluginLoader` : DLL posées par l'utilisateur dans `%APPDATA%\StreamDak\plugins\<Nom>\<Nom>.dll`, un contexte de chargement par plugin, SHA-256 consigné au journal. C'est du code de confiance : ne copier que des plugins de sources connues |
| `Process.Start` | 5 sites : `WindowsPlatform.Open`/`RunScript` (actions), `GoogleIntegration` (ouvre l'URL d'autorisation dans le navigateur), `SettingsViewModel` (ouvre le dossier ou le fichier de configuration). Aucun ne reçoit de texte venant du réseau |
| Téléchargement puis exécution | aucun |
| API locale | canal nommé `PipeOptions.CurrentUserOnly` (aucun port TCP), JSON par ligne, commandes limitées (voir `docs/local-api.md`) |
| WebView2 | profil isolé, aucun objet hôte, aucun message page → application ; micro, caméra et notifications seulement pour les pages https du même site que l'adresse saisie (ADR 0018) |

### Dépendances

`dotnet list package --vulnerable --include-transitive` sur l'application et sur les tests : **aucun paquet vulnérable** (sources nuget.org).

| Paquet | Version | Rôle | Remarque |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM | Microsoft |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | DI | Microsoft ; correctifs 10.0.x disponibles, sans CVE |
| Serilog.Extensions.Logging, Serilog.Sinks.File | 10.0.0, 7.0.0 | journal | éditeur officiel Serilog |
| Microsoft.Web.WebView2 | 1.0.3967.48 | cadres web | Microsoft ; le moteur (runtime) est mis à jour par Windows |
| WPF-UI | 4.3.0 | thème Fluent | lepoco, licence MIT |
| System.Diagnostics.PerformanceCounter, System.Security.Cryptography.ProtectedData | 10.0.0 | relevés système, DPAPI | Microsoft |

Aucun paquet suspect (pas de nom approchant d'un paquet connu, pas de paquet inutilisé). Tests : xunit, Microsoft.NET.Test.Sdk.

### Analyse statique

`Directory.Build.props` : `Nullable` activé, `AnalysisLevel=latest-recommended`, **`AnalysisModeSecurity=All`** (toutes les règles de sécurité
CA2xxx, CA3xxx, CA5xxx en erreur, `TreatWarningsAsErrors`), style appliqué à la compilation. Résultat : 0 avertissement.

## Reste à traiter

1. **Signature de code** : l'exécutable et l'installateur ne sont pas signés (SmartScreen avertira). Le script de signature est prêt, il faut un certificat.
2. **Historique git** : retirer les binaires `bin/Release/net9.0-windows/` des deux anciens commits demande une réécriture d'historique.
3. **Plugins** : pas de liste d'autorisation par empreinte ; l'empreinte SHA-256 n'est que consignée.
4. **Mises à jour de paquets** : Microsoft.Extensions.DependencyInjection 10.0.14 et WebView2 1.0.4314.35 existent ; à passer après essai sur machine tactile.
5. **Non vérifié ici** : le comportement de DPAPI sur un profil itinérant ou après changement de mot de passe Windows (le secret devient illisible
   et est ignoré avec un avertissement, sans valeur journalisée : il faut le ressaisir).
