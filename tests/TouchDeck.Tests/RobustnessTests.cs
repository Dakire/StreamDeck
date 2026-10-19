using System.IO;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Les erreurs attendues (saisie invalide, fichier abîmé) ne font pas tomber l'application et sont consignées.</summary>
[Collection("config")]
public class RobustnessTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("rouge vif")]
    [InlineData("#")]
    public void InvalidColorsAreRefusedWithoutThrowing(string hex)
    {
        Assert.False(Theme.TryBrush(hex, out _));
    }

    [Theory]
    [InlineData("#2563EB")]
    [InlineData("  #FFF ")]
    [InlineData("#802563EB")]
    public void ValidColorsAreAccepted(string hex)
    {
        Assert.True(Theme.TryBrush(hex, out var brush));
        Assert.True(brush.IsFrozen);
    }

    [Fact]
    public void ACorruptedButtonImageIsSkippedAndLogged()
    {
        using var dir = new TempConfigDir();
        using var log = new MemoryLogSink();
        Directory.CreateDirectory(ConfigStore.IconsDir);
        var name = new string('a', 32) + ".png";   // nom de hachage valide, contenu illisible
        File.WriteAllBytes(Path.Combine(ConfigStore.IconsDir, name), new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(IconStore.Load(name));
        Assert.True(log.Contains("Image de bouton illisible"));
    }

    [Fact]
    public void ABackupFailureIsLoggedAndDoesNotThrow()
    {
        using var dir = new TempConfigDir();
        using var log = new MemoryLogSink();
        // Un fichier à la place du dossier Backups : la sauvegarde ne peut pas s'écrire
        File.WriteAllText(ConfigStore.BackupsDir, "pas un dossier");
        Assert.Null(ConfigStore.CreateBackup(ConfigStore.DefaultConfig()));
        Assert.True(log.Contains("Sauvegarde de la configuration impossible"));
    }
}

/// <summary>Les identifiants d'action sont des clés de config.json : leur valeur ne doit jamais changer.</summary>
public class FormatKeysTests
{
    [Fact]
    public void ActionTypeIdentifiersAreStableConfigKeys()
    {
        Assert.Equal(new[] { "hotkey", "text", "media", "launch", "script", "url", "page", "profile", "delay", "focus", "http", "audio", "timer" },
            ActionTypes.All);
        Assert.Equal(new[] { "mic-toggle", "mic-mute", "mic-unmute", "out-toggle", "volume" }, AudioCommands.All);
        Assert.Equal(("home", "back"), (PageTargets.Home, PageTargets.Back));
    }
}
