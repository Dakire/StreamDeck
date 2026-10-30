using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Integrations;

/// <summary>Ordre des intégrations choisi par l'utilisateur (<see cref="DeckConfig.IntegrationOrder"/>) : paramètres et catalogue.</summary>
public static class IntegrationOrder
{
    /// <summary>
    /// Les éléments dans l'ordre enregistré ; ceux qui n'y figurent pas suivent dans leur ordre d'origine, un identifiant inconnu est ignoré.
    /// Sans ordre enregistré, l'ordre d'origine est conservé.
    /// </summary>
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, IReadOnlyList<string>? saved, Func<T, string> id)
    {
        var list = items.ToList();
        if (saved == null || saved.Count == 0) return list;
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in saved) rank.TryAdd(s, rank.Count);
        return list.Select((item, position) => (item, position))
            .OrderBy(x => rank.TryGetValue(id(x.item), out var r) ? r : int.MaxValue)
            .ThenBy(x => x.position)
            .Select(x => x.item).ToList();
    }

    /// <summary>Identifiants dans l'ordre donné, tels qu'ils s'écrivent dans la configuration.</summary>
    public static List<string> Ids<T>(IEnumerable<T> items, Func<T, string> id) => items.Select(id).ToList();
}
