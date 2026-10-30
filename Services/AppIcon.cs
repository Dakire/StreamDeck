using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace StreamDak;

/// <summary>Icône d'une application, d'un raccourci ou d'un fichier .ico enregistrée comme image de bouton.</summary>
public static class AppIcon
{
    /// <summary>
    /// Chemin complet du programme désigné par une étape « lancer » ou « fenêtre » : <c>%VARIABLES%</c> développées, guillemets retirés, et un nom seul
    /// (« notepad », « chrome.exe ») cherché dans le PATH. Null si rien n'existe.
    /// </summary>
    public static string? Resolve(string? program)
    {
        if (string.IsNullOrWhiteSpace(program)) return null;
        var text = Environment.ExpandEnvironmentVariables(program.Trim().Trim('"'));
        if (text.Length > 1024 || text.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        if (File.Exists(text)) return Path.GetFullPath(text);
        if (Path.IsPathRooted(text) || text.Contains('\\') || text.Contains('/')) return null;
        var extensions = Path.HasExtension(text) ? new[] { "" } : new[] { ".exe", ".com", ".bat", ".cmd", ".lnk" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), text + extension);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { /* entrée de PATH invalide : suivante */ }
            }
        return null;
    }

    /// <summary>
    /// Enregistre l'icône du fichier : un .ico est lu directement (sa plus grande taille), un programme ou un raccourci donne l'icône de Windows. Retourne le nom
    /// de l'image, ou null si le fichier n'existe pas ou n'a pas d'icône lisible.
    /// </summary>
    public static string? Store(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;   // un dossier garde son icône Fluent
        try
        {
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)) return IconStore.Import(path);
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return IconStore.Store(bitmap);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Warn($"Icône de {Path.GetFileName(path)} introuvable : {ex.Message}");
            return null;
        }
    }

    /// <summary>Première étape qui désigne un programme existant (lancer, fenêtre), ou null.</summary>
    public static string? FirstProgram(System.Collections.Generic.IEnumerable<DeckAction> steps) =>
        steps.Where(a => a.Type is ActionTypes.Launch or ActionTypes.Focus).Select(a => Resolve(a.Type == ActionTypes.Focus && !string.IsNullOrWhiteSpace(a.Args) ? a.Args : a.Value))
            .FirstOrDefault(p => p != null);
}
