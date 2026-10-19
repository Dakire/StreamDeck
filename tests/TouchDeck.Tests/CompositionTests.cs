using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchDeck.Actions;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>La racine de composition (<see cref="AppHost"/>) : services disponibles, uniques, simulés, et journal Serilog.</summary>
[Collection("ui")]
public class CompositionTests
{
    readonly WpfHost _host;
    public CompositionTests(WpfHost host) => _host = host;

    [Theory]
    [InlineData(typeof(DeckDocument))]
    [InlineData(typeof(ActionEngine))]
    [InlineData(typeof(PlatformServices))]
    [InlineData(typeof(ConfigPersistence))]
    [InlineData(typeof(AppearanceService))]
    [InlineData(typeof(DisplayService))]
    [InlineData(typeof(GridState))]
    [InlineData(typeof(IAppWindows))]
    [InlineData(typeof(StartupResult))]
    [InlineData(typeof(AppOptions))]
    public void EveryServiceIsRegisteredAsASingleton(Type service)
    {
        var first = _host.Services.GetRequiredService(service);
        Assert.Same(first, _host.Services.GetRequiredService(service));
    }

    [Fact]
    public void TheSharedDocumentIsTheOneEveryViewModelReceives()
    {
        var editor = _host.Run(() => _host.Create<ViewModels.EditorViewModel>(new FakeShell()));
        _host.Run(() => _host.Document.UpdateSettings(c => c.Spacing = 21));
        _host.Settle();
        Assert.Equal(21, _host.Services.GetRequiredService<DeckDocument>().Config.Spacing);
        Assert.NotNull(editor.Catalog);   // construit avec le même document que le deck
        _host.Run(editor.Dispose);
    }

    [Fact]
    public void TheHarnessRunsInSimulationOnItsOwnTemporaryFolder()
    {
        Assert.True(_host.Services.GetRequiredService<AppOptions>().Simulate);
        Assert.IsType<SimulatedPlatform>(_host.Services.GetRequiredService<PlatformServices>().Keyboard);
        Assert.Same(_host.Services.GetRequiredService<PlatformServices>().Keyboard, ActionValidator.Keyboard);
        Assert.StartsWith(Path.GetTempPath(), ConfigStore.Dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogMessagesFromTheCoreAndFromInjectedLoggersReachTheSerilogFile()
    {
        var core = "journal-core-" + Guid.NewGuid().ToString("N");
        var injected = "journal-injecte-" + Guid.NewGuid().ToString("N");
        Log.Warn(core);
        _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogError(new InvalidOperationException("boum"), "{Texte}", injected);

        var text = ReadLogs();
        Assert.Contains("WRN " + core, text);
        Assert.Contains("ERR " + injected, text);
        Assert.Contains("InvalidOperationException: boum", text);
    }

    static string ReadLogs()
    {
        var files = Directory.GetFiles(ConfigStore.Dir, "touchdeck*.log");
        Assert.NotEmpty(files);
        return string.Join("\n", files.Select(f =>
        {
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }));
    }

    [Fact]
    public void AWindowsPlatformIsNeverBuiltInSimulation()
    {
        // Les tests ne doivent jamais envoyer une touche réelle : le clavier du conteneur n'est pas celui de Windows
        Assert.IsNotType<Platform.Windows.WindowsPlatform>(_host.Services.GetRequiredService<PlatformServices>().Keyboard);
    }
}
