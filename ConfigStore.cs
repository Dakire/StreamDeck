using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TouchDeck;

public static class ConfigStore
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchDeck");
    public static readonly string FilePath = Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static DeckConfig Load()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath))
        {
            var def = Default();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(def, Options));
            return def;
        }
        var cfg = JsonSerializer.Deserialize<DeckConfig>(File.ReadAllText(FilePath), Options)
                  ?? throw new InvalidDataException("config vide");
        if (cfg.Pages.Count == 0) throw new InvalidDataException("aucune page définie");
        return cfg;
    }

    public static DeckConfig DefaultConfig() => Default();

    public static void Save(DeckConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(cfg, Options));
    }

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    static DeckButton B(string icon, string label, string color, string type, string value, string? args = null) => new()
    {
        Icon = icon, Label = label, Color = color,
        Actions = new List<DeckAction> { new() { Type = type, Value = value, Args = args } },
    };

    static DeckConfig Default() => new()
    {
        Pages = new List<DeckPage>
        {
            new()
            {
                Name = "Média",
                Buttons = new List<DeckButton>
                {
                    B("⏯️", "Lecture / Pause", "#1E7F5F", "media", "playpause"),
                    B("⏮️", "Précédent", "#272B38", "media", "prev"),
                    B("⏭️", "Suivant", "#272B38", "media", "next"),
                    B("🔇", "Muet", "#B23A48", "media", "mute"),
                    B("🔉", "Volume -", "#272B38", "media", "voldown"),
                    B("🔊", "Volume +", "#272B38", "media", "volup"),
                    B("🖥️", "Bureau", "#3466D6", "hotkey", "win+d"),
                    B("✂️", "Capture", "#3466D6", "hotkey", "win+shift+s"),
                    B("📊", "Gestionnaire", "#3466D6", "hotkey", "ctrl+shift+esc"),
                    B("🛠️", "Outils", "#6C3FD1", "page", "Outils"),
                }
            },
            new()
            {
                Name = "Outils",
                Buttons = new List<DeckButton>
                {
                    B("📝", "Bloc-notes", "#272B38", "launch", "notepad.exe"),
                    B("🧮", "Calculatrice", "#272B38", "launch", "calc.exe"),
                    B("📁", "Explorateur", "#272B38", "hotkey", "win+e"),
                    B("🌐", "Google", "#3466D6", "url", "https://www.google.com"),
                    B("⌨️", "Texte", "#272B38", "text", "Bonjour !"),
                    B("⚡", "Script PS", "#6C3FD1", "script", "Get-Date | Out-File \"$env:TEMP\\touchdeck.txt\"; notepad \"$env:TEMP\\touchdeck.txt\""),
                    B("🏠", "Retour", "#B7791F", "page", "home"),
                }
            },
        }
    };
}
