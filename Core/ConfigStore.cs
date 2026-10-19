using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TouchDeck;

/// <summary>Lecture et écriture de config.json : migration, écriture atomique, sauvegardes automatiques.</summary>
public static class ConfigStore
{
    /// <summary>Nombre de sauvegardes conservées ; les plus anciennes sont supprimées.</summary>
    public const int MaxBackups = 15;

    /// <summary>Dossier de configuration : %APPDATA%\TouchDeck, ou le dossier de la variable TOUCHDECK_DATA_DIR (essais, installation portable).</summary>
    public static string Dir { get; private set; } = Environment.GetEnvironmentVariable("TOUCHDECK_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchDeck");
    /// <summary>Chemin de config.json.</summary>
    public static string FilePath => Path.Combine(Dir, "config.json");
    /// <summary>Dossier des images importées (noms de hachage, voir IconStore).</summary>
    public static string IconsDir => Path.Combine(Dir, "Icons");
    /// <summary>Dossier des sauvegardes automatiques.</summary>
    public static string BackupsDir => Path.Combine(Dir, "Backups");
    static string LegacyBackupPath => Path.Combine(Dir, "config.v1.backup.json");

    /// <summary>Change le dossier de configuration (tests).</summary>
    public static void UseDirectory(string dir) { Dir = dir; _lastWrittenHash = null; }

    /// <summary>Options JSON du format : indentation, clés tolérées sans distinction de casse, valeurs par défaut omises (voir les JsonIgnore de Models.cs).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    /// <summary>Résultat du chargement de config.json.</summary>
    /// <param name="Config">Configuration normalisée.</param>
    /// <param name="Migrated">true si l'ancien format vient d'être converti (et réécrit).</param>
    /// <param name="Warnings">Réparations effectuées, à signaler à l'utilisateur.</param>
    public sealed record LoadResult(DeckConfig Config, bool Migrated, IReadOnlyList<string> Warnings);

    static string? _lastWrittenHash;

    /// <summary>Charge config.json (créé avec <see cref="DefaultConfig"/> s'il n'existe pas). Lève <see cref="System.IO.InvalidDataException"/> si le fichier est invalide.</summary>
    public static DeckConfig Load() => LoadWithInfo().Config;

