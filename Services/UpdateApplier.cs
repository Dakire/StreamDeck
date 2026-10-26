using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StreamDak;

/// <summary>Ce que l'applicateur doit faire : lancer cet installateur à la place de l'application en cours, puis la relancer.</summary>
/// <param name="Installer">Installateur téléchargé et vérifié.</param>
/// <param name="InstallDir">Dossier d'installation à conserver (passé à l'installateur : <c>/DIR</c>).</param>
/// <param name="Exe">Exécutable à relancer ensuite.</param>
/// <param name="ProcessId">Processus de l'application en cours : l'applicateur attend qu'il se termine.</param>
/// <param name="WorkDir">Dossier du journal, de la sauvegarde et du marqueur d'échec.</param>
public sealed record UpdatePlan(string Installer, string InstallDir, string Exe, int ProcessId, string WorkDir);

/// <summary>Lance l'installation d'une mise à jour hors de l'application (qui doit se fermer pour que ses fichiers soient remplacés).</summary>
public interface IUpdateApplier
{
    /// <summary>Démarre l'applicateur, sans l'attendre : l'appelant ferme ensuite l'application.</summary>
    void Launch(UpdatePlan plan);
}

/// <summary>
/// Applique une mise à jour avec un script PowerShell fourni par l'application (jamais téléchargé), exécuté par <c>-File</c> avec ses paramètres passés un à un :
/// attend la fermeture de l'application, <b>sauvegarde</b> le dossier d'installation, lance l'installateur en mode silencieux dans le même dossier (les profils, dans %AppData%,
/// ne sont jamais touchés), puis relance l'application. Si l'installateur échoue : <b>restauration</b> de la sauvegarde, relance de l'ancienne version et marqueur d'échec
/// (<c>update.failed</c>) que l'application affiche au démarrage suivant.
/// </summary>
public sealed class UpdateApplier : IUpdateApplier
{
    /// <summary>Nom du marqueur d'échec, dans le dossier de travail.</summary>
    public const string FailedMarker = "update.failed";
    /// <summary>Nom du journal de l'applicateur.</summary>
    public const string LogName = "update.log";

    /// <summary>Script exécuté (texte fixe, écrit à chaque mise à jour : aucune partie ne vient du réseau ni de l'utilisateur).</summary>
    public const string Script = """
        param(
            [Parameter(Mandatory)][int]$ProcessId,
            [Parameter(Mandatory)][string]$Installer,
            [Parameter(Mandatory)][string]$InstallDir,
            [Parameter(Mandatory)][string]$Exe,
            [Parameter(Mandatory)][string]$WorkDir
        )
        $ErrorActionPreference = 'Stop'
        $log = Join-Path $WorkDir 'update.log'
        $backup = Join-Path $WorkDir 'backup'
        $failed = Join-Path $WorkDir 'update.failed'
        function Log([string]$text) { Add-Content -Path $log -Value ('{0:s} {1}' -f (Get-Date), $text) -Encoding UTF8 }
        function Copy-Tree([string]$from, [string]$to) {
            & robocopy.exe $from $to /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy a échoué (code $LASTEXITCODE)" }
        }
        try {
            Remove-Item $failed -Force -ErrorAction SilentlyContinue
            Log "Mise à jour : attente de la fermeture de l'application (processus $ProcessId)"
            $app = if ($ProcessId -gt 0) { Get-Process -Id $ProcessId -ErrorAction SilentlyContinue }
            if ($app -and -not $app.WaitForExit(60000)) { throw "L'application ne s'est pas fermée dans la minute" }
            Log 'Sauvegarde du dossier d''installation'
            Copy-Tree $InstallDir $backup
            Log 'Lancement de l''installateur'
            $setup = Start-Process -FilePath $Installer -ArgumentList @('/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS', ('/DIR="{0}"' -f $InstallDir), ('/LOG="{0}"' -f (Join-Path $WorkDir 'installer.log'))) -Wait -PassThru
            if ($setup.ExitCode -ne 0) { throw "L'installateur a échoué (code $($setup.ExitCode))" }
            Log 'Installation réussie'
            Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath $Exe
        }
        catch {
            Log "ÉCHEC : $_"
            try {
                if (Test-Path $backup) { Log 'Restauration de la sauvegarde'; Copy-Tree $backup $InstallDir }
            } catch { Log "Restauration impossible : $_" }
            Set-Content -Path $failed -Value "$_" -Encoding UTF8
            Start-Process -FilePath $Exe -ErrorAction SilentlyContinue
        }
        """;

    /// <inheritdoc/>
    public void Launch(UpdatePlan plan) => Start(plan);

    /// <summary>Écrit le script et démarre PowerShell sur lui ; rend le processus (les tests l'attendent).</summary>
    public static Process Start(UpdatePlan plan)
    {
        Directory.CreateDirectory(plan.WorkDir);
        var script = Path.Combine(plan.WorkDir, "apply-update.ps1");
        File.WriteAllText(script, Script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // avec marque d'ordre des octets : PowerShell 5 lit alors les accents
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", script,
            "-ProcessId", plan.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-Installer", plan.Installer, "-InstallDir", plan.InstallDir.TrimEnd('\\'), "-Exe", plan.Exe, "-WorkDir", plan.WorkDir.TrimEnd('\\'),
        }) psi.ArgumentList.Add(argument);
        return Process.Start(psi) ?? throw new InvalidOperationException("PowerShell n'a pas démarré");
    }
}
