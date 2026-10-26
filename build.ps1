<#
.SYNOPSIS
    Construit les installateurs de StreamDak (Inno Setup) : publication win-x64 en deux variantes, installateurs, sommes SHA-256.

.DESCRIPTION
    Étapes : tests (facultatifs) › dotnet publish (self-contained et/ou framework-dependent, win-x64) › signature des programmes (facultative)
    › programme d'installation WebView2 de Microsoft (téléchargé, signature Authenticode vérifiée) › Inno Setup › signature des installateurs
    (facultative) › artifacts/installer/checksums.txt.

    Résultat, dans artifacts/installer :
      StreamDak-<version>-self-contained-Setup.exe        embarque le runtime .NET (≈ 150 Mo installé), rien d'autre à installer
      StreamDak-<version>-framework-dependent-Setup.exe   léger, demande le runtime « .NET Desktop 10 » (x64)
      checksums.txt                                       « <sha256>  <fichier> », vérifié par la mise à jour automatique

    Prérequis : SDK .NET 10 (global.json) et Inno Setup 6 (https://jrsoftware.org/isinfo.php ; winget install JRSoftware.InnoSetup).

.PARAMETER Flavor
    all (défaut), self-contained ou framework-dependent.

.PARAMETER Version
    Version SemVer. Par défaut celle de Directory.Build.props. Un tag « v1.4.0 » donne 1.4.0.

.PARAMETER Sign
    Signe StreamDak.exe, StreamDak*.dll et les installateurs avec installer/sign.ps1 (certificat par variables d'environnement, voir ce script).

.PARAMETER SkipTests
    Ne lance pas la suite de tests (déjà passée, ou CI qui les lance à part).

.PARAMETER Iscc
    Chemin de ISCC.exe si Inno Setup n'est pas à un emplacement habituel.

.PARAMETER NoWebView2Redist
    N'embarque pas le programme d'installation de WebView2 (build hors ligne) : l'installateur prévient si le composant manque.

.EXAMPLE
    ./build.ps1                        # tests, deux variantes
    ./build.ps1 -Flavor self-contained -SkipTests -Version 1.4.0
    ./build.ps1 -Sign                  # avec signature de code
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'self-contained', 'framework-dependent')] [string]$Flavor = 'all',
    [string]$Version,
    [switch]$Sign,
    [switch]$SkipTests,
    [string]$Iscc,
    [switch]$NoWebView2Redist,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publishRoot = Join-Path $artifacts 'publish'
$installerDir = Join-Path $artifacts 'installer'
$redistDir = Join-Path $artifacts 'redist'
$webView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'   # programme d'installation « Evergreen » officiel de Microsoft

function Write-Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Invoke-Native([string]$file, [string[]]$arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file a échoué (code $LASTEXITCODE)" }
}

function Get-ProjectVersion {
    $props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
    $node = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $node) { throw 'Version introuvable dans Directory.Build.props' }
    return $node.Trim()
}

function Find-Iscc {
    if ($Iscc) { if (Test-Path $Iscc) { return (Resolve-Path $Iscc).Path } else { throw "ISCC.exe introuvable : $Iscc" } }
    $candidates = @(
        $env:INNO_SETUP_ISCC,
        ($onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    if (-not $candidates) {
        throw "Inno Setup 6 est introuvable. Installe-le (winget install JRSoftware.InnoSetup, ou choco install innosetup) ou passe -Iscc <chemin>."
    }
    return @($candidates)[0]
}

function Get-WebView2Redist {
    New-Item -ItemType Directory -Force $redistDir | Out-Null
    $file = Join-Path $redistDir 'MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path $file)) {
        Write-Host "Téléchargement du programme d'installation WebView2 (Microsoft)…"
        Invoke-WebRequest -Uri $webView2Url -OutFile $file -UseBasicParsing
    }
    $signature = Get-AuthenticodeSignature $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        Remove-Item $file -Force
        throw "Le programme d'installation WebView2 n'a pas une signature Microsoft valide (état : $($signature.Status)) : il a été supprimé et n'est pas embarqué."
    }
    Write-Host "Signature vérifiée : $($signature.SignerCertificate.Subject)"
    return $redistDir
}

