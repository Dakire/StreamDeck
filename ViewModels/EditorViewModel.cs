using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchDeck.Actions;
using TouchDeck.Integrations;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

/// <summary>Ce qu'on fait glisser : une case de la grille ou une entrée du catalogue.</summary>
public sealed record DragPayload(Place? Source, int SourceIndex, string? CatalogId)
{
    /// <summary>true si l'élément glissé vient du catalogue (sinon d'une case de la grille).</summary>
    public bool IsCatalog => CatalogId != null;
}

/// <summary>Effet d'un glisser-déposer sur la case survolée (indiqué pendant le survol, appliqué au dépôt).</summary>
public enum DropKind
{
    /// <summary>Dépôt impossible ici.</summary>
    None,
    /// <summary>Déplace le bouton dans une case vide.</summary>
    Move,
    /// <summary>Copie le bouton (Ctrl enfoncé) dans une case vide.</summary>
    Copy,
    /// <summary>Échange les deux boutons.</summary>
    Swap,
    /// <summary>Range le bouton dans le dossier survolé.</summary>
    IntoFolder,
    /// <summary>Sort le bouton du dossier courant (case « retour »).</summary>
    ToParent,
    /// <summary>Remplace le bouton par l'action du catalogue.</summary>
    Replace,
}

/// <summary>
/// État de l'éditeur : profil, page et dossier en cours de modification, grille, sélection, presse-papiers,
/// glisser-déposer et menus contextuels. Toute modification passe par le <see cref="DeckDocument"/> partagé.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    /// <summary>Formats de grille proposés (le format « Automatique » suit l'écran du deck).</summary>
    public static readonly FormatChoice[] FormatPresets =
    {
        new("Automatique (selon l'écran)", 0, 0),
        new("3 × 2 · 6 touches", 3, 2),
        new("5 × 3 · 15 touches", 5, 3),
        new("6 × 3 · 18 touches", 6, 3),
        new("5 × 4 · 20 touches", 5, 4),
        new("8 × 4 · 32 touches", 8, 4),
    };

    readonly DeckDocument _doc;
    readonly DisplayService _displays;
    readonly GridState _grid;
    readonly IAppWindows _windows;
    readonly PlatformServices _platform;
    readonly IEditorShell _shell;
    readonly ConfigPersistence _persistence;
    readonly List<int> _folder = new();
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    string _profile;
    int _page;
    int _selectedIndex = -1;
    bool _suppressSelection;
    bool _refreshingLists;
    Notice? _toast;

    /// <summary>Cases de la grille affichée (avec la case « retour » dans un dossier).</summary>
    public ObservableCollection<SlotViewModel> Slots { get; } = new();
    /// <summary>Onglets des pages du profil affiché.</summary>
    public ObservableCollection<PageTabViewModel> Pages { get; } = new();
    /// <summary>Fil d'Ariane : profil, page, dossiers ouverts.</summary>
    public ObservableCollection<CrumbViewModel> Crumbs { get; } = new();
    /// <summary>Profils de la liste déroulante.</summary>
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();
    /// <summary>Formats de grille proposés pour le profil.</summary>
    public ObservableCollection<FormatChoice> Formats { get; } = new();
    /// <summary>Écrans proposés pour le deck.</summary>
    public ObservableCollection<ScreenChoice> Screens { get; } = new();

    /// <summary>Catalogue d'actions (panneau de gauche).</summary>
    public CatalogViewModel Catalog { get; }
    /// <summary>Propriétés de la case sélectionnée (panneau de droite).</summary>
    public PropertiesViewModel Properties { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(GridMaxWidth))] int _columns = 5;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GridMaxHeight))] int _rows = 3;
    [ObservableProperty] SlotViewModel? _selectedSlot;
    [ObservableProperty] PageTabViewModel? _selectedPage;
    [ObservableProperty] ProfileItemViewModel? _selectedProfile;
    [ObservableProperty] FormatChoice? _selectedFormat;
    [ObservableProperty] ScreenChoice? _selectedScreen;
    [ObservableProperty] string _deviceText = "";
    [ObservableProperty] bool _deviceConnected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasOverflow))] string _overflowText = "";
    [ObservableProperty] bool _isActiveProfile;
    [ObservableProperty] string _appMatchText = "";
    [ObservableProperty] bool _inFolder;
    /// <summary>true quand l'éditeur a été ouvert depuis le deck (clic droit, appui long) sur une seule case : « Enregistrer » et « Annuler » referment la fenêtre.</summary>
    [ObservableProperty] bool _quickEdit;

    // toast
    [ObservableProperty] bool _toastVisible;
    [ObservableProperty] string _toastText = "";
    [ObservableProperty] string? _toastActionText;
    [ObservableProperty] NoticeKind _toastKind;

    /// <summary>Taille maximale de la grille à l'écran : les cases ne grossissent pas indéfiniment sur un grand écran.</summary>
    public double GridMaxWidth => Columns * 180 + 24;
    /// <summary>Hauteur maximale de la grille (le reste défile).</summary>
    public double GridMaxHeight => Rows * 180 + 24;
    /// <summary>true si des boutons dépassent la capacité de la grille (sous-pages sur le deck).</summary>
    public bool HasOverflow => OverflowText.Length > 0;

    /// <summary>true s'il y a une modification à annuler.</summary>
    public bool CanUndo => _doc.CanUndo;
    /// <summary>true s'il y a une modification à rétablir.</summary>
    public bool CanRedo => _doc.CanRedo;
    /// <summary>Info-bulle d'« Annuler » : ce qui sera défait.</summary>
    public string UndoTooltip => _doc.CanUndo ? $"Annuler : {_doc.UndoLabel} (Ctrl+Z)" : "Rien à annuler";
    /// <summary>Info-bulle de « Rétablir » : ce qui sera refait.</summary>
    public string RedoTooltip => _doc.CanRedo ? $"Rétablir : {_doc.RedoLabel} (Ctrl+Y)" : "Rien à rétablir";
    /// <summary>true si une case est sélectionnée.</summary>
    public bool HasSelection => _selectedIndex >= 0;
    /// <summary>true si le presse-papiers interne contient un bouton.</summary>
    public bool CanPasteHere => _doc.HasClipboard;

    /// <summary>Emplacement affiché : profil, page, dossiers ouverts.</summary>
    public Place Place => new(_profile, _page, _folder.ToArray());
    DeckProfile Profile => _doc.FindProfile(_profile) ?? _doc.ActiveProfile;
    int Offset => _folder.Count > 0 ? 1 : 0;

    /// <summary>Éditeur sur le document partagé ; <paramref name="shell"/> fournit dialogues et focus.</summary>
    public EditorViewModel(IEditorShell shell, DeckDocument doc, DisplayService displays, GridState grid, IAppWindows windows, PlatformServices platform, IntegrationManager integrations, ConfigPersistence persistence)
    {
        _persistence = persistence;
        _platform = platform;
        _shell = shell;
        _doc = doc;
        _displays = displays;
        _grid = grid;
        _windows = windows;
        _profile = _doc.ActiveProfile.Name;
        Catalog = new CatalogViewModel(_doc);
        Properties = new PropertiesViewModel(_doc, this, shell, platform.Windows, integrations);

        _doc.Changed += OnDocumentChanged;
        Notifier.Posted += OnNotice;
        _displays.Changed += RefreshDevices;
        _toastTimer.Tick += (_, _) => DismissToast();

        RefreshAll();
    }

    /// <summary>Se désabonne du document, des messages et des écrans (fermeture de la fenêtre).</summary>
    public void Dispose()
    {
        _doc.Changed -= OnDocumentChanged;
        Notifier.Posted -= OnNotice;
        _displays.Changed -= RefreshDevices;
        _toastTimer.Stop();
    }

    // ---------- navigation ----------

    /// <summary>
    /// Se place sur un emplacement donné (appelé quand le deck demande à modifier un bouton). <paramref name="quick"/> : ouverture directe
    /// sur cette case, le focus va au titre (bouton) ou à la recherche d'actions (case vide) pour modifier ou créer sans étape intermédiaire.
    /// <paramref name="addCatalogId"/> : action du catalogue à poser aussitôt dans la case (modèle du deck qui demande des réglages).
    /// </summary>
    public void GoTo(Place? place, int index, bool quick = false, string? addCatalogId = null)
    {
        Properties.Flush();
        Properties.BeginSession();
        QuickEdit = false;
        if (place != null && _doc.FindProfile(place.Profile) != null)
        {
            _profile = _doc.FindProfile(place.Profile)!.Name;
            _page = Math.Clamp(place.Page, 0, Profile.Pages.Count - 1);
            _folder.Clear();
            foreach (var i in place.Path) _folder.Add(i);
            ValidatePlace();
            _selectedIndex = index;
        }
        RefreshAll();
        SelectIndex(_selectedIndex);
        QuickEdit = quick && _selectedIndex >= 0;
        if (!QuickEdit) _shell.FocusSlot(_selectedIndex);
        else if (_doc.Get(Place, _selectedIndex) == null) _shell.FocusCatalogSearch();
        else _shell.FocusTitle();
        // Modèle du deck à compléter : posé après l'ouverture, donc « Annuler » rend la case vide
        if (addCatalogId != null && quick && _selectedIndex >= 0) AddCatalogActionById(addCatalogId, _selectedIndex);
    }

    void ValidatePlace()
    {
        var profile = _doc.FindProfile(_profile) ?? _doc.ActiveProfile;
        _profile = profile.Name;
        _page = Math.Clamp(_page, 0, profile.Pages.Count - 1);
        // garde le plus long préfixe de chemin qui existe encore
        var place = new Place(_profile, _page);
        int keep = 0;
        foreach (var i in _folder)
        {
            var next = place.Enter(i);
            if (_doc.Container(next) == null) break;
            place = next; keep++;
        }
        if (keep < _folder.Count) { _folder.RemoveRange(keep, _folder.Count - keep); _selectedIndex = -1; }
    }

    void SwitchProfile(string name)
    {
        if (string.Equals(name, _profile, StringComparison.OrdinalIgnoreCase)) return;
        _profile = name; _page = 0; _folder.Clear(); _selectedIndex = -1; QuickEdit = false;
        RefreshAll();
        SelectIndex(-1);
    }

    void SwitchPage(int index)
    {
        if (index == _page && _folder.Count == 0) return;
        _page = index; _folder.Clear(); _selectedIndex = -1; QuickEdit = false;
        RefreshAll();
        SelectIndex(-1);
    }

    /// <summary>Ouvre le dossier sélectionné (Entrée, double-clic).</summary>
    public void OpenSelectedFolder()
    {
        if (_selectedIndex < 0 || _doc.Get(Place, _selectedIndex) is not { IsFolder: true }) return;
        _folder.Add(_selectedIndex);
        _selectedIndex = -1; QuickEdit = false;
        RefreshAll();
        SelectIndex(-1);
        _shell.FocusSlot(-1);
    }

    /// <summary>Remonte d'un dossier en sélectionnant celui qu'on quitte (Retour arrière).</summary>
    [RelayCommand]
    public void GoUp()
    {
        if (_folder.Count == 0) return;
        int from = _folder[^1];
        _folder.RemoveAt(_folder.Count - 1);
        _selectedIndex = from;
        RefreshAll();
        SelectIndex(from);
        _shell.FocusSlot(from);
    }

    void GoToDepth(int depth)
    {
        if (depth >= _folder.Count) return;
        int from = _folder[depth];
        _folder.RemoveRange(depth, _folder.Count - depth);
        _selectedIndex = from;
        RefreshAll();
        SelectIndex(from);
    }

    // ---------- rafraîchissement ----------

    /// <summary>
    /// Met à jour ce que la modification touche. Un bouton modifié sur place (titre, icône, étapes) ne change ni les profils, ni les pages,
    /// ni le catalogue, ni les écrans : seules la grille et le fil d'Ariane sont rafraîchis. Le catalogue ne dépend que des favoris et
    /// récents (réglages) et les écrans que des réglages : un changement de contenu ne les recalcule pas.
    /// </summary>
    void OnDocumentChanged(DocumentChange change)
    {
        // Le document a été remplacé (annuler, rétablir, rechargement, import) : une saisie en attente ne vise plus rien
        if (change.Kind == ChangeKind.Replaced) Properties.DiscardDraft();
        ValidatePlace();
        if (!change.InPlace)
        {
            RefreshProfiles();
            RefreshPages();
        }
        RefreshCrumbs();
        RefreshSlots();
        if (change.Kind != ChangeKind.Content)
        {
            RefreshDevices();
            Catalog.Refresh();
        }
        if (!Properties.IsApplying) Properties.Show(_selectedIndex >= 0 ? Place : null, _selectedIndex);
        NotifyHistory();
    }

    void RefreshAll()
    {
        ValidatePlace();
        RefreshProfiles();
        RefreshPages();
        RefreshCrumbs();
        RefreshSlots();
        RefreshDevices();
        Properties.Show(_selectedIndex >= 0 ? Place : null, _selectedIndex);
        NotifyHistory();
    }

    void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTooltip)); OnPropertyChanged(nameof(RedoTooltip));
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanPasteHere));
    }

    void RefreshProfiles()
    {
        _refreshingLists = true;
        try
        {
            Profiles.Clear();
            foreach (var p in _doc.Config.Profiles)
            {
                int buttons = p.Pages.Sum(pg => DeckDocument.AllButtons(pg.Buttons).Count());
                Profiles.Add(new ProfileItemViewModel
                {
                    Name = p.Name, AppMatch = p.AppMatch,
                    IsActive = p.Name.Equals(_doc.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase),
                    Detail = $"{p.Pages.Count} page(s) · {buttons} bouton(s)" + (p.AppMatch != null ? $" · {p.AppMatch}" : ""),
                });
            }
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name.Equals(_profile, StringComparison.OrdinalIgnoreCase));
            var cur = Profile;
            IsActiveProfile = cur.Name.Equals(_doc.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase);
            AppMatchText = cur.AppMatch == null ? "" : $"S'affiche quand « {cur.AppMatch} » est au premier plan";

            Formats.Clear();
            foreach (var f in FormatPresets) Formats.Add(f);
            var match = Formats.FirstOrDefault(f => f.Columns == cur.Columns && f.Rows == cur.Rows);
            if (match == null)
            {
                match = new FormatChoice($"{cur.Columns} × {cur.Rows} · personnalisé", cur.Columns, cur.Rows);
                Formats.Add(match);
            }
            SelectedFormat = match;
        }
        finally { _refreshingLists = false; }
    }

    void RefreshPages()
    {
        Pages.Clear();
        var pages = Profile.Pages;
        for (int i = 0; i < pages.Count; i++)
            Pages.Add(new PageTabViewModel { Index = i, Name = pages[i].Name, Hidden = pages[i].Hidden, IsSelected = i == _page });
        _refreshingLists = true;
        SelectedPage = Pages.ElementAtOrDefault(_page);
        _refreshingLists = false;
    }

    void RefreshCrumbs()
    {
        Crumbs.Clear();
        InFolder = _folder.Count > 0;
        var place = new Place(_profile, _page);
        Crumbs.Add(new CrumbViewModel
        {
            Text = Profile.Pages[_page].Name, Depth = 0, IsLast = _folder.Count == 0,
            Symbol = SymbolRegular.Home24, HasSymbol = true,
            Command = new RelayCommand(() => GoToDepth(0)),
        });
        for (int d = 0; d < _folder.Count; d++)
        {
            place = place.Enter(_folder[d]);
            int depth = d + 1;
            Crumbs.Add(new CrumbViewModel
            {
                Text = _doc.FolderAt(place)?.Label is { Length: > 0 } t ? t : "Dossier", Depth = depth, IsLast = depth == _folder.Count,
                Command = new RelayCommand(() => GoToDepth(depth)),
            });
        }
    }

    (int Cols, int Rows) Dimensions()
    {
        var p = Profile;
        var auto = _grid.Auto;
        int cols = p.Columns > 0 ? p.Columns : _doc.Config.Columns > 0 ? _doc.Config.Columns : auto.Columns;
        int rows = p.Rows > 0 ? p.Rows : _doc.Config.Rows > 0 ? _doc.Config.Rows : auto.Rows;
        return (Math.Max(1, cols), Math.Max(1, rows));
    }

    void RefreshSlots()
    {
        var list = _doc.Container(Place) ?? new List<DeckButton>();
        var (cols, rows) = Dimensions();
        int capacity = cols * rows;
        int offset = Offset;
        int last = -1;
        for (int i = 0; i < list.Count; i++) if (!list[i].Blank) last = i;
        int needed = last + 1 + offset;
        int total = capacity;
        if (needed > capacity) total = (int)Math.Ceiling(needed / (double)cols) * cols + cols;   // une ligne libre pour poursuivre
        if (_selectedIndex >= 0 && _selectedIndex + offset + 1 > total) total = (int)Math.Ceiling((_selectedIndex + offset + 1) / (double)cols) * cols;

        Columns = cols;
        Rows = total / cols;
        while (Slots.Count < total) Slots.Add(new SlotViewModel(Slots.Count));
        while (Slots.Count > total) Slots.RemoveAt(Slots.Count - 1);

        var cfg = _doc.Config;
        var profile = Profile;
        for (int pos = 0; pos < total; pos++)
        {
            var s = Slots[pos];
            s.IsBack = offset == 1 && pos == 0;
            s.Index = s.IsBack ? -1 : pos - offset;
            s.IsOverflow = pos >= capacity;
            var b = !s.IsBack && s.Index < list.Count && !list[s.Index].Blank ? list[s.Index] : null;
            s.IsEmpty = b == null && !s.IsBack;
            s.IsFolder = b?.IsFolder == true;
            s.IsDropTarget = false;
            s.IsDragSource = false;
            if (s.IsBack)
            {
                s.Face = BackFace;
                s.AutomationName = "Retour au dossier parent";
                s.Tooltip = "Retour au dossier parent";
                s.Issue = null; s.IssueText = "";
            }
            else if (b == null)
            {
                s.Face = TileFaceData.Empty;
                s.AutomationName = $"Case vide {pos + 1}";
                s.Tooltip = "Case vide : glisse une action du catalogue ou double-clique pour en ajouter";
                s.Issue = null; s.IssueText = "";
            }
            else
            {
                s.Face = TileFaceData.From(b);
                var issues = ActionValidator.Validate(b, cfg, profile);
                s.Issue = ActionValidator.Worst(issues);
                s.IssueText = string.Join(" ; ", issues.Select(i => i.Message));
                string kind = b.IsFolder ? "Dossier" : b.Actions.Count > 1 ? $"{b.Actions.Count} étapes" : "";
                string name = b.Label.Length > 0 ? b.Label : "Sans titre";
                s.AutomationName = $"Case {pos + 1} : {(kind.Length > 0 ? kind + " " : "")}{name}" + (s.Issue != null ? $". {(s.Issue == Severity.Error ? "Erreur" : "Attention")} : {s.IssueText}" : "");
                s.Tooltip = s.Issue != null ? $"{name}\n{(s.Issue == Severity.Error ? "Erreur" : "Attention")} : {s.IssueText}" : name;
            }
        }
        int overflowCount = Math.Max(0, needed - capacity);
        OverflowText = overflowCount > 0 ? $"{overflowCount} bouton(s) dépassent la grille : le deck les affiche sur une page suivante." : "";

        // La sélection suit l'indice, pas l'objet : les cases sont recyclées d'un dossier à l'autre
        _suppressSelection = true;
        SelectedSlot = _selectedIndex >= 0 ? Slots.FirstOrDefault(s => !s.IsBack && s.Index == _selectedIndex) : null;
        _suppressSelection = false;
    }

    static readonly TileFaceData BackFace = new()
    {
        Label = "Retour", Symbol = SymbolRegular.ArrowLeft24, HasSymbol = true,
        Foreground = System.Windows.Media.Brushes.White, Background = Theme.B("#37373D"),
    };

    /// <summary>Recharge la liste des écrans (branchement, débranchement).</summary>
    public void RefreshDevices()
    {
        _refreshingLists = true;
        var (choices, selected) = ScreenChoice.Build(_doc.Config, _displays.All(), "Automatique (écran secondaire)");
        Screens.Clear();
        foreach (var s in choices) Screens.Add(s);
        var (text, connected) = _displays.Describe(_doc.Config);
        DeviceText = text; DeviceConnected = connected;
        SelectedScreen = selected;
        _refreshingLists = false;
    }

    // ---------- sélection ----------

    partial void OnSelectedSlotChanged(SlotViewModel? value)
    {
        if (_suppressSelection) return;
        QuickEdit = false;
        _selectedIndex = value is { IsBack: false } ? value.Index : -1;
        Properties.Show(_selectedIndex >= 0 ? Place : null, _selectedIndex);
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Sélectionne une case (index de liste, -1 : aucune) et met à jour les propriétés.</summary>
    public void SelectIndex(int index)
    {
        _selectedIndex = index;
        var slot = index >= 0 ? Slots.FirstOrDefault(s => !s.IsBack && s.Index == index) : null;
        if (index >= 0 && slot == null) { RefreshSlots(); slot = Slots.FirstOrDefault(s => !s.IsBack && s.Index == index); }
        _suppressSelection = true;
        SelectedSlot = slot;
        _suppressSelection = false;
        Properties.Show(slot != null ? Place : null, slot != null ? index : -1);
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        if (_refreshingLists || value == null) return;
        SwitchProfile(value.Name);
    }

    partial void OnSelectedPageChanged(PageTabViewModel? value)
    {
        if (_refreshingLists || value == null) return;
        SwitchPage(value.Index);
    }

    partial void OnSelectedFormatChanged(FormatChoice? value)
    {
        if (_refreshingLists || value == null) return;
        _doc.SetProfileLayout(_profile, value.Columns, value.Rows);
    }

    partial void OnSelectedScreenChanged(ScreenChoice? value)
    {
        if (_refreshingLists || value == null || value.IsCurrent(_doc.Config)) return;
        _doc.UpdateSettings(value.ApplyTo, "Écran du deck");
    }

    /// <summary>Première case libre de la grille courante (au-delà du contenu si besoin).</summary>
    int FirstFree() => _doc.FirstEmptyIndex(Place);

    /// <summary>Case visée par une commande : la sélection, sinon la première case libre.</summary>
    int TargetIndex() => _selectedIndex >= 0 ? _selectedIndex : FirstFree();

    // ---------- historique ----------

    [RelayCommand] void Undo()
    {
        Properties.Flush();
        var label = _doc.UndoLabel;
        if (_doc.Undo()) Notifier.Info($"Annulé : {label}");
    }

    [RelayCommand] void Redo()
    {
        Properties.Flush();
        var label = _doc.RedoLabel;
        if (_doc.Redo()) Notifier.Info($"Rétabli : {label}");
    }

    // ---------- opérations sur la sélection ----------

    bool NeedSelection(string what)
    {
        Properties.Flush();   // l'opération porte sur le bouton tel qu'il est saisi
        if (_selectedIndex >= 0) return true;
        Notifier.Info($"Sélectionne d'abord une case pour {what}");
        return false;
    }

    [RelayCommand]
    void Copy()
    {
        if (!NeedSelection("la copier")) return;
        if (_doc.Copy(Place, _selectedIndex)) { Notifier.Info("Copié"); NotifyHistory(); Properties.Show(Place, _selectedIndex); }
        else Notifier.Info("Cette case est vide : rien à copier");
    }

    [RelayCommand]
    void Cut()
    {
        if (!NeedSelection("la couper")) return;
        var r = _doc.Cut(Place, _selectedIndex);
        if (r.Ok) Notifier.Success("Coupé : colle-le dans une autre case", "Annuler", () => _doc.Undo());
        else Notifier.Info("Cette case est vide : rien à couper");
    }

    [RelayCommand]
    void Paste()
    {
        Properties.Flush();
        if (!_doc.HasClipboard) { Notifier.Info("Le presse-papiers est vide : copie d'abord un bouton"); return; }
        int target = TargetIndex();
        bool replacing = _doc.Get(Place, target) != null;
        var r = _doc.Paste(Place, target);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Collage impossible"); return; }
        SelectIndex(target);
        Notifier.Success(replacing ? "Collé (case remplacée)" : "Collé", replacing ? "Annuler" : null, replacing ? () => _doc.Undo() : null);
    }

    [RelayCommand]
    void Duplicate()
    {
        if (!NeedSelection("la dupliquer")) return;
        var r = _doc.Duplicate(Place, _selectedIndex);
        if (!r.Ok) { Notifier.Info("Cette case est vide : rien à dupliquer"); return; }
        SelectIndex(r.Index);
        Notifier.Success("Bouton dupliqué");
    }

    /// <summary>Supprime le bouton sélectionné (confirmation pour un dossier non vide) ; annulable.</summary>
    [RelayCommand]
    public void DeleteSelection()
    {
        _ = DeleteSelectionAsync();
    }

    async System.Threading.Tasks.Task DeleteSelectionAsync()
    {
        if (!NeedSelection("la supprimer")) return;
        var b = _doc.Get(Place, _selectedIndex);
        if (b == null) { Notifier.Info("Cette case est déjà vide"); return; }
        int count = b.IsFolder ? DeckDocument.AllButtons(b.Children ?? new()).Count() : 0;
        if (count > 0 && !await _shell.ConfirmAsync("Supprimer le dossier",
                $"Le dossier « {b.Label} » contient {count} bouton(s). Ils seront supprimés avec lui (tu pourras annuler).", "Supprimer", danger: true))
            return;
        int idx = _selectedIndex;
        var r = _doc.ClearButton(Place, idx);
        if (!r.Ok) return;
        SelectIndex(idx);
        Notifier.Success(b.IsFolder ? "Dossier supprimé" : "Bouton supprimé", "Annuler", () => _doc.Undo());
        _shell.FocusSlot(idx);
    }

    /// <summary>Crée un dossier dans la case sélectionnée, ou dans la première case libre.</summary>
    public void CreateFolderHere()
    {
        int target = TargetIndex();
        if (_doc.Get(Place, target) != null) { Notifier.Info("Choisis une case vide pour créer un dossier"); return; }
        var r = _doc.CreateFolder(Place, target);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Création impossible"); return; }
        SelectIndex(target);
        Notifier.Success("Dossier créé : double-clique pour l'ouvrir");
        _shell.FocusTitle();
    }

    [RelayCommand] void NewFolder() => CreateFolderHere();

    [RelayCommand]
    void ResetButton()
    {
        if (!NeedSelection("la réinitialiser")) return;
        Properties.ResetAppearance();
    }

    /// <summary>
    /// Valide la saisie en cours et enregistre sur le disque sans attendre. Ouvert depuis le deck, referme l'éditeur : le bouton est prêt.
    /// </summary>
    [RelayCommand]
    public void SaveButton()
    {
        Properties.Flush();
        Properties.MarkSaved();
        _persistence.Flush();
        if (QuickEdit) _shell.Close();
        else Notifier.Success("Modifications enregistrées");
    }

    /// <summary>
    /// Annule ce qui a été fait sur la case depuis son ouverture (saisie en cours comprise) : le bouton retrouve son état d'origine,
    /// ou la case redevient vide si le bouton vient d'y être créé. Ouvert depuis le deck, referme l'éditeur.
    /// </summary>
    [RelayCommand]
    public void CancelButton()
    {
        bool reverted = Properties.RevertToBaseline();
        if (QuickEdit) _shell.Close();
        else if (reverted) Notifier.Info("Modifications annulées");
    }

    [RelayCommand] void RenameButton() { if (NeedSelection("modifier son titre")) _shell.FocusTitle(); }
    [RelayCommand] void ShowProperties() { if (NeedSelection("voir ses propriétés")) _shell.FocusProperties(); }

    /// <summary>Échange la case sélectionnée avec sa voisine (Alt + flèches, ou menu Déplacer).</summary>
    public void MoveSelection(int dx, int dy)
    {
        if (_selectedIndex < 0) return;
        Properties.Flush();
        int pos = _selectedIndex + Offset;
        int col = pos % Columns, row = pos / Columns;
        int ncol = col + dx, nrow = row + dy;
        if (ncol < 0 || ncol >= Columns || nrow < 0 || nrow >= Rows) { Notifier.Info("Bord de la grille atteint"); return; }
        int target = nrow * Columns + ncol - Offset;
        if (target < 0) return;
        if (_doc.Get(Place, _selectedIndex) == null) return;
        var r = _doc.MoveButton(Place, _selectedIndex, Place, target);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(target);
        _shell.FocusSlot(target);
    }

    // ---------- catalogue ----------

    /// <summary>Ajoute une action du catalogue dans la case visée, sinon sélectionnée, sinon la première libre.</summary>
    public void AddCatalogAction(CatalogAction action, int? targetIndex = null)
    {
        Properties.Flush();
        int target = targetIndex ?? (_selectedIndex >= 0 && _doc.Get(Place, _selectedIndex) == null ? _selectedIndex : FirstFree());
        bool replacing = _doc.Get(Place, target) != null;
        var button = action.CreateButton();
        var r = _doc.PutButton(Place, target, button, $"Ajouter « {action.Name} »");
        if (!r.Ok) { Notifier.Error(r.Message ?? "Ajout impossible"); return; }
        _doc.AddRecent(action.Id);
        SelectIndex(target);
        var errors = ActionValidator.Validate(button, _doc.Config, Profile).Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            // L'action doit être complétée (raccourci, chemin…) : on amène l'utilisateur sur le champ à remplir
            Notifier.Info($"« {action.Name} » ajouté : complète ses réglages à droite");
            _shell.FocusProperties();
        }
        else Notifier.Success($"« {action.Name} » ajouté", replacing ? "Annuler" : null, replacing ? () => _doc.Undo() : null);
    }

    /// <summary>Entrée dans la recherche du catalogue : ajoute la première action trouvée à la case visée et vide la recherche.</summary>
    public void AddFirstCatalogResult()
    {
        var first = Catalog.Sections.SelectMany(s => s.Items).FirstOrDefault();
        if (first == null || !Catalog.IsSearching) return;
        AddCatalogAction(first.Action);
        Catalog.Search = "";
    }

    /// <summary>Comme <see cref="AddCatalogAction"/>, d'après l'identifiant du catalogue.</summary>
    public void AddCatalogActionById(string id, int? targetIndex = null)
    {
        if (ActionCatalog.Find(id) is { } a) AddCatalogAction(a, targetIndex);
    }

    // ---------- glisser-déposer ----------

    /// <summary>Que se passerait-il si on lâchait ici ? Aucune modification : sert à l'effet du curseur et à la surbrillance.</summary>
    public DropKind EvaluateDrop(DragPayload payload, SlotViewModel target, bool copy)
    {
        var place = Place;
        if (target.IsBack)
            return !payload.IsCatalog && payload.Source != null && payload.Source.Equals(place) ? DropKind.ToParent : DropKind.None;
        var existing = _doc.Get(place, target.Index);
        if (payload.IsCatalog)
        {
            if (existing is { IsFolder: true } && ActionCatalog.Find(payload.CatalogId)?.IsFolder != true) return DropKind.IntoFolder;
            return existing == null ? DropKind.Move : DropKind.Replace;
        }
        if (payload.Source == null || _doc.Get(payload.Source, payload.SourceIndex) == null) return DropKind.None;
        bool same = payload.Source.Equals(place) && payload.SourceIndex == target.Index;
        if (same) return DropKind.None;
        if (existing == null) return copy ? DropKind.Copy : DropKind.Move;
        if (copy) return DropKind.None;                 // copier sur une case occupée écraserait un bouton : refusé
        if (existing.IsFolder && !(payload.Source.Equals(place) && payload.SourceIndex == target.Index)) return DropKind.IntoFolder;
        return DropKind.Swap;
    }

    /// <summary>Applique un glisser-déposer sur une case (voir <see cref="DropKind"/>) ; <paramref name="copy"/> : Ctrl enfoncé.</summary>
    public void Drop(DragPayload payload, SlotViewModel target, bool copy)
    {
        var kind = EvaluateDrop(payload, target, copy);
        var place = Place;
        OpResult r;
        string done;
        switch (kind)
        {
            case DropKind.None: return;
            case DropKind.ToParent:
                r = _doc.MoveToParent(payload.Source!, payload.SourceIndex); done = "Bouton sorti du dossier"; break;
            case DropKind.IntoFolder when payload.IsCatalog:
            {
                var a = ActionCatalog.Find(payload.CatalogId)!;
                var folderPlace = place.Enter(target.Index);
                int at = _doc.FirstEmptyIndex(folderPlace);
                r = _doc.PutButton(folderPlace, at, a.CreateButton(), $"Ajouter « {a.Name} »");
                if (r.Ok) _doc.AddRecent(a.Id);
                done = $"« {a.Name} » ajouté au dossier"; break;
            }
            case DropKind.IntoFolder:
                r = _doc.MoveIntoFolder(payload.Source!, payload.SourceIndex, place, target.Index); done = "Bouton déplacé dans le dossier"; break;
            case DropKind.Copy:
            {
                var b = _doc.Get(payload.Source!, payload.SourceIndex)!;
                r = _doc.PutButton(place, target.Index, ConfigStore.Clone(b), "Copier le bouton"); done = "Bouton copié"; break;
            }
            case DropKind.Move when payload.IsCatalog:
            case DropKind.Replace when payload.IsCatalog:
                AddCatalogActionById(payload.CatalogId!, target.Index);
                return;
            default:
                r = _doc.MoveButton(payload.Source!, payload.SourceIndex, place, target.Index);
                done = kind == DropKind.Swap ? "Boutons échangés" : "Bouton déplacé"; break;
        }
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        if (kind is not (DropKind.IntoFolder or DropKind.ToParent)) SelectIndex(target.Index);
        else SelectIndex(-1);
        Notifier.Success(done, "Annuler", () => _doc.Undo());
    }

    /// <summary>Dépôt sur un onglet de page : le bouton va à la première case libre de cette page.</summary>
    public bool CanDropOnPage(DragPayload payload, PageTabViewModel page) =>
        !payload.IsCatalog && payload.Source != null && _doc.Get(payload.Source, payload.SourceIndex) != null
        && !(payload.Source.Equals(new Place(_profile, page.Index)));

    /// <summary>Envoie l'élément glissé dans une autre page (première case libre).</summary>
    public void DropOnPage(DragPayload payload, PageTabViewModel page)
    {
        if (!CanDropOnPage(payload, page)) return;
        var dst = new Place(_profile, page.Index);
        var r = _doc.MoveButton(payload.Source!, payload.SourceIndex, dst, _doc.FirstEmptyIndex(dst));
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success($"Bouton déplacé vers la page « {page.Name} »", "Annuler", () => _doc.Undo());
    }

    // ---------- pages ----------

    [RelayCommand]
    async System.Threading.Tasks.Task AddPage()
    {
        var name = await _shell.PromptAsync("Nouvelle page", "Nom de la page", $"Page {Profile.Pages.Count + 1}", "Créer");
        if (name == null) return;
        var r = _doc.AddPage(_profile, name);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Création impossible"); return; }
        SwitchPage(r.Index);
        Notifier.Success("Page ajoutée");
    }

    /// <summary>Demande un nouveau nom de page.</summary>
    public async System.Threading.Tasks.Task RenamePageAsync(PageTabViewModel page)
    {
        var name = await _shell.PromptAsync("Renommer la page", "Nouveau nom", page.Name, "Renommer");
        if (name == null) return;
        var r = _doc.RenamePage(_profile, page.Index, name);
        if (!r.Ok && r.Message != "Nom inchangé") Notifier.Error(r.Message ?? "Renommage impossible");
    }

    /// <summary>Duplique la page.</summary>
    public void DuplicatePage(PageTabViewModel page)
    {
        var r = _doc.DuplicatePage(_profile, page.Index);
        if (r.Ok) { SwitchPage(r.Index); Notifier.Success("Page dupliquée"); }
    }

    /// <summary>Masque ou affiche la page dans les onglets du deck.</summary>
    public void TogglePageHidden(PageTabViewModel page)
    {
        var r = _doc.SetPageHidden(_profile, page.Index, !page.Hidden);
        if (r.Ok) Notifier.Info(page.Hidden ? "Page affichée dans les onglets du deck" : "Page masquée : accessible par une action « Page »");
    }

    /// <summary>Déplace la page de <paramref name="delta"/> rangs.</summary>
    public void MovePage(PageTabViewModel page, int delta)
    {
        var r = _doc.MovePage(_profile, page.Index, delta);
        if (r.Ok) { _page = r.Index; RefreshAll(); }
    }

    /// <summary>Supprime la page après confirmation (si elle contient des boutons) ; annulable.</summary>
    public async System.Threading.Tasks.Task DeletePageAsync(PageTabViewModel page)
    {
        if (Profile.Pages.Count <= 1) { Notifier.Info("Un profil doit garder au moins une page"); return; }
        int n = DeckDocument.AllButtons(Profile.Pages[page.Index].Buttons).Count();
        if (n > 0 && !await _shell.ConfirmAsync("Supprimer la page", $"La page « {page.Name} » contient {n} bouton(s). Tu pourras annuler.", "Supprimer", danger: true)) return;
        var r = _doc.DeletePage(_profile, page.Index);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Suppression impossible"); return; }
        SwitchPage(Math.Min(_page, Profile.Pages.Count - 1));
        Notifier.Success("Page supprimée", "Annuler", () => _doc.Undo());
    }

    // ---------- profils ----------

    [RelayCommand]
    async System.Threading.Tasks.Task NewProfile()
    {
        var name = await _shell.PromptAsync("Nouveau profil", "Nom du profil", "Nouveau profil", "Créer");
        if (name == null) return;
        var cur = Profile;
        var r = _doc.AddProfile(name, cur.Columns, cur.Rows);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Création impossible"); return; }
        SwitchProfile(_doc.Config.Profiles[r.Index].Name);
        Notifier.Success("Profil créé");
    }

    /// <summary>Crée un profil à partir d'un modèle livré et l'affiche dans l'éditeur ; annulable.</summary>
    public void NewProfileFromTemplate(ProfileTemplate template)
    {
        var r = _doc.AddImportedProfile(template.Build());
        if (!r.Ok) { Notifier.Error(r.Message ?? "Création impossible"); return; }
        SwitchProfile(_doc.Config.Profiles[r.Index].Name);
        Notifier.Success($"Profil « {template.Name} » créé", "Annuler", () => _doc.Undo());
    }
    /// <summary>Demande un nouveau nom de profil.</summary>
    public async System.Threading.Tasks.Task RenameProfileAsync(string name)
    {
        var newName = await _shell.PromptAsync("Renommer le profil", "Nouveau nom", name, "Renommer");
        if (newName == null) return;
        bool wasEdited = name.Equals(_profile, StringComparison.OrdinalIgnoreCase);
        var r = _doc.RenameProfile(name, newName);
        if (!r.Ok) { if (r.Message != "Nom inchangé") Notifier.Error(r.Message ?? "Renommage impossible"); return; }
        if (wasEdited) { _profile = newName.Trim(); RefreshAll(); }
    }

    /// <summary>Duplique le profil.</summary>
    public void DuplicateProfile(string name)
    {
        var r = _doc.DuplicateProfile(name);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Duplication impossible"); return; }
        SwitchProfile(_doc.Config.Profiles[r.Index].Name);
        Notifier.Success("Profil dupliqué");
    }

    /// <summary>Supprime le profil après confirmation ; annulable.</summary>
    public async System.Threading.Tasks.Task DeleteProfileAsync(string name)
    {
        var p = _doc.FindProfile(name);
        if (p == null) return;
        if (_doc.Config.Profiles.Count <= 1) { Notifier.Info("Il faut garder au moins un profil"); return; }
        int n = p.Pages.Sum(pg => DeckDocument.AllButtons(pg.Buttons).Count());
        if (!await _shell.ConfirmAsync("Supprimer le profil", $"Le profil « {p.Name} » et ses {n} bouton(s) seront supprimés. Tu pourras annuler.", "Supprimer", danger: true)) return;
        bool wasEdited = p.Name.Equals(_profile, StringComparison.OrdinalIgnoreCase);
        var r = _doc.DeleteProfile(name);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Suppression impossible"); return; }
        if (wasEdited) SwitchProfile(_doc.ActiveProfile.Name);
        Notifier.Success("Profil supprimé", "Annuler", () => _doc.Undo());
    }

    /// <summary>Affiche ce profil sur le deck.</summary>
    public void SetActive(string name)
    {
        if (_doc.SetActiveProfile(name)) Notifier.Success($"« {name} » est maintenant le profil affiché sur le deck");
    }

    /// <summary>Associe le profil à un programme (choisi parmi les fenêtres ouvertes ou saisi).</summary>
    public async System.Threading.Tasks.Task SetAppMatchAsync(string name)
    {
        var p = _doc.FindProfile(name);
        if (p == null) return;
        var running = string.Join(", ", _platform.Windows.OpenProcessNames().Take(8));
        var app = await _shell.PromptAsync("Application associée",
            "Nom du programme (ex. chrome). Le profil s'affiche seul quand il est au premier plan. Laisse vide pour retirer l'association."
            + (running.Length > 0 ? $"\nOuverts en ce moment : {running}" : ""), p.AppMatch ?? "", "Enregistrer");
        if (app == null) return;
        var r = _doc.SetProfileApp(name, app);
        if (!r.Ok) { if (r.Message != "Inchangé") Notifier.Error(r.Message ?? "Association impossible"); return; }
        Notifier.Success(string.IsNullOrWhiteSpace(app) ? "Association retirée" : $"Profil associé à « {app.Trim()} »");
    }

    /// <summary>Exporte le profil en .touchdeck.</summary>
    public void ExportProfile(string name)
    {
        var p = _doc.FindProfile(name);
        if (p == null) return;
        var safe = string.Concat(p.Name.Select(c => Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
        var path = _shell.PickSaveFile("Exporter le profil", ProfilePackage.DialogFilter, safe + ProfilePackage.Extension);
        if (path != null) TransferService.ExportProfile(p, path);
    }

    [RelayCommand]
    async System.Threading.Tasks.Task ImportProfile()
    {
        var path = _shell.PickOpenFile("Importer un profil", ProfilePackage.DialogFilter);
        if (path == null) return;
        var name = await TransferService.ImportProfileAsync(_shell, _doc, path);
        if (name != null) SwitchProfile(name);
    }

    [RelayCommand] void OpenSettings() => _windows.OpenSettings();
    [RelayCommand] void ActivateCurrentProfile() => SetActive(_profile);
    [RelayCommand] async System.Threading.Tasks.Task RenameCurrentProfile() => await RenameProfileAsync(_profile);
    [RelayCommand] void ExportCurrentProfile() => ExportProfile(_profile);

    // ---------- menus contextuels ----------

    /// <summary>Menu contextuel d'une case, selon son contenu et le presse-papiers.</summary>
    public IReadOnlyList<MenuItemSpec> BuildSlotMenu(SlotViewModel slot)
    {
        if (slot.IsBack)
            return new[] { new MenuItemSpec("Retour au dossier parent", GoUp, SymbolRegular.ArrowLeft24) };

        var place = Place;
        bool occupied = !slot.IsEmpty;
        bool folder = slot.IsFolder;
        int pos = slot.Position;
        bool canLeft = pos % Columns > 0, canRight = pos % Columns < Columns - 1, canUp = pos >= Columns, canDown = pos + Columns < Slots.Count;

        var items = new List<MenuItemSpec>();
        if (folder) items.Add(new("Ouvrir le dossier", OpenSelectedFolder, SymbolRegular.FolderOpen24, "Entrée"));
        items.Add(new("Configurer l'action", () => _shell.FocusProperties(), SymbolRegular.Settings24, "Entrée", Enabled: occupied && !folder));
        items.Add(new("Modifier le titre", () => _shell.FocusTitle(), SymbolRegular.TextT24, "F2", Enabled: occupied));
        items.Add(new("Modifier l'icône", () => _shell.FocusIcon(), SymbolRegular.Image24, Enabled: occupied));
        items.Add(MenuItemSpec.Separator);
        items.Add(new("Copier", () => CopyCommand.Execute(null), SymbolRegular.Copy24, "Ctrl+C", Enabled: occupied));
        items.Add(new("Couper", () => CutCommand.Execute(null), SymbolRegular.Cut24, "Ctrl+X", Enabled: occupied));
        items.Add(new("Coller", () => PasteCommand.Execute(null), SymbolRegular.ClipboardPaste24, "Ctrl+V", Enabled: _doc.HasClipboard));
        items.Add(new("Dupliquer le bouton", () => DuplicateCommand.Execute(null), SymbolRegular.CopyAdd24, "Ctrl+D", Enabled: occupied));

        var move = new List<MenuItemSpec>
        {
            new("À gauche", () => MoveSelection(-1, 0), SymbolRegular.ArrowLeft24, "Alt+←", Enabled: occupied && canLeft),
            new("À droite", () => MoveSelection(1, 0), SymbolRegular.ArrowRight24, "Alt+→", Enabled: occupied && canRight),
            new("Vers le haut", () => MoveSelection(0, -1), SymbolRegular.ArrowUp24, "Alt+↑", Enabled: occupied && canUp),
            new("Vers le bas", () => MoveSelection(0, 1), SymbolRegular.ArrowDown24, "Alt+↓", Enabled: occupied && canDown),
        };
        var otherPages = Pages.Where(p => p.Index != _page).ToList();
        if (otherPages.Count > 0 && occupied)
        {
            move.Add(MenuItemSpec.Separator);
            move.Add(new("Vers la page", Icon: SymbolRegular.Document24,
                Children: otherPages.Select(p => new MenuItemSpec(p.Name, () => MoveToPage(slot.Index, p))).ToList()));
        }
        var folders = Slots.Where(s => s.IsFolder && s.Index != slot.Index).ToList();
        if (folders.Count > 0 && occupied)
            move.Add(new("Vers le dossier", Icon: SymbolRegular.Folder24,
                Children: folders.Select(f => new MenuItemSpec(_doc.Get(place, f.Index)?.Label is { Length: > 0 } t ? t : "Dossier", () => MoveToFolder(slot.Index, f.Index))).ToList()));
        if (_folder.Count > 0 && occupied)
            move.Add(new("Sortir du dossier", () => MoveOut(slot.Index), SymbolRegular.ArrowUpLeft24));
        items.Add(new("Déplacer le bouton", Icon: SymbolRegular.ArrowMove24, Enabled: occupied, Children: move));

        items.Add(MenuItemSpec.Separator);
        items.Add(new("Ajouter une action", Icon: SymbolRegular.Add24, Children: BuildAddMenu(slot.Index)));
        items.Add(new("Créer un dossier", () => { SelectIndex(slot.Index); CreateFolderHere(); }, SymbolRegular.FolderAdd24, Enabled: !occupied));
        items.Add(new("Réinitialiser le bouton", () => ResetButtonCommand.Execute(null), SymbolRegular.ArrowReset24, Enabled: occupied));
        items.Add(MenuItemSpec.Separator);
        items.Add(new("Afficher les propriétés", () => _shell.FocusProperties(), SymbolRegular.Info24, Enabled: occupied));
        items.Add(new("Supprimer le bouton", () => DeleteSelection(), SymbolRegular.Delete24, "Suppr", Enabled: occupied, Danger: true));
        return items;
    }

    List<MenuItemSpec> BuildAddMenu(int index) => CatalogMenu.Categories(a => AddCatalogAction(a, index));

    void MoveToPage(int index, PageTabViewModel page)
    {
        var dst = new Place(_profile, page.Index);
        var r = _doc.MoveButton(Place, index, dst, _doc.FirstEmptyIndex(dst));
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success($"Bouton déplacé vers « {page.Name} »", "Annuler", () => _doc.Undo());
    }

    void MoveToFolder(int index, int folderIndex)
    {
        var r = _doc.MoveIntoFolder(Place, index, Place, folderIndex);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success("Bouton déplacé dans le dossier", "Annuler", () => _doc.Undo());
    }

    void MoveOut(int index)
    {
        var r = _doc.MoveToParent(Place, index);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success("Bouton sorti du dossier", "Annuler", () => _doc.Undo());
    }

    /// <summary>Menu contextuel d'un profil.</summary>
    public IReadOnlyList<MenuItemSpec> BuildProfileMenu(string name)
    {
        bool active = name.Equals(_doc.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase);
        bool canDelete = _doc.Config.Profiles.Count > 1;
        return new List<MenuItemSpec>
        {
            new("Définir comme profil actif", () => SetActive(name), SymbolRegular.Checkmark24, Enabled: !active, Checked: active),
            MenuItemSpec.Separator,
            new("Renommer", async () => await RenameProfileAsync(name), SymbolRegular.Rename24, "F2"),
            new("Dupliquer", () => DuplicateProfile(name), SymbolRegular.CopyAdd24),
            new("Application associée…", async () => await SetAppMatchAsync(name), SymbolRegular.Window24),
            MenuItemSpec.Separator,
            new("Exporter…", () => ExportProfile(name), SymbolRegular.ArrowExportUp24),
            new("Importer…", () => ImportProfileCommand.Execute(null), SymbolRegular.ArrowImport24),
            new("Nouveau profil depuis un modèle", Icon: SymbolRegular.DocumentAdd24, Children: ProfileTemplates.All.Select(t => new MenuItemSpec(t.Name, () => NewProfileFromTemplate(t), Enum.TryParse(t.Icon, out SymbolRegular icon) ? icon : null)).ToList()),
            MenuItemSpec.Separator,
            new("Supprimer", async () => await DeleteProfileAsync(name), SymbolRegular.Delete24, Enabled: canDelete, Danger: true),
        };
    }

    /// <summary>Menu contextuel d'un onglet de page.</summary>
    public IReadOnlyList<MenuItemSpec> BuildPageMenu(PageTabViewModel page)
    {
        int count = Pages.Count;
        return new List<MenuItemSpec>
        {
            new("Renommer", async () => await RenamePageAsync(page), SymbolRegular.Rename24),
            new("Dupliquer", () => DuplicatePage(page), SymbolRegular.CopyAdd24),
            new(page.Hidden ? "Afficher dans les onglets" : "Masquer des onglets", () => TogglePageHidden(page), page.Hidden ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24),
            MenuItemSpec.Separator,
            new("Déplacer à gauche", () => MovePage(page, -1), SymbolRegular.ArrowLeft24, Enabled: page.Index > 0),
            new("Déplacer à droite", () => MovePage(page, 1), SymbolRegular.ArrowRight24, Enabled: page.Index < count - 1),
            MenuItemSpec.Separator,
            new("Supprimer la page", async () => await DeletePageAsync(page), SymbolRegular.Delete24, Enabled: count > 1, Danger: true),
        };
    }

    /// <summary>Menu contextuel d'une action du catalogue.</summary>
    public IReadOnlyList<MenuItemSpec> BuildCatalogMenu(CatalogItemViewModel item)
    {
        var a = item.Action;
        bool selectedFree = _selectedIndex >= 0 && _doc.Get(Place, _selectedIndex) == null;
        return new List<MenuItemSpec>
        {
            new(_selectedIndex >= 0 ? "Ajouter à la case sélectionnée" : "Ajouter à la première case libre", () => AddCatalogAction(a), SymbolRegular.Add24),
            new("Ajouter à la première case libre", () => AddCatalogAction(a, FirstFree()), SymbolRegular.AddSquare24, Enabled: _selectedIndex >= 0 && !selectedFree),
            new("Configurer", () => { AddCatalogAction(a); _shell.FocusProperties(); }, SymbolRegular.Settings24),
            MenuItemSpec.Separator,
            new(item.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", () => item.ToggleFavoriteCommand.Execute(null),
                item.IsFavorite ? SymbolRegular.StarOff24 : SymbolRegular.Star24),
            new("Afficher les informations", async () => await ShowInfoAsync(a), SymbolRegular.Info24),
        };
    }

    /// <summary>Décrit une action du catalogue (description, catégorie, étapes).</summary>
    public async System.Threading.Tasks.Task ShowInfoAsync(CatalogAction a)
    {
        var steps = a.IsFolder ? "Ouvre une grille de sous-boutons." : string.Join("\n", a.Actions.Select((s, i) =>
            $"{i + 1}. {s.Type}{(string.IsNullOrEmpty(s.Value) ? "" : " : " + s.Value)}"));
        var text = $"{a.Description}\n\nCatégorie : {a.Category}\n\nÉtapes :\n{steps}" + (a.Note != null ? $"\n\n{a.Note}" : "");
        await _shell.InfoAsync(a.Name, text);
    }

    // ---------- notifications (toast) ----------

    void OnNotice(Notice n)
    {
        _toast = n;
        ToastText = n.Message;
        ToastKind = n.Kind;
        ToastActionText = n.ActionText;
        ToastVisible = true;
        _toastTimer.Interval = TimeSpan.FromSeconds(n.Kind == NoticeKind.Error ? 10 : n.ActionText != null ? 8 : 4);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    void DismissToast()
    {
        _toastTimer.Stop();
        ToastVisible = false;
        _toast = null;
    }

    [RelayCommand]
    void ToastAction()
    {
        var a = _toast?.Action;
        DismissToast();
        a?.Invoke();
    }
}
