using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Actions;
using StreamDak.Integrations;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>Contenu du panneau de propriétés selon la sélection.</summary>
public enum PropertiesMode
{
    /// <summary>Aucune case sélectionnée.</summary>
    None,
    /// <summary>Case vide : propositions pour la remplir.</summary>
    Empty,
    /// <summary>Bouton : apparence et étapes.</summary>
    Button,
    /// <summary>Dossier : apparence seulement.</summary>
    Folder,
}

/// <summary>
/// Panneau de propriétés du bouton sélectionné. Les champs de saisie (titre, icône, couleurs, valeurs des étapes) forment un
/// brouillon : l'aperçu se met à jour à chaque frappe sur une copie du bouton, et le document (historique, sauvegarde, deck,
/// grille de l'éditeur) n'est modifié qu'à la validation (<see cref="Flush"/> : Entrée, perte du focus, autre case, fermeture).
/// Les choix par pastille ou bascule s'appliquent tout de suite. Chaque application est annulable.
/// </summary>
public sealed partial class PropertiesViewModel : ObservableObject, IStepOwner
{
    /// <summary>Couleurs proposées pour les boutons (texte blanc lisible sur chacune, testé).</summary>
    public static readonly string[] Palette =
        { "#334155", "#047857", "#BE123C", "#2563EB", "#7C3AED", "#B45309", "#0E7490", "#C2410C", "#BE185D", "#4F46E5" };
    static readonly string[] TextPalette = { "#FFFFFF", "#000000", "#FFD166", "#9FB1FF", "#7CF2C1", "#FF8A97" };

    const string DefaultColor = "#334155";

    readonly DeckDocument _doc;
    readonly IWindowService _windows;
    readonly IntegrationManager _integrations;
    readonly EditorViewModel _editor;
    readonly IEditorShell _shell;
    Place? _place;
    int _index = -1;
    bool _loading;
    List<SymbolRegular>? _allSymbols;

    // brouillon : saisies pas encore écrites dans le document (clé = champ, la dernière frappe remplace la précédente)
    readonly Dictionary<string, (string Label, Action<DeckButton> Edit)> _draft = new();
    DeckButton? _target;                  // bouton du document auquel le brouillon s'applique
    // état de la case à l'ouverture : « Annuler » y revient (null : la case était vide)
    DeckButton? _baseline;
    Place? _baselinePlace;
    int _baselineIndex = -2;

    /// <summary>true pendant l'écriture d'une modification dans le document : l'éditeur ne recharge pas les propriétés en retour.</summary>
    public bool IsApplying { get; private set; }
    /// <summary>true si des saisies attendent d'être validées.</summary>
    public bool HasDraft => _draft.Count > 0;

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

    /// <summary>true si aucune case n'est sélectionnée.</summary>
    public bool IsNone => Mode == PropertiesMode.None;
    /// <summary>true pour une case vide.</summary>
    public bool IsEmpty => Mode == PropertiesMode.Empty;
    /// <summary>true pour un bouton.</summary>
    public bool IsButton => Mode == PropertiesMode.Button;
    /// <summary>true pour un dossier.</summary>
    public bool IsFolder => Mode == PropertiesMode.Folder;
    /// <summary>true si l'apparence est modifiable (bouton ou dossier).</summary>
    public bool IsEditable => Mode is PropertiesMode.Button or PropertiesMode.Folder;
    /// <summary>true si le bouton a une image importée.</summary>
    public bool HasImage => !string.IsNullOrEmpty(ImageName);
    /// <summary>true si le bouton a des problèmes de configuration.</summary>
    public bool HasButtonIssues => ButtonIssueCount > 0;
    /// <summary>true si le bouton a au moins une étape.</summary>
    public bool HasSteps => StepCount > 0;
    /// <summary>Titre de la section des étapes (« Action » ou « Étapes »).</summary>
    public string StepsCaption => StepCount > 1 ? "Étapes (exécutées dans l'ordre)" : "Action";

    /// <summary>Étapes éditables du bouton.</summary>
    public ObservableCollection<ActionStepViewModel> Steps { get; } = new();
    /// <summary>Problèmes du bouton lui-même (image manquante, aucune action).</summary>
    public ObservableCollection<IssueViewModel> ButtonIssues { get; } = new();
    /// <summary>Positions du titre proposées.</summary>
    public ObservableCollection<ChoiceViewModel> PositionChoices { get; } = new();
    /// <summary>Ajustements d'image proposés (remplir, contenir).</summary>
    public ObservableCollection<ChoiceViewModel> FitChoices { get; } = new();
    /// <summary>Nuancier de la couleur du bouton.</summary>
    public ObservableCollection<SwatchViewModel> Swatches { get; } = new();
    /// <summary>Nuancier de la couleur du texte.</summary>
    public ObservableCollection<SwatchViewModel> TextSwatches { get; } = new();
    /// <summary>Icônes Fluent proposées.</summary>
    public ObservableCollection<IconPickViewModel> IconChoices { get; } = new();

