# 0030 — Durcissement : droits des pages web importées, import d'intégrations, images, redirections HTTP, empreintes des plugins

- **Statut** : acceptée.
- **Date** : 4 novembre 2026.
- **Précise** : ADR 0018 (droits des cadres web), ADR 0028 (liste d'autorisation des plugins).

## Contexte

Un audit de sécurité a relevé cinq points où un fichier importé ou une réponse réseau pouvait obtenir plus que ce que l'utilisateur a décidé.

## Décisions

### 1. Caméra, micro et notifications des cadres web

- La règle « même site » (deux dernières étiquettes, liste de suffixes « co.uk ») est **supprimée** : elle accordait les droits à tous les sous-domaines d'un domaine d'hébergement partagé
  (`attacker.github.io` pour `victim.github.io`) ou à un domaine frère. Le demandeur doit être **l'hôte saisi, ou un sous-domaine de celui-ci** (`WebPermissions.IsHostOrSubdomain`),
  toujours en https (ou sur la machine locale). Conséquence assumée : une page saisie en `calendar.google.com` ne donne plus ses droits à `meet.google.com` ; saisir `google.com` les donne aux deux.
- Les hôtes des pages et widgets web **venus d'un import** (profil ou configuration) et absents de la configuration actuelle sont inscrits dans `imported-web-hosts.json`, à côté de `config.json`
  (`ImportedWebHosts`). Pour ces hôtes, le cadre ne répond plus « accorder » : il laisse WebView2 **interroger l'utilisateur** (`PermissionState.Default`, rien n'est mémorisé).
- Le drapeau est un fichier à part et non une clé de `config.json` : le format de la configuration ne change pas, et l'import d'une configuration ne peut pas l'effacer. Il survit au redémarrage.
  Il n'existe pas de bouton « confirmer » : tant que l'hôte est marqué, l'utilisateur répond à l'invite de WebView2 ; supprimer le fichier (ou l'entrée) rétablit le comportement de l'ADR 0018.

### 2. Import de configuration et intégrations à secret

Les secrets (clé Hue, mot de passe OBS) sont dans le magasin DPAPI, indexés par intégration, pas dans le fichier : une configuration importée qui change `host`, `port` ou `scheme` les enverrait
à l'hôte de son auteur. `ImportedIntegrations.Neutralize` compare l'adresse effective (valeur par défaut comprise) de Hue et d'OBS à la configuration actuelle ; en cas de différence, l'intégration
importée est **désactivée et ramenée à l'adresse actuelle**, et la confirmation d'import le dit. Hue n'utilise plus que `https`, sauf `http` explicite vers la machine locale (essais).

### 3. Images

`IconStore.DecodeLargest` lit les dimensions **sans décoder** (`DelayCreation`, `CacheOption.None`) et refuse au-delà de `MaxPixels` avant d'allouer le moindre pixel ; `OutOfMemoryException` devient
un `InvalidDataException`. Importé par `Import`, `StoreBytes` (paquets) et `SiteIcon`.

### 4. HttpClient des actions

`ActionHttp.CreateClient` : `AllowAutoRedirect = false`. Un webhook n'est jamais renvoyé (corps, en-têtes d'autorisation) vers une adresse choisie par le serveur ; une réponse 3xx donne un message explicite.

### 5. Empreintes des plugins

- L'empreinte couvre aussi les fichiers `*.deps.json`.
- Chaque fichier est lu **une seule fois** (`PluginSnapshot`) : les octets hachés sont ceux qui sont chargés (`LoadFromStream`, y compris les dépendances résolues). L'empreinte journalisée est celle qui a été vérifiée.
- **Compatibilité** : une entrée de `trusted.json` calculée à l'ancienne (bibliothèques seules) est acceptée **une fois**, puis remplacée par la nouvelle empreinte (`PluginTrust.IsTrusted`).
  Le contenu du `.deps.json` à ce moment-là est donc entériné sans avoir été vérifié avant.

## Limites

- Les assemblies chargés depuis un flux ont un `Assembly.Location` vide : un plugin qui s'en sert doit être adapté.
- Le résolveur de dépendances du runtime lit `*.deps.json` sur le disque à la création du contexte : cette lecture n'est pas celle de l'empreinte (le fichier ne contient pas de code).
- Un `.deps.json` mal formé peut faire échouer le résolveur natif : seuls des plugins déjà autorisés y arrivent.
