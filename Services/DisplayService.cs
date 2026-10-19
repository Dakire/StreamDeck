using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TouchDeck;

/// <summary>Écrans Windows (System.Windows.Forms.Screen) ; les changements d'affichage arrivent sur le thread d'interface.</summary>
public sealed class WindowsDisplayService : IDisplayService
{
    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>S'abonne aux changements d'affichage de Windows.</summary>
    public WindowsDisplayService()
    {
        var dispatcher = Application.Current?.Dispatcher;
        SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            if (dispatcher != null) dispatcher.BeginInvoke(() => Changed?.Invoke());
            else Changed?.Invoke();
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<DisplayInfo> All() =>
        Forms.Screen.AllScreens.Select((s, i) => new DisplayInfo(i, s.DeviceName, Rect(s.Bounds), Rect(s.WorkingArea), s.Primary)).ToList();

    static PixelRect Rect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}

/// <summary>
/// Écrans disponibles et écran cible du deck. C'est le « périphérique » de TouchDeck : branchement et débranchement
/// d'un écran tactile sont détectés. <see cref="Provider"/> est remplaçable (tests : écrans simulés).
/// </summary>
public static class DisplayService
{
    static IDisplayService? _provider;
    static readonly HashSet<IDisplayService> Attached = new();

    /// <summary>Source des écrans : Windows par défaut, écrans simulés dans les tests.</summary>
    public static IDisplayService Provider
    {
        get => _provider ??= Attach(new WindowsDisplayService());
        set => _provider = Attach(value);
    }

    /// <summary>Un écran a été branché, débranché ou reconfiguré.</summary>
    public static event Action? Changed;

    static IDisplayService Attach(IDisplayService p)
    {
        if (!Attached.Add(p)) return p;
        p.Changed += () =>
        {
            if (!ReferenceEquals(p, _provider)) return;
            Log.Info("Écrans : " + string.Join(", ", p.All()));
            Changed?.Invoke();
        };
        return p;
    }

    /// <summary>Signale un changement simulé (tests) ou forcé.</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>Écrans actuellement branchés.</summary>
    public static IReadOnlyList<DisplayInfo> All() => Provider.All();

    /// <summary>Écran visé par la configuration, ou null s'il est absent.</summary>
    public static DisplayInfo? ResolveTarget(DeckConfig cfg) => DisplayResolver.Resolve(All(), cfg.Monitor, cfg.MonitorDevice);

    /// <summary>Description de l'écran choisi dans la configuration (paramètres) et s'il est branché.</summary>
    public static (string Text, bool Connected) Describe(DeckConfig cfg) => DisplayResolver.Describe(All(), cfg.Monitor, cfg.MonitorDevice);
}
