using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchDeck.Actions;
using Xunit;

namespace TouchDeck.Tests;

[CollectionDefinition("ui", DisableParallelization = true)]
public class UiCollection : ICollectionFixture<WpfHost> { }

/// <summary>Collecte les erreurs de liaison de données WPF : une propriété mal nommée dans un XAML n'échoue pas, elle s'affiche vide.</summary>
public sealed class BindingErrors : TraceListener
{
    public List<string> Messages { get; } = new();
    public override void Write(string? message) { }
    public override void WriteLine(string? message)
    {
        if (message != null && (message.Contains("BindingExpression") || message.Contains("Cannot find") || message.Contains("not found")))
            lock (Messages) Messages.Add(message);
    }
}

/// <summary>
/// Une application WPF réelle sur un thread STA dédié, sans aucune fenêtre affichée : les fenêtres sont construites,
/// mises en page et rendues hors écran (RenderTargetBitmap). Rien ne touche au bureau de l'utilisateur ni à sa configuration.
/// </summary>
public sealed class WpfHost : IDisposable
{
    readonly Thread _thread;
    readonly string _dir;
    IHost? _app;
    public Dispatcher Dispatcher { get; private set; } = null!;
    /// <summary>Conteneur de services de l'application, construit comme en production mais en simulation.</summary>
    public IServiceProvider Services => _app!.Services;
    public DeckDocument Document => Services.GetRequiredService<DeckDocument>();
    public ActionEngine Engine => Services.GetRequiredService<ActionEngine>();
    public ConfigPersistence Persistence => Services.GetRequiredService<ConfigPersistence>();
    public BindingErrors Bindings { get; } = new();
    public string ShotDir { get; }

    public WpfHost()
    {
        _dir = Path.Combine(Path.GetTempPath(), "touchdeck-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ShotDir = Environment.GetEnvironmentVariable("TOUCHDECK_SHOTS") is { Length: > 0 } s ? s : Path.Combine(_dir, "shots");
        Directory.CreateDirectory(ShotDir);
        ConfigStore.UseDirectory(_dir);

        var ready = new ManualResetEventSlim();
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            try
            {
                // Headless : le démarrage normal (vraie fenêtre du deck, plateforme Windows) ne s'exécute pas
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, Headless = true };
                app.InitializeComponent();
                System.Diagnostics.PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(Bindings);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                _app = AppHost.Build(new AppOptions(Simulate: true));   // aucun raccourci envoyé, aucun programme lancé pendant les tests
                Dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex) { failure = ex; }
            ready.Set();
            if (failure == null) Dispatcher.Run();
        }) { IsBackground = true, Name = "WPF test host" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        if (failure != null) throw new InvalidOperationException("Démarrage WPF impossible : " + failure, failure);
    }

    public T Run<T>(Func<T> f) => Dispatcher.Invoke(f);
    public void Run(Action a) => Dispatcher.Invoke(a);

    /// <summary>Laisse WPF traiter la mise en page, les liaisons et les tâches d'arrière-plan.</summary>
    public void Settle() => Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>Repart d'une configuration propre pour chaque scénario.</summary>
    public void Reset(DeckConfig? cfg = null)
    {
        Run(() =>
        {
            Document.Reset(cfg ?? ConfigStore.DefaultConfig());
            Document.UpdateSettings(c => c.Theme = "dark");
        });
        Settle();
        lock (Bindings.Messages) Bindings.Messages.Clear();
    }

    public void SetTheme(string theme) { Run(() => Document.UpdateSettings(c => c.Theme = theme)); Settle(); }

    /// <summary>Construit un objet par le conteneur (fenêtre, ViewModel), les arguments donnés complétant les services.</summary>
    public T Create<T>(params object[] args) where T : class => ActivatorUtilities.CreateInstance<T>(Services, args);

    /// <summary>Met en page une fenêtre sans l'afficher et la rend en PNG ; retourne le chemin de l'image.</summary>
    public string Render(Window window, string name, double width, double height)
    {
        return Run(() =>
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            root.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var path = Path.Combine(ShotDir, name + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
        });
    }

    public void Dispose()
    {
        try { Dispatcher.Invoke(() => { _app?.Dispose(); Dispatcher.InvokeShutdown(); }); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }
}
