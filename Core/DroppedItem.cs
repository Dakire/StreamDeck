using System;
using System.IO;

namespace StreamDak;

/// <summary>
/// Transforme ce qu'on dépose sur une case vide du deck (fichier, raccourci .lnk, exécutable, dossier, adresse web) en bouton
/// « Lancer » ou « Ouvrir », avec son titre. L'icône d'application (image) est ajoutée par l'interface ; ici, une icône Fluent de repli.
/// </summary>
public static class DroppedItem
{
    const string LaunchColor = "#7C3AED";
    const string UrlColor = "#2563EB";
    const string FolderColor = "#334155";
    const int MaxTitle = 40;

    /// <summary>Bouton « Lancer » pour un fichier ou un dossier existant (.lnk, .exe, document…), ou « Ouvrir » pour un raccourci Internet (.url).</summary>
    public static bool TryFromPath(string? path, out DeckButton button, out string error)
    {
        button = new DeckButton();
        path = path?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) { error = "Rien à ajouter : dépose un fichier, un dossier ou une adresse"; return false; }
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(path))
        {
            error = "Ce chemin n'est pas valide : dépose le fichier depuis l'Explorateur";
            return false;
        }

        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) { error = "Ce fichier est introuvable : il a peut-être été déplacé"; return false; }

        if (!isDir && string.Equals(Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase)
            && ReadInternetShortcut(path) is { } target && TryFromUrl(target, out button, out error))
        {
            button.Label = Title(Path.GetFileNameWithoutExtension(path), button.Label);
            return true;
        }

        var name = isDir ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path);
        button = new DeckButton
        {
            Label = Title(name, path), Icon = isDir ? "Folder24" : "Rocket24", Color = isDir ? FolderColor : LaunchColor,
            Actions = { new DeckAction { Type = ActionTypes.Launch, Value = path } },
        };
        error = "";
        return true;
    }

    /// <summary>Bouton « Ouvrir » pour une adresse web (http ou https ; « www.… » reçoit https://).</summary>
    public static bool TryFromUrl(string? text, out DeckButton button, out string error)
    {
        button = new DeckButton();
        var value = text?.Trim() ?? "";
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
        {
            error = "Cette adresse n'est pas valide : elle doit commencer par https://";
            return false;
        }
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        button = new DeckButton
        {
            Label = Title(host, value), Icon = "Globe24", Color = UrlColor,
            Actions = { new DeckAction { Type = ActionTypes.Url, Value = uri.AbsoluteUri } },
        };
        error = "";
        return true;
    }

    /// <summary>Texte déposé ou collé : une adresse web, sinon un chemin de fichier ou de dossier.</summary>
    public static bool TryFromText(string? text, out DeckButton button, out string error)
    {
        var t = text?.Trim().Trim('"') ?? "";
        if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return TryFromUrl(t, out button, out error);
        if (t.Length > 0 && Path.IsPathRooted(t)) return TryFromPath(t, out button, out error);
        button = new DeckButton();
        error = "Ce texte n'est ni une adresse web ni un fichier : dépose un lien (https://…), un fichier ou un dossier";
        return false;
    }

    static string Title(string? name, string fallback)
    {
        var t = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        return t.Length <= MaxTitle ? t : t[..(MaxTitle - 1)] + "…";
    }

    /// <summary>Adresse d'un fichier .url (ligne « URL=… »), ou null ; lecture bornée à 64 Ko.</summary>
    static string? ReadInternetShortcut(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) return null;
            foreach (var line in File.ReadLines(path))
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line[4..].Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Raccourci Internet illisible ({Path.GetFileName(path)}) : {ex.Message}"); }
        return null;
    }
}
