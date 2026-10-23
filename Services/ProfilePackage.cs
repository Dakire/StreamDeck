using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace StreamDak;

/// <summary>Contenu vérifié d'un fichier .streamdak, prêt à être installé. Rien n'est écrit sur le disque avant <see cref="ProfilePackage.InstallIcons"/>.</summary>
public sealed record PackageContents(
    DeckProfile? Profile, DeckConfig? Config, int RiskyActions, IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, byte[]> Icons);

/// <summary>
/// Export et import de profils ou de configurations complètes : une archive zip (manifest.json, profile.json ou config.json,
/// icons/*.png). Tout est borné et validé : taille, nombre d'entrées, profondeur, types d'actions, noms d'images.
/// </summary>
public static class ProfilePackage
{
    /// <summary>Extension des archives de profil ou de configuration.</summary>
    public const string Extension = ".streamdak";
    /// <summary>Filtre de la boîte de dialogue des profils.</summary>
    public const string DialogFilter = "Profil ou configuration StreamDak|*.streamdak;*.touchdeck|Tous les fichiers|*.*";
    /// <summary>Filtre de la boîte de dialogue de configuration (archive ou JSON brut).</summary>
    public const string ConfigDialogFilter = "Configuration StreamDak|*.streamdak;*.touchdeck;*.json|Tous les fichiers|*.*";

    /// <summary>Taille maximale du JSON d'une archive (protection contre les archives piégées).</summary>
    public const long MaxJsonBytes = 10 * 1024 * 1024;
    /// <summary>Taille maximale d'une archive.</summary>
    public const long MaxFileBytes = 60 * 1024 * 1024;
    /// <summary>Nombre maximal d'images dans une archive.</summary>
    public const int MaxIcons = 600;
    /// <summary>Nombre maximal de boutons importés.</summary>
    public const int MaxButtons = 5000;
    /// <summary>Nombre maximal de pages importées.</summary>
    public const int MaxPages = 200;
    /// <summary>Nombre maximal de profils importés.</summary>
    public const int MaxProfiles = 100;
    /// <summary>Longueur maximale d'un texte importé (titre, valeur, arguments).</summary>
    public const int MaxStringLength = 8192;

    /// <summary>Écrit un profil et ses images dans une archive .streamdak.</summary>
    public static void ExportProfile(DeckProfile profile, string path) =>
        Write(path, "profile", "profile.json", JsonSerializer.Serialize(profile, ConfigStore.Options),
            IconNames(new[] { profile }));

    /// <summary>Écrit toute la configuration et ses images dans une archive .streamdak.</summary>
    public static void ExportConfig(DeckConfig cfg, string path) =>
        Write(path, "config", "config.json", ConfigStore.Serialize(cfg), IconNames(cfg.Profiles));

    static HashSet<string> IconNames(IEnumerable<DeckProfile> profiles) =>
        profiles.SelectMany(p => p.Pages).SelectMany(p => DeckDocument.AllButtons(p.Buttons))
            .Select(b => b.ImagePath).Where(IconStore.IsValidName).Select(n => n!).ToHashSet();

    static void Write(string path, string kind, string jsonName, string json, HashSet<string> icons)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            Add(zip, "manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { format = "streamdak", kind, version = 1 })));
            Add(zip, jsonName, Encoding.UTF8.GetBytes(json));
            foreach (var name in icons)
            {
                var file = IconStore.Resolve(name);
                if (file != null && File.Exists(file)) Add(zip, "icons/" + name, File.ReadAllBytes(file));
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    static void Add(ZipArchive zip, string name, byte[] data)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data, 0, data.Length);
    }

    /// <summary>Lit et valide un fichier. Lève <see cref="InvalidDataException"/> avec un message affichable.</summary>
    public static PackageContents Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException("Le fichier n'existe pas");
        if (info.Length > MaxFileBytes) throw new InvalidDataException("Fichier trop volumineux");

        byte[] head = new byte[2];
        using (var fs = File.OpenRead(path)) { _ = fs.Read(head, 0, 2); }
        bool isZip = head[0] == (byte)'P' && head[1] == (byte)'K';

        var warnings = new List<string>();
        var icons = new Dictionary<string, byte[]>();
        string? profileJson = null, configJson = null;

