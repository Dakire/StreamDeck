using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StreamDak;

/// <summary>
/// Images de boutons : importées, redimensionnées (512 px max) et stockées en PNG sous un nom dérivé de leur contenu,
/// dans le dossier Icons de la configuration. La configuration ne contient que ce nom : aucun chemin arbitraire.
/// </summary>
public static class IconStore
{
    /// <summary>Taille maximale d'un fichier image importé.</summary>
    public const long MaxSourceBytes = 15 * 1024 * 1024;
    /// <summary>Dimension maximale (largeur ou hauteur) d'une image importée : au-delà, elle est refusée.</summary>
    public const int MaxPixels = 8192;
    /// <summary>Côté maximal de l'image enregistrée : les images plus grandes sont réduites à l'import.</summary>
    public const int StoredSize = 512;
    /// <summary>Taille maximale d'une image enregistrée (PNG).</summary>
    public const long MaxStoredBytes = 2 * 1024 * 1024;

    static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    /// <summary>Extensions proposées à l'import.</summary>
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".webp" };
    /// <summary>Filtre de la boîte de dialogue d'import.</summary>
    public const string DialogFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp|Tous les fichiers|*.*";

    /// <summary>true pour un nom produit par IconStore (hachage + .png) : seul ce format est jamais lu, aucun chemin libre.</summary>
    public static bool IsValidName(string? name) => IconNames.IsValid(name);

    /// <summary>Chemin complet d'une image stockée, ou null si le nom n'est pas un nom d'image valide (jamais de chemin libre).</summary>
    public static string? Resolve(string? name) => IconNames.Resolve(name);

    /// <summary>true si les octets commencent par la signature PNG (contrôle des archives importées).</summary>
    public static bool LooksLikePng(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngSignature.Length) return false;
        for (int i = 0; i < PngSignature.Length; i++) if (data[i] != PngSignature[i]) return false;
        return true;
    }

    /// <summary>Importe une image depuis le disque. Lève <see cref="InvalidDataException"/> avec un message affichable.</summary>
    public static string Import(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new InvalidDataException("Le fichier n'existe plus");
        if (info.Length > MaxSourceBytes) throw new InvalidDataException("Image trop volumineuse (15 Mo maximum)");
        if (Array.IndexOf(Extensions, info.Extension.ToLowerInvariant()) < 0)
            throw new InvalidDataException("Format non pris en charge (PNG, JPEG, BMP, GIF, ICO, TIFF ou WebP)");
        try
        {
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Store(DecodeLargest(stream));
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Impossible de lire cette image : fichier corrompu ou format non pris en charge", ex);
        }
    }

    /// <summary>
    /// Décode la plus grande image d'un flux, après avoir lu ses dimensions SANS la décoder (en-têtes seulement) : une image déclarée plus grande que
    /// <see cref="MaxPixels"/> est refusée avant que le moindre pixel soit alloué (« bombe de décompression »). Le flux doit permettre le repositionnement.
    /// </summary>
    public static BitmapFrame DecodeLargest(Stream stream)
    {
        try
        {
            var probe = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (probe.Frames.Count == 0) throw new InvalidDataException("Image vide");
            foreach (var f in probe.Frames)
                if (f.PixelWidth <= 0 || f.PixelHeight <= 0 || f.PixelWidth > MaxPixels || f.PixelHeight > MaxPixels)
                    throw new InvalidDataException($"Dimensions non acceptées (maximum {MaxPixels} px)");
            stream.Seek(0, SeekOrigin.Begin);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return LargestFrame(decoder) ?? throw new InvalidDataException("Image vide");
        }
        catch (OutOfMemoryException ex) { throw new InvalidDataException("Image trop grande pour être décodée", ex); }
    }

    /// <summary>Plus grande image d'un fichier (un .ico en contient plusieurs tailles : 16, 32, 256 px) ; null s'il n'en a aucune.</summary>
    public static BitmapFrame? LargestFrame(BitmapDecoder decoder) =>
        decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).FirstOrDefault();

    /// <summary>Redimensionne, encode en PNG et enregistre une image déjà décodée.</summary>
    public static string Store(BitmapSource frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > MaxPixels || frame.PixelHeight > MaxPixels)
            throw new InvalidDataException($"Dimensions non acceptées (maximum {MaxPixels} px)");
        BitmapSource source = frame;
        int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longest > StoredSize)
        {
            double k = (double)StoredSize / longest;
            source = new TransformedBitmap(frame, new ScaleTransform(k, k));
        }
        return StoreBytes(EncodePng(source));
    }

    /// <summary>Encode une image en PNG (sans la redimensionner).</summary>
    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Nom d'enregistrement d'un PNG : dérivé de son contenu, identique d'une machine à l'autre pour les mêmes octets.</summary>
    public static string NameOf(byte[] png) => Convert.ToHexString(SHA256.HashData(png))[..32].ToLowerInvariant() + ".png";

    /// <summary>Enregistre un PNG déjà encodé (import de paquet) après vérification de son contenu.</summary>
    public static string StoreBytes(byte[] png)
    {
        if (png.Length > MaxStoredBytes) throw new InvalidDataException("Image trop volumineuse");
        if (!LooksLikePng(png)) throw new InvalidDataException("Image non valide (PNG attendu)");
        try
        {
            using var check = new MemoryStream(png);
            DecodeLargest(check);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException
                                       or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        { throw new InvalidDataException("Image PNG corrompue", ex); }

        var name = NameOf(png);
        Directory.CreateDirectory(ConfigStore.IconsDir);
        var path = Path.Combine(ConfigStore.IconsDir, name);
        if (!File.Exists(path)) File.WriteAllBytes(path, png);
        Cache.TryRemove(name, out _);
        foreach (var key in Sized.Keys.Where(k => k.Item1 == name)) Sized.TryRemove(key, out _);
        return name;
    }

    /// <summary>Largeur au-delà de laquelle une image est décodée réduite : 8192 px décodés coûteraient 256 Mo pour une touche de quelques centaines de pixels.</summary>
    public const int MaxDecodedWidth = 1024;
    // Image absente ou illisible : retentée au plus une fois toutes les 30 s (une image restaurée plus tard réapparaît), consignée une fois
    static readonly ConcurrentDictionary<string, long> Failed = new();
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>Image prête à afficher (figée, mise en cache), ou null si absente ou illisible.</summary>
    public static ImageSource? Load(string? name)
    {
        if (!IsValidName(name)) return null;
        if (Cache.TryGetValue(name!, out var cached) && cached != null) return cached;
        if (Failed.TryGetValue(name!, out var at) && Environment.TickCount64 - at < RetryAfter.TotalMilliseconds) return null;
        try
        {
            var path = Path.Combine(ConfigStore.IconsDir, name!);
            if (!File.Exists(path)) { Failed[name!] = Environment.TickCount64; return null; }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // Profil colorimétrique appliqué (pas d'IgnoreColorProfile) : une photo avec profil ICC garde ses couleurs d'origine
            bmp.UriSource = new Uri(path);
            if (PixelWidth(path) > MaxDecodedWidth) bmp.DecodePixelWidth = MaxDecodedWidth;
            bmp.EndInit();
            bmp.Freeze();
            Cache[name!] = bmp;
            Failed.TryRemove(name!, out _);
            return bmp;
        }
        // Image effacée, tronquée ou format inconnu : le bouton s'affiche sans image (consigné une fois)
        // (WIC signale aussi les fichiers corrompus par COMException, d'où ExternalException)
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException
                                   or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            if (Failed.TryAdd(name!, Environment.TickCount64)) Log.Warn($"Image de bouton illisible ({name}) : {ex.Message}");
            else Failed[name!] = Environment.TickCount64;
            return null;
        }
    }

    /// <summary>Paliers de décodage (côté le plus long, pixels) : une touche redimensionnée réutilise le décodage du palier au lieu de redécoder à chaque pixel.</summary>
    static readonly int[] DecodeSteps = { 32, 48, 64, 96, 128, 192, 256, 384, 512 };
    static readonly ConcurrentDictionary<(string, int), ImageSource> Sized = new();

    /// <summary>Palier de décodage pour un affichage de <paramref name="targetPx"/> pixels physiques : le plus petit palier qui le couvre (512 au plus).</summary>
    public static int DecodeStep(int targetPx) => DecodeSteps.FirstOrDefault(step => step >= targetPx, DecodeSteps[^1]);

    /// <summary>
    /// Image décodée pour un affichage de <paramref name="targetPx"/> pixels physiques sur son côté le plus long (échelle de l'écran comprise) : une image de
    /// 512 px montrée en 96 px est décodée en 96 px par WIC, plus net qu'une réduction au rendu. Jamais agrandie : une source plus petite est rendue telle quelle.
    /// </summary>
    public static ImageSource? LoadForDisplay(string? name, int targetPx)
    {
        if (Load(name) is not BitmapSource full) return null;
        int longest = Math.Max(full.PixelWidth, full.PixelHeight), step = DecodeStep(targetPx);
        if (step >= longest) return full;
        if (Sized.TryGetValue((name!, step), out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(Path.Combine(ConfigStore.IconsDir, name!));
            if (full.PixelWidth >= full.PixelHeight) bmp.DecodePixelWidth = step; else bmp.DecodePixelHeight = step;
            bmp.EndInit();
            bmp.Freeze();
            return Sized[(name!, step)] = bmp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException
                                   or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            return full;   // le fichier a changé entre-temps : l'image déjà décodée reste affichable
        }
    }

    static int PixelWidth(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return decoder.Frames[0].PixelWidth;
    }

    /// <summary>Décode des images sur le pool de threads pour que la première construction de la grille les trouve en cache (le décodage sur l'UI saccadait).</summary>
    public static Task PreloadAsync(IEnumerable<string?> names) =>
        Task.Run(() =>
        {
            foreach (var name in names.Where(n => n != null).Distinct())
                Load(name);
        });

    /// <summary>Vide le cache des images chargées (changement de dossier de configuration dans les tests).</summary>
    public static void ClearCache() { Cache.Clear(); Failed.Clear(); Sized.Clear(); }
}
