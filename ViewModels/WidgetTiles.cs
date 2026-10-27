using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

/// <summary>
/// Tuile d'un widget (hors mode édition) : elle affiche une information au lieu de lancer une action. Un toucher bref n'a pas d'effet ;
/// un appui long ou un clic droit ouvre le menu de la case, comme pour un bouton.
/// </summary>
public abstract partial class WidgetTileViewModel : PlacedTile, IEditableTile
{
    /// <summary>Fond teinté et liseré de la couleur du bouton (même rendu que les touches).</summary>
    [ObservableProperty] TileFaceData _face = TileFaceData.Empty;

    /// <summary>Bouton de la configuration représenté.</summary>
    public required DeckButton Button { get; init; }
    /// <inheritdoc/>
    public ICommand? Edit { get; init; }
    /// <summary>Nom accessible du widget.</summary>
    public string AccessibleName { get; init; } = "";
}

/// <summary>Widget horloge : l'heure en grand, la date dessous.</summary>
public sealed partial class ClockWidgetViewModel : WidgetTileViewModel
{
    [ObservableProperty] string _time = "";
    [ObservableProperty] string _date = "";

    /// <summary>true si la date est affichée.</summary>
    public bool ShowDate => Button.OptionFlag(WidgetOptionKeys.Date, true);

    /// <summary>true si l'heure change chaque seconde (secondes affichées).</summary>
    public bool NeedsSeconds => Button.OptionFlag(WidgetOptionKeys.Seconds, false);

    /// <summary>Recalcule l'heure et la date.</summary>
    public void Update(DateTime now)
    {
        Time = WidgetClock.Time(now, NeedsSeconds, Button.OptionFlag(WidgetOptionKeys.Hour12, false));
        Date = ShowDate ? WidgetClock.Date(now) : "";
    }
}

/// <summary>Un événement de l'agenda affiché par le widget.</summary>
/// <param name="When">Heure de début (« 14:30 »), « Toute la journée » ou le jour pour un événement à venir.</param>
/// <param name="Title">Titre de l'événement.</param>
/// <param name="Day">Libellé du jour (« Aujourd'hui », « Demain », « Lundi 26 »), vide si le même que l'événement précédent.</param>
/// <param name="IsNow">true si l'événement est en cours.</param>
public sealed record AgendaEntry(string When, string Title, string Day, bool IsNow);

/// <summary>Widget agenda : les prochains événements, ou l'explication de ce qui manque (compte non connecté, aucun événement).</summary>
public sealed partial class AgendaWidgetViewModel : WidgetTileViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus), nameof(HasEntries))] string _status = "";

    /// <summary>Événements affichés, dans l'ordre.</summary>
    public ObservableCollection<AgendaEntry> Entries { get; } = new();

    /// <summary>true si un message remplace la liste.</summary>
    public bool HasStatus => Status.Length > 0;
    /// <summary>true si la liste a des événements à montrer.</summary>
    public bool HasEntries => Status.Length == 0 && Entries.Count > 0;

    /// <summary>Remplace le contenu : une liste d'événements, ou un message si elle est vide.</summary>
    public void Show(System.Collections.Generic.IReadOnlyList<AgendaEntry> entries, string status)
    {
        Entries.Clear();
        foreach (var e in entries) Entries.Add(e);
        Status = status;
        OnPropertyChanged(nameof(HasEntries));
    }
}

/// <summary>Widget page web : l'adresse à afficher dans un cadre isolé (WebView2), et ce que la vue en dit (échec, saisie au clavier, masqué).</summary>
public sealed partial class WebWidgetViewModel : WidgetTileViewModel
{
    string _failure = "";

    /// <summary>Adresse affichée, ou null si elle manque ou n'est pas une adresse http(s).</summary>
    public Uri? Source { get; init; }

    /// <summary>
    /// Identité du cadre : tant qu'elle ne change pas, la page chargée est gardée quand la tuile quitte l'écran (changement de page du deck) et retrouvée
    /// telle quelle au retour, sans rechargement. Null : le cadre est libéré avec la tuile.
    /// </summary>
    public string? FrameKey { get; init; }

    /// <summary>Nom du site (en-tête de la tuile).</summary>
    public string Title => Source?.Host ?? "Page web";

    /// <summary>Message à la place de la page : adresse manquante, page injoignable, composant WebView2 absent. Vide quand la page s'affiche.</summary>
    public string Status => _failure.Length > 0 ? _failure : Source == null ? MissingAddressMessage : "";