        if (isZip)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                if (zip.Entries.Count > MaxIcons + 10) throw new InvalidDataException("Archive invalide : trop d'entrées");
                long total = 0;
                foreach (var e in zip.Entries)
                {
                    if (e.FullName == "profile.json") profileJson = Encoding.UTF8.GetString(ReadBounded(e, MaxJsonBytes, ref total));
                    else if (e.FullName == "config.json") configJson = Encoding.UTF8.GetString(ReadBounded(e, MaxJsonBytes, ref total));
                    else if (e.FullName.StartsWith("icons/", StringComparison.Ordinal))
                    {
                        var name = e.FullName[6..];
                        if (!IconStore.IsValidName(name)) { warnings.Add($"Image ignorée (nom non valide) : {Truncate(name)}"); continue; }
                        var data = ReadBounded(e, IconStore.MaxStoredBytes, ref total);
                        if (!IconStore.LooksLikePng(data)) { warnings.Add($"Image ignorée (pas un PNG) : {name}"); continue; }
                        icons[name] = data;
                    }
                    // toute autre entrée est ignorée : on n'extrait jamais par nom, donc pas de « zip slip »
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException("Archive illisible ou corrompue", ex);
            }
        }
        else
        {
            if (info.Length > MaxJsonBytes) throw new InvalidDataException("Fichier JSON trop volumineux");
            configJson = File.ReadAllText(path);
        }

        if (profileJson != null)
        {
            DeckProfile? profile;
            try { profile = JsonSerializer.Deserialize<DeckProfile>(profileJson, ConfigStore.Options); }
            catch (JsonException ex) { throw new InvalidDataException("profile.json invalide : " + ex.Message, ex); }
            if (profile == null) throw new InvalidDataException("Profil vide");
            var holder = new DeckConfig { Version = ConfigNormalizer.CurrentVersion, Profiles = { profile } };
            var norm = ConfigNormalizer.Normalize(holder);
            warnings.AddRange(norm.Warnings);
            profile = holder.Profiles[0];
            var risky = Validate(holder, warnings);
            CheckIcons(holder, icons, warnings);
            return new PackageContents(profile, null, risky, warnings, icons);
        }
        if (configJson != null)
        {
            var cfg = ConfigStore.Parse(configJson, out var norm);
            warnings.AddRange(norm.Warnings);
            var risky = Validate(cfg, warnings);
            CheckIcons(cfg, icons, warnings);
            return new PackageContents(null, cfg, risky, warnings, icons);
        }
        throw new InvalidDataException("Ce fichier ne contient ni profil ni configuration StreamDak");
    }

    /// <summary>Écrit les images du paquet dans le dossier Icons (après confirmation de l'utilisateur).</summary>
    public static void InstallIcons(PackageContents pkg)
    {
        foreach (var (name, data) in pkg.Icons)
        {
            var stored = IconStore.StoreBytes(data);
            if (stored != name) throw new InvalidDataException($"L'image {name} ne correspond pas à son contenu");
        }
    }

    static byte[] ReadBounded(ZipArchiveEntry e, long max, ref long total)
    {
        if (e.Length > max) throw new InvalidDataException($"Entrée trop volumineuse : {Truncate(e.FullName)}");
        using var s = e.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            total += n;
            if (ms.Length > max || total > MaxFileBytes) throw new InvalidDataException("Archive invalide : contenu plus grand que prévu");
        }
        return ms.ToArray();
    }

    /// <summary>Vérifie les bornes et les types d'actions ; retourne le nombre d'actions qui lancent du code.</summary>
    static int Validate(DeckConfig cfg, List<string> warnings)
    {
        if (cfg.Profiles.Count > MaxProfiles) throw new InvalidDataException($"Trop de profils (maximum {MaxProfiles})");
        int buttons = 0, risky = 0;
        foreach (var p in cfg.Profiles)
        {
            Check(p.Name, "nom de profil");
            if (p.Pages.Count > MaxPages) throw new InvalidDataException($"Trop de pages dans « {p.Name} » (maximum {MaxPages})");
            foreach (var page in p.Pages)
            {
                Check(page.Name, "nom de page");
                Check(page.Url ?? "", "adresse de page");
                foreach (var b in DeckDocument.AllButtons(page.Buttons))
                {
                    if (++buttons > MaxButtons) throw new InvalidDataException($"Trop de boutons (maximum {MaxButtons})");
                    Check(b.Label, "libellé"); Check(b.Icon, "icône");
                    foreach (var a in b.Actions)
                    {
                        if (!ActionValidator.KnownTypes.Contains(a.Type.ToLowerInvariant()))
                            throw new InvalidDataException($"Action de type inconnu : « {Truncate(a.Type)} » (bouton « {Truncate(b.Label)} »)");
                        Check(a.Value, "valeur d'action"); Check(a.Args ?? "", "arguments d'action");
                        if (ActionValidator.IsRisky(a)) risky++;
                    }
                    if (b.ImagePath != null && !IconStore.IsValidName(b.ImagePath))
                    {
                        warnings.Add($"Image invalide ignorée sur « {Truncate(b.Label)} »");
                        b.ImagePath = null;
                    }
                }
            }
        }
        return risky;
    }

    static void Check(string s, string what)
    {
        if (s.Length > MaxStringLength) throw new InvalidDataException($"Un {what} dépasse {MaxStringLength} caractères");
    }

    static void CheckIcons(DeckConfig cfg, Dictionary<string, byte[]> icons, List<string> warnings)
    {
        foreach (var name in IconNames(cfg.Profiles))
        {
            if (icons.ContainsKey(name)) continue;
            var file = IconStore.Resolve(name);
            if (file == null || !File.Exists(file)) warnings.Add($"Image manquante dans le fichier : {name}");
        }
    }

    static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
