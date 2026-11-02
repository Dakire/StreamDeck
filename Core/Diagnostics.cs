using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace StreamDak;

/// <summary>Une pause du thread d'interface : quand, combien de temps, et ce qu'il faisait si on le sait.</summary>
public readonly record struct UiPause(DateTimeOffset At, long Milliseconds, string Context);

/// <summary>Résumé des pauses de l'interface depuis le lancement.</summary>
public sealed record UiPauseSummary(int Count, long TotalMilliseconds, long MaxMilliseconds, int LongTasks, IReadOnlyList<UiPause> Recent);

/// <summary>
/// Compteur des pauses du thread d'interface (alimenté par le chien de garde) et des tâches longues (alimenté par <see cref="LongTask"/>).
/// Affiché dans Paramètres › Diagnostic et dans le diagnostic copiable ; n'écrit jamais rien d'autre que des durées et des noms d'opérations.
/// </summary>
public static class UiPauses
{
    /// <summary>Nombre de pauses récentes gardées.</summary>
    public const int MaxRecent = 20;

    static readonly object Gate = new();
    static readonly Queue<UiPause> Recent = new();
    static int _count, _longTasks;
    static long _total, _max;

    /// <summary>Enregistre une pause de l'interface.</summary>
    public static void Record(long milliseconds, string context)
    {
        lock (Gate)
        {
            _count++;
            _total += milliseconds;
            _max = Math.Max(_max, milliseconds);
            Recent.Enqueue(new UiPause(DateTimeOffset.Now, milliseconds, context));
            while (Recent.Count > MaxRecent) Recent.Dequeue();
        }
    }

    /// <summary>Compte une tâche longue repérée par <see cref="LongTask"/>.</summary>
    public static void RecordLongTask() => Interlocked.Increment(ref _longTasks);

    /// <summary>État actuel du compteur.</summary>
    public static UiPauseSummary Snapshot()
    {
        lock (Gate) return new UiPauseSummary(_count, _total, _max, Volatile.Read(ref _longTasks), Recent.ToList());
    }

    /// <summary>Remet le compteur à zéro (tests, bouton de diagnostic).</summary>
    public static void Reset()
    {
        lock (Gate) { _count = 0; _total = 0; _max = 0; Recent.Clear(); }
        Interlocked.Exchange(ref _longTasks, 0);
    }
}

/// <summary>
/// Trace des « tâches longues » : entoure un traitement de l'interface (tick de minuterie, reconstruction de la grille) d'un chronomètre et le consigne
/// au-delà du seuil. Le nom de la tâche en cours sert aussi de contexte au chien de garde quand l'interface ne répond plus.
/// </summary>
public static class LongTask
{
    /// <summary>Durée à partir de laquelle un traitement est consigné.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(150);

    static readonly Dictionary<string, long> LastLogged = new();
    static volatile string? _current;

    /// <summary>Nom de la tâche en cours d'exécution sur le thread d'interface, ou null (mesure approchée : un seul thread la renseigne).</summary>
    public static string? Current => _current;

    /// <summary>Sortie des messages (remplaçable par les tests).</summary>
    public static Action<string> Sink { get; set; } = Log.Warn;

    /// <summary>Exécute <paramref name="action"/> et consigne sa durée si elle dépasse <see cref="Threshold"/>.</summary>
    public static void Run(string name, Action action)
    {
        var previous = _current;
        _current = name;
        long start = Stopwatch.GetTimestamp();
        try { action(); }
        finally
        {
            _current = previous;
            Report(name, Stopwatch.GetElapsedTime(start));
        }
    }

    /// <summary>Variante qui rend une valeur.</summary>
    public static T Run<T>(string name, Func<T> function)
    {
        T result = default!;
        Run(name, (Action)(() => result = function()));   // (Action) : sans lui la lambda se résoudrait vers cette même surcharge et bouclerait
        return result;
    }

    /// <summary>Consigne une durée déjà mesurée (au plus une ligne toutes les 5 s par nom : une tâche lente répétée n'inonde pas le journal).</summary>
    internal static void Report(string name, TimeSpan elapsed)
    {
        if (elapsed < Threshold) return;
        UiPauses.RecordLongTask();
        long now = Environment.TickCount64;
        lock (LastLogged)
        {
            if (LastLogged.TryGetValue(name, out var last) && now - last < 5_000) return;
            LastLogged[name] = now;
        }
        Sink($"Tâche longue : {name} a pris {elapsed.TotalMilliseconds:0} ms");
    }
}
