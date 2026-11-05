using System;
using System.Collections.Generic;

namespace StreamDak;

/// <summary>
/// Écartement des touches voisines pendant un glissé : celles qui entourent la case visée s'écartent d'elle de quelques points, pour montrer où le bouton va se poser.
/// Logique pure (cases numérotées ligne par ligne) : les offsets sont appliqués et animés par la vue.
/// </summary>
public static class DragSpread
{
    /// <summary>Écartement maximal (DIP) d'une voisine directe.</summary>
    public const double Amount = 10;

    /// <summary>
    /// Décalage (x, y) de chaque case de <paramref name="cells"/> quand le bouton de <paramref name="sourceCell"/> survole <paramref name="targetCell"/>.
    /// Les huit voisines de la cible s'en écartent (les diagonales de 70 %) ; la cible, la source et les cases éloignées ne bougent pas.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Offsets(int columns, IReadOnlyList<int> cells, int sourceCell, int targetCell, double amount = Amount)
    {
        var result = new (double X, double Y)[cells.Count];
        if (columns <= 0 || targetCell < 0 || targetCell == sourceCell) return result;
        int tr = targetCell / columns, tc = targetCell % columns;
        for (int i = 0; i < cells.Count; i++)
        {
            int cell = cells[i];
            if (cell < 0 || cell == targetCell || cell == sourceCell) continue;
            int dr = cell / columns - tr, dc = cell % columns - tc;
            if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1) continue;
            double k = dr != 0 && dc != 0 ? 0.7 : 1;
            result[i] = (dc * amount * k, dr * amount * k);
        }
        return result;
    }
}
