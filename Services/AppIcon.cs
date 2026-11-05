using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
    /// Enregistre l'icône du fichier : un .ico est lu directement (sa plus grande taille), un programme ou un raccourci donne la plus grande icône de Windows
    /// (256 px quand elle existe, sinon 48 px, sinon 32 px). Retourne le nom de l'image, ou null si le fichier n'existe pas ou n'a pas d'icône lisible.
    /// </summary>
    public static string? Store(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;   // un dossier garde son icône Fluent
        try
        {
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase)) return IconStore.Import(path);
            if (Largest(path) is { } large) return IconStore.Store(large);
            return Legacy(path) is { } small ? IconStore.Store(small) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Warn($"Icône de {Path.GetFileName(path)} introuvable : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Nom qu'aurait l'image de ce programme enregistrée par les versions précédentes (icône de 32 px de <c>ExtractAssociatedIcon</c>), sans rien écrire ;
    /// null si elle n'est pas lisible. Sert à reconnaître une ancienne icône d'application pour la remplacer par sa grande version (<see cref="IconUpgrade"/>).
    /// </summary>
    public static string? LegacyName(string path)
    {
        try { return Legacy(path) is { } small ? IconStore.NameOf(IconStore.EncodePng(small)) : null; }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Icône de 32 px rendue par Windows (ancien chemin, et repli si la liste d'images système n'en a pas).</summary>
    static BitmapSource? Legacy(string path)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon == null) return null;
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Plus grande icône de la liste d'images système, marges transparentes retirées ; null si Windows n'en donne pas.</summary>
    static BitmapSource? Largest(string path)
    {
        var handle = Platform.Windows.WindowsPlatform.ExtractLargestIcon(path);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var trimmed = TrimPadding(bitmap);
            trimmed.Freeze();
            return trimmed;
        }
        finally { Platform.Windows.WindowsPlatform.DestroyIconHandle(handle); }
    }

    /// <summary>
    /// Un programme qui n'a qu'une icône de 32 ou 48 px est rendu par la liste « jumbo » dans le coin d'un carré transparent de 256 px : affiché tel quel, il
    /// serait minuscule et décentré. Si le dessin occupe moins de 60 % du carré, l'image est recadrée sur lui (carré, centré). Sinon elle est rendue telle quelle.
    /// </summary>
    internal static BitmapSource TrimPadding(BitmapSource source)
    {
        var bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (pixels[y * stride + x * 4 + 3] > 8)
                {
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
        if (right < 0) return source;   // image entièrement transparente : rien à recadrer
        int side = Math.Max(right - left + 1, bottom - top + 1);
        if (side >= Math.Max(w, h) * 0.6) return source;
        // Carré centré sur le dessin, tenu dans l'image
        int cx = (left + right + 1) / 2, cy = (top + bottom + 1) / 2;
        int x0 = Math.Clamp(cx - side / 2, 0, Math.Max(0, w - side)), y0 = Math.Clamp(cy - side / 2, 0, Math.Max(0, h - side));
        return new CroppedBitmap(source, new Int32Rect(x0, y0, Math.Min(side, w - x0), Math.Min(side, h - y0)));
    }

    /// <summary>Première étape qui désigne un programme existant (lancer, fenêtre), ou null.</summary>
    public static string? FirstProgram(System.Collections.Generic.IEnumerable<DeckAction> steps) =>
        steps.Where(a => a.Type is ActionTypes.Launch or ActionTypes.Focus).Select(a => Resolve(a.Type == ActionTypes.Focus && !string.IsNullOrWhiteSpace(a.Args) ? a.Args : a.Value))
            .FirstOrDefault(p => p != null);
}
