using System;
using System.Collections.Generic;
using StreamDak.Integrations;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Durcissement : lancement de scripts sans injection, journal sans secret, secrets déplacés hors de config.json.</summary>
public class SecurityTests
{
    // ---------- ScriptCommand ----------

    [Fact]
    public void AScriptFileIsPassedToPowerShellAsASingleFileArgument()
    {
        var launch = ScriptCommand.Build(@"C:\Outils\mon script.ps1", "-Nom \"Jean Dupont\" 42");
        Assert.Equal("powershell.exe", launch.FileName);
        Assert.Equal(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", @"C:\Outils\mon script.ps1", "-Nom", "Jean Dupont", "42" }, launch.Arguments);
    }

    [Fact]
    public void AQuoteInAScriptPathCannotBreakOutOfTheArgument()
    {
        // L'ancien code l'insérait dans « -File \"…\" » : un guillemet permettait d'ajouter des instructions
        var launch = ScriptCommand.Build("x\" ; calc ; \".ps1", null);
        Assert.Equal("x\" ; calc ; \".ps1", launch.Arguments[^1]);
        Assert.Equal(6, launch.Arguments.Count);
    }

    [Theory]
    [InlineData("& calc")]
    [InlineData("a | b")]
    [InlineData("a > c:\\x.txt")]
    [InlineData("%COMSPEC%")]
    [InlineData("^&calc")]
    public void BatchFilesRefuseShellMetaCharactersInArguments(string args)
    {
        var ex = Assert.Throws<ArgumentException>(() => ScriptCommand.Build(@"C:\a\run.bat", args));
        Assert.Contains("cmd.exe", ex.Message);
    }

    [Fact]
    public void ABatchFileIsStartedDirectlyWithSeparateArguments()
    {
        var launch = ScriptCommand.Build(@"C:\a\run.cmd", "one \"two words\"");
        Assert.Equal(@"C:\a\run.cmd", launch.FileName);
        Assert.Equal(new[] { "one", "two words" }, launch.Arguments);
        Assert.Throws<ArgumentException>(() => ScriptCommand.Build(@"C:\a & b\run.bat", null));
    }

    [Fact]
    public void AnInlineCommandIsOneArgumentAndTakesNoExtraArguments()
    {
        var launch = ScriptCommand.Build("Get-Process | Sort CPU -desc | Select -First 3", null);
        Assert.Equal(new[] { "-NoProfile", "-NonInteractive", "-Command", "Get-Process | Sort CPU -desc | Select -First 3" }, launch.Arguments);
        Assert.Throws<ArgumentException>(() => ScriptCommand.Build("Get-Date", "; calc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyScriptIsRefused(string? value) => Assert.Throws<ArgumentException>(() => ScriptCommand.Build(value, null));

    [Fact]
    public void ControlCharactersAndHugeValuesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => ScriptCommand.Build("Get-Date\0calc", null));
        Assert.Throws<ArgumentException>(() => ScriptCommand.Build(new string('a', ScriptCommand.MaxLength + 1), null));
        Assert.Throws<ArgumentException>(() => ScriptCommand.Build("a\r\nb.ps1", null));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a b", 2)]
    [InlineData("  a   b  ", 2)]
    [InlineData("\"a b\" c", 2)]
    [InlineData("\"\"", 1)]
    public void ArgumentsAreSplitOnSpacesAndQuotes(string text, int count) => Assert.Equal(count, ScriptCommand.SplitArguments(text).Count);

    // ---------- LogRedactor ----------

    [Theory]
    [InlineData("GET https://api.exemple.fr/v1?access_token=abc123&x=1", "abc123")]
    [InlineData("https://hooks.exemple.fr/run?key=SuperCle42", "SuperCle42")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("connexion à https://admin:hunter2@192.168.1.5/", "hunter2")]
    [InlineData("password=hunter2 puis suite", "hunter2")]
    [InlineData("{\"token\": \"ya29.a0AfH6\", \"x\": 1}", "ya29.a0AfH6")]
    [InlineData("client_secret: GOCSPX-abcdef", "GOCSPX-abcdef")]
    public void SecretsAreMaskedInLogText(string line, string secret)
    {
        var redacted = LogRedactor.Redact(line);
        Assert.DoesNotContain(secret, redacted);
        Assert.Contains(LogRedactor.Mask, redacted);
    }

    [Fact]
    public void OrdinaryLogLinesAreLeftAlone()
    {
        const string line = "Bouton « Copier » (étape 2) : échec — Le serveur a répondu 500 sur https://exemple.fr/page?id=3";
        Assert.Equal(line, LogRedactor.Redact(line));
        Assert.Equal("", LogRedactor.Redact(null));
    }

    // ---------- secrets en clair dans config.json ----------

    [Fact]
    public void ASecretFoundInClearInTheConfigurationMovesToTheEncryptedStore()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        doc.UpdateSettings(c => c.Integrations = new()
        {
            ["fake"] = new IntegrationConfig { Enabled = false, Values = new() { ["host"] = "pc", ["password"] = "hunter2" } },
        });
        var secrets = new MemorySecrets();
        var manager = new IntegrationManager(new IIntegration[] { new FakeIntegration() }, doc, secrets);

        Assert.Equal(1, manager.MigratePlaintextSecrets());

        Assert.Equal("hunter2", secrets.Get(IntegrationManager.SecretKey("fake", "password")));
        var values = doc.Config.Integrations!["fake"].Values!;
        Assert.False(values.ContainsKey("password"));
        Assert.Equal("pc", values["host"]);                       // les réglages ordinaires restent
        Assert.DoesNotContain("hunter2", ConfigStore.Serialize(doc.Config));
        Assert.Equal(0, manager.MigratePlaintextSecrets());       // rien à refaire
    }

    [Fact]
    public void AnExistingEncryptedSecretIsNeverOverwrittenByTheClearOne()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        doc.UpdateSettings(c => c.Integrations = new() { ["fake"] = new IntegrationConfig { Values = new() { ["password"] = "ancien" } } });
        var secrets = new MemorySecrets();
        secrets.Set(IntegrationManager.SecretKey("fake", "password"), "recent");
        new IntegrationManager(new IIntegration[] { new FakeIntegration() }, doc, secrets).MigratePlaintextSecrets();

        Assert.Equal("recent", secrets.Get(IntegrationManager.SecretKey("fake", "password")));
        Assert.Null(doc.Config.Integrations!["fake"].Values);     // le clair a disparu, dictionnaire vide retiré
    }

    [Fact]
    public void ExportedConfigurationNeverCarriesStoredSecrets()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Integrations = new Dictionary<string, IntegrationConfig> { ["fake"] = new() { Enabled = true, Values = new() { ["host"] = "pc" } } };
        Assert.DoesNotContain("password", ConfigStore.Serialize(cfg), StringComparison.OrdinalIgnoreCase);
    }
}
