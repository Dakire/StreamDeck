using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StreamDak.Actions;
using StreamDak.Integrations;
using StreamDak.Integrations.ClickUp;
using StreamDak.Integrations.Discord;
using StreamDak.Integrations.ElgatoLight;
using StreamDak.Integrations.Google;
using StreamDak.Integrations.Hue;
using StreamDak.Integrations.Monday;
using StreamDak.Integrations.Notifications;
using StreamDak.Integrations.Sonar;
using StreamDak.Integrations.Notion;
using StreamDak.Integrations.Obs;
using StreamDak.Integrations.Twitch;
using StreamDak.Integrations.Vmix;
using StreamDak.Integrations.WindowsSystem;
using StreamDak.Integrations.XPlane;
using StreamDak.Integrations.Teams;
using StreamDak.Platform.Windows;

namespace StreamDak;

/// <summary>Formate chaque ligne du journal puis masque ce qui ressemble à un secret (<see cref="LogRedactor"/>) : message et exception.</summary>
sealed class RedactingFormatter : Serilog.Formatting.ITextFormatter
{
    readonly Serilog.Formatting.Display.MessageTemplateTextFormatter _inner;

    public RedactingFormatter(string outputTemplate) { _inner = new(outputTemplate, System.Globalization.CultureInfo.InvariantCulture); }

