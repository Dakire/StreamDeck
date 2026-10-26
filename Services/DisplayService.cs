using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace StreamDak;

/// <summary>Écrans Windows (System.Windows.Forms.Screen) ; les changements d'affichage arrivent sur le thread d'interface.</summary>
public sealed class WindowsDisplayService : IDisplayService, IDisposable
{
    readonly System.Windows.Threading.Dispatcher? _dispatcher = Application.Current?.Dispatcher;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>S'abonne aux changements d'affichage de Windows.</summary>
    public WindowsDisplayService()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Reprise après veille : les écrans mettent quelques secondes à revenir, puis le deck se replace comme après un changement d'affichage.</summary>
    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Log.Info("Reprise après veille : placement du deck revérifié");
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => OnDisplaySettingsChanged(this, EventArgs.Empty), System.Threading.Tasks.TaskScheduler.Default);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_dispatcher != null) _dispatcher.BeginInvoke(() => Changed?.Invoke());
        else Changed?.Invoke();
    }

    /// <inheritdoc/>
    public IReadOnlyList<DisplayInfo> All() =>
        Forms.Screen.AllScreens.Select((s, i) => new DisplayInfo(i, s.DeviceName, Rect(s.Bounds), Rect(s.WorkingArea), s.Primary)).ToList();

    static PixelRect Rect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>Se désabonne des changements d'affichage.</summary>
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}

/// <summary>
/// Écrans disponibles et écran cible du deck. C'est le « périphérique » de StreamDak : branchement et débranchement
/// d'un écran tactile sont détectés. La source (<see cref="IDisplayService"/>) est injectée : Windows en production,
/// des écrans simulés dans les tests.
/// </summary>
public sealed class DisplayService : IDisposable
{
    readonly IDisplayService _provider;

    /// <summary>Un écran a été branché, débranché ou reconfiguré.</summary>
    public event Action? Changed;

    /// <summary>Suit les changements de la source <paramref name="provider"/> et les consigne.</summary>
    public DisplayService(IDisplayService provider)
    {
        _provider = provider;
        _provider.Changed += OnProviderChanged;
    }

    void OnProviderChanged()
    {
        Log.Info("Écrans : " + string.Join(", ", _provider.All()));
        Changed?.Invoke();
    }

    /// <summary>Signale un changement forcé.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Écrans actuellement branchés.</summary>
    public IReadOnlyList<DisplayInfo> All() => _provider.All();

    /// <summary>Écran visé par la configuration, ou null s'il est absent.</summary>
    public DisplayInfo? ResolveTarget(DeckConfig cfg) => DisplayResolver.Resolve(All(), cfg.Monitor, cfg.MonitorDevice);

    /// <summary>Description de l'écran choisi dans la configuration (paramètres) et s'il est branché.</summary>
    public (string Text, bool Connected) Describe(DeckConfig cfg) => DisplayResolver.Describe(All(), cfg.Monitor, cfg.MonitorDevice);

    /// <summary>Se désabonne de la source.</summary>
    public void Dispose() => _provider.Changed -= OnProviderChanged;
}
