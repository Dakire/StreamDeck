using System;
using System.Threading;
using System.Windows.Threading;

namespace StreamDak;

/// <summary>
/// Chien de garde de l'interface : plusieurs fois par seconde il poste une demande au thread d'interface et mesure le temps qu'elle met à être traitée.
/// Au-delà de <see cref="Threshold"/> il le consigne (durée, tâche en cours si <see cref="LongTask"/> la connaît, priorité de la dernière opération)
/// et alimente <see cref="UiPauses"/> (compteur des Paramètres › Diagnostic). Un deck « figé » ou « saccadé » laisse ainsi une trace dans streamdak.log.
/// </summary>
/// <remarks>
/// Pas de capture de pile du thread d'interface : .NET ne sait plus suspendre un thread pour lire sa pile, et un échantillonnage EventPipe demande un
/// processus externe. Le nom de la tâche (<see cref="LongTask.Run(string, Action)"/>) et la priorité de l'opération en cours en tiennent lieu.
/// </remarks>
internal sealed class UiWatchdog : IDisposable
{
    /// <summary>Durée sans réponse à partir de laquelle l'interface est dite en pause.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(200);
    /// <summary>Période de vérification, deck visible.</summary>
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(100);
    /// <summary>Période de vérification, deck masqué ou mode économie : une pause n'a plus d'importance pour personne.</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1);
    // Une durée plus longue n'est pas une pause mais une mise en veille de la machine
    static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(60);

    readonly Dispatcher _dispatcher;
    readonly Func<long> _clock;
    readonly Action<string> _report;
    readonly Timer? _timer;
    readonly DispatcherHookEventHandler? _started;
    long _sentAt;
    int _pending;
    volatile string _priority = "?";
    volatile string? _blockedContext;

    /// <summary>Démarre la surveillance du dispatcher donné.</summary>
    public UiWatchdog(Dispatcher dispatcher) : this(dispatcher, () => Environment.TickCount64, Log.Warn, startTimer: true) { }

    /// <summary>Variante testable : horloge et sortie injectées, sans minuterie (appeler <see cref="Check"/>).</summary>
    internal UiWatchdog(Dispatcher dispatcher, Func<long> clock, Action<string> report, bool startTimer = false)
    {
        _dispatcher = dispatcher;
        _clock = clock;
        _report = report;
        if (startTimer)
        {
            _started = (_, a) => _priority = a.Operation.Priority.ToString();
            dispatcher.Hooks.OperationStarted += _started;
            _timer = new Timer(_ => Check(), null, ActiveInterval, ActiveInterval);
        }
    }

    /// <summary>Le deck est visible (période courte) ou masqué (période longue).</summary>
    public void SetBackground(bool background) =>
        _timer?.Change(background ? IdleInterval : ActiveInterval, background ? IdleInterval : ActiveInterval);

    /// <summary>Une vérification : consigne un blocage en cours, puis poste une demande de réponse si aucune n'est en attente.</summary>
    internal void Check()
    {
        long now = _clock();
        if (Volatile.Read(ref _pending) == 1)
        {
            long silent = now - Interlocked.Read(ref _sentAt);
            if (silent >= Threshold.TotalMilliseconds && silent < SleepGap.TotalMilliseconds && _blockedContext == null)
            {
                _blockedContext = Context();   // pris pendant le blocage : c'est la tâche fautive qui est encore en cours
                _report($"Interface sans réponse depuis {silent} ms ({_blockedContext})");
            }
            return;
        }
        Interlocked.Exchange(ref _sentAt, now);
        Volatile.Write(ref _pending, 1);
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            long latency = _clock() - Interlocked.Read(ref _sentAt);
            var context = _blockedContext ?? Context();
            if (latency >= Threshold.TotalMilliseconds && latency < SleepGap.TotalMilliseconds)
            {
                UiPauses.Record(latency, context);
                _report(_blockedContext != null ? $"Interface de nouveau réactive après {latency} ms" : $"Pause de l'interface : {latency} ms ({context})");
            }
            _blockedContext = null;
            Volatile.Write(ref _pending, 0);
        });
    }

    string Context() => $"tâche : {LongTask.Current ?? "inconnue"}, dernière priorité : {_priority}";

    public void Dispose()
    {
        _timer?.Dispose();
        if (_started != null) _dispatcher.Hooks.OperationStarted -= _started;
    }
}
