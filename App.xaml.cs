using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TouchDeck;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppServices.Init();      // configuration, thème et sauvegarde automatique avant la création des fenêtres
        base.OnStartup(e);
        // « TouchDeck.exe --edit » ouvre aussi l'éditeur au lancement
        if (e.Args.Contains("--edit", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(() => AppServices.OpenEditor(), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        base.OnExit(e);
    }

    /// <summary>Une erreur d'interface ne doit pas faire perdre le deck : on la consigne et on continue.</summary>
    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(ConfigStore.Dir, "error.log"), $"{DateTime.Now:s}  {e.Exception}\n\n");
        }
        catch { /* journal best-effort */ }
        Notifier.Error("Erreur inattendue : " + e.Exception.Message);
        e.Handled = true;
    }
}