function Invoke-Sign([string[]]$files) {
    if (-not $Sign) { return }
    Invoke-Native 'powershell.exe' (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'installer\sign.ps1'), '-Files') + $files)
}

Push-Location $root
try {
    if (-not $Version) { $Version = Get-ProjectVersion }
    $Version = $Version.TrimStart('v')
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Version SemVer attendue (1.4.0, 1.5.0-beta.1) : « $Version »" }
    $flavors = if ($Flavor -eq 'all') { @('self-contained', 'framework-dependent') } else { @($Flavor) }
    Write-Host "StreamDak $Version — variantes : $($flavors -join ', ')"

    Write-Step 'Nettoyage'
    if (Test-Path $publishRoot) { Remove-Item $publishRoot -Recurse -Force }
    if (Test-Path $installerDir) { Remove-Item $installerDir -Recurse -Force }
    New-Item -ItemType Directory -Force $publishRoot, $installerDir | Out-Null

    $isccPath = Find-Iscc
    Write-Host "Inno Setup : $isccPath"

    if (-not $SkipTests) {
        Write-Step 'Tests'
        Invoke-Native 'dotnet' @('test', 'tests/StreamDak.Tests', '-c', $Configuration, '--nologo')
    }

    $redist = $null
    if (-not $NoWebView2Redist) { Write-Step 'Programme d''installation WebView2'; $redist = Get-WebView2Redist }

    foreach ($f in $flavors) {
        $selfContained = ($f -eq 'self-contained').ToString().ToLowerInvariant()
        $out = Join-Path $publishRoot $f

        Write-Step "Publication ($f, win-x64)"
        Invoke-Native 'dotnet' @(
            'publish', 'StreamDak.csproj', '-c', $Configuration, '-r', 'win-x64', "--self-contained:$selfContained", '-o', $out,
            "-p:Version=$Version", '-p:PublishReadyToRun=true', '-p:ContinuousIntegrationBuild=true', '-p:Deterministic=true',
            '-p:DebugType=none', '-p:DebugSymbols=false', '--nologo')
        Get-ChildItem $out -Filter 'StreamDak*.xml' | Remove-Item -Force   # fichiers de documentation des API : inutiles à l'exécution
        Set-Content -Path (Join-Path $out 'flavor.txt') -Value $f -NoNewline -Encoding ascii   # lu par la mise à jour : elle reprend la même variante

        if ($Sign) {
            Write-Step "Signature des programmes ($f)"
            Invoke-Sign (@(Get-ChildItem $out -File | Where-Object { $_.Name -like 'StreamDak*.exe' -or $_.Name -like 'StreamDak*.dll' } | ForEach-Object FullName))
        }

        Write-Step "Installateur ($f)"
        $isccArgs = @("/DAppVersion=$Version", "/DSourceDir=$out", "/DFlavor=$f", "/DOutputDir=$installerDir", '/Q')
        if ($f -eq 'framework-dependent') { $isccArgs += '/DFrameworkDependent=1' }
        if ($redist) { $isccArgs += "/DRedistDir=$redist" }
        if ($Sign) {
            $signScript = Join-Path $root 'installer\sign.ps1'
            $isccArgs += '/DSignTool=1'
            $isccArgs += "/Ssigntool=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" -Files `$f"
        }
        Invoke-Native $isccPath ($isccArgs + (Join-Path $root 'installer\StreamDak.iss'))
    }

    Write-Step 'Sommes SHA-256'
    $setups = Get-ChildItem $installerDir -Filter '*-Setup.exe' | Sort-Object Name
    if (-not $setups) { throw 'Aucun installateur produit' }
    $lines = foreach ($s in $setups) { '{0}  {1}' -f (Get-FileHash $s.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $s.Name }
    Set-Content -Path (Join-Path $installerDir 'checksums.txt') -Value $lines -Encoding ascii
    $lines | ForEach-Object { Write-Host $_ }

    Write-Step 'Terminé'
    foreach ($s in $setups) { '{0,-62} {1,8:N1} Mo' -f $s.Name, ($s.Length / 1MB) | Write-Host }
    Write-Host "Dossier : $installerDir"
}
finally { Pop-Location }
