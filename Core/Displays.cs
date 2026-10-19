using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchDeck;

/// <summary>Rectangle en pixels physiques (coordonnées du bureau virtuel).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Un écran : <see cref="Device"/> est l'identifiant Windows (\\.\DISPLAY2), stable tant que l'écran reste sur la même sortie.</summary>
public sealed record DisplayInfo(int Index, string Device, PixelRect Bounds, PixelRect WorkArea, bool Primary)
{
    /// <summary>Largeur en pixels physiques.</summary>
    public int Width => Bounds.Width;
    /// <summary>Hauteur en pixels physiques.</summary>
    public int Height => Bounds.Height;
    /// <summary>true si l'écran est plus haut que large.</summary>
    public bool IsPortrait => Height > Width;
    /// <summary>Nom affiché : « Écran 2 », « Écran 1 (principal) ».</summary>
    public string Name => Primary ? $"Écran {Index + 1} (principal)" : $"Écran {Index + 1}";
    /// <summary>Résolution affichée : « 1920×1080 », avec « portrait » le cas échéant.</summary>
    public string Resolution => $"{Width}×{Height}" + (IsPortrait ? " portrait" : "");
    /// <summary>Nom et résolution, pour les listes de choix.</summary>
    public override string ToString() => $"{Name} · {Resolution}";
}

/// <summary>Écrans disponibles. L'implémentation Windows signale tout branchement, débranchement ou changement de résolution.</summary>
public interface IDisplayService
{
    /// <summary>Écrans branchés, dans l'ordre de Windows.</summary>
    IReadOnlyList<DisplayInfo> All();
    /// <summary>Un écran a été branché, débranché ou reconfiguré (sur le thread d'interface).</summary>
    event Action? Changed;
}

/// <summary>Choix de l'écran du deck à partir de la configuration. Logique pure : testée sans écran réel.</summary>
public static class DisplayResolver
{
    /// <summary>
    /// Écran visé : l'identifiant mémorisé s'il est branché, sinon l'index (ancien réglage, ou écran renuméroté),
    /// sinon pour « automatique » (-1) le premier écran secondaire. Null si aucun ne convient.
    /// </summary>
    public static DisplayInfo? Resolve(IReadOnlyList<DisplayInfo> displays, int monitor, string? device)
    {
        if (!string.IsNullOrEmpty(device))
        {
            var byDevice = displays.FirstOrDefault(d => string.Equals(d.Device, device, StringComparison.OrdinalIgnoreCase));
            if (byDevice != null) return byDevice;
        }
        if (monitor >= 0) return monitor < displays.Count ? displays[monitor] : null;
        return displays.FirstOrDefault(d => !d.Primary);
    }

    /// <summary>Résumé pour l'interface : (texte, écran trouvé).</summary>
    public static (string Text, bool Connected) Describe(IReadOnlyList<DisplayInfo> displays, int monitor, string? device)
    {
        bool auto = monitor < 0 && string.IsNullOrEmpty(device);
        var target = Resolve(displays, monitor, device);
        if (target == null)
            return auto
                ? ("Aucun écran secondaire — le deck s'affiche en fenêtre", false)
                : ($"Écran {Math.Max(monitor, 0) + 1} absent — le deck reste sur l'écran courant", false);
        if (!string.IsNullOrEmpty(device) && !string.Equals(target.Device, device, StringComparison.OrdinalIgnoreCase))
            return ($"{target} — écran mémorisé absent, celui-ci le remplace", true);
        return ($"{target} — connecté{(auto ? " (automatique)" : "")}", true);
    }
}
