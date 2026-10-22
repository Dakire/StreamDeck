using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

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

    /// <summary>Nom du site (en-tête de la tuile).</summary>
    public string Title => Source?.Host ?? "Page web";

    /// <summary>Message à la place de la page : adresse manquante, page injoignable, composant WebView2 absent. Vide quand la page s'affiche.</summary>
    public string Status => _failure.Length > 0 ? _failure : Source == null ? "Renseigne l'adresse de la page dans les propriétés (mode édition)" : "";

    /// <summary>true si un message remplace la page.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>true tant qu'un menu ou une boîte de dialogue est ouvert : le cadre est une fenêtre Win32 qui masquerait leur contenu.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPage))] bool _suspended;

    /// <summary>true quand le cadre de la page est affiché.</summary>
    public bool ShowPage => !HasStatus && !Suspended;

    /// <summary>true pendant la saisie au clavier : la fenêtre du deck est activable et le focus est dans la page.</summary>
    [ObservableProperty] bool _isTyping;

    /// <summary>Demande de la vue : recharger la page (renseignée par la vue).</summary>
    public Action? ReloadRequested { get; set; }

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
    }

    [RelayCommand]
    void Reload()
    {
        Recover();
        ReloadRequested?.Invoke();
    }

    [RelayCommand]
    void ToggleTyping() => TypingRequested?.Invoke(!IsTyping);
}
