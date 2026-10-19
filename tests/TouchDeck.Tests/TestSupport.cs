using System;
using System.IO;
using Xunit;

// ConfigStore.Dir est global au processus : une collection qui en change (TempConfigDir) ne doit jamais tourner pendant que
// l'hôte WPF sauvegarde (sinon des sauvegardes de l'interface atterrissent dans le dossier d'un autre test).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TouchDeck.Tests;

/// <summary>Les tests qui touchent au dossier de configuration partagent un répertoire temporaire et ne tournent pas en parallèle.</summary>
[CollectionDefinition("config", DisableParallelization = true)]
public class ConfigCollection { }

public sealed class TempConfigDir : IDisposable
{
    public string Path { get; }
    readonly string _previous;

    public TempConfigDir()
    {
        _previous = ConfigStore.Dir;
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "touchdeck-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        ConfigStore.UseDirectory(Path);
        IconStore.ClearCache();
    }

    public void Dispose()
    {
        ConfigStore.UseDirectory(_previous);
        IconStore.ClearCache();
        try { Directory.Delete(Path, true); } catch { }
    }
}

static class Samples
{
    /// <summary>Ancien format (version 1) tel qu'écrit par les versions précédentes.</summary>
    public const string LegacyJson = """
    {
      "Monitor": -1, "Fullscreen": true, "AlwaysOnTop": false, "Columns": 0, "Rows": 0, "Spacing": 14,
      "Background": "#0E1015", "Theme": "light", "IconStyle": "fluent",
      "Pages": [
        { "Name": "Média", "Hidden": false, "Buttons": [
            { "Label": "Lecture", "Icon": "Play24", "Color": "#1E7F5F", "Actions": [ { "Type": "media", "Value": "playpause", "Args": null } ] },
            { "Label": "Outils", "Icon": "Wrench24", "Color": "#6C3FD1", "Actions": [ { "Type": "page", "Value": "Outils" } ] } ] },
        { "Name": "Outils", "Hidden": true, "Buttons": [
            { "Label": "Bloc-notes", "Icon": "Notepad24", "Color": "#272B38", "Actions": [ { "Type": "launch", "Value": "notepad.exe" } ] } ] }
      ]
    }
    """;

    public static DeckButton Btn(string label, string type = "hotkey", string value = "ctrl+c") => new()
    {
        Label = label, Icon = "Star24", Actions = { new DeckAction { Type = type, Value = value } },
    };

    public static DeckDocument Doc(params string[] labels)
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P",
            Profiles = { new DeckProfile { Name = "P", Pages = { new DeckPage { Name = "A" }, new DeckPage { Name = "B" } } } },
        };
        foreach (var l in labels) cfg.Profiles[0].Pages[0].Buttons.Add(Btn(l));
        return new DeckDocument(cfg);
    }

    public static Place Root(int page = 0) => new("P", page);
}
