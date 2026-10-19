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
    public const int MaxBackups = 15;

    /// <summary>Dossier de configuration : %APPDATA%\TouchDeck, ou le dossier de la variable TOUCHDECK_DATA_DIR (essais, installation portable).</summary>
    public static string Dir { get; private set; } = Environment.GetEnvironmentVariable("TOUCHDECK_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchDeck");
    public static string FilePath => Path.Combine(Dir, "config.json");
    public static string IconsDir => Path.Combine(Dir, "Icons");
    public static string BackupsDir => Path.Combine(Dir, "Backups");
    static string LegacyBackupPath => Path.Combine(Dir, "config.v1.backup.json");

    /// <summary>Change le dossier de configuration (tests).</summary>
    public static void UseDirectory(string dir) { Dir = dir; _lastWrittenHash = null; }

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    public sealed record LoadResult(DeckConfig Config, bool Migrated, IReadOnlyList<string> Warnings);

    static string? _lastWrittenHash;

    public static DeckConfig Load() => LoadWithInfo().Config;

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
            try { if (!File.Exists(LegacyBackupPath)) File.WriteAllText(LegacyBackupPath, text); } catch { /* sauvegarde best-effort */ }
            Save(cfg);
        }
        return new LoadResult(cfg, info.Migrated, info.Warnings);
    }

    /// <summary>Analyse un JSON de configuration (ancien ou nouveau format) et le normalise. Lève <see cref="InvalidDataException"/>.</summary>
    public static DeckConfig Parse(string json) => Parse(json, out _);

    public static DeckConfig Parse(string json, out NormalizeResult info)
    {
        DeckConfig? cfg;
        try { cfg = JsonSerializer.Deserialize<DeckConfig>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException("JSON invalide : " + ex.Message, ex); }
        if (cfg == null) throw new InvalidDataException("configuration vide");
        info = ConfigNormalizer.Normalize(cfg);
        return cfg;
    }

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
        catch { return false; }
    }

    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    // ---------- sauvegardes ----------

    public sealed record BackupInfo(string Path, DateTime Time, long Size)
    {
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
            foreach (var old in ListBackups().Skip(MaxBackups)) { try { File.Delete(old.Path); } catch { } }
            return path;
        }
        catch { return null; }
    }

    public static List<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(BackupsDir)) return new List<BackupInfo>();
        return new DirectoryInfo(BackupsDir).GetFiles("config-*.json")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupInfo(f.FullName, f.LastWriteTime, f.Length))
            .ToList();
    }

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
                            B("Play24", "Lecture / Pause", "#1E7F5F", "media", "playpause"),
                            B("Previous24", "Précédent", "#272B38", "media", "prev"),
                            B("Next24", "Suivant", "#272B38", "media", "next"),
                            B("SpeakerMute24", "Muet", "#B23A48", "media", "mute"),
                            B("Speaker124", "Volume -", "#272B38", "media", "voldown"),
                            B("Speaker224", "Volume +", "#272B38", "media", "volup"),
                            B("Desktop24", "Bureau", "#3466D6", "hotkey", "win+d"),
                            B("Cut24", "Capture", "#3466D6", "hotkey", "win+shift+s"),
                            B("DataBarVertical24", "Gestionnaire", "#3466D6", "hotkey", "ctrl+shift+esc"),
                            B("Wrench24", "Outils", "#6C3FD1", "page", "Outils"),
                        }
                    },
                    new()
                    {
                        Name = "Outils",
                        Buttons = new List<DeckButton>
                        {
                            B("Notepad24", "Bloc-notes", "#272B38", "launch", "notepad.exe"),
                            B("Calculator24", "Calculatrice", "#272B38", "launch", "calc.exe"),
                            B("Folder24", "Explorateur", "#272B38", "hotkey", "win+e"),
                            B("Globe24", "Google", "#3466D6", "url", "https://www.google.com"),
                            B("Keyboard24", "Texte", "#272B38", "text", "Bonjour !"),
                            B("Flash24", "Script PS", "#6C3FD1", "script", "Get-Date | Out-File \"$env:TEMP\\touchdeck.txt\"; notepad \"$env:TEMP\\touchdeck.txt\""),
                            B("Home24", "Retour", "#99620F", "page", "home"),
                        }
                    },
                }
            }
        }
    };
}
