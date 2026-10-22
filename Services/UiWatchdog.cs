using System;
using System.Threading;
using System.Windows.Threading;

namespace TouchDeck;

/// <summary>
/// Chien de garde de l'interface : une fois par seconde il demande au thread d'interface de répondre. Sans réponse après
/// <see cref="Threshold"/>, il le consigne (avec la durée), puis consigne le retour à la normale. Un deck « figé » laisse ainsi une trace
/// dans touchdeck.log au lieu de rien.
/// </summary>
internal sealed class UiWatchdog : IDisposable
{
    /// <summary>Durée sans réponse à partir de laquelle l'interface est dite bloquée.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(3);

    readonly Dispatcher _dispatcher;
    readonly Func<long> _clock;
    readonly Action<string> _report;
    readonly Timer? _timer;
    long _answeredAt;
    int _pending;
    bool _blocked;

    /// <summary>Démarre la surveillance du dispatcher donné (une vérification par seconde).</summary>
    public UiWatchdog(Dispatcher dispatcher) : this(dispatcher, () => Environment.TickCount64, Log.Warn, startTimer: true) { }

    /// <summary>Variante testable : horloge et sortie injectées, sans minuterie (appeler <see cref="Check"/>).</summary>
    internal UiWatchdog(Dispatcher dispatcher, Func<long> clock, Action<string> report, bool startTimer = false)
    {
        _dispatcher = dispatcher;
        _clock = clock;
        _report = report;
        _answeredAt = clock();
        if (startTimer) _timer = new Timer(_ => Check(), null, 1000, 1000);
    }

    /// <summary>Une vérification : consigne un blocage ou un retour, puis relance une demande de réponse si aucune n'est en attente.</summary>
    internal void Check()
    {
        long silent = _clock() - Interlocked.Read(ref _answeredAt);
        if (silent >= Threshold.TotalMilliseconds)
        {
            if (!_blocked) { _blocked = true; _report($"Interface sans réponse depuis {silent} ms"); }
        }
        else if (_blocked)
        {
            _blocked = false;
            _report("Interface de nouveau réactive");
        }
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            Interlocked.Exchange(ref _answeredAt, _clock());
            Interlocked.Exchange(ref _pending, 0);
        });
    }

    public void Dispose() => _timer?.Dispose();
}
