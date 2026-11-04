using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace StreamDak;

/// <summary>Application WPF : branche la gestion des erreurs, construit le conteneur de services et ouvre le deck.</summary>
// Application n'est pas IDisposable : le verrou d'instance, le serveur d'API et le conteneur sont libérés dans OnExit
#pragma warning disable CA1001
public partial class App : Application
{
    ServiceProvider? _services;
    System.Threading.Mutex? _instance;
    LocalApiServer? _api;
    TrayIcon? _tray;
    UiWatchdog? _watchdog;

    /// <summary>
    /// Hôte de tests : le constructeur d'<see cref="Application"/> programme <see cref="OnStartup"/> au premier tour du
    /// Dispatcher, même sans <c>Run()</c>. Sans cet indicateur, les tests ouvriraient la vraie fenêtre du deck et
    /// remplaceraient la plateforme simulée par la plateforme Windows.
    /// </summary>
    public bool Headless { get; init; }

    /// <summary>Démarrage : gestion des erreurs non traitées, services (configuration, thème, sauvegarde), puis fenêtre du deck.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (Headless) return;
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Erreur fatale", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Tâche en échec non observée", a.Exception); a.SetObserved(); };
        // « --simulate » : les actions sont affichées au lieu d'être exécutées (essai de la grille sans risque)
        var options = AppOptions.FromArgs(e.Args);
        // Avant l'instance unique (sa clé dépend du dossier) : reprend le dossier de l'ancien nom du produit (TouchDeck)
        if (ConfigStore.MigrateLegacyDirectory()) Log.Info("Dossier de configuration repris de l'ancien nom du produit");
        if (!TakeSingleInstance(options)) { Shutdown(); return; }
        try
        {
            _services = AppHost.Build(options);   // configuration, thème et sauvegarde automatique avant la création des fenêtres
            base.OnStartup(e);
            // Fenêtre créée ici plutôt que par StartupUri : StartupUri s'appliquerait aussi dans l'hôte de tests
            MainWindow = new MainWindow(_services);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            // Sans fenêtre, WPF ne s'arrêterait pas : le processus resterait vivant avec le verrou d'instance et bloquerait tout relancement
            Log.Error("Échec du démarrage", ex);
            MessageBox.Show("StreamDak n'a pas pu démarrer : " + ex.Message + "\nDétails dans streamdak.log.", "StreamDak", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        _api = new LocalApiServer(_services.GetRequiredService<LocalApi>());
        _api.Start();
        _tray = new TrayIcon(_services.GetRequiredService<IApiShell>());
        _watchdog = new UiWatchdog(Dispatcher);
        // Deck masqué ou réduit : une vérification par seconde au lieu de dix
        void WatchVisibility() => _watchdog?.SetBackground(!MainWindow.IsVisible || MainWindow.WindowState == WindowState.Minimized);
        MainWindow.IsVisibleChanged += (_, _) => WatchVisibility();
        MainWindow.StateChanged += (_, _) => WatchVisibility();
        if (!options.Simulate) _services.GetRequiredService<UpdateService>().StartBackgroundCheck();   // une fois par jour au plus, 20 s après le démarrage, désactivable
        // « StreamDak.exe --edit » ouvre aussi l'éditeur au lancement
        if (options.OpenEditor)
        {
            var windows = _services.GetRequiredService<IAppWindows>();
            Dispatcher.BeginInvoke(() => windows.OpenEditor(), DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>Arrêt : libérer le conteneur enregistre les modifications en attente et interrompt les actions en cours.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Arrêt");   // avant la libération du conteneur : celle-ci ferme le fichier du journal
        // La configuration d'abord : elle ne doit pas attendre la déconnexion des intégrations (le conteneur libère dans l'ordre inverse de la création)
        try { _services?.GetService<ConfigPersistence>()?.Flush(); }
        catch (Exception ex) { Log.Error("Enregistrement à l'arrêt", ex); }
        _watchdog?.Dispose();
        _tray?.Dispose();
        _api?.Dispose();
        _services?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Une seule instance par dossier de configuration : une seconde instance demande à la première d'afficher le deck (ou l'éditeur
    /// avec <c>--edit</c>) puis s'arrête. Retourne false si l'application doit s'arrêter.
    /// </summary>
    bool TakeSingleInstance(AppOptions options)
    {
        _instance = new System.Threading.Mutex(true, @"Local\StreamDak." + LocalApiServer.InstanceKey(), out bool first);
        if (first) return true;
        try
        {
            var request = options.OpenEditor ? "{\"cmd\":\"edit\"}" : "{\"cmd\":\"show\"}";
            System.Threading.Tasks.Task.Run(() => LocalApiClient.SendAsync(LocalApiServer.DefaultPipeName, request, TimeSpan.FromSeconds(3))).GetAwaiter().GetResult();   // hors du contexte d'interface : pas d'interblocage
        }
        catch (Exception ex) when (ex is TimeoutException or System.IO.IOException)
        {
            Log.Warn($"Seconde instance : la première ne répond pas ({ex.Message})");
        }
        return false;
    }

    /// <summary>Une erreur d'interface ne doit pas faire perdre le deck : on la consigne et on continue.</summary>
    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erreur inattendue de l'interface", e.Exception);
        Notifier.Error("Erreur inattendue : " + e.Exception.Message);
        e.Handled = true;
    }
}
