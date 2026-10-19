using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace TouchDeck;

/// <summary>Application WPF : branche la gestion des erreurs, construit le conteneur de services et ouvre le deck.</summary>
public partial class App : Application
{
    ServiceProvider? _services;

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
        _services = AppHost.Build(options);   // configuration, thème et sauvegarde automatique avant la création des fenêtres
        base.OnStartup(e);
        // Fenêtre créée ici plutôt que par StartupUri : StartupUri s'appliquerait aussi dans l'hôte de tests
        MainWindow = new MainWindow(_services);
        MainWindow.Show();
        // « TouchDeck.exe --edit » ouvre aussi l'éditeur au lancement
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
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Une erreur d'interface ne doit pas faire perdre le deck : on la consigne et on continue.</summary>
    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erreur inattendue de l'interface", e.Exception);
        Notifier.Error("Erreur inattendue : " + e.Exception.Message);
        e.Handled = true;
    }
}
