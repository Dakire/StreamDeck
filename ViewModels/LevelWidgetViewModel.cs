using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Audio;

namespace StreamDak.ViewModels;

/// <summary>
/// Tuile « Niveau audio » : une roue ou un curseur lié à un canal Sonar ou à un périphérique audio de Windows. Elle montre le niveau réel (relu à chaque
/// changement signalé par la source, ou à intervalle régulier pour Windows), envoie ce que l'utilisateur règle (un envoi à la fois, toujours le dernier niveau)
/// et se met en pause quand le deck est masqué. Après un geste, les relevés sont ignorés un instant pour que la roue ne revienne pas en arrière avant que
/// le système n'ait pris le nouveau niveau.
/// </summary>
public sealed partial class LevelWidgetViewModel : WidgetTileViewModel, IDisposable
{
    /// <summary>Durée pendant laquelle un relevé n'écrase pas le niveau que l'utilisateur vient de régler.</summary>
    public static readonly TimeSpan HoldAfterChange = TimeSpan.FromMilliseconds(2500);

    readonly ILevelSource? _source;
    readonly Action<Action> _post;
    readonly Func<long> _clock;
    readonly LevelSender _sender;
    readonly object _gate = new();
    Timer? _timer;
    long _holdUntil;
    bool _paused, _started, _disposed;