    /// <summary>Panneau de propriétés de l'éditeur.</summary>
    public PropertiesViewModel(DeckDocument doc, EditorViewModel editor, IEditorShell shell, IWindowService windows, IntegrationManager integrations)
    {
        _doc = doc; _editor = editor; _shell = shell; _windows = windows; _integrations = integrations;
        foreach (var (key, label) in new (string?, string)[] { (null, "Sous l'icône"), ("top", "En haut"), ("middle", "Au centre"), ("bottom", "En bas") })
        {
            var k = key;
            PositionChoices.Add(new ChoiceViewModel(label, () => SetPosition(k)));
        }
        FitChoices.Add(new ChoiceViewModel("Remplir", () => ImageFit = "cover"));
        FitChoices.Add(new ChoiceViewModel("Image entière", () => ImageFit = "contain"));
        foreach (var c in Palette) Swatches.Add(new SwatchViewModel(c, v => { Color = v; Flush(); }));
        foreach (var c in TextPalette) TextSwatches.Add(new SwatchViewModel(c, v => { TextColor = v; Flush(); }));
        InitWidgetChoices();
        RebuildIcons();
    }

    // ---------- chargement ----------

    /// <summary>Désigne le bouton à éditer (ou une case vide, ou rien) et recharge les champs.</summary>
    public void Show(Place? place, int index)
    {
        Flush();   // saisies de la case précédente : validées avant de changer de case
        _place = place; _index = index;
        if (place != null && index >= 0 && !(_baselineIndex == index && place.Equals(_baselinePlace)))
        {
            // Nouvelle case : on retient son état d'origine. La même case rechargée garde le sien (annuler défait aussi une création).
            _baselinePlace = place; _baselineIndex = index;
            _baseline = _doc.Get(place, index) is { } cur ? ConfigStore.Clone(cur) : null;
        }
        Reload();
    }

    /// <summary>La prochaine sélection ouvre une nouvelle session d'édition (ouverture depuis le deck) : l'état d'origine est relevé à nouveau.</summary>
    public void BeginSession() => _baselineIndex = -2;

