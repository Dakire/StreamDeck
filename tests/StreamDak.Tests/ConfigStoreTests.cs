using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace StreamDak.Tests;

[Collection("config")]
public class ConfigStoreTests
{
    [Fact]
    public void DataDirectoryFollowsEnvironmentVariablesThenAppData()
    {
        var appData = Path.Combine(Path.GetTempPath(), "ad");
        Assert.Equal(Path.GetFullPath("a"), ConfigStore.DefaultDirectory("a", "b", appData));
        Assert.Equal(Path.GetFullPath("b"), ConfigStore.DefaultDirectory(null, "b", appData));   // ancienne variable TOUCHDECK_DATA_DIR
        Assert.Equal(Path.Combine(appData, "StreamDak"), ConfigStore.DefaultDirectory("", null, appData));
    }

    [Fact]
    public void OldProductFolderIsMovedOnceAndKeepsItsFiles()
    {
        var appData = Path.Combine(Path.GetTempPath(), "streamdak-ad-" + System.Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(appData, "TouchDeck");
        var current = Path.Combine(appData, "StreamDak");
        Directory.CreateDirectory(Path.Combine(legacy, "plugins"));
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");
        var previous = ConfigStore.Dir;
        try
        {
            ConfigStore.UseDirectory(current);
            Assert.True(ConfigStore.MigrateLegacyDirectory(appData));
            Assert.False(Directory.Exists(legacy));
            Assert.True(File.Exists(Path.Combine(current, "config.json")));
            Assert.True(Directory.Exists(Path.Combine(current, "plugins")));
            Assert.False(ConfigStore.MigrateLegacyDirectory(appData));   // déjà fait : rien à déplacer
        }
        finally
        {
            ConfigStore.UseDirectory(previous);
            try { Directory.Delete(appData, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void OldProductFolderIsLeftAloneWhenTheNewOneExistsOrADirectoryIsImposed()
    {
        var appData = Path.Combine(Path.GetTempPath(), "streamdak-ad-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(appData, "TouchDeck"));
        Directory.CreateDirectory(Path.Combine(appData, "StreamDak"));
        var previous = ConfigStore.Dir;
        try
        {
            ConfigStore.UseDirectory(Path.Combine(appData, "StreamDak"));
            Assert.False(ConfigStore.MigrateLegacyDirectory(appData));          // le nouveau dossier existe déjà
            Directory.Delete(Path.Combine(appData, "StreamDak"));
            ConfigStore.UseDirectory(Path.Combine(appData, "essai"));            // dossier imposé (variable d'environnement, tests)
            Assert.False(ConfigStore.MigrateLegacyDirectory(appData));
            Assert.True(Directory.Exists(Path.Combine(appData, "TouchDeck")));
        }
        finally
        {
            ConfigStore.UseDirectory(previous);
            try { Directory.Delete(appData, true); } catch (IOException) { }
        }
    }

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
    public void SavingUnchangedContentDoesNotRewriteTheFile()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        ConfigStore.Save(cfg);
        var stamp = new System.DateTime(2020, 1, 2, 3, 4, 5, System.DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ConfigStore.FilePath, stamp);

        ConfigStore.Save(cfg);                                                    // même contenu : aucune écriture
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(ConfigStore.FilePath));
        Assert.True(ConfigStore.IsOwnWrite());                                    // et le rechargement à chaud l'ignore toujours

        cfg.Spacing += 1;
        ConfigStore.Save(cfg);                                                    // contenu différent : écrit
        Assert.NotEqual(stamp, File.GetLastWriteTimeUtc(ConfigStore.FilePath));
        Assert.Equal(cfg.Spacing, ConfigStore.Load().Spacing);
    }

    [Fact]
    public void AnExternallyEditedFileIsStillOverwrittenByTheNextRealSave()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        ConfigStore.Save(cfg);
        File.WriteAllText(ConfigStore.FilePath, "{ pas du JSON");                 // éditeur externe : fichier invalide, refusé au rechargement
        ConfigStore.Save(cfg);                                                    // le contenu en mémoire reste la référence
        Assert.Equal(ConfigStore.Serialize(cfg), File.ReadAllText(ConfigStore.FilePath));
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
        // Rien ne se perd : les dossiers trop profonds sont vidés, leur contenu est remonté dans le dernier dossier autorisé
        Assert.Equal(20, DeckDocument.AllButtons(new[] { root }).Count(b => b.IsFolder));
        Assert.Contains(DeckDocument.AllButtons(new[] { root }), b => b.Label == "profond");
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
