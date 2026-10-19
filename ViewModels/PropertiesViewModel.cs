using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

public enum PropertiesMode { None, Empty, Button, Folder }

/// <summary>
/// Panneau de propriétés du bouton sélectionné. Chaque modification est appliquée tout de suite au document
/// (annulable, regroupée pendant la frappe), l'aperçu et les avertissements se mettent à jour en direct.
/// </summary>
public sealed partial class PropertiesViewModel : ObservableObject, IStepOwner
{
    public static readonly string[] Palette =
        { "#272B38", "#1E7F5F", "#B23A48", "#3466D6", "#6C3FD1", "#99620F", "#0A7A8C", "#C2410C", "#BE185D", "#4D7C0F" };
    static readonly string[] TextPalette = { "#FFFFFF", "#000000", "#FFD166", "#9FB1FF", "#7CF2C1", "#FF8A97" };

    const string DefaultColor = "#2B2F3A";

    readonly DeckDocument _doc;
    readonly EditorViewModel _editor;
    readonly IEditorShell _shell;
    Place? _place;
    int _index = -1;
    bool _loading;
    List<SymbolRegular>? _allSymbols;

    public bool IsApplying { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNone), nameof(IsEmpty), nameof(IsButton), nameof(IsFolder), nameof(IsEditable))] PropertiesMode _mode;
    [ObservableProperty] string _title = "";
    [ObservableProperty] bool _showTitle = true;
    [ObservableProperty] string? _titlePosition;
    [ObservableProperty] string _icon = "";
    [ObservableProperty] string _iconSearch = "";
    [ObservableProperty] string _color = DefaultColor;
    [ObservableProperty] string _textColor = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImage))] string? _imageName;
    [ObservableProperty] string _imageFit = "cover";
    [ObservableProperty] string _colorError = "";
    [ObservableProperty] string _textColorError = "";
    [ObservableProperty] TileFaceData _preview = TileFaceData.Empty;
    [ObservableProperty] string _headerText = "";
    [ObservableProperty] string _subtitle = "";
    [ObservableProperty] string _folderInfo = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasButtonIssues))] int _buttonIssueCount;
    [ObservableProperty] bool _canPaste;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSteps), nameof(StepsCaption))] int _stepCount;

    public bool IsNone => Mode == PropertiesMode.None;
    public bool IsEmpty => Mode == PropertiesMode.Empty;
    public bool IsButton => Mode == PropertiesMode.Button;
    public bool IsFolder => Mode == PropertiesMode.Folder;
    public bool IsEditable => Mode is PropertiesMode.Button or PropertiesMode.Folder;
    public bool HasImage => !string.IsNullOrEmpty(ImageName);
    public bool HasButtonIssues => ButtonIssueCount > 0;
    public bool HasSteps => StepCount > 0;
    public string StepsCaption => StepCount > 1 ? "Étapes (exécutées dans l'ordre)" : "Action";

    public ObservableCollection<ActionStepViewModel> Steps { get; } = new();
    public ObservableCollection<IssueViewModel> ButtonIssues { get; } = new();
    public ObservableCollection<ChoiceViewModel> PositionChoices { get; } = new();
    public ObservableCollection<ChoiceViewModel> FitChoices { get; } = new();
    public ObservableCollection<SwatchViewModel> Swatches { get; } = new();
    public ObservableCollection<SwatchViewModel> TextSwatches { get; } = new();
    public ObservableCollection<IconPickViewModel> IconChoices { get; } = new();

    public PropertiesViewModel(DeckDocument doc, EditorViewModel editor, IEditorShell shell)
    {
        _doc = doc; _editor = editor; _shell = shell;
        foreach (var (key, label) in new (string?, string)[] { (null, "Sous l'icône"), ("top", "En haut"), ("middle", "Au centre"), ("bottom", "En bas") })
        {
            var k = key;
            PositionChoices.Add(new ChoiceViewModel(label, () => SetPosition(k)));
        }
        FitChoices.Add(new ChoiceViewModel("Remplir", () => ImageFit = "cover"));
        FitChoices.Add(new ChoiceViewModel("Image entière", () => ImageFit = "contain"));
        foreach (var c in Palette) Swatches.Add(new SwatchViewModel(c, v => Color = v));
        foreach (var c in TextPalette) TextSwatches.Add(new SwatchViewModel(c, v => TextColor = v));
        RebuildIcons();
    }

    // ---------- chargement ----------

    /// <summary>Désigne le bouton à éditer (ou une case vide, ou rien) et recharge les champs.</summary>
    public void Show(Place? place, int index)
    {
        _place = place; _index = index;
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        try
        {
            var b = _place != null && _index >= 0 ? _doc.Get(_place, _index) : null;
            Mode = _place == null || _index < 0 ? PropertiesMode.None
                : b == null ? PropertiesMode.Empty
                : b.IsFolder ? PropertiesMode.Folder : PropertiesMode.Button;
            CanPaste = _doc.HasClipboard;
            Steps.Clear();
            if (b == null) { Preview = TileFaceData.Empty; ButtonIssues.Clear(); ButtonIssueCount = 0; StepCount = 0; return; }

            Title = b.Label; ShowTitle = !b.HideTitle; TitlePosition = b.TitlePosition;
            Icon = b.Icon; Color = b.Color; TextColor = b.TextColor ?? "";
            ImageName = b.ImagePath; ImageFit = b.ImageFit ?? "cover";
            ColorError = TextColorError = "";
            if (!b.IsFolder)
                foreach (var a in b.Actions) Steps.Add(new ActionStepViewModel(a, this));
            RefreshOrder();
            RefreshDerived(b);
        }
        finally { _loading = false; }
        RefreshSelection();
    }

    void RefreshOrder()
    {
        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            s.Title = Steps.Count > 1 ? $"Étape {i + 1}" : "Action";
            s.CanMoveUp = i > 0;
            s.CanMoveDown = i < Steps.Count - 1;
            s.CanRemove = true;
        }
        StepCount = Steps.Count;
    }

    void RefreshSelection()
    {
        for (int i = 0; i < PositionChoices.Count; i++)
            PositionChoices[i].IsSelected = (i == 0 ? null : i == 1 ? "top" : i == 2 ? "middle" : "bottom") == TitlePosition;
        FitChoices[0].IsSelected = ImageFit != "contain";
        FitChoices[1].IsSelected = ImageFit == "contain";
        foreach (var s in Swatches) s.IsSelected = string.Equals(s.Color, Color, StringComparison.OrdinalIgnoreCase);
        foreach (var s in TextSwatches) s.IsSelected = string.Equals(s.Color, TextColor, StringComparison.OrdinalIgnoreCase);
        foreach (var c in IconChoices) c.IsSelected = string.Equals(c.Symbol.ToString(), Icon?.Trim(), StringComparison.Ordinal);
    }

    void RefreshDerived(DeckButton? b)
    {
        if (b == null) return;
        Preview = TileFaceData.From(b);
        var cat = ActionCatalog.Identify(b);
        HeaderText = b.IsFolder ? "Dossier" : cat?.Name ?? (b.Actions.Count == 1 ? TypeName(b.Actions[0].Type) : b.Actions.Count == 0 ? "Bouton sans action" : "Multi-actions");
        Subtitle = b.IsFolder ? "" : b.Actions.Count switch { 0 => "", 1 => cat?.Description ?? "", _ => $"{b.Actions.Count} étapes" };
        FolderInfo = b.IsFolder ? (b.Children?.Count(c => !c.Blank) ?? 0) switch { 0 => "Dossier vide", 1 => "1 bouton dans ce dossier", var n => $"{n} boutons dans ce dossier" } : "";

        // Les problèmes d'une étape s'affichent sous l'étape ; le bouton ne garde que les siens (aucune action, image absente)
        ButtonIssues.Clear();
        if (!b.IsFolder && b.Actions.Count == 0)
            ButtonIssues.Add(new IssueViewModel(Severity.Warning, "Ce bouton n'a aucune action"));
        if (!string.IsNullOrEmpty(b.ImagePath) && IconStore.Resolve(b.ImagePath) is { } img && !File.Exists(img))
            ButtonIssues.Add(new IssueViewModel(Severity.Warning, "L'image du bouton est introuvable"));
        ButtonIssueCount = ButtonIssues.Count;
        foreach (var s in Steps) s.Revalidate();
    }

    static string TypeName(string type) => type switch
    {
        "hotkey" => "Raccourci clavier", "text" => "Saisie de texte", "media" => "Touche média", "launch" => "Ouvrir un programme",
        "script" => "Script", "url" => "Ouvrir un site web", "page" => "Aller à une page", "profile" => "Changer de profil",
        "delay" => "Pause", "focus" => "Fenêtre au premier plan", _ => "Action",
    };

    // ---------- application au document ----------

    void Apply(Action<DeckButton> edit, string label, string? coalesce = null)
    {
        if (_loading || _place == null || _index < 0) return;
        IsApplying = true;
        try
        {
            var r = _doc.UpdateButton(_place, _index, edit, label, coalesce);
            if (!r.Ok) { Notifier.Error(r.Message ?? "Modification impossible"); return; }
        }
        finally { IsApplying = false; }
        RefreshDerived(_doc.Get(_place, _index));
        RefreshSelection();
    }

    partial void OnTitleChanged(string value) => Apply(b => b.Label = value, "Modifier le titre", "title");
    partial void OnShowTitleChanged(bool value) => Apply(b => b.HideTitle = !value, value ? "Afficher le titre" : "Masquer le titre");

    void SetPosition(string? position)
    {
        TitlePosition = position;
        Apply(b => b.TitlePosition = position, "Position du titre");
    }

    partial void OnIconChanged(string value) => Apply(b => b.Icon = value, "Modifier l'icône", "icon");

    partial void OnIconSearchChanged(string value) => RebuildIcons();

    partial void OnColorChanged(string value)
    {
        if (_loading) return;
        if (!Theme.TryBrush(value, out _)) { ColorError = "Couleur non valide. Exemple : #3466D6"; return; }
        ColorError = "";
        Apply(b => b.Color = value.Trim(), "Modifier la couleur", "color");
    }

    partial void OnTextColorChanged(string value)
    {
        if (_loading) return;
        if (value.Length == 0) { TextColorError = ""; Apply(b => b.TextColor = null, "Couleur du texte", "textcolor"); return; }
        if (!Theme.TryBrush(value, out _)) { TextColorError = "Couleur non valide. Exemple : #FFFFFF"; return; }
        TextColorError = "";
        Apply(b => b.TextColor = value.Trim(), "Couleur du texte", "textcolor");
    }

    partial void OnImageFitChanged(string value) => Apply(b => b.ImageFit = value == "contain" ? "contain" : null, "Ajustement de l'image");

    void RebuildIcons()
    {
        IconChoices.Clear();
        IEnumerable<SymbolRegular> source;
        var q = IconSearch.Trim();
        if (q.Length == 0) source = IconCatalog.Picker;
        else
        {
            // Toutes les icônes Fluent 24 px (noms anglais), limitées pour garder le sélecteur rapide
            _allSymbols ??= Enum.GetValues<SymbolRegular>().Where(s => s.ToString().EndsWith("24", StringComparison.Ordinal)).Distinct().ToList();
            source = _allSymbols.Where(s => s.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)).Take(60);
        }
        foreach (var sym in source)
        {
            var s = sym;
            IconChoices.Add(new IconPickViewModel(s, () => Icon = s.ToString()));
        }
        foreach (var c in IconChoices) c.IsSelected = string.Equals(c.Symbol.ToString(), Icon?.Trim(), StringComparison.Ordinal);
    }

    // ---------- étapes (IStepOwner) ----------

    public IReadOnlyList<string> PageNames => _place == null ? Array.Empty<string>()
        : _doc.FindProfile(_place.Profile)?.Pages.Select(p => p.Name).ToList() ?? new List<string>();
    public IReadOnlyList<string> ProfileNames => _doc.Config.Profiles.Select(p => p.Name).ToList();
    public DeckConfig Config => _doc.Config;
    public DeckProfile? Profile => _place == null ? null : _doc.FindProfile(_place.Profile);

    void CommitSteps(string label, string? coalesce)
    {
        var actions = Steps.Select(s => s.ToDeckAction()).ToList();
        Apply(b => b.Actions = actions, label, coalesce);
    }

    public void StepChanged(ActionStepViewModel step) => CommitSteps("Modifier l'action", "steps");

    public void MoveStep(ActionStepViewModel step, int delta)
    {
        int i = Steps.IndexOf(step), j = i + delta;
        if (i < 0 || j < 0 || j >= Steps.Count) return;
        Steps.Move(i, j);
        RefreshOrder();
        CommitSteps("Réordonner les étapes", null);
    }

    public void RemoveStep(ActionStepViewModel step)
    {
        if (!Steps.Remove(step)) return;
        RefreshOrder();
        CommitSteps("Supprimer l'étape", null);
    }

    [RelayCommand]
    void AddStep()
    {
        Steps.Add(new ActionStepViewModel(new DeckAction { Type = "hotkey" }, this));
        RefreshOrder();
        CommitSteps("Ajouter une étape", null);
    }

    // ---------- image ----------

    [RelayCommand]
    void ImportImage()
    {
        var path = _shell.PickOpenFile("Choisir une image pour le bouton", IconStore.DialogFilter);
        if (path == null) return;
        try
        {
            var name = IconStore.Import(path);
            ImageName = name;
            Apply(b => b.ImagePath = name, "Importer une image");
            Notifier.Success("Image importée");
        }
        catch (InvalidDataException ex) { Notifier.Error(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notifier.Error("Lecture impossible : " + ex.Message); }
    }

    [RelayCommand]
    void RemoveImage()
    {
        ImageName = null;
        Apply(b => b.ImagePath = null, "Retirer l'image");
    }

    // ---------- réinitialisation et actions rapides ----------

    /// <summary>Remet l'apparence aux valeurs d'origine de l'action (catalogue) ou aux valeurs neutres ; les actions ne bougent pas.</summary>
    [RelayCommand]
    public void ResetAppearance()
    {
        if (_place == null) return;
        var current = _doc.Get(_place, _index);
        if (current == null) return;
        var cat = ActionCatalog.Identify(current);
        Apply(b =>
        {
            b.HideTitle = false; b.TitlePosition = null; b.TextColor = null; b.ImagePath = null; b.ImageFit = null;
            b.Color = cat?.Color ?? DefaultColor;
            if (cat != null) { b.Icon = cat.Icon; if (!b.IsFolder) b.Label = cat.Name; }
        }, "Réinitialiser le bouton");
        Reload();
        Notifier.Success("Apparence réinitialisée");
    }

    [RelayCommand] void CreateFolder() => _editor.CreateFolderHere();
    [RelayCommand] void Paste() => _editor.PasteCommand.Execute(null);
    [RelayCommand] void OpenFolder() => _editor.OpenSelectedFolder();
    [RelayCommand] void Delete() => _editor.DeleteSelection();
    [RelayCommand] void BrowseCatalog() => _shell.FocusCatalogSearch();
}