    /// <summary>Comme <see cref="Load"/>, en indiquant aussi la migration et les réparations.</summary>
    public static LoadResult LoadWithInfo()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath))
        {
            var def = DefaultConfig();
            Save(def);
            return new LoadResult(def, false, Array.Empty<string>());
        }
        var text = File.ReadAllText(FilePath);
        var cfg = Parse(text, out var info);
        if (info.Migrated)
        {
            // Première lecture d'un ancien fichier : copie intacte avant toute réécriture
            // Au mieux : la migration continue même si la copie échoue, mais l'échec est consigné
            try { if (!File.Exists(LegacyBackupPath)) File.WriteAllText(LegacyBackupPath, text); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("Copie de l'ancien fichier impossible avant migration", ex); }
            Save(cfg);
        }
        return new LoadResult(cfg, info.Migrated, info.Warnings);
    }

    /// <summary>Analyse un JSON de configuration (ancien ou nouveau format) et le normalise. Lève <see cref="InvalidDataException"/>.</summary>
    public static DeckConfig Parse(string json) => Parse(json, out _);

    /// <summary>Analyse un JSON de configuration et le normalise. Lève <see cref="System.IO.InvalidDataException"/>.</summary>
    /// <param name="json">Contenu du fichier (ancien ou nouveau format).</param>
    /// <param name="info">Migration et réparations effectuées.</param>
    public static DeckConfig Parse(string json, out NormalizeResult info)
    {
        DeckConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<DeckConfig>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException("JSON invalide : " + ex.Message, ex); }
        if (cfg == null) throw new InvalidDataException("configuration vide");
        info = ConfigNormalizer.Normalize(cfg);
        return cfg;
    }

    /// <summary>JSON du format config.json (mêmes options que l'écriture sur disque).</summary>
    public static string Serialize(DeckConfig cfg) => JsonSerializer.Serialize(cfg, Options);

    /// <summary>Écrit dans un fichier temporaire puis le substitue : un arrêt en cours d'écriture ne corrompt jamais config.json.</summary>
    public static void Save(DeckConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        var json = Serialize(cfg);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        _lastWrittenHash = Hash(json);
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
        else File.Move(tmp, FilePath);
    }

    /// <summary>true si le fichier sur disque est exactement ce que nous venons d'écrire (le rechargement à chaud l'ignore).</summary>
    public static bool IsOwnWrite()
    {
        try { return _lastWrittenHash != null && Hash(File.ReadAllText(FilePath)) == _lastWrittenHash; }
        // Fichier en cours de remplacement ou verrouillé par l'éditeur externe : on le relira (ce n'est pas une erreur)
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>Copie profonde par sérialisation JSON : la copie ne partage aucune liste avec l'original (annuler, presse-papiers).</summary>
    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    // ---------- sauvegardes ----------

    /// <summary>Fichier de sauvegarde du dossier Backups.</summary>
    /// <param name="Path">Chemin complet.</param>
    /// <param name="Time">Date de dernière écriture.</param>
    /// <param name="Size">Taille en octets.</param>
    public sealed record BackupInfo(string Path, DateTime Time, long Size)
    {
        /// <summary>Nom du fichier, affiché dans les paramètres.</summary>
        public string Name => System.IO.Path.GetFileName(Path);
    }

    /// <summary>Copie la configuration courante dans Backups (sauf si identique à la dernière). Retourne le chemin ou null.</summary>
    public static string? CreateBackup(DeckConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(BackupsDir);
            var json = Serialize(cfg);
            var last = ListBackups().FirstOrDefault();
            if (last != null && Hash(File.ReadAllText(last.Path)) == Hash(json)) return null;
            string path;
            int n = 0;
            do { path = Path.Combine(BackupsDir, $"config-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{n++:00}.json"); }
            while (File.Exists(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));
            foreach (var old in ListBackups().Skip(MaxBackups))
            {
                try { File.Delete(old.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Ancienne sauvegarde non supprimée ({old.Name}) : {ex.Message}"); }
            }
            return path;
        }
        // La sauvegarde ne doit jamais empêcher l'opération qu'elle précède (import, démarrage) : on consigne et on continue
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("Sauvegarde de la configuration impossible", ex); return null; }
    }

    /// <summary>Sauvegardes, de la plus récente à la plus ancienne.</summary>
    public static List<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(BackupsDir)) return new List<BackupInfo>();
        return new DirectoryInfo(BackupsDir).GetFiles("config-*.json")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupInfo(f.FullName, f.LastWriteTime, f.Length))
            .ToList();
    }

    /// <summary>Lit une sauvegarde ; refuse tout fichier situé hors du dossier Backups (le chemin peut venir de l'interface).</summary>
    public static DeckConfig LoadBackup(string path)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(BackupsDir), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sauvegarde hors du dossier prévu");
        return Parse(File.ReadAllText(path));
    }

    // ---------- configuration par défaut ----------

    static DeckButton B(string icon, string label, string color, string type, string value, string? args = null) => new()
    {
        Icon = icon, Label = label, Color = color,
        Actions = new List<DeckAction> { new() { Type = type, Value = value, Args = args } },
    };

    /// <summary>Configuration du premier lancement : un profil « Principal » avec une page de raccourcis et une page masquée.</summary>
    public static DeckConfig DefaultConfig() => new()
    {
        Version = ConfigNormalizer.CurrentVersion,
        Theme = "dark",
        ActiveProfile = "Principal",
        Profiles = new List<DeckProfile>
        {
            new()
            {
                Name = "Principal",
                Pages = new List<DeckPage>
                {
                    new()
                    {
                        Name = "Média",
                        Buttons = new List<DeckButton>
                        {
                            B("Play24", "Lecture / Pause", "#047857", ActionTypes.Media, "playpause"),
                            B("Previous24", "Précédent", "#334155", ActionTypes.Media, "prev"),
                            B("Next24", "Suivant", "#334155", ActionTypes.Media, "next"),
                            B("SpeakerMute24", "Muet", "#BE123C", ActionTypes.Media, "mute"),
                            B("Speaker124", "Volume -", "#334155", ActionTypes.Media, "voldown"),
                            B("Speaker224", "Volume +", "#334155", ActionTypes.Media, "volup"),
                            B("Desktop24", "Bureau", "#2563EB", ActionTypes.Hotkey, "win+d"),
                            B("Cut24", "Capture", "#2563EB", ActionTypes.Hotkey, "win+shift+s"),
                            B("DataBarVertical24", "Gestionnaire", "#2563EB", ActionTypes.Hotkey, "ctrl+shift+esc"),
                            B("Wrench24", "Outils", "#7C3AED", ActionTypes.Page, "Outils"),
                        }
                    },
                    new()
                    {
                        Name = "Outils",
                        Buttons = new List<DeckButton>
                        {
                            B("Notepad24", "Bloc-notes", "#334155", ActionTypes.Launch, "notepad.exe"),
                            B("Calculator24", "Calculatrice", "#334155", ActionTypes.Launch, "calc.exe"),
                            B("Folder24", "Explorateur", "#334155", ActionTypes.Hotkey, "win+e"),
                            B("Globe24", "Google", "#2563EB", ActionTypes.Url, "https://www.google.com"),
                            B("Keyboard24", "Texte", "#334155", ActionTypes.Text, "Bonjour !"),
                            B("Flash24", "Script PS", "#7C3AED", ActionTypes.Script, "Get-Date | Out-File \"$env:TEMP\\touchdeck.txt\"; notepad \"$env:TEMP\\touchdeck.txt\""),
                            B("Home24", "Retour", "#B45309", ActionTypes.Page, PageTargets.Home),
                        }
                    },
                }
            }
        }
    };
}
