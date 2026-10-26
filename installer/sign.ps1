<#
.SYNOPSIS
    Signe des fichiers avec signtool (Authenticode, SHA-256, horodatage). Appelé par build.ps1 -Sign et par Inno Setup (SignTool).

.DESCRIPTION
    Le certificat n'est jamais dans le dépôt. Deux façons de le fournir, par variables d'environnement :
      SIGN_CERT_THUMBPRINT   empreinte d'un certificat de code installé dans le magasin de l'utilisateur ou de l'ordinateur (recommandé : clé non exportable, jeton USB, HSM, Azure Key Vault via un fournisseur de clé)
      SIGN_PFX_PATH + SIGN_PFX_PASSWORD   fichier .pfx et son mot de passe (CI : importer le secret dans un fichier temporaire, jamais dans le dépôt ; le mot de passe est visible dans la ligne de commande de signtool le temps de l'appel)
    Autres : SIGN_TIMESTAMP_URL (défaut http://timestamp.digicert.com), SIGNTOOL_PATH (défaut : le plus récent du SDK Windows, ou le PATH).

    Sans certificat, le script s'arrête avec une erreur : build.ps1 -Sign ne produit jamais un installateur « presque signé ».

.EXAMPLE
    $env:SIGN_CERT_THUMBPRINT = 'ABCDEF…'; ./build.ps1 -Sign
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]]$Files,
    [string]$Thumbprint = $env:SIGN_CERT_THUMBPRINT,
    [string]$PfxPath = $env:SIGN_PFX_PATH,
    [string]$PfxPassword = $env:SIGN_PFX_PASSWORD,
    [string]$TimestampUrl = $(if ($env:SIGN_TIMESTAMP_URL) { $env:SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' })
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    if ($env:SIGNTOOL_PATH -and (Test-Path $env:SIGNTOOL_PATH)) { return $env:SIGNTOOL_PATH }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $found = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'signtool.exe introuvable : installe le SDK Windows 10/11 (composant « Outils de signature ») ou définis SIGNTOOL_PATH.'
}

if (-not $Thumbprint -and -not $PfxPath) {
    throw 'Aucun certificat : définis SIGN_CERT_THUMBPRINT, ou SIGN_PFX_PATH et SIGN_PFX_PASSWORD (voir installer/sign.ps1).'
}

$signtool = Find-SignTool
foreach ($file in $Files) {
    if (-not (Test-Path $file)) { throw "Fichier à signer introuvable : $file" }
    $arguments = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/d', 'StreamDak')
    if ($Thumbprint) { $arguments += @('/sha1', $Thumbprint) }
    else { $arguments += @('/f', $PfxPath); if ($PfxPassword) { $arguments += @('/p', $PfxPassword) } }
    $arguments += $file
    & $signtool @arguments
    if ($LASTEXITCODE -ne 0) { throw "signtool a échoué pour $file (code $LASTEXITCODE)" }
    & $signtool verify /pa /q $file
    if ($LASTEXITCODE -ne 0) { throw "La signature de $file ne se vérifie pas (code $LASTEXITCODE)" }
    Write-Host "Signé : $file"
}
