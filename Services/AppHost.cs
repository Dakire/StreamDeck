using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TouchDeck.Actions;
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
/// </summary>
public static class AppHost
{
    /// <summary>Fichier du journal dans le dossier de configuration (1 Mo au maximum, 3 fichiers conservés).</summary>
    public const string LogFileName = "touchdeck.log";
    const long LogMaxBytes = 1_000_000;
    const int LogFilesKept = 3;

    /// <summary>
    /// Construit l'hôte : journal, configuration, document, plateforme (réelle ou simulée), moteur d'actions, sauvegarde
    /// automatique, apparence et fenêtres. Doit être appelé sur le thread d'interface, après <c>ConfigStore.UseDirectory</c>
    /// éventuel. Libérer l'hôte enregistre les modifications en attente et interrompt les actions en cours.
    /// </summary>
    public static IHost Build(AppOptions options)
    {
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(ConfigStore.Dir, LogFileName), fileSizeLimitBytes: LogMaxBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: LogFilesKept, shared: true, formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Sink = new SerilogSink(serilog);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var services = builder.Services;
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
        services.AddSingleton(sp => new ActionEngine(new ActionContext
        {
            Platform = sp.GetRequiredService<PlatformServices>(),
            Http = sp.GetRequiredService<HttpClient>(),
        }));

        services.AddSingleton<IDisplayService, WindowsDisplayService>();
        services.AddSingleton<DisplayService>();
        services.AddSingleton<ConfigPersistence>();
        services.AddSingleton<AppearanceService>();
        services.AddSingleton<IAppWindows, AppWindows>();

        var host = builder.Build();
        // Services qui agissent dès leur création (apparence, sauvegarde, validation des raccourcis) : instanciés ici,
        // dans cet ordre, avant l'ouverture de la première fenêtre
        Log.Info($"Démarrage (dossier {ConfigStore.Dir}{(options.Simulate ? ", simulation" : "")})");
        var sp = host.Services;
        ActionValidator.Keyboard = sp.GetRequiredService<PlatformServices>().Keyboard;
        sp.GetRequiredService<AppearanceService>();
        sp.GetRequiredService<ConfigPersistence>();
        return host;
    }

    static PlatformServices CreatePlatform(AppOptions options)
    {
        if (!options.Simulate) return WindowsPlatform.Create();
        var sim = new SimulatedPlatform();
        sim.Performed += what => Application.Current?.Dispatcher.BeginInvoke(() => Notifier.Info("Simulation : " + what));
        return sim.Services;
    }
}
