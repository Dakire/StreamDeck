using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchDeck;

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

    static readonly Regex NameRx = new("^[0-9a-f]{32}\\.png$", RegexOptions.Compiled);
    static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    /// <summary>Extensions proposées à l'import.</summary>
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".webp" };
    /// <summary>Filtre de la boîte de dialogue d'import.</summary>
    public const string DialogFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp|Tous les fichiers|*.*";

    /// <summary>true pour un nom produit par IconStore (hachage + .png) : seul ce format est jamais lu, aucun chemin libre.</summary>
    public static bool IsValidName(string? name) => name != null && NameRx.IsMatch(name);

    /// <summary>Chemin complet d'une image stockée, ou null si le nom n'est pas un nom d'image valide (jamais de chemin libre).</summary>
    public static string? Resolve(string? name) => IsValidName(name) ? Path.Combine(ConfigStore.IconsDir, name!) : null;

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
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("Image vide");
            return Store(decoder.Frames[0]);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Impossible de lire cette image : fichier corrompu ou format non pris en charge", ex);
        }
    }

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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return StoreBytes(ms.ToArray());
    }

    /// <summary>Enregistre un PNG déjà encodé (import de paquet) après vérification de son contenu.</summary>
    public static string StoreBytes(byte[] png)
    {
        if (png.Length > MaxStoredBytes) throw new InvalidDataException("Image trop volumineuse");
        if (!LooksLikePng(png)) throw new InvalidDataException("Image non valide (PNG attendu)");
        try
        {
            using var check = new MemoryStream(png);
            var decoder = BitmapDecoder.Create(check, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var f = decoder.Frames[0];
            if (f.PixelWidth > MaxPixels || f.PixelHeight > MaxPixels) throw new InvalidDataException("Dimensions non acceptées");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) { throw new InvalidDataException("Image PNG corrompue", ex); }

        var name = Convert.ToHexString(SHA256.HashData(png))[..32].ToLowerInvariant() + ".png";
        Directory.CreateDirectory(ConfigStore.IconsDir);
        var path = Path.Combine(ConfigStore.IconsDir, name);
        if (!File.Exists(path)) File.WriteAllBytes(path, png);
        Cache.TryRemove(name, out _);
        return name;
    }

    /// <summary>Image prête à afficher (figée, mise en cache), ou null si absente ou illisible.</summary>
    public static ImageSource? Load(string? name)
    {
        if (!IsValidName(name)) return null;
        return Cache.GetOrAdd(name!, n =>
        {
            try
            {
                var path = Path.Combine(ConfigStore.IconsDir, n);
                if (!File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // Profil colorimétrique appliqué (pas d'IgnoreColorProfile) : une photo avec profil ICC garde ses couleurs d'origine
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            // Image effacée, tronquée ou format inconnu : le bouton s'affiche sans image (résultat mis en cache, consigné une fois)
            // (WIC signale aussi les fichiers corrompus par COMException, d'où ExternalException)
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException
                                       or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            {
                Log.Warn($"Image de bouton illisible ({n}) : {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>Vide le cache des images chargées (changement de dossier de configuration dans les tests).</summary>
    public static void ClearCache() => Cache.Clear();
}
