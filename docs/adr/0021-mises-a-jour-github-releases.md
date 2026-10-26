# 0021 — Mises à jour par les releases GitHub

- **Statut** : acceptée.
- **Date** : 26 octobre 2026.

## Contexte

L'application s'installe par `Setup.exe` (ADR 0020 et `docs/INSTALLATION.md`). Il faut la mettre à jour sans que l'utilisateur retrouve le dépôt, sans jamais
exécuter un fichier non vérifié, et sans toucher à ses profils.

## Décision

**Source** : `GET /repos/{owner}/{repo}/releases/latest` (stable) ou `…/releases?per_page=20` (canal bêta, pré-versions comprises, brouillons jamais). Dépôt dans
`update.defaults.json` (livré) ou `update.json` (utilisateur) : `config.json` ne change pas. Tag et version de l'assembly sont comparés en **SemVer** (`SemVer`).
Requêtes HTTPS, `User-Agent`, `If-None-Match` (un 304 rejoue la réponse gardée et ne compte pas dans la limite), `Retry-After` / `X-RateLimit-Reset` respectés, une
vérification automatique par jour au plus, 20 s après le démarrage, en arrière-plan, désactivable. **Aucun jeton** : un dépôt public se lit sans compte.

**Intégrité (obligatoire)** : la release doit porter `checksums.txt` et l'installateur de la variante installée (`flavor.txt`). Seules les adresses
`https://github.com/{owner}/{repo}/releases/download/{tag}/{fichier}` du dépôt configuré sont suivies ; les redirections (CDN de GitHub) ne le sont que vers
`github.com` et `*.githubusercontent.com`, une à une. Téléchargement asynchrone, annulable, avec progression, **repris** par `Range` après une coupure (4 tentatives),
SHA-256 recalculé sur le fichier entier : en cas de différence il est supprimé et l'installation refusée. Signature Authenticode (`WinVerifyTrust`) : invalide = refus ;
l'application signée n'accepte qu'une release signée par le même éditeur. Non signé des deux côtés (cas actuel) : intégrité par `checksums.txt` seulement.

**Installation, jamais sans accord** : la vérification automatique n'allume qu'un point sur le bouton de menu et une entrée de menu. Paramètres › Mises à jour montre
la version, les notes (Markdown simple, sans HTML ni lien suivi) et *Installer maintenant* / *Plus tard* / *Ignorer cette version*. Une copie non posée par l'installateur
(développement) n'a que *Voir la release*. L'installation passe par un script PowerShell **fixe** (`UpdateApplier.Script`, paramètres passés un à un) : il attend la fermeture de
l'application, **sauvegarde** le dossier d'installation, lance l'installateur en silence dans le même dossier (`%AppData%` n'est jamais touché), relance l'application ; si
l'installateur échoue, il **restaure** la sauvegarde, relance l'ancienne version et laisse un marqueur que l'application affiche au démarrage suivant.

## Conséquences

- Une release sans `checksums.txt` ou sans le nom de fichier attendu n'est jamais installée (`docs/RELEASING.md`).
- Une installation « pour tous les utilisateurs » (Program Files) déclenche l'invite UAC de l'installateur, et la restauration d'un échec y exige les mêmes droits.
- Non vérifié ici : une vraie release publiée sur GitHub (le dépôt n'en a pas encore) ; tout le reste, y compris l'applicateur PowerShell réel, la signature Windows et les
  coupures réseau, est testé avec de faux serveurs et de vrais fichiers.
