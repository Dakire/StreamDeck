using System;
using System.IO;
using System.Linq;
using StreamDak.Integrations;
using StreamDak.Integrations.ElgatoLight;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Chargement des plugins : contexte isolé, contrat partagé, plugins défectueux ignorés sans bloquer les autres.</summary>
public class PluginLoaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "streamdak-plugins-" + Guid.NewGuid().ToString("N"));

    public PluginLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fichier encore mappé par le contexte de chargement : le dossier temporaire sera nettoyé plus tard */ }
        GC.SuppressFinalize(this);
    }

    // Copie une des bibliothèques d'intégration de StreamDak comme si c'était un plugin posé par l'utilisateur
    string InstallSampleCopy(string name)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        var source = typeof(ElgatoLightIntegration).Assembly.Location;
        var target = Path.Combine(folder, name + ".dll");
        File.Copy(source, target);
        return target;
    }

    [Fact]
    public void ALibraryWithAnIntegrationIsLoadedInItsOwnContextAndSharesTheCoreContract()
    {
        var dll = InstallSampleCopy("MonPlugin");

        var plugins = PluginLoader.Load(_dir);

        var plugin = Assert.Single(plugins);
        Assert.Equal("MonPlugin", plugin.Name);
        Assert.Equal(dll, plugin.Path);
        Assert.Equal(64, plugin.Sha256.Length);
        var type = Assert.Single(plugin.Integrations);
        Assert.NotSame(typeof(ElgatoLightIntegration), type);                       // chargé dans un autre contexte
        Assert.True(typeof(IIntegration).IsAssignableFrom(type));            // mais le contrat Core est le même
        using var instance = (IDisposable)Activator.CreateInstance(type)!;
        Assert.Equal("elgato", ((IIntegration)instance).Id);
    }

    [Fact]
    public void ABrokenOrEmptyPluginNeverStopsTheOthers()
    {
        InstallSampleCopy("Bon");
        Directory.CreateDirectory(Path.Combine(_dir, "SansDll"));                                  // dossier sans bibliothèque
        Directory.CreateDirectory(Path.Combine(_dir, "Corrompu"));
        File.WriteAllText(Path.Combine(_dir, "Corrompu", "Corrompu.dll"), "ceci n'est pas une bibliothèque .NET");
        var vide = Path.Combine(_dir, "StreamDak.Core");                                           // bibliothèque valide sans intégration concrète
        Directory.CreateDirectory(vide);
        File.Copy(typeof(IIntegration).Assembly.Location, Path.Combine(vide, "StreamDak.Core.dll"));

        var plugins = PluginLoader.Load(_dir);

        Assert.Equal(new[] { "Bon" }, plugins.Select(p => p.Name));
    }

    [Fact]
    public void FirstUseTrustsPluginsAlreadyThereThenRefusesNewOnesAndChangedOnes()
    {
        InstallSampleCopy("Ancien");
        var trust = PluginTrust.Open(_dir);
        Assert.False(trust.FileExists);

        // Première utilisation : le plugin déjà posé reste chargé et la liste est créée
        Assert.Equal(new[] { "Ancien" }, PluginLoader.Load(_dir, trust).Select(p => p.Name));
        Assert.True(File.Exists(Path.Combine(_dir, PluginTrust.FileName)));

        // Un plugin posé ensuite n'est pas chargé tant que son empreinte n'est pas listée
        InstallSampleCopy("Nouveau");
        Assert.Equal(new[] { "Ancien" }, PluginLoader.Load(_dir, PluginTrust.Open(_dir)).Select(p => p.Name));

        // Une fois listée, il l'est
        var reopened = PluginTrust.Open(_dir);
        reopened.TrustAndSave(new[] { ("Nouveau", PluginTrust.FolderHash(Path.Combine(_dir, "Nouveau"))) });
        Assert.Equal(new[] { "Ancien", "Nouveau" }, PluginLoader.Load(_dir, PluginTrust.Open(_dir)).Select(p => p.Name));

        // Un plugin modifié après coup n'est plus autorisé
        File.AppendAllText(Path.Combine(_dir, "Ancien", "Ancien.deps.json"), "{}");
        File.WriteAllBytes(Path.Combine(_dir, "Ancien", "Autre.dll"), new byte[] { 1, 2, 3 });
        Assert.Equal(new[] { "Nouveau" }, PluginLoader.Load(_dir, PluginTrust.Open(_dir)).Select(p => p.Name));
    }

    [Fact]
    public void AnUnreadableTrustListLoadsNothingInsteadOfEverything()
    {
        InstallSampleCopy("Bon");
        File.WriteAllText(Path.Combine(_dir, PluginTrust.FileName), "pas du json");
        var trust = PluginTrust.Open(_dir);
        Assert.False(trust.IsValid);
        Assert.Empty(PluginLoader.Load(_dir, trust));
    }

    [Fact]
    public void ANonexistentFolderMeansNoPlugin()
    {
        Assert.Empty(PluginLoader.Load(Path.Combine(_dir, "absent")));
        Assert.Empty(PluginLoader.Load(_dir));
    }

    [Fact]
    public void OnlyPublicConcreteIntegrationsWithAnEmptyConstructorAreOffered()
    {
        var found = PluginLoader.FindIntegrations(typeof(ElgatoLightIntegration).Assembly);
        Assert.Equal(new[] { typeof(ElgatoLightIntegration) }, found);
        Assert.DoesNotContain(typeof(PolledHttpIntegration), PluginLoader.FindIntegrations(typeof(PolledHttpIntegration).Assembly));
    }
}
