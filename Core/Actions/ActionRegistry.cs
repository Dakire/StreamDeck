using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Actions;

/// <summary>
/// Types d'action disponibles : un gestionnaire par identifiant (sans distinction de casse). Deux gestionnaires pour le
/// même type sont refusés au lieu de laisser l'un masquer l'autre en silence.
/// </summary>
public sealed class ActionRegistry
{
    readonly Dictionary<string, IActionHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registre contenant <paramref name="handlers"/> ; lève <see cref="InvalidOperationException"/> si un type est en double.</summary>
    public ActionRegistry(IEnumerable<IActionHandler> handlers)
    {
        foreach (var handler in handlers) Register(handler);
    }

    /// <summary>Registre des types fournis avec StreamDak (<see cref="BuiltInActions.All"/>).</summary>
    public static ActionRegistry CreateDefault() => new(BuiltInActions.All);

    /// <summary>Identifiants des types disponibles.</summary>
    public IReadOnlyCollection<string> Types => _handlers.Keys;

    /// <summary>Ajoute un gestionnaire ; lève <see cref="InvalidOperationException"/> si son type existe déjà.</summary>
    public void Register(IActionHandler handler)
    {
        if (string.IsNullOrWhiteSpace(handler.Type)) throw new InvalidOperationException("Un type d'action doit avoir un identifiant");
        if (!_handlers.TryAdd(handler.Type, handler))
            throw new InvalidOperationException($"Le type d'action « {handler.Type} » est déjà enregistré ({_handlers[handler.Type].GetType().Name})");
    }

    /// <summary>Gestionnaire du type donné, ou false s'il n'existe pas.</summary>
    public bool TryGet(string? type, out IActionHandler handler)
    {
        handler = null!;
        return type != null && _handlers.TryGetValue(type, out handler!);
    }

    /// <summary>Identifiants triés, pour les tests et les messages.</summary>
    public override string ToString() => string.Join(", ", _handlers.Keys.OrderBy(k => k, StringComparer.Ordinal));
}