    /// <summary>Recharge les propriétés depuis le document (modification externe, annuler).</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var b = _place != null && _index >= 0 ? _doc.Get(_place, _index) : null;
            _target = b;
            _draft.Clear();
            Mode = _place == null || _index < 0 ? PropertiesMode.None
                : b == null ? PropertiesMode.Empty
                : b.IsFolder ? PropertiesMode.Folder : PropertiesMode.Button;
            CanPaste = _doc.HasClipboard;
            Steps.Clear();
            LoadWidget(b);
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
        RefreshWidgetSelection(_target);
    }

    void RefreshDerived(DeckButton? b)
    {
        if (b == null) return;
        Preview = TileFaceData.From(b);
        var cat = ActionCatalog.Identify(b);
        HeaderText = b.IsFolder ? "Dossier" : WidgetTypes.Find(b.Widget)?.Name ?? cat?.Name ?? (b.Actions.Count == 1 ? TypeName(b.Actions[0].Type) : b.Actions.Count == 0 ? "Bouton sans action" : "Multi-actions");
        Subtitle = b.IsFolder ? "" : b.Actions.Count switch { 0 => "", 1 => cat?.Description ?? "", _ => $"{b.Actions.Count} étapes" };
        FolderInfo = b.IsFolder ? (b.Children?.Count(c => !c.Blank) ?? 0) switch { 0 => "Dossier vide", 1 => "1 bouton dans ce dossier", var n => $"{n} boutons dans ce dossier" } : "";

        // Les problèmes d'une étape s'affichent sous l'étape ; le bouton ne garde que les siens (aucune action, image absente)
        ButtonIssues.Clear();
        if (b.Widget != null)
        {
            foreach (var issue in ActionValidator.Validate(b, _doc.Config, Profile)) ButtonIssues.Add(new IssueViewModel(issue.Severity, issue.Message));
        }
        else if (!b.IsFolder && b.Actions.Count == 0)
            ButtonIssues.Add(new IssueViewModel(Severity.Warning, "Ce bouton n'a aucune action"));
        if (!string.IsNullOrEmpty(b.ImagePath) && IconStore.Resolve(b.ImagePath) is { } img && !File.Exists(img))
            ButtonIssues.Add(new IssueViewModel(Severity.Warning, "L'image du bouton est introuvable"));
        ButtonIssueCount = ButtonIssues.Count;
        foreach (var s in Steps) s.Revalidate();
    }

    static string TypeName(string type) => type switch
    {
        ActionTypes.Hotkey => "Raccourci clavier", ActionTypes.Text => "Saisie de texte", ActionTypes.Media => "Touche média", ActionTypes.Launch => "Ouvrir un programme",
        ActionTypes.Script => "Script", ActionTypes.Url => "Ouvrir un site web", ActionTypes.Page => "Aller à une page", ActionTypes.Profile => "Changer de profil",
        ActionTypes.Delay => "Pause", ActionTypes.Focus => "Fenêtre au premier plan", _ => "Action",
    };

    // ---------- application au document ----------

    /// <summary>Retient une saisie sans toucher au document : l'aperçu montre le résultat sur une copie du bouton.</summary>
    void Draft(string key, string label, Action<DeckButton> edit)
    {
        if (_loading || _place == null || _index < 0 || _target == null) return;
        _draft[key] = (label, edit);
        RefreshDraftPreview();
    }

    /// <summary>Retire un brouillon devenu invalide (le champ contient une valeur refusée).</summary>
    void DropDraft(string key)
    {
        if (_draft.Remove(key)) RefreshDraftPreview();
    }

    void RefreshDraftPreview()
    {
        if (_target == null) return;
        var preview = ConfigStore.Clone(_target);
        foreach (var d in _draft.Values) d.Edit(preview);
        Preview = TileFaceData.From(preview);
    }

    /// <summary>Valide les saisies en attente : une écriture dans le document par champ modifié (titre, icône, couleurs, étapes).</summary>
    public void Flush()
    {
        if (_draft.Count == 0) return;
        var items = _draft.ToList();
        _draft.Clear();
        // La case a changé sous nos pieds (déplacée, supprimée, document rechargé) : les saisies ne visent plus ce bouton
        if (_place == null || _target == null || !ReferenceEquals(_doc.Get(_place, _index), _target)) return;
        foreach (var (key, d) in items) Apply(d.Edit, d.Label, key);
    }

    /// <summary>Abandonne les saisies en attente et réaffiche les valeurs du document (Échap dans un champ) ; faux s'il n'y en avait pas.</summary>
    public bool RevertDraft()
    {
        if (_draft.Count == 0) return false;
        _draft.Clear();
        Reload();
        return true;
    }

    /// <summary>Abandonne les saisies sans recharger (le document vient d'être remplacé).</summary>
    public void DiscardDraft() => _draft.Clear();

    /// <summary>Enregistrement : l'état actuel de la case devient celui auquel « Annuler » revient.</summary>
    public void MarkSaved()
    {
        if (_place == null || _index < 0) return;
        _baselinePlace = _place; _baselineIndex = _index;
        _baseline = _doc.Get(_place, _index) is { } cur ? ConfigStore.Clone(cur) : null;
    }

    /// <summary>Remet la case dans l'état où elle était à l'ouverture (supprime le bouton qu'on y a créé) ; annulable. Faux si rien n'a été relevé.</summary>
    public bool RevertToBaseline()
    {
        _draft.Clear();
        if (_place == null || _index < 0 || _baselineIndex != _index || !_place.Equals(_baselinePlace)) { Reload(); return false; }
        var cur = _doc.Get(_place, _index);
        if (_baseline == null)
        {
            if (cur != null) _doc.ClearButton(_place, _index, "Annuler la création");
        }
        else if (cur == null || JsonSerializer.Serialize(cur, ConfigStore.Options) != JsonSerializer.Serialize(_baseline, ConfigStore.Options))
            _doc.PutButton(_place, _index, ConfigStore.Clone(_baseline), "Annuler les modifications");
        Reload();
        return true;
    }

    /// <summary>Modification immédiate (pastille, bascule, bouton) : les saisies en attente passent d'abord, dans l'ordre.</summary>
    void ApplyNow(Action<DeckButton> edit, string label, string? coalesce = null)
    {
        Flush();
        Apply(edit, label, coalesce);
    }

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
        _target = _doc.Get(_place, _index);
        RefreshDerived(_target);
        RefreshSelection();
    }

    partial void OnTitleChanged(string value) => Draft("title", "Modifier le titre", b => b.Label = value);
    partial void OnShowTitleChanged(bool value) => ApplyNow(b => b.HideTitle = !value, value ? "Afficher le titre" : "Masquer le titre");

    void SetPosition(string? position)
    {
        TitlePosition = position;
        ApplyNow(b => b.TitlePosition = position, "Position du titre");
    }

    partial void OnIconChanged(string value)
    {
        Draft("icon", "Modifier l'icône", b => b.Icon = value);
        if (!_loading) RefreshIconSelection();
    }

    partial void OnIconSearchChanged(string value) => RebuildIcons();

    partial void OnColorChanged(string value)
    {
        if (_loading) return;
        if (!Theme.TryBrush(value, out _)) { ColorError = "Couleur non valide. Exemple : #2563EB"; DropDraft("color"); return; }
        ColorError = "";
        Draft("color", "Modifier la couleur", b => b.Color = value.Trim());
    }

    partial void OnTextColorChanged(string value)
    {
        if (_loading) return;
        if (value.Length == 0) { TextColorError = ""; Draft("textcolor", "Couleur du texte", b => b.TextColor = null); return; }
        if (!Theme.TryBrush(value, out _)) { TextColorError = "Couleur non valide. Exemple : #FFFFFF"; DropDraft("textcolor"); return; }
        TextColorError = "";
        Draft("textcolor", "Couleur du texte", b => b.TextColor = value.Trim());
    }

    partial void OnImageFitChanged(string value) => ApplyNow(b => b.ImageFit = value == "contain" ? "contain" : null, "Ajustement de l'image");

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
            IconChoices.Add(new IconPickViewModel(s, () => { Icon = s.ToString(); Flush(); }));
        }
        RefreshIconSelection();
    }

    void RefreshIconSelection()
    {
        foreach (var c in IconChoices) c.IsSelected = string.Equals(c.Symbol.ToString(), Icon?.Trim(), StringComparison.Ordinal);
    }

    // ---------- étapes (IStepOwner) ----------

    /// <inheritdoc/>
    public IReadOnlyList<string> PageNames => _place == null ? Array.Empty<string>()
        : _doc.FindProfile(_place.Profile)?.Pages.Select(p => p.Name).ToList() ?? new List<string>();
    /// <inheritdoc/>
    public IReadOnlyList<string> ProfileNames => _doc.Config.Profiles.Select(p => p.Name).ToList();
    /// <inheritdoc/>
    public IReadOnlyList<IIntegration> Integrations => _integrations.Integrations;
    /// <inheritdoc/>
    public IReadOnlyList<string> OpenPrograms => _windows.OpenProcessNames();
    /// <inheritdoc/>
    public DeckConfig Config => _doc.Config;
    /// <inheritdoc/>
    public DeckProfile? Profile => _place == null ? null : _doc.FindProfile(_place.Profile);

    /// <summary>Écrit les étapes : tout de suite (ajout, retrait, ordre) ou en brouillon (<paramref name="draft"/> : saisie d'une valeur).</summary>
    void CommitSteps(string label, string? coalesce, bool draft = false)
    {
        var actions = Steps.Select(s => s.ToDeckAction()).ToList();
        if (draft) Draft("steps", label, b => b.Actions = actions);
        else ApplyNow(b => b.Actions = actions, label, coalesce);
    }

    /// <inheritdoc/>
    public void StepChanged(ActionStepViewModel step) => CommitSteps("Modifier l'action", "steps", draft: true);

    /// <inheritdoc/>
    public void MoveStep(ActionStepViewModel step, int delta)
    {
        int i = Steps.IndexOf(step), j = i + delta;
        if (i < 0 || j < 0 || j >= Steps.Count) return;
        Steps.Move(i, j);
        RefreshOrder();
        CommitSteps("Réordonner les étapes", null);
    }

    /// <inheritdoc/>
    public void RemoveStep(ActionStepViewModel step)
    {
        if (!Steps.Remove(step)) return;
        RefreshOrder();
        CommitSteps("Supprimer l'étape", null);
    }

    [RelayCommand]
    void AddStep()
    {
        Steps.Add(new ActionStepViewModel(new DeckAction { Type = ActionTypes.Hotkey }, this));
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
            ApplyNow(b => b.ImagePath = name, "Importer une image");
            Notifier.Success("Image importée");
        }
        catch (InvalidDataException ex) { Notifier.Error(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notifier.Error("Lecture impossible : " + ex.Message); }
    }

    [RelayCommand]
    void RemoveImage()
    {
        ImageName = null;
        ApplyNow(b => b.ImagePath = null, "Retirer l'image");
    }

    // ---------- réinitialisation et actions rapides ----------

    /// <summary>Remet l'apparence aux valeurs d'origine de l'action (catalogue) ou aux valeurs neutres ; les actions ne bougent pas.</summary>
    [RelayCommand]
    public void ResetAppearance()
    {
        if (_place == null) return;
        Flush();
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
