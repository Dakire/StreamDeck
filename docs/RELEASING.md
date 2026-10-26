# Publier une version

1. Mettre à jour `<Version>` dans `Directory.Build.props` et ajouter la section `## [x.y.z]` du `CHANGELOG.md` (elle devient le texte de la release).
2. Vérifier en local : `./build.ps1` (tests + installateurs + `checksums.txt`).
3. Commiter, puis poser le tag et le pousser :

   ```powershell
   git tag v1.4.0
   git push origin v1.4.0        # v1.5.0-beta.1 : publiée comme pré-version (canal bêta)
   ```

Le workflow `.github/workflows/release.yml` (sur un tag `vX.Y.Z` ou `vX.Y.Z-préversion`) compile, lance les tests, construit les deux installateurs avec Inno Setup,
calcule les SHA-256 (`checksums.txt`, relus et recontrôlés), puis crée la release avec ces fichiers :

| Fichier | Rôle |
|---|---|
| `StreamDak-<version>-self-contained-Setup.exe` | installateur par défaut, runtime .NET embarqué |
| `StreamDak-<version>-framework-dependent-Setup.exe` | installateur léger |
| `checksums.txt` | `<sha256>  <fichier>` ; **obligatoire** : la mise à jour automatique refuse une release sans lui |

Les noms de fichiers sont ceux que la mise à jour attend (`InstallFlavor.SetupName`) : ne pas les changer. L'application compare le tag à sa propre version (SemVer) ;
une pré-version (tag avec `-`) n'est proposée qu'au canal bêta.

**Signature de code** : sans certificat les installateurs ne sont pas signés (SmartScreen avertit). Pour signer en CI, importer le certificat dans le magasin du runner
et définir `SIGN_CERT_THUMBPRINT` ; remplacer la commande de construction par `./build.ps1 -Version … -Sign` (voir `docs/INSTALLATION.md`).
Une fois les releases signées, la mise à jour exige le même éditeur et refuse toute release qui perdrait sa signature.

**Dépôt des mises à jour** : `update.defaults.json` (livré avec l'application) désigne `Dakire/StreamDeck` ; un utilisateur ou un fork le change dans `%AppData%\StreamDak\update.json`
(`{ "owner": "…", "repo": "…" }`), sans toucher au code.
