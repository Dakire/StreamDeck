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
                    B("Home24", "Retour", "#B7791F", "page", "home"),
                }
            },
        }
    };
}
