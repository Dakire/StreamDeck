using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StreamDak;

/// <summary>
/// WPF-UI 4.3 : chaque <see cref="TitleBar"/> s'abonne à l'événement statique <c>ApplicationThemeManager.Changed</c> et ne s'en désabonne
/// jamais. Une fenêtre fermée reste donc retenue avec tout son contenu (mesuré : 0,86 Mo par ouverture de l'éditeur, 42 Mo pour 50).
/// À la fermeture de nos fenêtres, on retire ce gestionnaire. Si une version de WPF-UI renomme le gestionnaire, on le consigne une fois
/// et la fenêtre fermée reste retenue comme avant : aucune autre conséquence.
/// </summary>
public static class WindowCleanup
{
    // Plusieurs surcharges existent le long de la hiérarchie : on prend celle qui a la signature de l'événement
    static readonly MethodInfo? Handler = typeof(TitleBar).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .FirstOrDefault(m => m.Name == "OnThemeChanged" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ApplicationTheme), typeof(Color) }));
    static readonly EventInfo? ThemeChanged = typeof(ApplicationThemeManager).GetEvent(nameof(ApplicationThemeManager.Changed));
    static bool _reported;

    /// <summary>Détache de l'événement statique de thème tous les <see cref="TitleBar"/> de la fenêtre ; à appeler dans <c>Closed</c>.</summary>
    public static void ReleaseTitleBars(Window window)
    {
        if (window.Content is not DependencyObject root) return;
        foreach (var bar in Descendants(root).OfType<TitleBar>()) Detach(bar);
    }

    static void Detach(TitleBar bar)
    {
        if (Handler == null || ThemeChanged?.EventHandlerType is not { } type)
        {
            if (!_reported) { _reported = true; Log.Warn("WPF-UI : gestionnaire de thème de la barre de titre introuvable, les fenêtres fermées resteront retenues"); }
            return;
        }
        try { ThemeChanged.RemoveEventHandler(null, Delegate.CreateDelegate(type, bar, Handler)); }
        catch (Exception ex) when (ex is ArgumentException or TargetException or MethodAccessException)
        {
            if (!_reported) { _reported = true; Log.Warn("WPF-UI : désabonnement de la barre de titre impossible : " + ex.Message); }
        }
    }

    /// <summary>
    /// Éléments de l'arbre visuel et de l'arbre logique, chacun une seule fois. Un panneau a les mêmes enfants dans les deux arbres : sans mémoire des
    /// éléments vus, chaque niveau doublait le parcours (mesuré sur les paramètres : 44,8 millions de visites pour 2 552 éléments, 6,6 s de gel à la fermeture).
    /// </summary>
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            int count = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(current) : 0;
            for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(current, i));
            foreach (var logical in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>()) stack.Push(logical);
        }
    }
}
