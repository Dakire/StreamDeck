using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

// Réglages d'un widget (horloge, agenda, page web) et de sa taille en cases, dans le panneau de propriétés
public sealed partial class PropertiesViewModel
{
    static readonly (int Cols, int Rows)[] Sizes = { (1, 1), (2, 1), (3, 1), (4, 1), (2, 2), (3, 2), (4, 2), (3, 3), (4, 3), (5, 3) };
    static readonly int[] AgendaCounts = { 3, 4, 6, 8 };
    static readonly int[] AgendaDayChoices = { 1, 3, 7, 14 };

    string? _widgetId;

    /// <summary>Tailles proposées (largeur × hauteur en cases).</summary>
    public ObservableCollection<ChoiceViewModel> SizeChoices { get; } = new();
    /// <summary>Nombre d'événements affichés par l'agenda.</summary>
    public ObservableCollection<ChoiceViewModel> AgendaCountChoices { get; } = new();
    /// <summary>Nombre de jours à venir affichés par l'agenda.</summary>
    public ObservableCollection<ChoiceViewModel> AgendaDaysChoices { get; } = new();

    [ObservableProperty] bool _clockSeconds;
    [ObservableProperty] bool _clockDate = true;
    [ObservableProperty] bool _clockHour12;
    [ObservableProperty] string _widgetUrl = "";
    [ObservableProperty] string _widgetUrlError = "";
    [ObservableProperty] string _widgetName = "";
    [ObservableProperty] string _widgetDescription = "";

    /// <summary>true si le bouton sélectionné est un widget.</summary>
    public bool IsWidget => _widgetId != null;
    /// <summary>true pour un bouton ordinaire (étapes d'action) : ni dossier, ni widget.</summary>
    public bool IsPlainButton => IsButton && _widgetId == null;
    /// <summary>true pour le widget horloge.</summary>
    public bool IsClockWidget => _widgetId == WidgetTypes.Clock;
    /// <summary>true pour le widget agenda.</summary>
    public bool IsAgendaWidget => _widgetId == WidgetTypes.Agenda;
    /// <summary>true pour le widget page web.</summary>
    public bool IsWebWidget => _widgetId == WidgetTypes.Web;

    void InitWidgetChoices()
    {
        foreach (var (cols, rows) in Sizes)
        {
            var (c, r) = (cols, rows);
            SizeChoices.Add(new ChoiceViewModel($"{c} × {r}", () => SetSize(c, r)));
        }
        foreach (var n in AgendaCounts)
        {
            int count = n;
            AgendaCountChoices.Add(new ChoiceViewModel(count.ToString(CultureInfo.InvariantCulture), () => ApplyNow(b => b.SetOption(WidgetOptionKeys.Count, count.ToString(CultureInfo.InvariantCulture)), "Nombre d'événements")));
        }
        foreach (var n in AgendaDayChoices)
        {
            int days = n;
            AgendaDaysChoices.Add(new ChoiceViewModel(days == 1 ? "Aujourd'hui" : $"{days} jours", () => ApplyNow(b => b.SetOption(WidgetOptionKeys.Days, days.ToString(CultureInfo.InvariantCulture)), "Jours de l'agenda")));
        }
    }

    /// <summary>Charge les réglages du widget (ou les remet à zéro pour un bouton ordinaire) ; appelé pendant le rechargement.</summary>
    void LoadWidget(DeckButton? b)
    {
        _widgetId = b?.Widget;
        var info = WidgetTypes.Find(_widgetId);
        WidgetName = info?.Name ?? "";
        WidgetDescription = info?.Description ?? "";
        if (b != null && info != null)
        {
            ClockSeconds = b.OptionFlag(WidgetOptionKeys.Seconds, false);
            ClockDate = b.OptionFlag(WidgetOptionKeys.Date, true);
            ClockHour12 = b.OptionFlag(WidgetOptionKeys.Hour12, false);
            WidgetUrl = b.Option(WidgetOptionKeys.Url);
            WidgetUrlError = "";
        }
        OnPropertyChanged(nameof(IsWidget));
        OnPropertyChanged(nameof(IsPlainButton));
        OnPropertyChanged(nameof(IsClockWidget));
        OnPropertyChanged(nameof(IsAgendaWidget));
        OnPropertyChanged(nameof(IsWebWidget));
        RefreshWidgetSelection(b);
    }

    void RefreshWidgetSelection(DeckButton? b)
    {
        var (cols, rows) = b?.Span() ?? (1, 1);
        for (int i = 0; i < Sizes.Length; i++) SizeChoices[i].IsSelected = Sizes[i] == (cols, rows);
        int count = b?.OptionInt(WidgetOptionKeys.Count, 4, 1, 20) ?? 4, days = b?.OptionInt(WidgetOptionKeys.Days, 7, 1, 60) ?? 7;
        for (int i = 0; i < AgendaCounts.Length; i++) AgendaCountChoices[i].IsSelected = AgendaCounts[i] == count;
        for (int i = 0; i < AgendaDayChoices.Length; i++) AgendaDaysChoices[i].IsSelected = AgendaDayChoices[i] == days;
    }

    /// <summary>
    /// Change la taille du widget : refusée (avec un message) tant que les cases à recouvrir ne sont pas vides, jamais rognée en silence.
    /// </summary>
    void SetSize(int cols, int rows)
    {
        if (_place == null || _index < 0 || _widgetId == null) return;
        var slot = _editor.Slots.FirstOrDefault(s => !s.IsBack && s.Index == _index);
        if (slot == null) return;
        bool Occupied(int cell) => cell >= _editor.Slots.Count || _editor.Slots[cell].IsBack || !_editor.Slots[cell].IsEmpty;
        var (fitCols, fitRows) = TileSpans.Effective(slot.Position, cols, rows, Math.Max(1, _editor.Columns), Math.Max(1, _editor.Rows), Occupied);
        if (fitCols != cols || fitRows != rows)
        {
            Notifier.Error($"Pas la place pour {cols} × {rows} : libère les cases à droite ou en dessous, ou choisis une taille plus petite.");
            RefreshWidgetSelection(_target);
            return;
        }
        ApplyNow(b => { b.ColSpan = cols; b.RowSpan = rows; }, $"Taille {cols} × {rows}");
    }

    partial void OnClockSecondsChanged(bool value) => ApplyNow(b => b.SetOption(WidgetOptionKeys.Seconds, value ? "1" : null), value ? "Afficher les secondes" : "Masquer les secondes");
    partial void OnClockDateChanged(bool value) => ApplyNow(b => b.SetOption(WidgetOptionKeys.Date, value ? null : "0"), value ? "Afficher la date" : "Masquer la date");
    partial void OnClockHour12Changed(bool value) => ApplyNow(b => b.SetOption(WidgetOptionKeys.Hour12, value ? "1" : null), value ? "Heure sur 12 heures" : "Heure sur 24 heures");

    partial void OnWidgetUrlChanged(string value)
    {
        if (_loading) return;
        if (value.Trim().Length == 0) { WidgetUrlError = ""; Draft("url", "Adresse de la page", b => b.SetOption(WidgetOptionKeys.Url, null)); return; }
        if (!WidgetTypes.IsWebAddress(value, out _)) { WidgetUrlError = "Adresse non valide : commence par https:// (ou http://)"; DropDraft("url"); return; }
        WidgetUrlError = "";
        Draft("url", "Adresse de la page", b => b.SetOption(WidgetOptionKeys.Url, value));
    }
}
