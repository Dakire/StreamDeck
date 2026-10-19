using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TouchDeck.Tests;

[Collection("config")]
public class ConfigStoreTests
{
    [Fact]
    public void LegacyConfigIsMigratedToOneProfileWithoutLosingButtons()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, Samples.LegacyJson);

        var result = ConfigStore.LoadWithInfo();

        Assert.True(result.Migrated);
        var cfg = result.Config;
        Assert.Equal(ConfigNormalizer.CurrentVersion, cfg.Version);
        Assert.Single(cfg.Profiles);
        Assert.Equal(cfg.Profiles[0].Name, cfg.ActiveProfile);
        Assert.Equal(new[] { "Média", "Outils" }, cfg.Profiles[0].Pages.Select(p => p.Name));
        Assert.True(cfg.Profiles[0].Pages[1].Hidden);
        Assert.Equal(2, cfg.Profiles[0].Pages[0].Buttons.Count);
        Assert.Equal("light", cfg.Theme);          // les réglages existants sont conservés
        Assert.Null(cfg.Pages);
    }

    [Fact]
    public void MigrationKeepsAnUntouchedCopyOfTheOldFile()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, Samples.LegacyJson);
        ConfigStore.Load();
        Assert.Equal(Samples.LegacyJson, File.ReadAllText(Path.Combine(dir.Path, "config.v1.backup.json")));
        // le fichier réécrit est au nouveau format et ne contient plus la clé « Pages » à la racine
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigStore.FilePath));
        Assert.False(doc.RootElement.TryGetProperty("Pages", out _));
        Assert.Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void SecondLoadDoesNotMigrateAgain()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, Samples.LegacyJson);
        ConfigStore.Load();
        Assert.False(ConfigStore.LoadWithInfo().Migrated);
    }

    [Fact]
    public void DefaultConfigIsCreatedOnFirstRunAndIsValid()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.Load();
        Assert.True(File.Exists(ConfigStore.FilePath));
        Assert.Equal("dark", cfg.Theme);
        Assert.NotEmpty(cfg.Profiles[0].Pages);
        foreach (var b in DeckDocument.AllButtons(cfg.Profiles[0].Pages.SelectMany(p => p.Buttons)))
            Assert.DoesNotContain(ActionValidator.Validate(b, cfg, cfg.Profiles[0]), i => i.Severity == Severity.Error);
    }

    [Fact]
    public void NewFieldsRoundTripAndDefaultsAreNotWritten()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        var folder = DeckDocument.NewFolder("Jeux");
        folder.Children!.Add(Samples.Btn("Dedans"));
        cfg.Profiles[0].Pages[0].Buttons.Add(new DeckButton { Blank = true });
        cfg.Profiles[0].Pages[0].Buttons.Add(folder);
        cfg.Profiles[0].Pages[0].Buttons.Add(new DeckButton { Label = "T", HideTitle = true, TitlePosition = "top", TextColor = "#FF0000", ImageFit = "contain" });
        ConfigStore.Save(cfg);

        var text = File.ReadAllText(ConfigStore.FilePath);
        Assert.DoesNotContain("\"Blank\": false", text);
        Assert.DoesNotContain("\"IsFolder\": false", text);
        Assert.DoesNotContain("\"ImagePath\"", text);

        var back = ConfigStore.Load();
        var buttons = back.Profiles[0].Pages[0].Buttons;
        Assert.True(buttons[^3].Blank);
        Assert.True(buttons[^2].IsFolder);
        Assert.Equal("Dedans", buttons[^2].Children![0].Label);
        Assert.True(buttons[^1].HideTitle);
        Assert.Equal("top", buttons[^1].TitlePosition);
    }

    [Fact]
    public void InvalidJsonRaisesInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => ConfigStore.Parse("{ not json"));
        Assert.Throws<InvalidDataException>(() => ConfigStore.Parse("null"));
    }

    [Fact]
    public void UnknownKeysAreIgnoredAndMissingListsRepaired()
    {
        var cfg = ConfigStore.Parse("""{ "Version": 2, "Futur": 1, "Profiles": [ { "Name": "X", "Pages": null }, { "Name": "x" }, { "Name": "" } ] }""");
        Assert.Equal(new[] { "X", "x (2)", "Profil 3" }, cfg.Profiles.Select(p => p.Name));
        Assert.All(cfg.Profiles, p => Assert.NotEmpty(p.Pages));
        Assert.Equal("X", cfg.ActiveProfile);
    }

    [Fact]
    public void BoundsAreClampedAndDuplicatePageNamesRenamed()
    {
        var cfg = ConfigStore.Parse("""{ "Version": 2, "Columns": 999, "Rows": -4, "Spacing": 1e9, "Theme": "rose", "Profiles": [ { "Name": "P", "Pages": [ { "Name": "A" }, { "Name": "a" } ] } ] }""");
        Assert.Equal(20, cfg.Columns);
        Assert.Equal(0, cfg.Rows);
        Assert.Equal(80, cfg.Spacing);
        Assert.Equal("system", cfg.Theme);
        Assert.Equal(new[] { "A", "a (2)" }, cfg.Profiles[0].Pages.Select(p => p.Name));
    }

    [Fact]
    public void FolderNestingIsBounded()
    {
        var root = DeckDocument.NewFolder("0");
        var cur = root;
        for (int i = 1; i < 20; i++) { var next = DeckDocument.NewFolder(i.ToString()); cur.Children!.Add(next); cur = next; }
        cur.Children!.Add(Samples.Btn("profond"));
        var warnings = new System.Collections.Generic.List<string>();
        ConfigNormalizer.NormalizeButtons(new() { root }, 0, warnings);
        int depth = 0;
        for (var c = root; c.Children!.Count > 0; c = c.Children[0]) depth++;
        Assert.True(depth <= ConfigNormalizer.MaxFolderDepth);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void SaveIsAtomicAndOwnWritesAreRecognised()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        ConfigStore.Save(cfg);
        Assert.False(File.Exists(ConfigStore.FilePath + ".tmp"));
        Assert.True(ConfigStore.IsOwnWrite());

        File.AppendAllText(ConfigStore.FilePath, " ");          // modification externe
        Assert.False(ConfigStore.IsOwnWrite());
    }

    [Fact]
    public void BackupsAreCreatedOnlyOnChangeAndPruned()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        Assert.NotNull(ConfigStore.CreateBackup(cfg));
        Assert.Null(ConfigStore.CreateBackup(cfg));              // identique : pas de doublon
        for (int i = 0; i < ConfigStore.MaxBackups + 5; i++)
        {
            cfg.Profiles[0].Name = "P" + i;
            ConfigStore.CreateBackup(cfg);
        }
        Assert.Equal(ConfigStore.MaxBackups, ConfigStore.ListBackups().Count);
        var latest = ConfigStore.ListBackups()[0];
        Assert.Equal("P" + (ConfigStore.MaxBackups + 4), ConfigStore.LoadBackup(latest.Path).Profiles[0].Name);
    }

    [Fact]
    public void BackupLoadingRefusesFilesOutsideTheBackupFolder()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, Samples.LegacyJson);
        Assert.Throws<InvalidDataException>(() => ConfigStore.LoadBackup(ConfigStore.FilePath));
    }
}