    /// <summary>Message quand l'adresse manque ou n'est pas valide (un widget et une page web ne se règlent pas au même endroit).</summary>
    public string MissingAddressMessage { get; init; } = "Renseigne l'adresse de la page dans les propriétés (mode édition)";

    /// <summary>true si un message remplace la page.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>true tant qu'un menu ou une boîte de dialogue est ouvert : le cadre est une fenêtre Win32 qui masquerait leur contenu.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPage), nameof(ShowTouchShield), nameof(ShowPassiveShield))] bool _suspended;

    /// <summary>true quand le cadre de la page est affiché.</summary>
    public bool ShowPage => !HasStatus && !Suspended;

    string _inputMode = WebInputModes.Default;

    /// <summary>
    /// Interaction avec la souris et le doigt (<see cref="WebInputModes"/>). Hors du mode interactif, le cadre est désactivé : il ne capte plus rien et ce qui le
    /// touche revient au deck (appui long, clic droit).
    /// </summary>
    public string InputMode
    {
        get => _inputMode;
        init
        {
            _inputMode = WebInputModes.Normalize(value);
            IsInteracting = _inputMode == WebInputModes.Interactive;
        }
    }

    /// <summary>true si la page reçoit la souris, le doigt et le clavier (le cadre est activé).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTouchShield), nameof(ShowPassiveShield), nameof(InteractHint), nameof(HasInteractHint))] bool _isInteracting;

    /// <summary>true en mode interactif permanent : pas de bouton « Interagir », rien ne se termine seul.</summary>
    public bool IsAlwaysInteractive => InputMode == WebInputModes.Interactive;
    /// <summary>true si le bouton « Interagir » / « Terminer » est proposé.</summary>
    public bool CanToggleInteraction => !IsAlwaysInteractive;
    /// <summary>Mode tactile, page au repos : un toucher sur la page l'ouvre à l'interaction.</summary>
    public bool ShowTouchShield => ShowPage && !IsInteracting && InputMode == WebInputModes.Touch;
    /// <summary>Mode passif, page au repos : la page ne réagit à rien (appui long et clic droit vont au deck).</summary>
    public bool ShowPassiveShield => ShowPage && !IsInteracting && InputMode == WebInputModes.Passive;
    /// <summary>Ce qu'il faut faire pour se servir de la page, affiché dans la bande du cadre ; vide pendant l'interaction.</summary>
    public string InteractHint => IsInteracting ? "" : InputMode == WebInputModes.Touch ? "Toucher la page pour interagir" : "Affichage seul";
    /// <summary>true si l'indication est affichée.</summary>
    public bool HasInteractHint => InteractHint.Length > 0;

    /// <summary>La page reçoit la souris et le doigt (toucher en mode tactile, bouton « Interagir »).</summary>
    [RelayCommand]
    void Interact()
    {
        if (!IsInteracting) IsInteracting = true;
    }

    /// <summary>Fin de l'interaction (bouton « Terminer » ou délai sans geste) : la saisie au clavier s'arrête, la page redevient passive. Sans effet en mode interactif.</summary>
    [RelayCommand]
    public void EndInteraction()
    {
        if (IsAlwaysInteractive || !IsInteracting) return;
        IsInteracting = false;
        if (IsTyping) { AutoTyping = false; TypingRequested?.Invoke(false); }
    }

    [RelayCommand]
    void ToggleInteraction()
    {
        if (IsInteracting) EndInteraction(); else Interact();
    }

    /// <summary>true pendant la saisie au clavier : la fenêtre du deck est activable et le focus est dans la page.</summary>
    [ObservableProperty] bool _isTyping;

    /// <summary>Zoom de la page en pourcentage (25 à 500) ; la vue l'applique au cadre, le deck l'enregistre.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ZoomText))] int _zoomPercent = WebZoom.Normal;

    /// <summary>Zoom affiché entre les boutons − et + (« 125 % »).</summary>
    public string ZoomText => $"{ZoomPercent} %";

    /// <summary>Appelé quand l'utilisateur change le zoom (boutons ou pincement) : le deck l'enregistre (renseigné par le deck).</summary>
    public Action<int>? ZoomChanged { get; set; }

    /// <summary>Change le zoom (borné) ; sans effet si c'est déjà celui-là.</summary>
    public void SetZoom(int percent)
    {
        percent = WebZoom.Clamp(percent);
        if (percent == ZoomPercent) return;
        ZoomPercent = percent;
        ZoomChanged?.Invoke(percent);
    }

