using System;
using System.Collections.Generic;

namespace StreamDak.Hotkeys;

/// <summary>Raccourcis globaux simulés (tests et mode <c>--simulate</c>) : rien n'est demandé au système ; <see cref="Press"/> joue une pression.</summary>
public sealed class SimulatedHotkeyService : IHotkeyService
{
    readonly Dictionary<int, string> _registered = new();

    /// <summary>Combinaisons que le « système » refuse (déjà prises par une autre application).</summary>
    public HashSet<string> Taken { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Raccourcis actuellement enregistrés (identifiant → combinaison).</summary>
    public IReadOnlyDictionary<int, string> Registered => _registered;

    /// <inheritdoc/>
    public event Action<int>? Pressed;

    /// <inheritdoc/>
    public HotkeyResult Register(int id, string combo)
    {
        if (Taken.Contains(combo)) return new HotkeyResult(false, "Déjà utilisé par une autre application");
        _registered[id] = combo;
        return new HotkeyResult(true);
    }

    /// <inheritdoc/>
    public void Unregister(int id) => _registered.Remove(id);

    /// <summary>Joue la pression du raccourci enregistré sous cet identifiant.</summary>
    public void Press(int id) { if (_registered.ContainsKey(id)) Pressed?.Invoke(id); }

    /// <inheritdoc/>
    public void Dispose() => _registered.Clear();
}
