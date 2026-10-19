using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TouchDeck;

public interface IConfirm
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false);
}

/// <summary>Import et export de profils et de configurations : lecture vérifiée, confirmation, sauvegarde préalable, opération annulable.</summary>
public static class TransferService
{
    public static void ExportProfile(DeckProfile profile, string path)
    {
        try
        {
            ProfilePackage.ExportProfile(profile, path);
            Notifier.Success($"Profil « {profile.Name} » exporté");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Export impossible : " + ex.Message);
        }
    }

    public static void ExportConfig(DeckConfig cfg, string path)
    {
        try
        {
            ProfilePackage.ExportConfig(cfg, path);
            Notifier.Success("Configuration exportée");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Export impossible : " + ex.Message);
        }
    }

    /// <summary>Importe un profil. Retourne le nom du profil ajouté, ou null (annulé ou refusé : l'erreur est notifiée).</summary>
    public static async Task<string?> ImportProfileAsync(IConfirm ui, DeckDocument doc, string path)
    {
        PackageContents pkg;
        try { pkg = ProfilePackage.Read(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Import impossible : " + ex.Message);
            return null;
        }
        if (pkg.Profile == null)
        {
            Notifier.Error("Ce fichier contient une configuration complète, pas un profil. Utilise Paramètres › Sauvegardes › Importer.");
            return null;
        }

        var profile = pkg.Profile;
        int buttons = profile.Pages.Sum(p => DeckDocument.AllButtons(p.Buttons).Count());
        var msg = new StringBuilder();
        msg.AppendLine(CultureInfo.CurrentCulture, $"Ajouter le profil « {profile.Name} » ({profile.Pages.Count} page(s), {buttons} bouton(s)) ?");
        if (pkg.RiskyActions > 0)
            msg.AppendLine().AppendLine(CultureInfo.CurrentCulture, $"Il contient {pkg.RiskyActions} action(s) qui lancent des programmes ou des scripts avec tes droits. N'importe que des fichiers de confiance.");
        foreach (var w in pkg.Warnings.Take(5)) msg.AppendLine("• " + w);
        if (!await ui.ConfirmAsync("Importer un profil", msg.ToString().TrimEnd(), "Importer", danger: pkg.RiskyActions > 0)) return null;

        try { ProfilePackage.InstallIcons(pkg); }
        catch (InvalidDataException ex) { Notifier.Error("Import impossible : " + ex.Message); return null; }

        ConfigStore.CreateBackup(doc.Config);
        var r = doc.AddImportedProfile(profile);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Import impossible"); return null; }
        Notifier.Success($"Profil « {profile.Name} » importé", "Annuler", () => doc.Undo());
        return profile.Name;
    }

    /// <summary>Remplace toute la configuration par celle du fichier. Retourne true si importée.</summary>
    public static async Task<bool> ImportConfigAsync(IConfirm ui, DeckDocument doc, string path)
    {
        PackageContents pkg;
        try { pkg = ProfilePackage.Read(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Import impossible : " + ex.Message);
            return false;
        }
        if (pkg.Config == null)
        {
            Notifier.Error("Ce fichier contient un seul profil. Utilise l'import de profil dans l'éditeur.");
            return false;
        }
        var cfg = pkg.Config;
        int buttons = cfg.Profiles.Sum(pr => pr.Pages.Sum(p => DeckDocument.AllButtons(p.Buttons).Count()));
        var msg = new StringBuilder();
        msg.AppendLine(CultureInfo.CurrentCulture, $"Remplacer toute la configuration actuelle par celle du fichier ({cfg.Profiles.Count} profil(s), {buttons} bouton(s)) ?");
        msg.AppendLine().AppendLine("Une sauvegarde de la configuration actuelle est faite avant, et tu peux annuler.");
        if (pkg.RiskyActions > 0)
            msg.AppendLine().AppendLine(CultureInfo.CurrentCulture, $"Le fichier contient {pkg.RiskyActions} action(s) qui lancent des programmes ou des scripts avec tes droits. N'importe que des fichiers de confiance.");
        foreach (var w in pkg.Warnings.Take(5)) msg.AppendLine("• " + w);
        if (!await ui.ConfirmAsync("Importer une configuration", msg.ToString().TrimEnd(), "Remplacer", danger: true)) return false;

        try { ProfilePackage.InstallIcons(pkg); }
        catch (InvalidDataException ex) { Notifier.Error("Import impossible : " + ex.Message); return false; }

        ConfigStore.CreateBackup(doc.Config);
        cfg.StartWithWindows = doc.Config.StartWithWindows;   // réglage de la machine, pas du fichier
        doc.ReplaceAll(cfg, "Importer une configuration");
        Notifier.Success("Configuration importée", "Annuler", () => doc.Undo());
        return true;
    }

    public static bool RestoreBackup(DeckDocument doc, string backupPath)
    {
        try
        {
            var cfg = ConfigStore.LoadBackup(backupPath);
            ConfigStore.CreateBackup(doc.Config);
            cfg.StartWithWindows = doc.Config.StartWithWindows;
            doc.ReplaceAll(cfg, "Restaurer une sauvegarde");
            Notifier.Success("Sauvegarde restaurée", "Annuler", () => doc.Undo());
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Restauration impossible : " + ex.Message);
            return false;
        }
    }
}
