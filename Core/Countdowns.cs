using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchDeck;

/// <summary>
/// Comptes à rebours des boutons « minuteur ». État d'exécution uniquement : rien n'est enregistré dans la configuration,
/// et un rechargement de config (nouveaux objets bouton) les abandonne. À utiliser depuis le thread d'interface.
/// </summary>
public sealed class Countdowns
{
    readonly Func<DateTime> _now;
    readonly Dictionary<DeckButton, DateTime> _ends = new(ReferenceEqualityComparer.Instance);

    /// <summary>Un compte à rebours vient d'arriver à zéro (signalé par <see cref="Tick"/>).</summary>
    public event Action<DeckButton>? Finished;

    public Countdowns(Func<DateTime>? now = null) { _now = now ?? (() => DateTime.UtcNow); }

    public bool Any => _ends.Count > 0;

    /// <summary>Démarre le compte à rebours de ce bouton, ou l'arrête s'il tourne. Retourne true s'il vient de démarrer.</summary>
    public bool Toggle(DeckButton button, TimeSpan duration)
    {
        if (_ends.Remove(button)) return false;
        _ends[button] = _now() + duration;
        return true;
    }

    public void Stop(DeckButton button) => _ends.Remove(button);

    /// <summary>Temps restant (arrondi à la seconde supérieure), ou null si le bouton n'a pas de minuteur en cours.</summary>
    public TimeSpan? Remaining(DeckButton button)
    {
        if (!_ends.TryGetValue(button, out var end)) return null;
        var left = end - _now();
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
    }

    /// <summary>Signale et retire les comptes à rebours arrivés à zéro.</summary>
    public void Tick()
    {
        var now = _now();
        foreach (var b in _ends.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
        {
            _ends.Remove(b);
            Finished?.Invoke(b);
        }
    }

    /// <summary>« 4:05 », « 1:02:03 ».</summary>
    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}
