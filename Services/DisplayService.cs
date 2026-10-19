using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TouchDeck;

public sealed record DisplayInfo(int Index, string Device, int Width, int Height, bool Primary)
{
    public string Name => Primary ? $"Écran {Index + 1} (principal)" : $"Écran {Index + 1}";
    public string Resolution => $"{Width}×{Height}";
    public override string ToString() => $"{Name} · {Resolution}";
}

/// <summary>
/// Écrans disponibles et écran cible du deck. C'est le « périphérique » de TouchDeck : branchement et débranchement
/// d'un écran tactile sont détectés (Windows signale tout changement de configuration d'affichage).
/// </summary>
public static class DisplayService
{
    public static event Action? Changed;

    static bool _hooked;

    /// <summary>À appeler une fois depuis le thread d'interface.</summary>
    public static void Start()
    {
        if (_hooked) return;
        _hooked = true;
        var dispatcher = Application.Current.Dispatcher;
        SystemEvents.DisplaySettingsChanged += (_, _) => dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    public static List<DisplayInfo> All() =>
        Forms.Screen.AllScreens
            .Select((s, i) => new DisplayInfo(i, s.DeviceName, s.Bounds.Width, s.Bounds.Height, s.Primary))
            .ToList();

    /// <summary>Écran visé par la configuration : index explicite, ou -1 = premier écran secondaire. Null si absent.</summary>
    public static Forms.Screen? ResolveTarget(int monitor)
    {
        var screens = Forms.Screen.AllScreens;
        if (monitor >= 0) return monitor < screens.Length ? screens[monitor] : null;
        return screens.FirstOrDefault(s => !s.Primary);
    }

    /// <summary>Résumé de l'état pour l'interface : (texte, connecté).</summary>
    public static (string Text, bool Connected) Describe(int monitor)
    {
        var all = All();
        if (monitor >= 0)
        {
            return monitor < all.Count
                ? ($"{all[monitor].Name} · {all[monitor].Resolution} — connecté", true)
                : ($"Écran {monitor + 1} absent — le deck reste sur l'écran courant", false);
        }
        var second = all.FirstOrDefault(d => !d.Primary);
        return second != null
            ? ($"{second.Name} · {second.Resolution} — connecté (automatique)", true)
            : ("Aucun écran secondaire — le deck s'affiche en fenêtre", false);
    }
}
