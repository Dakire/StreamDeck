using System.IO;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Audit 3 : une configuration corrompue reprend la dernière sauvegarde valide au lieu d'un deck vide.</summary>
public class StartupRecoveryTests
{
    [Fact]
    public void InvalidConfigRestoresTheLatestValidBackup()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Name = "Ancien"; ConfigStore.CreateBackup(cfg);
        cfg.Profiles[0].Name = "Récent"; ConfigStore.CreateBackup(cfg);
        File.WriteAllText(ConfigStore.FilePath, "{ pas du JSON");

        var result = StartupLoader.Load();

        Assert.Equal("Récent", result.Config.Profiles[0].Name);
        Assert.Contains("sauvegarde", result.Message!);
        Assert.Single(Directory.GetFiles(ConfigStore.Dir, "config.invalide-*.json"));   // le fichier abîmé reste lisible
    }

    [Fact]
    public void CorruptNewestBackupFallsBackToTheNextOne()
    {
        using var dir = new TempConfigDir();
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Name = "Bon"; ConfigStore.CreateBackup(cfg);
        cfg.Profiles[0].Name = "Autre"; ConfigStore.CreateBackup(cfg);
        File.WriteAllText(ConfigStore.ListBackups()[0].Path, "{ abîmé");
        File.WriteAllText(ConfigStore.FilePath, "{ pas du JSON");

        Assert.Equal("Bon", StartupLoader.Load().Config.Profiles[0].Name);
    }

    [Fact]
    public void WithoutAnyBackupTheDefaultConfigIsUsed()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, "{ pas du JSON");
        var result = StartupLoader.Load();
        Assert.NotEmpty(result.Config.Profiles);
        Assert.StartsWith("Config invalide", result.Message!);
    }

    [Fact]
    public void RepeatedInvalidStartsDoNotPileUpIdenticalCopies()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(ConfigStore.FilePath, "{ pas du JSON");
        for (int i = 0; i < 4; i++) StartupLoader.Load();
        Assert.Single(Directory.GetFiles(ConfigStore.Dir, "config.invalide-*.json"));

        for (int i = 0; i < StartupLoader.MaxInvalidCopies + 3; i++)
        {
            File.WriteAllText(ConfigStore.FilePath, "{ cassé " + i);
            StartupLoader.Load();
            System.Threading.Thread.Sleep(1100);   // le nom de la copie est à la seconde près
        }
        Assert.True(Directory.GetFiles(ConfigStore.Dir, "config.invalide-*.json").Length <= StartupLoader.MaxInvalidCopies);
    }
}
