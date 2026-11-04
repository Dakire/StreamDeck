# Sécurité

Audit du 26 octobre 2026, complété le 4 novembre 2026 (voir « Audit 3 »). Les chiffres de tests des audits sont datés : la suite compte aujourd'hui plus de 1 400 tests. Ce document décrit le modèle de menace, les résultats de l'audit,
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
- **Stockage** : les champs de type « secret » des intégrations (mots de passe OBS, jetons Notion, Twitch, clé du pont Hue,
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

**Confirmation d'import** : elle compte comme « à risque » les actions `launch`, `script`, `text` (injection de frappes), `hotkey` avec la touche Windows (Win+R) et `url` hors http, https et mailto
(un nom de programme ou un protocole passe par `ShellExecute`). Un profil piégé ne peut donc plus exécuter du code sans alerte (`ActionValidator.IsRisky`).
**Intégrations à secret** : un import de configuration qui change l'adresse de Hue ou d'OBS désactive l'intégration et la remet à l'adresse actuelle (la clé Hue ou le défi OBS ne partent pas vers un hôte choisi par le fichier), et la confirmation le signale (ADR 0030).
**Images** : les dimensions sont lues sans décoder et refusées au-delà de `MaxPixels` avant toute allocation (`IconStore.DecodeLargest`).

### Réseau

Aucune télémétrie, aucun suivi d'usage (recherche de `telemetry`, `analytics`, `sentry`, `appcenter`, `ApplicationInsights` : aucune occurrence). Une **vérification de mise à jour**
part seule 20 s après le démarrage, une fois par jour au plus, activée par défaut et désactivable (Paramètres › Mises à jour). Les seuls appels sortants sont ceux que l'utilisateur configure :

- actions `http` du deck ;
- **mises à jour** : `api.github.com` (releases du dépôt configuré, sans jeton), puis `github.com` et son CDN `*.githubusercontent.com` pour l'installateur (redirections contrôlées une à une) ;
  désactivable dans Paramètres › Mises à jour ;
- widgets de données : hôtes saisis (ping, états), adresse saisie (compteur) et `geocoding-api.open-meteo.com` / `api.open-meteo.com` (météo : le nom de la ville seulement, HTTPS) ;
- **badges de notifications** : pages WebView2 cachées vers les services dont un bouton porte l'adresse (jamais sans le connecteur activé) ;
- intégrations : OBS, Elgato, Hue (réseau local), Teams, Notion, Twitch, Discord (RPC local), Sonar (local), Google (OAuth « application de bureau » avec le
  projet Google Cloud de l'utilisateur) ; les adresses d'API qu'un réglage caché peut remplacer (`baseurl`, `apiurl`) ne sont acceptées que **vers la machine locale** : une configuration importée ne peut pas rediriger un jeton ;
- icône d'un site (favicon) pour un bouton « lien web » ;
- pages web affichées dans WebView2 (adresses saisies par l'utilisateur).

**Certificats** : seul le pont Hue accepte un certificat non public (autorité propre à Hue), **uniquement pour l'adresse du pont configurée**.
Aucun autre `ServerCertificateCustomValidationCallback`.

### Code chargé ou exécuté dynamiquement

| Point | Constat |
|---|---|
| `Assembly.Load*` | seulement dans `PluginLoader` : DLL posées par l'utilisateur dans `%APPDATA%\StreamDak\plugins\<Nom>\<Nom>.dll`, un contexte de chargement par plugin. **Liste d'autorisation** `plugins\trusted.json` : l'empreinte SHA-256 des `*.dll` et du `.deps.json` est vérifiée **avant** le chargement, un plugin non listé ou modifié n'est pas chargé (ADR 0028) ; les octets hachés sont ceux qui sont chargés (`LoadFromStream`, ADR 0030). C'est du code de confiance : ne copier que des plugins de sources connues |
| `Process.Start` | 8 sites : `WindowsPlatform.Open`/`RunScript` (actions), `GoogleIntegration` (ouvre l'URL d'autorisation dans le navigateur), `SettingsViewModel` (dossier, fichier de configuration, journal), mise à jour (script fixe). Aucun ne reçoit de texte venant du réseau |
| Téléchargement puis exécution | **l'installateur d'une mise à jour**, jamais sans clic de l'utilisateur, après vérification SHA-256 et signature (voir « Mises à jour ») ; aucun autre |
| API locale | canal nommé `PipeOptions.CurrentUserOnly` (aucun port TCP), JSON par ligne, commandes limitées (voir `docs/local-api.md`) |
| WebView2 | profil isolé, aucun objet hôte, **cadres visibles : aucun message page → application** (ADR 0029) ; micro, caméra et notifications seulement pour l'hôte saisi (ou un sous-domaine), en https (ADR 0018 et 0030) ; une page venue d'un import n'obtient rien sans que WebView2 interroge l'utilisateur. Les sessions cachées de badges n'acceptent qu'un message validé (type, hôte, entier borné), voir ci-dessous |

### Mises à jour (ADR 0021)

Seules les adresses de fichiers de la release du dépôt configuré sont suivies (HTTPS, hôte `github.com`, chemin `/{owner}/{repo}/releases/download/{tag}/{fichier}`) ; les redirections
ne mènent qu'à `github.com` ou `*.githubusercontent.com`. `checksums.txt` est obligatoire, la somme SHA-256 est recalculée sur le fichier entier (un fichier différent est supprimé),
la signature Authenticode est vérifiée par Windows (`WinVerifyTrust`) : signature invalide = refus, application signée = même éditeur exigé. Aucun jeton GitHub stocké. L'installation ne part
que d'un clic de l'utilisateur ; elle passe par un script PowerShell fixe (jamais téléchargé) avec sauvegarde et restauration. **Non signé aujourd'hui** : l'intégrité repose alors sur
`checksums.txt`, publié par la même release que l'installateur : il protège d'une corruption ou d'un miroir, **pas d'un compte GitHub compromis** (seule la signature de code le ferait).

### Pages web cachées (badges)

Mêmes garde-fous que les cadres visibles (ADR 0017, 0018) : profil WebView2 de StreamDak, aucun objet hôte, permissions refusées **sans être mémorisées dans le profil** (elles ne bloquent pas le cadre visible du même site), téléchargements et nouvelles fenêtres bloqués, son coupé, mémoire basse.
Seule différence : quand un observateur est installé (mode `selector`, Google Chat), `IsWebMessageEnabled` est actif sur cette seule session ; le message est refusé s'il ne vient pas d'un document de l'hôte du service ou s'il n'a pas la forme `{"type":"streamdak.badge","count":N}` (N entier de 0 à 999 999).
Le script de lecture n'existe que pour les services à sélecteur CSS : lecture seule, sélecteur passé comme chaîne JSON. Une expression régulière saisie est interrompue après 100 ms.

### Chaîne de publication

`.github/workflows/*.yml` utilisent `actions/checkout` et `actions/setup-dotnet`, **épinglées par empreinte de commit** (le commentaire donne la version), et Inno Setup (Chocolatey, version non épinglée : voir « Reste à traiter »).
Dependabot propose les mises à jour des actions et des paquets NuGet (`.github/dependabot.yml`). Le nom du tag de release passe par une variable d'environnement, jamais interpolé dans le script.

### Dépendances

`dotnet list package --vulnerable --include-transitive` sur l'application et sur les tests : **aucun paquet vulnérable** (sources nuget.org).

| Paquet | Version | Rôle | Remarque |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM | Microsoft |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | DI | Microsoft |
| Serilog.Extensions.Logging, Serilog.Sinks.File, Serilog.Sinks.Async | 10.0.0, 7.0.0, 2.1.0 | journal (écriture asynchrone) | éditeur officiel Serilog |
| Microsoft.Web.WebView2 | 1.0.4314.35 | cadres web | Microsoft ; le moteur (runtime) est mis à jour par Windows |
| WPF-UI | 4.3.0 | thème Fluent | lepoco, licence MIT |
| System.Diagnostics.PerformanceCounter, System.Security.Cryptography.ProtectedData | 10.0.0 | relevés système, DPAPI | Microsoft |

Aucun paquet suspect (pas de nom approchant d'un paquet connu, pas de paquet inutilisé). Tests : xunit, Microsoft.NET.Test.Sdk.

### Analyse statique

`Directory.Build.props` : `Nullable` activé, `AnalysisLevel=latest-recommended`, **`AnalysisModeSecurity=All`** (toutes les règles de sécurité
CA2xxx, CA3xxx, CA5xxx en erreur, `TreatWarningsAsErrors`), style appliqué à la compilation. Résultat : 0 avertissement.

### Audit 3 (4 novembre 2026)

Corrigé : confirmation d'import étendue, hôtes Hue et OBS à l'import, droits des pages web par hôte exact, images décodées après contrôle de taille, redirections HTTP non suivies
(les en-têtes personnalisés et le corps ne partent plus vers un autre hôte), empreinte des plugins unique et couvrant `deps.json`. Détail : ADR 0030.

Corrigé ensuite (même jour) :
- **Installateur de mise à jour** : l'applicateur recontrôle son SHA-256 juste avant de le lancer et refuse un fichier remplacé depuis la vérification.
- **`.deps.json` des plugins** : refusé s'il n'a pas la forme produite par le SDK (`runtimeTarget` et `targets`) ou s'il diffère des octets vérifiés ; le résolveur natif, qui plante sur un fichier incomplet, ne le voit jamais.
- **API locale** : le document est lu sur le thread d'interface (`IApiShell.Read`), et la boucle d'acceptation survit à toute erreur (nouvel essai, journal).
- **Inno Setup** épinglé à la version 6.7.1 dans `release.yml`.

## Reste à traiter

1. **Signature de code** : l'exécutable et l'installateur ne sont pas signés (SmartScreen avertira). Le script de signature est prêt, il faut un certificat.
2. **Historique git** : retirer les binaires `bin/Release/net9.0-windows/` des deux anciens commits demande une réécriture d'historique.
3. **Plugins** : la liste d'autorisation protège d'un ajout à l'insu de l'utilisateur, pas d'un attaquant qui écrit déjà dans `%APPDATA%\StreamDak` (hors modèle de menace).
4. **URL de webhook** (Discord, Slack…) en clair dans `config.json` et les exports (action `http`) : à traiter avec un champ secret, ce qui change le format.
5. **Journal** : `LogRedactor` est un filet par motifs ; il ne couvre ni les toasts ni les réponses de l'API locale. Le diagnostic copié le traverse.
6. **Non vérifié ici** : le comportement de DPAPI sur un profil itinérant ou après changement de mot de passe Windows (le secret devient illisible
   et est ignoré avec un avertissement, sans valeur journalisée : il faut le ressaisir).
7. **Pages Google** : l'hôte exact est exigé ; `calendar.google.com` n'accorde plus ses droits à `meet.google.com` (saisir `google.com` pour couvrir les deux). Les hôtes importés restent marqués sans bouton de confirmation.
