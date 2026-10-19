using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TouchDeck;

public partial class App : Application
{
    /// <summary>
    /// Hôte de tests : le constructeur d'<see cref="Application"/> programme <see cref="OnStartup"/> au premier tour du
    /// Dispatcher, même sans <c>Run()</c>. Sans cet indicateur, les tests ouvriraient la vraie fenêtre du deck et
    /// remplaceraient la plateforme simulée par la plateforme Windows.
    /// </summary>
    public bool Headless { get; init; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (Headless) return;
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Erreur fatale", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Tâche en échec non observée", a.Exception); a.SetObserved(); };
        // « --simulate » : les actions sont affichées au lieu d'être exécutées (essai de la grille sans risque)
        AppServices.Init(simulate: HasArg(e, "--simulate"));   // configuration, thème et sauvegarde automatique avant la création des fenêtres
        base.OnStartup(e);
        // Fenêtre créée ici plutôt que par StartupUri : StartupUri s'appliquerait aussi dans l'hôte de tests
        MainWindow = new MainWindow();
        MainWindow.Show();
        // « TouchDeck.exe --edit » ouvre aussi l'éditeur au lancement
        if (HasArg(e, "--edit"))
            Dispatcher.BeginInvoke(() => AppServices.OpenEditor(), DispatcherPriority.ApplicationIdle);
    }

    static bool HasArg(StartupEventArgs e, string name) => e.Args.Contains(name, StringComparer.OrdinalIgnoreCase);

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
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
