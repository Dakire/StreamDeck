using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

public sealed record TemplateGroupViewModel(string Name, IReadOnlyList<ChoiceViewModel> Items);

/// <summary>Édition d'un bouton : libellé, icône, couleur et liste d'actions.</summary>
public partial class ButtonEditorViewModel : ObservableObject
{
    static readonly string[] Palette =
        { "#272B38", "#1E7F5F", "#B23A48", "#3466D6", "#6C3FD1", "#B7791F", "#0E8FA3", "#C2410C", "#BE185D", "#4D7C0F" };

    readonly string[] _pageNames;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewIcon), nameof(PreviewSymbol), nameof(HasPreviewSymbol), nameof(HasPreviewText))] string _icon;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewLabel))] string _label;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewBrush))] string _color;
    [ObservableProperty] bool _templatesOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] string _note = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DeleteText))] bool _confirmDelete;

    public bool IsNew { get; }
    public bool IsExisting => !IsNew;
    public string Title => IsNew ? "Nouveau bouton" : "Modifier le bouton";
    public string SaveText => IsNew ? "Ajouter" : "Enregistrer";
    public string DeleteText => ConfirmDelete ? "Confirmer la suppression ?" : "Supprimer";
    public bool HasNote => Note.Length > 0;
    public string PreviewIcon => Icon;
    public Wpf.Ui.Controls.SymbolRegular PreviewSymbol => IconCatalog.TryResolve(Icon, out var s) ? s : default;
    public bool HasPreviewSymbol => IconCatalog.TryResolve(Icon, out _);
    public bool HasPreviewText => !HasPreviewSymbol && Icon.Length > 0;
    public string PreviewLabel => Label;
    public Brush PreviewBrush
    {
        get
        {
            try { return (Brush)new BrushConverter().ConvertFromString(Color)!; }
            catch { return (Brush)new BrushConverter().ConvertFromString(Theme.SurfaceHi)!; }
        }
    }

    public ObservableCollection<ActionEditorViewModel> Actions { get; } = new();
    public IReadOnlyList<SwatchViewModel> Swatches { get; }
    public IReadOnlyList<IconPickViewModel> IconChoices { get; }
    public IReadOnlyList<TemplateGroupViewModel> TemplateGroups { get; }

    // résultat
    public bool Deleted { get; private set; }
    public int Move { get; private set; }
    public DeckButton Result { get; private set; }

    /// <summary>La fenêtre se ferme : true = valider, false = annuler.</summary>
    public event Action<bool>? CloseRequested;

    public ButtonEditorViewModel(DeckButton source, IEnumerable<string> pageNames, bool isNew)
    {
        IsNew = isNew;
        _pageNames = pageNames.ToArray();
        _icon = source.Icon; _label = source.Label; _color = source.Color;
        Result = source;

        var swatches = Palette.Select(c => new SwatchViewModel(c, SelectColor)).ToList();
        Swatches = swatches;

        IconChoices = IconCatalog.Picker.Select(sym => new IconPickViewModel(sym, () => Icon = sym.ToString())).ToList();
        RefreshIcons();

        TemplateGroups = Templates.All.GroupBy(t => t.Group)
            .Select(g => new TemplateGroupViewModel(g.Key.ToUpperInvariant(),
                g.Select(t => new ChoiceViewModel(t.Name, () => ApplyTemplate(t))).ToList()))
            .ToList();

        LoadActions(source.Actions.Count == 0 ? new List<DeckAction> { new() { Type = "hotkey" } } : source.Actions);
        RefreshSwatches();
    }

    partial void OnColorChanged(string value) => RefreshSwatches();
    partial void OnIconChanged(string value) => RefreshIcons();

    void RefreshIcons()
    {
        if (IconChoices == null) return;
        foreach (var c in IconChoices) c.IsSelected = string.Equals(c.Symbol.ToString(), Icon?.Trim(), StringComparison.Ordinal);
    }

    void RefreshSwatches()
    {
        foreach (var s in Swatches) s.IsSelected = string.Equals(s.Color, Color, StringComparison.OrdinalIgnoreCase);
    }

    void SelectColor(string c) => Color = c;

    void LoadActions(IEnumerable<DeckAction> actions)
    {
        Actions.Clear();
        foreach (var a in actions) Actions.Add(new ActionEditorViewModel(a, this, _pageNames));
        RefreshOrder();
    }

    void RefreshOrder()
    {
        for (int i = 0; i < Actions.Count; i++)
        {
            var a = Actions[i];
            a.Title = $"Action {i + 1}";
            a.CanMoveUp = i > 0;
            a.CanMoveDown = i < Actions.Count - 1;
            a.CanRemove = Actions.Count > 1;
        }
    }

    void ApplyTemplate(ButtonTemplate t)
    {
        LoadActions(ConfigStore.Clone(t.Actions.ToList()));
        Label = t.Name; Icon = t.Icon; Color = t.Color;
        Note = t.Note == null ? "" : "ℹ  " + t.Note;
    }

    public void MoveAction(ActionEditorViewModel a, int delta)
    {
        int i = Actions.IndexOf(a), j = i + delta;
        if (i < 0 || j < 0 || j >= Actions.Count) return;
        Actions.Move(i, j);
        RefreshOrder();
    }

    public void RemoveAction(ActionEditorViewModel a)
    {
        if (Actions.Count <= 1) return;
        Actions.Remove(a);
        RefreshOrder();
    }

    // ---------- commandes ----------

    [RelayCommand] void ToggleTemplates() => TemplatesOpen = !TemplatesOpen;

    [RelayCommand]
    void AddAction()
    {
        Actions.Add(new ActionEditorViewModel(new DeckAction { Type = "hotkey" }, this, _pageNames));
        RefreshOrder();
    }

    [RelayCommand]
    void Delete()
    {
        if (!ConfirmDelete) { ConfirmDelete = true; return; }
        Deleted = true;
        Close(true);
    }

    [RelayCommand] void MoveBefore() { Move = -1; Close(true); }
    [RelayCommand] void MoveAfter() { Move = 1; Close(true); }
    [RelayCommand] void Save() => Close(true);
    [RelayCommand] void Cancel() => Close(false);

    void Close(bool ok)
    {
        if (ok) Result = new DeckButton { Label = Label, Icon = Icon, Color = Color, Actions = Actions.Select(a => a.ToDeckAction()).ToList() };
        CloseRequested?.Invoke(ok);
    }
}