    [RelayCommand]
    void ZoomIn() => SetZoom(WebZoom.Next(ZoomPercent, 1));

    [RelayCommand]
    void ZoomOut() => SetZoom(WebZoom.Next(ZoomPercent, -1));

    [RelayCommand]
    void ZoomReset() => SetZoom(WebZoom.Normal);

    /// <summary>Demande de la vue : page précédente, ou adresse de départ s'il n'y en a pas (renseignée par la vue).</summary>
    public Action? BackRequested { get; set; }

    [RelayCommand]
    void GoBack()
    {
        Recover();
        BackRequested?.Invoke();
    }

    /// <summary>Demande de la vue : recharger la page (renseignée par la vue).</summary>
    public Action? ReloadRequested { get; set; }

    /// <summary>Demande de la tuile : ouvrir l'adresse de la tuile dans le navigateur par défaut (renseignée par le deck).</summary>
    public Action<Uri>? BrowserRequested { get; set; }

    /// <summary>true quand la saisie au clavier a été lancée par la vue (un champ de texte de la page a le focus), pas par le bouton clavier.</summary>
    public bool AutoTyping { get; set; }

    /// <summary>Demande de la tuile : commencer (true) ou finir (false) la saisie au clavier (renseignée par le deck).</summary>
    public Action<bool>? TypingRequested { get; set; }

    /// <summary>La page ne peut pas s'afficher : le message prend sa place.</summary>
    public void Fail(string message)
    {
        _failure = message;
        NotifyStatus();
    }

    /// <summary>La page s'est chargée : l'éventuel message d'échec disparaît.</summary>
    public void Recover()
    {
        if (_failure.Length == 0) return;
        _failure = "";
        NotifyStatus();
    }

    void NotifyStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowPage));
        OnPropertyChanged(nameof(ShowTouchShield));
        OnPropertyChanged(nameof(ShowPassiveShield));
    }

    [RelayCommand]
    void Reload()
    {
        Recover();
        ReloadRequested?.Invoke();
    }

    [RelayCommand]
    void ToggleTyping()
    {
        AutoTyping = false;   // le bouton clavier reprend la main : la saisie ne s'arrêtera plus toute seule
        if (!IsTyping) Interact();   // taper dans la page suppose qu'elle reçoit le clavier
        TypingRequested?.Invoke(!IsTyping);
    }

    /// <summary>Ouvre l'adresse configurée (jamais celle où la navigation a mené) dans le navigateur par défaut.</summary>
    [RelayCommand]
    void OpenInBrowser()
    {
        if (Source != null) BrowserRequested?.Invoke(Source);
    }
}

/// <summary>
/// Widget Processeur ou Mémoire : une valeur en grand, un détail et une jauge. Le deck le met à jour à chaque relevé de l'intégration
/// « Système Windows » (toutes les secondes) ; tant qu'il n'y a pas de relevé, un message dit pourquoi.
/// </summary>
public sealed partial class SystemMeterWidgetViewModel : WidgetTileViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasReading), nameof(HasStatus))] string _status = "";
    [ObservableProperty] string _headline = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] string _detail = "";
    [ObservableProperty] double _percent;
    [ObservableProperty] bool _isHigh;

    /// <summary>true pour le widget Mémoire, false pour le widget Processeur.</summary>
    public bool IsRam => Button.Widget == WidgetTypes.Ram;
    /// <summary>Titre de la tuile (« Processeur » ou « Mémoire »).</summary>
    public string Title => WidgetTypes.Find(Button.Widget)?.Name ?? "Système";
    /// <summary>true quand une mesure est affichée (et pas un message).</summary>
    public bool HasReading => Status.Length == 0;
    /// <summary>true quand un message remplace la mesure.</summary>
    public bool HasStatus => Status.Length > 0;
    /// <summary>true si un détail est affiché sous la valeur.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Affiche une mesure.</summary>
    public void Show(SystemReading reading)
    {
        Headline = reading.Headline;
        Detail = reading.Detail;
        Percent = reading.Percent;
        IsHigh = reading.IsHigh;
        Status = "";
    }

    /// <summary>Remplace la mesure par un message (intégration absente, en cours de connexion, en erreur).</summary>
    public void ShowStatus(string message)
    {
        Status = message;
        Headline = "";
        Detail = "";
        Percent = 0;
        IsHigh = false;
    }
}
