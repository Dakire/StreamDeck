using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>
/// Tuile d'un widget de données (<see cref="IWidget"/>) : elle relève la mesure à son intervalle tant qu'elle existe, c'est-à-dire tant que sa
/// page est affichée (le deck la libère quand la page change), et se met en pause quand le deck est masqué (<see cref="SetPaused"/>).
/// </summary>
public sealed partial class DataWidgetViewModel : WidgetTileViewModel, IDisposable
{
    readonly IWidget _widget;
    readonly WidgetContext _context;
    readonly Action<Action> _post;
    readonly object _gate = new();
    CancellationTokenSource? _stop;
    CancellationTokenSource? _sleep;
    TaskCompletionSource? _resume;
    bool _disposed;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReading), nameof(HasStatus))] string _status = "Mesure en cours…";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] string _headline = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] string _detail = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPercent))] double? _percent;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGood), nameof(IsWarning), nameof(IsDanger))] WidgetLevel _level;

    /// <summary>Tuile pour <paramref name="widget"/> ; <paramref name="post"/> exécute une action sur le thread d'interface.</summary>
    public DataWidgetViewModel(IWidget widget, WidgetContext context, TimeSpan interval, Action<Action> post)
    {
        _widget = widget;
        _context = context;
        _post = post;
        Interval = interval;
    }

    /// <summary>Intervalle entre deux relevés.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Nom du widget (en-tête de la tuile).</summary>
    public string Title => _widget.Info.Name;

    /// <summary>Icône de l'en-tête.</summary>
    public SymbolRegular Symbol => Enum.TryParse<SymbolRegular>(_widget.Info.Icon, out var symbol) ? symbol : SymbolRegular.Apps24;

    /// <summary>true quand une mesure est affichée (et pas un message).</summary>
    public bool HasReading => Status.Length == 0;
    /// <summary>true quand un message (mesure en cours, erreur) remplace la mesure.</summary>
    public bool HasStatus => Status.Length > 0;
    /// <summary>true si un détail est affiché sous la valeur.</summary>
    public bool HasDetail => Detail.Length > 0;
    /// <summary>true si la jauge est affichée.</summary>
    public bool HasPercent => Percent != null;
    /// <summary>Tout va bien : valeur en vert.</summary>
    public bool IsGood => Level == WidgetLevel.Good;
    /// <summary>À surveiller : valeur en ambre.</summary>
    public bool IsWarning => Level == WidgetLevel.Warning;
    /// <summary>Problème : valeur en couleur d'alerte.</summary>
    public bool IsDanger => Level == WidgetLevel.Danger;

    /// <summary>true tant que les relevés sont suspendus (deck masqué).</summary>
    public bool IsPaused { get { lock (_gate) return _resume != null; } }

    /// <summary>true une fois la tuile libérée : plus aucun relevé ne part.</summary>
    public bool IsStopped { get { lock (_gate) return _disposed; } }

    /// <summary>Démarre les relevés : un tout de suite, puis un par intervalle. Sans effet s'ils tournent déjà ou si la tuile est libérée.</summary>
    public void Start()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || _stop != null) return;
            _stop = new CancellationTokenSource();
            token = _stop.Token;
        }
        _ = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    /// <summary>Suspend (true) ou reprend (false) les relevés ; à la reprise, la mesure est relue tout de suite.</summary>
    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (paused) _resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            else if (_resume != null)
            {
                _resume.TrySetResult();
                _resume = null;
                CancelSleep();
            }
        }
    }

    async Task RunAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Task? paused;
                lock (_gate) paused = _resume?.Task;
                if (paused != null) { await paused.WaitAsync(stop); continue; }
                await ReadOnceAsync(stop);
                await SleepAsync(stop);
            }
        }
        catch (OperationCanceledException) { /* tuile libérée */ }
    }

    async Task SleepAsync(CancellationToken stop)
    {
        using var sleep = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_gate) _sleep = sleep;
        try { await Task.Delay(Interval, sleep.Token); }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { /* reprise : relevé immédiat */ }
        finally { lock (_gate) { if (ReferenceEquals(_sleep, sleep)) _sleep = null; } }
    }

    void CancelSleep()
    {
        try { _sleep?.Cancel(); }
        catch (ObjectDisposedException) { /* l'attente venait de se terminer */ }
    }

    /// <summary>Un relevé : met la tuile à jour avec la mesure, ou avec le message de l'erreur (journalisée).</summary>
    public async Task ReadOnceAsync(CancellationToken cancellation)
    {
        try
        {
            var reading = await _widget.ReadAsync(_context, cancellation);
            _post(() => Show(reading));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"Widget « {_widget.Info.Id} » : {ex.Message}");   // le message d'un widget ne contient jamais de secret : aucun n'en reçoit
            _post(() => ShowStatus(ex is TimeoutException or InvalidOperationException ? ex.Message : "Mesure impossible : " + ex.Message));
        }
    }

    /// <summary>Affiche une mesure.</summary>
    public void Show(WidgetReading reading)
    {
        Headline = reading.Headline;
        Detail = reading.Detail;
        Percent = reading.Percent;
        Level = reading.Level;
        Status = "";
    }

    /// <summary>Remplace la mesure par un message.</summary>
    public void ShowStatus(string message)
    {
        Status = message.Length == 0 ? "Mesure impossible" : message;
        Headline = "";
        Detail = "";
        Percent = null;
        Level = WidgetLevel.Normal;
    }

    /// <summary>Arrête les relevés : la page n'est plus affichée.</summary>
    public void Dispose()
    {
        CancellationTokenSource? stop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            stop = _stop;
            _resume?.TrySetResult();
        }
        try { stop?.Cancel(); }
        catch (ObjectDisposedException) { /* déjà arrêtée */ }
        stop?.Dispose();
    }
}
