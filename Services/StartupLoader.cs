using System;
using System.IO;
using StreamDak.Platform.Windows;

namespace StreamDak;

/// <summary>Charge la configuration au démarrage sans jamais perdre le fichier de l'utilisateur.</summary>
public static class StartupLoader
{
    /// <summary>
    /// Lit config.json. Un fichier invalide est mis de côté (<c>config.invalide-&lt;date&gt;.json</c>) avant que la sauvegarde
    /// automatique ne le remplace, et le deck démarre sur la configuration par défaut avec un message.
    /// </summary>
    public static StartupResult Load()
    {
        try
        {
            var result = ConfigStore.LoadWithInfo();
            string? message = null;
            if (result.Migrated) message = "Configuration mise à jour vers les profils. Copie de l'ancien fichier : config.v1.backup.json";
            else if (result.Warnings.Count > 0) message = result.Warnings[0];
            return new StartupResult(result.Config, message);
        }
        catch (Exception ex)
        {
            // Le fichier reste lisible par l'utilisateur : on le met de côté avant que la sauvegarde automatique ne le remplace
            string note = "";
            try
            {
                var copy = Path.Combine(ConfigStore.Dir, $"config.invalide-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(ConfigStore.FilePath, copy, true);
                note = $" Ton fichier est conservé sous {Path.GetFileName(copy)}.";
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Copie du fichier invalide impossible : " + copyEx.Message);
            }
            Log.Error("Configuration invalide au démarrage", ex);
            return new StartupResult(ConfigStore.DefaultConfig(), "Config invalide : " + ex.Message + note);
        }
    }

    /// <summary>Crée le document : le registre fait foi pour le lancement avec Windows, et une sauvegarde est faite au démarrage.</summary>
    public static DeckDocument CreateDocument(StartupResult startup)
    {
        var cfg = startup.Config;
        cfg.StartWithWindows = AutoStart.IsEnabled();
        var document = new DeckDocument(cfg);
        ConfigStore.CreateBackup(cfg);
        return document;
    }
}