    public void Format(Serilog.Events.LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        _inner.Format(logEvent, buffer);
        output.Write(LogRedactor.Redact(buffer.ToString()));
    }
}

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
    public const string LogFileName = "streamdak.log";
    const long LogMaxBytes = 1_000_000;
    const int LogFilesKept = 3;

    /// <summary>
    /// Niveau du journal : variable <c>STREAMDAK_LOG_LEVEL</c> (<c>warning</c> pour un journal plus court, <c>info</c> par défaut). Les diagnostics détaillés
    /// restent derrière leurs propres variables (<c>STREAMDAK_TOUCHLOG=1</c> pour le toucher) : jamais des milliers de lignes par défaut.
    /// </summary>
    internal static Serilog.Events.LogEventLevel LogLevelFromEnvironment() =>
        Environment.GetEnvironmentVariable("STREAMDAK_LOG_LEVEL")?.Trim().ToLowerInvariant() switch
        {
            "warning" or "warn" => Serilog.Events.LogEventLevel.Warning,
            "error" => Serilog.Events.LogEventLevel.Error,
            _ => Serilog.Events.LogEventLevel.Information,
        };

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
            .MinimumLevel.Is(LogLevelFromEnvironment())
            .WriteTo.File(new RedactingFormatter("{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}"),
                Path.Combine(ConfigStore.Dir, LogFileName), fileSizeLimitBytes: LogMaxBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: LogFilesKept, shared: true)
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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("StreamDak/1.0");
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

        // Identifiants déjà pris (types d'action de StreamDak et intégrations livrées) : un plugin ne peut pas les reprendre
        var takenIds = new HashSet<string>(BuiltInActions.All.Select(h => h.Type), StringComparer.OrdinalIgnoreCase);
        AddIntegration<ObsIntegration>(services, takenIds);
        AddIntegration<VmixIntegration>(services, takenIds);
        AddIntegration<ElgatoLightIntegration>(services, takenIds);
        AddIntegration<HueIntegration>(services, takenIds);
        AddIntegration<WindowsSystemIntegration>(services, takenIds);
        AddIntegration<XPlaneIntegration>(services, takenIds);
        AddIntegration<TeamsIntegration>(services, takenIds);
        AddIntegration<ClickUpIntegration>(services, takenIds);
        AddIntegration<NotionIntegration>(services, takenIds);
        AddIntegration<MondayIntegration>(services, takenIds);
        AddIntegration<TwitchIntegration>(services, takenIds);
        AddIntegration<GoogleIntegration>(services, takenIds);
        AddIntegration<NotificationIntegration>(services, takenIds);
        AddIntegration<SonarIntegration>(services, takenIds);
        AddIntegration<DiscordIntegration>(services, takenIds);
        AddPlugins(services, takenIds);

        services.AddSingleton<IDisplayService, WindowsDisplayService>();
        services.AddSingleton<DisplayService>();
        services.AddSingleton<ConfigPersistence>();
        services.AddSingleton<AppearanceService>();
        services.AddSingleton<TaskbarBadgeService>();   // pastille de la barre des tâches (total de non lus), attachée par MainWindow
        services.AddSingleton<IAppWindows, AppWindows>();
        // Éditeur intégré au deck (mode édition) : le deck le crée à l'entrée du mode et le libère à la sortie
        services.AddSingleton<Func<StreamDak.ViewModels.IEditorShell, StreamDak.ViewModels.EditorViewModel>>(sp =>
            shell => ActivatorUtilities.CreateInstance<StreamDak.ViewModels.EditorViewModel>(sp, shell));
        // Widgets de données (IWidget) : un par ligne ; leur description est dans WidgetTypes.All (un test vérifie la correspondance)
        services.AddSingleton<IWidget, DiskWidget>();
        services.AddSingleton<IWidget, NetworkWidget>();
        services.AddSingleton<IWidget, PingWidget>();
        services.AddSingleton<IWidget, ServiceStatusWidget>();
        services.AddSingleton<IWidget, WeatherWidget>();
        services.AddSingleton<IWidget, NextEventWidget>();
        services.AddSingleton<IWidget, CounterWidget>();
        services.AddSingleton<IWidget>(_ => new BatteryWidget(options.Simulate ? () => new BatteryState(true, 76, false, TimeSpan.FromMinutes(190)) : WindowsPlatform.ReadBattery));
        services.AddSingleton<IWidget, UptimeWidget>();
        services.AddSingleton<IWidget>(sp => new AudioDevicesWidget(sp.GetRequiredService<PlatformServices>().Audio));
        services.AddSingleton<IWidget, NowPlayingWidget>();
        services.AddSingleton<IWidget, FeedWidget>();
        services.AddSingleton(sp => new WidgetRegistry(sp.GetServices<IWidget>()));
        // Badges de notifications : services connus + ceux de notifications.json, suivis dans des pages WebView2 cachées (seulement si le connecteur est activé)
        services.AddSingleton(_ => NotificationProviders.Load(ConfigStore.Dir));
        services.AddSingleton<INotificationWatcher>(sp => new WebNotificationWatcher(sp.GetRequiredService<DeckDocument>(), sp.GetRequiredService<NotificationProviders>()));
        // Mises à jour par les releases GitHub (réglages dans update.json, dépôt par défaut dans update.defaults.json : rien n'est écrit en dur)
        services.AddSingleton(_ => CreateUpdateService(options));
        services.AddSingleton<IApiShell, ApiShell>();
        services.AddSingleton<LocalApi>();

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
            if (captured.Id == "sonar") ActionCatalog.Register(captured.Category, captured.Icon, StreamDak.Audio.LevelOptions.SonarSliderEntries());   // curseurs tactiles en tête de la catégorie
            ActionCatalog.Register(captured.Category, captured.Icon, IntegrationCatalog.Entries(captured));
        }
        sp.GetRequiredService<AppearanceService>();
        sp.GetRequiredService<ConfigPersistence>();
        sp.GetRequiredService<IntegrationManager>().Start();
        Log.Info($"Services prêts en {clock.ElapsedMilliseconds} ms (journal {tLog}, déclarations {tRegistered - tLog}, conteneur {tBuilt - tRegistered})");
        return sp;
    }

    /// <summary>Enregistre un connecteur : lui-même, comme <see cref="IIntegration"/>, et son type d'action.</summary>
    static void AddIntegration<T>(IServiceCollection services, HashSet<string> takenIds) where T : class, IIntegration
    {
        services.AddSingleton<T>();
        services.AddSingleton<IIntegration>(sp => sp.GetRequiredService<T>());
        services.AddSingleton<IActionHandler>(sp => new IntegrationAction(sp.GetRequiredService<T>()));
        takenIds.Add(IdOf(typeof(T)));
    }


    static string IdOf(Type type)
    {
        var probe = (IIntegration)Activator.CreateInstance(type)!;
        try { return probe.Id; }
        finally { (probe as IDisposable)?.Dispose(); }
    }

    /// <summary>Ajoute les intégrations des plugins posés dans le dossier <c>plugins</c> (code de confiance, voir <see cref="PluginLoader"/>).</summary>
    static void AddPlugins(IServiceCollection services, HashSet<string> takenIds)
    {
        foreach (var plugin in PluginLoader.Load(Path.Combine(ConfigStore.Dir, PluginLoader.FolderName)))
        {
            foreach (var type in plugin.Integrations)
            {
                string id;
                try { id = IdOf(type); }
                catch (Exception ex) { Log.Warn($"Plugin « {plugin.Name} » : {type.Name} ignoré, construction impossible ({ex.Message})"); continue; }
                if (!takenIds.Add(id)) { Log.Warn($"Plugin « {plugin.Name} » : {type.Name} ignoré, l'identifiant « {id} » est déjà pris"); continue; }
                services.AddSingleton(type);
                services.AddSingleton<IIntegration>(sp => (IIntegration)sp.GetRequiredService(type));
                services.AddSingleton<IActionHandler>(sp => new IntegrationAction((IIntegration)sp.GetRequiredService(type)));
            }
        }
    }

    /// <summary>Service de mises à jour : API GitHub (redirections HTTPS permises), téléchargement sans redirection automatique (chacune est contrôlée), signature Authenticode de Windows.</summary>
    static UpdateService CreateUpdateService(AppOptions options)
    {
        var current = UpdateEnvironment.CurrentVersion();
        // Hors installation, avec un dossier de configuration imposé (essais, tests) : le dossier de travail reste dans ce dossier et ne touche pas au vrai profil local
        var work = Environment.GetEnvironmentVariable("STREAMDAK_DATA_DIR") is { Length: > 0 }
            ? Path.Combine(ConfigStore.Dir, "updates")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDak", "updates");
        var environment = new UpdateEnvironment(ConfigStore.Dir, AppContext.BaseDirectory, work, current, Environment.ProcessPath ?? "", Environment.ProcessId);
        var api = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3 }) { Timeout = Timeout.InfiniteTimeSpan };
        var download = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        IUpdateApplier applier = options.Simulate ? new SimulatedApplier() : new UpdateApplier();
        return new UpdateService(environment, new UpdateChecker(api, current), new UpdateDownloader(download, current), WindowsPlatform.CreateSignatureVerifier(), applier,
            () => Application.Current?.Dispatcher.BeginInvoke(() => Application.Current.Shutdown()));
    }

    /// <summary>Mode « --simulate » : l'installation est annoncée, jamais lancée.</summary>
    sealed class SimulatedApplier : IUpdateApplier
    {
        public void Launch(UpdatePlan plan) => Log.Info($"Simulation : installation de {Path.GetFileName(plan.Installer)} non lancée");
    }

    static PlatformServices CreatePlatform(AppOptions options)
    {
        if (!options.Simulate) return WindowsPlatform.Create();
        var sim = new SimulatedPlatform();
        sim.Performed += what => Application.Current?.Dispatcher.BeginInvoke(() => Notifier.Info("Simulation : " + what));
        return sim.Services;
    }
}
