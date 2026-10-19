using System;
using System.IO;
using System.Linq;
using TouchDeck.Integrations;
using TouchDeck.Integrations.Vmix;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Chargement des plugins : contexte isolé, contrat partagé, plugins défectueux ignorés sans bloquer les autres.</summary>
public class PluginLoaderTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "touchdeck-plugins-" + Guid.NewGuid().ToString("N"));

    public PluginLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fichier encore mappé par le contexte de chargement : le dossier temporaire sera nettoyé plus tard */ }
        GC.SuppressFinalize(this);
    }

    // Copie une des bibliothèques d'intégration de TouchDeck comme si c'était un plugin posé par l'utilisateur
    string InstallVmixCopy(string name)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        var source = typeof(VmixIntegration).Assembly.Location;
        var target = Path.Combine(folder, name + ".dll");
        File.Copy(source, target);
        return target;
    }

    [Fact]
    public void ALibraryWithAnIntegrationIsLoadedInItsOwnContextAndSharesTheCoreContract()
    {
        var dll = InstallVmixCopy("MonPlugin");

        var plugins = PluginLoader.Load(_dir);

        var plugin = Assert.Single(plugins);
        Assert.Equal("MonPlugin", plugin.Name);
        Assert.Equal(dll, plugin.Path);
        Assert.Equal(64, plugin.Sha256.Length);
        var type = Assert.Single(plugin.Integrations);
        Assert.NotSame(typeof(VmixIntegration), type);                       // chargé dans un autre contexte
        Assert.True(typeof(IIntegration).IsAssignableFrom(type));            // mais le contrat Core est le même
        using var instance = (IDisposable)Activator.CreateInstance(type)!;
        Assert.Equal("vmix", ((IIntegration)instance).Id);
    }

    [Fact]
    public void ABrokenOrEmptyPluginNeverStopsTheOthers()
    {
        InstallVmixCopy("Bon");
        Directory.CreateDirectory(Path.Combine(_dir, "SansDll"));                                  // dossier sans bibliothèque
        Directory.CreateDirectory(Path.Combine(_dir, "Corrompu"));
        File.WriteAllText(Path.Combine(_dir, "Corrompu", "Corrompu.dll"), "ceci n'est pas une bibliothèque .NET");
        var vide = Path.Combine(_dir, "TouchDeck.Core");                                           // bibliothèque valide sans intégration concrète
        Directory.CreateDirectory(vide);
        File.Copy(typeof(IIntegration).Assembly.Location, Path.Combine(vide, "TouchDeck.Core.dll"));

        var plugins = PluginLoader.Load(_dir);

        Assert.Equal(new[] { "Bon" }, plugins.Select(p => p.Name));
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
        var found = PluginLoader.FindIntegrations(typeof(VmixIntegration).Assembly);
        Assert.Equal(new[] { typeof(VmixIntegration) }, found);
        Assert.DoesNotContain(typeof(PolledHttpIntegration), PluginLoader.FindIntegrations(typeof(PolledHttpIntegration).Assembly));
    }
}
