# Installation et publication

## Pour l'utilisateur

Télécharge le dernier installateur dans les [Releases](https://github.com/Dakire/StreamDeck/releases) :

| Fichier | Taille | Quand le choisir |
|---|---|---|
| `StreamDak-<version>-self-contained-Setup.exe` | ≈ 65 Mo | **Par défaut** : embarque le runtime .NET, rien d'autre à installer |
| `StreamDak-<version>-framework-dependent-Setup.exe` | ≈ 11 Mo | Le runtime « .NET Desktop 10 » (x64) est déjà là (parc géré, mises à jour du runtime par l'IT) |

L'installateur propose le dossier d'installation (par défaut `%LocalAppData%\Programs\StreamDak`, pour tous les utilisateurs si tu choisis
*Pour tous les utilisateurs* au lancement), un raccourci sur le Bureau, le raccourci du menu Démarrer (toujours), et *Lancer StreamDak à l'ouverture de session
Windows* (c'est la valeur `Run` de ton compte : la même que Paramètres › Démarrer avec Windows). Si le composant **WebView2** manque (Windows 10 sans Edge récent),
il est installé avec le programme d'installation officiel de Microsoft embarqué (signature Authenticode vérifiée à la construction).

**Tes données ne sont jamais dans le dossier d'installation** : profils, réglages, secrets chiffrés, images, profil WebView2 et journal sont dans
`%AppData%\StreamDak`. Mettre à jour (relancer un installateur plus récent, ou la mise à jour intégrée) remplace les fichiers sur place, au même endroit,
sans toucher à `%AppData%`. L'application en cours est fermée proprement avant la copie.

**Désinstaller** (Paramètres Windows › Applications) retire le dossier d'installation, les raccourcis et le lancement automatique. Il demande ensuite, *Non par défaut*,
s'il faut aussi supprimer `%AppData%\StreamDak` ; en mode silencieux (`/VERYSILENT`) il ne le supprime jamais.

Options en ligne de commande (déploiement par script) : `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="C:\Outils\StreamDak" /TASKS="autostart"`
(`/TASKS=` vide : aucun raccourci ni lancement automatique ; `/LOG="fichier"` pour le détail).

## Pourquoi Inno Setup (et pas WiX)

| Critère | Inno Setup 6 | WiX v5 (MSI) |
|---|---|---|
| Un seul fichier `Setup.exe` à publier et à télécharger | oui | un `.msi` (+ un bootstrapper Burn pour les prérequis) |
| Choix du dossier, tâches facultatives (Bureau, démarrage), page en français | assistant intégré, quelques lignes | à écrire (UI WiX, `WixToolset.UI`) |
| Installation **sans droits administrateur**, ou pour tous si on le choisit | `PrivilegesRequiredOverridesAllowed` | MSI per-user possible, plus contraint |
| Mise à jour silencieuse en place par l'application (`/SILENT`), fermeture de l'application en cours | `CloseApplications`, `/DIR` repris | majeure upgrade à gérer, application ouverte = redémarrage |
| Prérequis (WebView2, .NET) avec vérification | section `[Code]` en quelques lignes | chaîne Burn, plus lourde |
| Déploiement d'entreprise (GPO, Intune) | `Setup.exe /VERYSILENT` suffit | **MSI plus naturel** pour GPO / SCCM |

Décision : **Inno Setup**, plus simple et suffisant pour un deck personnel ou un poste de technicien. Si un déploiement par GPO devient nécessaire, un MSI WiX
pourra être ajouté à côté : `build.ps1` publie déjà le même dossier pour les deux.

## Construire

Prérequis : SDK .NET 10 (`global.json`), [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
./build.ps1                                   # tests, puis les deux installateurs dans artifacts/installer
./build.ps1 -Flavor self-contained -SkipTests # une variante, sans relancer les tests
./build.ps1 -Version 1.4.0                    # version imposée (sinon Directory.Build.props)
./build.ps1 -Sign                             # signature de code
```

`artifacts/installer` contient les installateurs et `checksums.txt` (`<sha256>  <fichier>`). Un build est **reproductible** : SDK épinglé (`global.json`),
`Deterministic`, paquets restaurés à versions exactes, publication ReadyToRun win-x64. Le programme d'installation WebView2 est téléchargé depuis Microsoft et refusé si sa
signature n'est pas celle de Microsoft ; `-NoWebView2Redist` construit hors ligne (l'installateur prévient alors si le composant manque).

### Signature de code

`installer/sign.ps1` signe (Authenticode SHA-256, horodatage) l'exécutable, les bibliothèques `StreamDak*.dll`, l'installateur et le désinstallateur. Le certificat n'est **jamais** dans le dépôt :

- `SIGN_CERT_THUMBPRINT` : empreinte d'un certificat de code installé dans le magasin Windows (recommandé : jeton matériel / clé non exportable) ;
- ou `SIGN_PFX_PATH` + `SIGN_PFX_PASSWORD` (fichier `.pfx` ; en CI, un secret écrit dans un fichier temporaire) ;
- `SIGN_TIMESTAMP_URL` (défaut DigiCert), `SIGNTOOL_PATH` (sinon le SDK Windows).

Sans certificat, `./build.ps1 -Sign` s'arrête avec une erreur : il ne produit jamais d'installateur à moitié signé. **Non signé aujourd'hui** : SmartScreen avertit au premier lancement
(*Informations complémentaires › Exécuter quand même*) ; l'intégrité reste vérifiable par `checksums.txt`.

## Publier une version

Voir `docs/RELEASING.md` : `git tag v1.4.0 && git push origin v1.4.0` déclenche le workflow qui construit, calcule les SHA-256 et publie la release.
