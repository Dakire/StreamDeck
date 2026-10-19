using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchDeck.Actions;
using TouchDeck.Integrations;
using TouchDeck.Integrations.ElgatoLight;
using TouchDeck.Integrations.Hue;
using TouchDeck.Integrations.Obs;
using TouchDeck.Integrations.Vmix;
using TouchDeck.Integrations.WindowsSystem;
using TouchDeck.Platform.Windows;

namespace TouchDeck;

/// <summary>Branche la façade <see cref="Log"/> du Core sur Serilog.</summary>
sealed class SerilogSink : ILogSink
{
    readonly Serilog.ILogger _logger;

    public SerilogSink(Serilog.ILogger logger) { _logger = logger; }

    /// <inheritdoc/>
    public void Write(LogSeverity severity, string message, Exception? exception)
    {
        switch (severity)
        {
            case LogSeverity.Info: _logger.Information(exception, "{Message}", message); break;
            case LogSeverity.Warning: _logger.Warning(exception, "{Message}", message); break;
            default: _logger.Error(exception, "{Message}", message); break;
        }
    }
}

/// <summary>
/// Racine de composition : construit le conteneur de services de l'application (journal, document, plateforme, moteur
/// d'actions, persistance, fenêtres). Les tests construisent le même conteneur, en simulation, sur un dossier temporaire.
/// Conteneur d'injection seul, sans l'hôte générique (Microsoft.Extensions.Hosting) : l'application n'a ni service de
/// fond ni configuration externe à gérer, et l'hôte coûtait environ 40 ms au démarrage (voir ADR 0007).
/// </summary>
public static class AppHost
{
    /// <summary>Fichier du journal dans le dossier de configuration (1 Mo au maximum, 3 fichiers conservés).</summary>
    public const string LogFileName = "touchdeck.log";
    const long LogMaxBytes = 1_000_000;
    const int LogFilesKept = 3;

    /// <summary>
    /// Construit le conteneur : journal, configuration, document, plateforme (réelle ou simulée), moteur d'actions,
    /// sauvegarde automatique, apparence et fenêtres. Doit être appelé sur le thread d'interface, après
    /// <c>ConfigStore.UseDirectory</c> éventuel. Libérer le conteneur enregistre les modifications en attente et interrompt
    /// les actions en cours.
    /// </summary>
    public static ServiceProvider Build(AppOptions options)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(ConfigStore.Dir, LogFileName), fileSizeLimitBytes: LogMaxBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: LogFilesKept, shared: true, formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Sink = new SerilogSink(serilog);
        long tLog = clock.ElapsedMilliseconds;

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(serilog, dispose: true));

        services.AddSingleton(options);
        services.AddSingleton(_ => StartupLoader.Load());
        services.AddSingleton(sp => StartupLoader.CreateDocument(sp.GetRequiredService<StartupResult>()));
        services.AddSingleton<GridState>();

        services.AddSingleton(_ => CreatePlatform(options));
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // le moteur gère le délai de chaque étape
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchDeck/1.0");
            return http;
        });
        // Un gestionnaire par type d'action ; les intégrations en ajoutent d'autres de la même façon
        foreach (var handler in BuiltInActions.All) services.AddSingleton(handler);
        services.AddSingleton(sp => new ActionRegistry(sp.GetServices<IActionHandler>()));
        services.AddSingleton(sp => new ActionEngine(new ActionContext
        {
            Platform = sp.GetRequiredService<PlatformServices>(),
            Http = sp.GetRequiredService<HttpClient>(),
        }, sp.GetRequiredService<ActionRegistry>()));

        // Intégrations : chaque connecteur est un IIntegration du conteneur et fournit son type d'action
        services.AddSingleton<ISecretStore>(_ => new DpapiSecretStore(ConfigStore.Dir));
        services.AddSingleton(sp => new IntegrationManager(sp.GetServices<IIntegration>(), sp.GetRequiredService<DeckDocument>(), sp.GetRequiredService<ISecretStore>()));

        AddIntegration<ObsIntegration>(services);
        AddIntegration<VmixIntegration>(services);
        AddIntegration<ElgatoLightIntegration>(services);
        AddIntegration<HueIntegration>(services);
        AddIntegration<WindowsSystemIntegration>(services);

        services.AddSingleton<IDisplayService, WindowsDisplayService>();
        services.AddSingleton<DisplayService>();
        services.AddSingleton<ConfigPersistence>();
        services.AddSingleton<AppearanceService>();
        services.AddSingleton<IAppWindows, AppWindows>();

        long tRegistered = clock.ElapsedMilliseconds;
        var sp = services.BuildServiceProvider();
        long tBuilt = clock.ElapsedMilliseconds;
        // Services qui agissent dès leur création (apparence, sauvegarde, validation des raccourcis) : instanciés ici,
        // dans cet ordre, avant l'ouverture de la première fenêtre
        Log.Info($"Démarrage (dossier {ConfigStore.Dir}{(options.Simulate ? ", simulation" : "")})");
        ActionValidator.Keyboard = sp.GetRequiredService<PlatformServices>().Keyboard;
        foreach (var integration in sp.GetServices<IIntegration>())
        {
            var captured = integration;
            ActionValidator.RegisterExternalType(captured.Id, a => IntegrationAction.Validate(captured, a));
            ActionCatalog.Register(captured.Category, captured.Icon, IntegrationCatalog.Entries(captured));
        }
        sp.GetRequiredService<AppearanceService>();
        sp.GetRequiredService<ConfigPersistence>();
        sp.GetRequiredService<IntegrationManager>().Start();
        Log.Info($"Services prêts en {clock.ElapsedMilliseconds} ms (journal {tLog}, déclarations {tRegistered - tLog}, conteneur {tBuilt - tRegistered})");
        return sp;
    }

    /// <summary>Enregistre un connecteur : lui-même, comme <see cref="IIntegration"/>, et son type d'action.</summary>
    static void AddIntegration<T>(IServiceCollection services) where T : class, IIntegration
    {
        services.AddSingleton<T>();
        services.AddSingleton<IIntegration>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IActionHandler>(sp => new IntegrationAction(sp.GetRequiredService<T>()));
    }

    static PlatformServices CreatePlatform(AppOptions options)
    {
        if (!options.Simulate) return WindowsPlatform.Create();
        var sim = new SimulatedPlatform();
        sim.Performed += what => Application.Current?.Dispatcher.BeginInvoke(() => Notifier.Info("Simulation : " + what));
        return sim.Services;
    }
}