    [ObservableProperty] int _level;
    [ObservableProperty] bool _isMuted;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus), nameof(HasReading), nameof(IsAvailable), nameof(Availability))] string _status = "";

    /// <summary>Tuile pour <paramref name="source"/> (null : la cible du widget manque ou est illisible) ; <paramref name="post"/> exécute sur le thread d'interface.</summary>
    /// <param name="source">Ce que le contrôle règle.</param>
    /// <param name="shape">Roue ou curseur.</param>
    /// <param name="post">Exécute sur le thread d'interface.</param>
    /// <param name="clock">Horloge en millisecondes (tests).</param>
    /// <param name="delay">Attente entre deux envois (tests).</param>
    /// <param name="resetLevel">Niveau du bouton de réinitialisation ; null : celui de la cible.</param>
    public LevelWidgetViewModel(ILevelSource? source, LevelShape shape, Action<Action> post, Func<long>? clock = null, Func<TimeSpan, Task>? delay = null, int? resetLevel = null)
    {
        _source = source;
        Shape = shape;
        ResetLevel = Math.Clamp(resetLevel ?? source?.ResetLevel ?? 100, 0, 100);
        Symbol = IconCatalog.TryResolve(source?.Icon, out var symbol) ? symbol : Wpf.Ui.Controls.SymbolRegular.Speaker224;
        _post = post;
        _clock = clock ?? (() => Environment.TickCount64);
        _sender = new LevelSender(async percent => await Required().SetLevelAsync(percent), delay, ReportError);
        _status = source == null ? "Choisis un canal Sonar ou un périphérique dans les propriétés (mode édition)" : "Lecture du niveau…";   // jamais un faux zéro en attendant le premier relevé
    }

    ILevelSource Required() => _source ?? throw new InvalidOperationException("Aucune cible");

    /// <summary>Forme du contrôle.</summary>
    public LevelShape Shape { get; }
    /// <summary>true pour la roue.</summary>
    public bool IsWheel => Shape == LevelShape.Wheel;
    /// <summary>Nom affiché dans l'en-tête de la tuile.</summary>
    public string Title => _source?.Title ?? "Niveau audio";
    /// <summary>true quand un message remplace le contrôle.</summary>
    public bool HasStatus => Status.Length > 0;
    /// <summary>true quand le niveau est affiché.</summary>
    public bool HasReading => Status.Length == 0;
    /// <summary>true quand la cible répond (pastille verte) ; false : périphérique absent, Sonar ou Discord fermé (pastille grise, message à la place du contrôle).</summary>
    public bool IsAvailable => HasReading;
    /// <summary>Disponibilité en toutes lettres (lecteurs d'écran, info-bulle de la pastille).</summary>
    public string Availability => HasReading ? "Disponible" : "Indisponible : " + Status;
    /// <summary>Ce que le contrôle règle réellement (« Volume du canal Jeu dans le mélangeur Sonar »).</summary>
    public string Controls => _source?.Controls ?? "";
    /// <summary>true si la description de la cible existe.</summary>
    public bool HasControls => Controls.Length > 0;
    /// <summary>Icône de la cible.</summary>
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; }
    /// <summary>Niveau rendu par le bouton de réinitialisation.</summary>
    public int ResetLevel { get; }
    /// <summary>Texte du bouton de réinitialisation (« Revenir à 100 % »).</summary>
    public string ResetText => $"Revenir à {ResetLevel} %";
    /// <summary>true pour un curseur : la coupure a son propre bouton (la roue coupe au toucher du centre).</summary>
    public bool ShowMuteButton => !IsWheel;
    /// <summary>true si la tuile a la place d'afficher ses boutons (couper, réinitialiser) : au moins deux cases.</summary>
    public bool ShowActions => Math.Max(1, ColSpan) * Math.Max(1, RowSpan) >= 2;

    /// <summary>Remet le niveau de réinitialisation (réglage <c>reset</c> du widget, sinon celui de la cible).</summary>
    [RelayCommand]
    void Reset() => Change(ResetLevel);

    /// <summary>Appelé avec le message d'une erreur d'envoi (renseigné par le deck : barre d'état).</summary>
    public Action<string>? ErrorReported { get; set; }

    /// <summary>Le doigt règle un niveau (0 à 100) : affiché tout de suite, envoyé à la source.</summary>
    [RelayCommand]
    void Change(int percent)
    {
        if (_source == null) return;
        percent = Math.Clamp(percent, 0, 100);
        Hold();
        Level = percent;
        _sender.Post(percent);
    }

    /// <summary>Coupe ou rétablit.</summary>
    [RelayCommand]
    async Task ToggleMute()
    {
        if (_source == null) return;
        bool muted = !IsMuted;
        Hold();
        IsMuted = muted;
        try { await _source.SetMutedAsync(muted); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            IsMuted = !muted;
            ReportError(ex);
        }
    }

    void Hold() => Interlocked.Exchange(ref _holdUntil, _clock() + (long)HoldAfterChange.TotalMilliseconds);

    void ReportError(Exception ex)
    {
        var message = ex is System.Runtime.InteropServices.COMException ? "Périphérique audio injoignable" : ex.Message;
        _post(() => ErrorReported?.Invoke(message));
    }

    /// <summary>Démarre la lecture du niveau. Sans effet s'il a déjà démarré ou si la tuile est libérée.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started || _source == null) return;
            _started = true;
        }
        _source.Changed += OnSourceChanged;
        if (_source.PollInterval is { } interval && !_paused) _timer = new Timer(_ => Refresh(), null, interval, interval);
        Refresh();
    }

    void OnSourceChanged() => Refresh();

    /// <summary>Relit le niveau (sur n'importe quel thread) et l'affiche, sauf pendant le court délai qui suit un réglage de l'utilisateur.</summary>
    public void Refresh()
    {
        ILevelSource? source;
        lock (_gate) { if (_disposed || _paused) return; source = _source; }
        if (source == null) return;
        var reading = source.Read(out var problem);
        _post(() =>
        {
            lock (_gate) { if (_disposed) return; }
            OnPropertyChanged(nameof(Title));   // le nom du périphérique n'est connu qu'une fois trouvé
            OnPropertyChanged(nameof(Controls));
            OnPropertyChanged(nameof(HasControls));
            if (reading == null) { Status = problem; return; }
            Status = "";
            if (_clock() < Interlocked.Read(ref _holdUntil) || _sender.IsBusy) return;
            Level = reading.Percent;
            IsMuted = reading.Muted == true;
        });
    }

    /// <summary>Deck masqué (true) : les relevés s'arrêtent ; affiché (false) : ils reprennent et relisent tout de suite.</summary>
    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused) return;
            _paused = paused;
        }
        if (_source?.PollInterval is not { } interval) { if (!paused) Refresh(); return; }
        if (paused) { _timer?.Dispose(); _timer = null; return; }
        if (_started) _timer = new Timer(_ => Refresh(), null, interval, interval);
        Refresh();
    }

    /// <summary>true tant que les relevés sont suspendus.</summary>
    public bool IsPaused { get { lock (_gate) return _paused; } }
    /// <summary>true une fois la tuile libérée.</summary>
    public bool IsStopped { get { lock (_gate) return _disposed; } }

    /// <summary>Arrête les relevés et libère la source.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer?.Dispose();
        _timer = null;
        if (_source != null) _source.Changed -= OnSourceChanged;
        (_source as IDisposable)?.Dispose();
    }
}

/// <summary>
/// Widget « Mélangeur audio » : un curseur horizontal précis par canal (Sonar, Discord, périphérique Windows), chacun avec son nom, son icône, sa valeur,
/// sa coupure, sa réinitialisation et sa disponibilité. Chaque ligne est une <see cref="LevelWidgetViewModel"/> : mêmes relevés, même envoi limité.
/// </summary>
public sealed class MixerWidgetViewModel : WidgetTileViewModel, IDisposable
{
    /// <summary>Mélangeur des lignes données (dans l'ordre d'affichage).</summary>
    public MixerWidgetViewModel(IEnumerable<LevelWidgetViewModel> channels) { Channels = channels.ToList(); }

    /// <summary>Une ligne par canal.</summary>
    public IReadOnlyList<LevelWidgetViewModel> Channels { get; }

    /// <summary>Démarre les relevés de toutes les lignes.</summary>
    public void Start() { foreach (var c in Channels) c.Start(); }

    /// <summary>Suspend (deck masqué) ou reprend les relevés.</summary>
    public void SetPaused(bool paused) { foreach (var c in Channels) c.SetPaused(paused); }

    /// <summary>Arrête les relevés et libère les sources.</summary>
    public void Dispose() { foreach (var c in Channels) c.Dispose(); }
}
