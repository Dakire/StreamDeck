using System;
using System.IO;
using System.Linq;
using StreamDak.Platform.Windows;

namespace StreamDak;

/// <summary>Charge la configuration au démarrage sans jamais perdre le fichier de l'utilisateur.</summary>
public static class StartupLoader
{
    /// <summary>
    /// Lit config.json. Un fichier invalide est mis de côté (<c>config.invalide-&lt;date&gt;.json</c>) avant que la sauvegarde
    /// automatique ne le remplace ; le deck reprend la dernière sauvegarde valide (sinon la configuration par défaut), avec un message.
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
                note = $" Ton fichier est conservé sous {KeepInvalidCopy()}.";
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Copie du fichier invalide impossible : " + copyEx.Message);
            }
            Log.Error("Configuration invalide au démarrage", ex);
            // Mieux qu'un deck vide : la sauvegarde valide la plus récente
            foreach (var backup in ConfigStore.ListBackups())
            {
                try
                {
                    var restored = ConfigStore.LoadBackup(backup.Path);
                    Log.Warn($"Configuration reprise depuis la sauvegarde du {backup.Time:g}");
                    return new StartupResult(restored, $"Config invalide : reprise depuis la sauvegarde du {backup.Time:d MMM yyyy à HH:mm}.{note}");
                }
                catch (Exception backupEx) when (backupEx is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    Log.Warn($"Sauvegarde {backup.Name} illisible : {backupEx.Message}");
                }
            }
            return new StartupResult(ConfigStore.DefaultConfig(), "Config invalide : " + ex.Message + note);
        }
    }

    /// <summary>Plus de copies du fichier invalide conservées (les plus anciennes sont supprimées).</summary>
    public const int MaxInvalidCopies = 5;

    /// <summary>Copie config.json en <c>config.invalide-&lt;date&gt;.json</c> (sans doublon d'un contenu déjà copié, au plus <see cref="MaxInvalidCopies"/>) ; retourne le nom du fichier.</summary>
    static string KeepInvalidCopy()
    {
        var content = File.ReadAllBytes(ConfigStore.FilePath);
        var existing = new DirectoryInfo(ConfigStore.Dir).GetFiles("config.invalide-*.json").OrderByDescending(f => f.Name).ToList();
        foreach (var f in existing)
        {
            try { if (f.Length == content.Length && File.ReadAllBytes(f.FullName).AsSpan().SequenceEqual(content)) return f.Name; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var copy = Path.Combine(ConfigStore.Dir, $"config.invalide-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllBytes(copy, content);
        foreach (var old in existing.Skip(MaxInvalidCopies - 1))
        {
            try { old.Delete(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn($"Copie invalide non supprimée ({old.Name}) : {e.Message}"); }
        }
        return Path.GetFileName(copy);
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
