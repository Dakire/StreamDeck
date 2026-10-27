using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDak.Actions;
using StreamDak.Integrations;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>État du deck affiché en plein écran : profil, page, dossier, sous-page, mode édition tactile et menu.</summary>
public partial class MainViewModel : ObservableObject, INavigator
{
    /// <summary>Case de la vue courante : un bouton de la grille, un emplacement vide ou la flèche « retour » d'un dossier.</summary>
    readonly record struct ViewItem(DeckButton? Button, int ListIndex, bool IsBack);

    /// <summary>Délai avant d'afficher l'indicateur « en cours » : une action instantanée ne clignote pas.</summary>
    public static readonly TimeSpan RunningDelay = TimeSpan.FromMilliseconds(250);

    readonly IShell _shell;
    readonly DeckDocument _doc;
    readonly ActionEngine _engine;
    readonly AppOptions _options;
    readonly GridState _grid;
    readonly IAppWindows _windows;
    readonly ConfigPersistence _persistence;
    readonly IntegrationManager _integrations;
    readonly WidgetRegistry _widgets;
    readonly NotificationProviders _notifications;
    readonly UpdateService _updates;

    /// <summary>true si une version plus récente existe : un point d'accent discret sur le bouton de menu.</summary>
    public bool UpdateAvailable => _updates.HasUpdate;

    /// <summary>Info-bulle du bouton de menu (« Menu » ou « Menu · version 1.4.0 disponible »).</summary>
    public string MenuTooltip => _updates.HasUpdate ? $"Menu · version {_updates.NewVersionText} disponible" : "Menu";
    readonly HashSet<DataWidgetViewModel> _dataWidgets = new();
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    bool _integrationRefreshPending;
    static readonly TimeSpan StatusDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan StatusActionDelay = TimeSpan.FromSeconds(8);
    Action? _statusAction;
    readonly DispatcherTimer _statusTimer = new() { Interval = StatusDelay };
    readonly DispatcherTimer _appTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };   // horloges et minuteurs visibles
    string _lastMinute = "";
    readonly List<int> _folder = new();     // chemin de dossiers ouverts
    string? _autoProfile;                   // profil imposé par l'application au premier plan (non enregistré)
    string? _lastForeground;
    string? _lastActive;
    (int Monitor, string? Device, bool Fullscreen, bool OnTop) _lastPlacement;
    int _page, _previousPage;
    int _sub;        // sous-page courante quand une page dépasse la capacité de la grille
    int _capacity = 1;
    double _w, _h;            // zone de la grille (largeur de la fenêtre moins les panneaux d'édition)
    double _rawW, _rawH;      // taille de la zone de contenu de la fenêtre
    readonly Func<IEditorShell, EditorViewModel> _editorFactory;
    bool _profileMenu;

    /// <summary>Cases de la grille affichée : <see cref="DeckTileViewModel"/>, <see cref="AddTileViewModel"/> ou <see cref="SpacerTileViewModel"/>.</summary>
    public ObservableCollection<object> Tiles { get; } = new();
    /// <summary>Onglets des pages du profil affiché.</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    /// <summary>Entrées du menu : <see cref="MenuEntryViewModel"/> ou <see cref="MenuSeparatorViewModel"/>.</summary>
    public ObservableCollection<object> MenuEntries { get; } = new();

    [ObservableProperty] LayoutMetrics l = LayoutMetrics.Initial;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrandText))] bool isEdit;
    [ObservableProperty] bool menuOpen;
    /// <summary>Total des notifications non lues de toutes les pages web de tous les profils (pages exclues ou sans badge ignorées, « 99+ » à l'affichage).</summary>
    [ObservableProperty] int totalUnread;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] string? status;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatusAction))] string? statusActionText;
    [ObservableProperty] bool statusIsError = true;
    [ObservableProperty] Brush rootBackground = Brushes.Transparent;
    [ObservableProperty] bool isFullscreen;
    [ObservableProperty] bool hasSubs;
    [ObservableProperty] string subText = "";
    [ObservableProperty] bool inFolder;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrandText))] string pathText = "";

    /// <summary>true si un message est affiché dans la barre d'état.</summary>
    public bool HasStatus => Status != null;
    /// <summary>true si le message propose un bouton (« Annuler » après une création).</summary>
    public bool HasStatusAction => StatusActionText != null;
    /// <summary>Texte de la barre du haut : mode, profil et chemin des dossiers ouverts.</summary>
    public string BrandText => (IsEdit ? "StreamDak  ·  Mode édition" : "StreamDak")
        + (ProfileCount > 1 ? "  ·  " + CurrentProfile.Name : "") + (PathText.Length > 0 ? "  ›  " + PathText : "");

    // ---------- barre du haut et dock ----------

    /// <summary>Profil affiché (pastille de la barre du haut) ; « auto » s'il est imposé par l'application au premier plan.</summary>
    public string ProfileName => CurrentProfile.Name;
    /// <summary>true si le profil affiché vient de l'application au premier plan (non enregistré).</summary>
    public bool IsAutoProfile => _autoProfile != null;
    /// <summary>true s'il y a plusieurs profils (la pastille de profil est alors utile).</summary>
    public bool HasProfiles => ProfileCount > 1;
    /// <summary>true en mode --simulate (badge « Simulation »).</summary>
    public bool IsSimulated => _options.Simulate;
    /// <summary>Points des sous-pages (indicateur du dock).</summary>
    public ObservableCollection<PageDotViewModel> Dots { get; } = new();

    /// <summary>La vue a changé de page, de sous-page ou de dossier (+1 vers l'avant, -1 vers l'arrière) : la fenêtre anime l'entrée de la grille.</summary>
    public event Action<int>? Navigated;

    /// <summary>true si la page affichée (ou celle qu'on s'apprête à afficher) est une page web plein cadre : son cadre WebView2 ne doit pas être animé.</summary>
    public bool ShowsWebPage => CurrentPage.IsWeb || CurrentPage.Buttons.Any(b => b.Widget == WidgetTypes.Web);
    int _shownPage = -1, _shownSub, _shownDepth;
    string _shownProfile = "";
    /// <summary>Configuration courante (placement de la fenêtre).</summary>
    public DeckConfig Config => _doc.Config;
    int ProfileCount => _doc.Config.Profiles.Count;

    /// <summary>Deck sur le document partagé ; <paramref name="shell"/> fournit le placement Win32.</summary>
    public MainViewModel(IShell shell, DeckDocument doc, ActionEngine engine, AppOptions options, StartupResult startup,
        GridState grid, IAppWindows windows, ConfigPersistence persistence, IntegrationManager integrations,
        Func<IEditorShell, EditorViewModel> editorFactory, WidgetRegistry widgets, NotificationProviders notifications, UpdateService updates)
    {
        _updates = updates;
        updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UpdateService.HasUpdate) or nameof(UpdateService.NewVersionText))
            {
                OnPropertyChanged(nameof(UpdateAvailable));
                OnPropertyChanged(nameof(MenuTooltip));
            }
        };
        _notifications = notifications;
        _editorFactory = editorFactory;
        _widgets = widgets;
        Tiles.CollectionChanged += (_, _) => ReleaseDataWidgets();
        _integrations = integrations;
        _shell = shell;
        _doc = doc;
        _engine = engine;
        _options = options;
        _grid = grid;
        _windows = windows;
        _persistence = persistence;
        ApplyBackground();
        // Changement de thème (rare) : toutes les tuiles sont recréées, y compris la case « retour » dont la face est fixe
        Theme.Changed += () => { ApplyBackground(); TileFaceData.ClearCache(); Tiles.Clear(); _tileKeys.Clear(); Rebuild(); };
        _doc.Changed += OnDocumentChanged;
        Notifier.Posted += OnNotice;
        _lastActive = _doc.Config.ActiveProfile;
        _lastPlacement = (_doc.Config.Monitor, _doc.Config.MonitorDevice, _doc.Config.Fullscreen, _doc.Config.AlwaysOnTop);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); SetStatus(null); };
        _appTimer.Tick += (_, _) => PollForeground();
        _appTimer.Start();
        _liveTimer.Tick += (_, _) => RefreshLive();
        _engine.Context.Countdowns.Finished += OnCountdownFinished;
        _integrations.ValuesChanged += _ => QueueIntegrationRefresh();
        _integrations.StateChanged += _ => QueueIntegrationRefresh();
        UpdateTotalUnread();
        if (startup.Message != null) SetStatus(startup.Message, error: false);
    }

    /// <summary>Appelé par la fenêtre à chaque changement de taille de la zone de contenu.</summary>
    public void Resize(double width, double height)
    {
        _rawW = width; _rawH = height;
        ApplyPanelWidths();
        Rebuild();
    }

    // ---------- état partagé ----------

    DeckProfile CurrentProfile => _doc.FindProfile(_autoProfile ?? _doc.Config.ActiveProfile) ?? _doc.Config.Profiles[0];

    DeckPage CurrentPage
    {
        get
        {
            var pages = CurrentProfile.Pages;
            _page = Math.Clamp(_page, 0, pages.Count - 1);
            return pages[_page];
        }
    }

    internal Place CurrentPlace => new(CurrentProfile.Name, _page, _folder.ToArray());

    /// <summary>Grille affichée ; si le chemin de dossier n'existe plus (modification externe), retour à la racine de la page.</summary>
    List<DeckButton> CurrentList
    {
        get
        {
            _ = CurrentPage;
            var list = _doc.Container(CurrentPlace);
            if (list == null) { _folder.Clear(); list = _doc.Container(CurrentPlace)!; }
            return list;
        }
    }

    void OnDocumentChanged(DocumentChange change)
    {
        var cfg = _doc.Config;
        if (_autoProfile != null && _doc.FindProfile(_autoProfile) == null) _autoProfile = null;
        if (!string.Equals(_lastActive, cfg.ActiveProfile, StringComparison.Ordinal))
        {
            _lastActive = cfg.ActiveProfile;
            _autoProfile = null;          // un choix manuel l'emporte sur l'application au premier plan
            ResetNavigation();
        }
        if ((cfg.Monitor, cfg.MonitorDevice, cfg.Fullscreen, cfg.AlwaysOnTop) != _lastPlacement)
        {
            _lastPlacement = (cfg.Monitor, cfg.MonitorDevice, cfg.Fullscreen, cfg.AlwaysOnTop);
            _shell.ReapplyPlacement();
        }
        ApplyBackground();
        OnPropertyChanged(nameof(BrandText));
        UpdateTotalUnread();   // une page web ajoutée, retirée, masquée ou réglée, dans n'importe quel profil
        Rebuild(navigation: !change.InPlace);   // un bouton modifié sur place ne change ni les pages ni les onglets
    }

    void OnNotice(Notice n)
    {
        if (n.Kind is NoticeKind.Error or NoticeKind.Warning) SetStatus(n.Message);
        else SetStatus(n.Message, error: false, n.ActionText, n.Action);
    }

    void ApplyBackground()
    {
        // Couleur par défaut : fond nuancé du thème (lueur en haut) ; couleur personnalisée : fond uni
        var backdrop = Application.Current.Resources["DeckBackdrop"] as Brush ?? Brushes.Transparent;
        if (string.Equals(_doc.Config.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase)) { RootBackground = backdrop; return; }
        RootBackground = Theme.TryBrush(_doc.Config.Background, out var b) ? b : backdrop;   // couleur invalide : fond du thème
    }

    /// <summary>Message de la barre d'état. <paramref name="actionText"/> / <paramref name="action"/> : bouton touchable (« Annuler »), qui laisse plus de temps pour l'atteindre.</summary>
    void SetStatus(string? text, bool error = true, string? actionText = null, Action? action = null)
    {
        StatusIsError = error;
        _statusAction = text == null ? null : action;
        StatusActionText = _statusAction == null ? null : actionText;
        Status = text;
        _statusTimer.Stop();
        _statusTimer.Interval = _statusAction == null ? StatusDelay : StatusActionDelay;
        if (text != null) _statusTimer.Start();
    }

    [RelayCommand]
    void RunStatusAction()
    {
        var action = _statusAction;
        SetStatus(null);
        action?.Invoke();
    }

    void ResetNavigation()
    {
        _page = 0; _previousPage = 0; _sub = 0;
        _folder.Clear();
    }

    // ---------- profil lié à l'application au premier plan ----------

    void PollForeground()
    {
        var profiles = _doc.Config.Profiles;
        if (!profiles.Any(p => p.AppMatch != null))
        {
            if (_autoProfile != null) { _autoProfile = null; ResetNavigation(); Rebuild(); }
            return;
        }
        var fg = _engine.Context.Platform.Windows.ForegroundProcessName();
        if (fg == null || string.Equals(fg, _lastForeground, StringComparison.OrdinalIgnoreCase)) return;
        _lastForeground = fg;

        var match = profiles.FirstOrDefault(p => string.Equals(p.AppMatch, fg, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            if (!match.Name.Equals(CurrentProfile.Name, StringComparison.Ordinal))
            {
                _autoProfile = match.Name;
                ResetNavigation();
                Rebuild();
            }
        }
        else if (_autoProfile != null)
        {
            _autoProfile = null;      // l'application a perdu le premier plan : retour au profil choisi
            ResetNavigation();
            Rebuild();
        }
    }

    // ---------- navigation ----------

    void INavigator.GoToPage(string name) => NavigateTo(name);
    void INavigator.SwitchProfile(string name) => SwitchProfile(name);

    void NavigateTo(string name)
    {
        var pages = CurrentProfile.Pages;
        int idx = name.Equals(PageTargets.Home, StringComparison.OrdinalIgnoreCase) ? 0
            : name.Equals(PageTargets.Back, StringComparison.OrdinalIgnoreCase) ? Math.Min(_previousPage, pages.Count - 1)
            : pages.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) { SetStatus($"Page introuvable : {name}"); return; }
        GoTo(idx);
    }

    void GoTo(int idx)
    {
        if (idx == _page && _folder.Count == 0) return;
        if (idx != _page) _previousPage = _page;
        _page = idx;
        _sub = 0;
        _folder.Clear();
        Rebuild();
    }

    void SwitchProfile(string name)
    {
        var p = _doc.FindProfile(name);
        if (p == null) { SetStatus($"Profil introuvable : {name}"); return; }
        _autoProfile = null;
        if (!_doc.SetActiveProfile(p.Name)) { ResetNavigation(); Rebuild(); }   // déjà actif : retour à l'accueil du profil
    }

    void EnterFolder(int listIndex)
    {
        _folder.Add(listIndex);
        _sub = 0;
        Rebuild();
    }

    void FolderUp()
    {
        if (_folder.Count == 0) return;
        _folder.RemoveAt(_folder.Count - 1);
        _sub = 0;
        Rebuild();
    }

    // ---------- état d'affichage ----------

    List<ViewItem> BuildView()
    {
        var items = new List<ViewItem>();
        if (_folder.Count > 0) items.Add(new ViewItem(null, -1, true));
        var list = CurrentList;
        for (int i = 0; i < list.Count; i++)
            items.Add(new ViewItem(list[i].Blank ? null : list[i], i, false));
        return items;
    }

    int SubPages(int itemCount) =>
        Math.Max(1, (int)Math.Ceiling((itemCount + (IsEdit ? 1 : 0)) / (double)_capacity));

    void Rebuild(bool navigation = true)
    {
        if (_w < 50 || _h < 50 || _doc.Config.Profiles.Count == 0) return;

        var profile = CurrentProfile;
        // Mode édition : les panneaux prennent de la largeur, mais la grille garde le nombre de cases qu'elle a en usage normal
        var (cols, rows) = LayoutMetrics.GridSize(_rawW, _rawH, _doc.Config, profile.Columns, profile.Rows);
        var m = LayoutMetrics.Compute(_w, _h, _doc.Config, cols, rows);
        _capacity = m.Columns * m.Rows;
        _grid.Auto = LayoutMetrics.GridSize(_rawW, _rawH, _doc.Config);
        L = m;

        var view = BuildView();
        int subs = _folder.Count == 0 && CurrentPage.IsWeb ? 1 : SubPages(view.Count);   // une page web n'a ni sous-pages ni grille
        _sub = Math.Clamp(_sub, 0, subs - 1);
        HasSubs = subs > 1;
        SubText = $"{_sub + 1} / {subs}";
        if (Dots.Count != subs || Dots.Where((d, i) => d.IsActive != (i == _sub)).Any())
        {
            Dots.Clear();
            for (int i = 0; i < subs; i++)
            {
                int target = i;
                Dots.Add(new PageDotViewModel { IsActive = i == _sub, Text = $"Sous-page {i + 1} sur {subs}", Command = new RelayCommand(() => { _sub = target; Rebuild(); }) });
            }
        }
        // Transition : seulement quand l'emplacement affiché change, jamais pour une simple mise à jour de contenu
        var where = (Profile: profile.Name, Page: _page, Sub: _sub, Depth: _folder.Count);
        if (_shownPage >= 0 && (where.Profile != _shownProfile || where.Page != _shownPage || where.Sub != _shownSub || where.Depth != _shownDepth))
        {
            int dir = where.Profile != _shownProfile ? 1
                : where.Page != _shownPage ? Math.Sign(where.Page - _shownPage)
                : where.Depth != _shownDepth ? Math.Sign(where.Depth - _shownDepth)
                : Math.Sign(where.Sub - _shownSub);
            Navigated?.Invoke(dir == 0 ? 1 : dir);
        }
        (_shownProfile, _shownPage, _shownSub, _shownDepth) = where;
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(IsAutoProfile));
        OnPropertyChanged(nameof(HasProfiles));
        InFolder = _folder.Count > 0;
        PathText = BuildPath();

        if (Editor != null && !Editor.Place.Equals(CurrentPlace)) Editor.GoTo(CurrentPlace, -1);   // autre page ou dossier : plus de sélection
        BuildTiles(view);
        if (navigation) BuildTabs();
    }

    string BuildPath()
    {
        if (_folder.Count == 0) return "";
        var names = new List<string>();
        var place = new Place(CurrentProfile.Name, _page);
        foreach (var i in _folder)
        {
            place = place.Enter(i);
            names.Add(_doc.FolderAt(place)?.Label ?? "?");
        }
        return string.Join(" › ", names);
    }

    /// <summary>
    /// Identité d'une case : tant qu'elle ne change pas, la tuile existante est gardée et seule sa face est recalculée.
    /// Le bouton est comparé par référence (le document modifie les boutons en place) ; le titre et le dossier font partie
    /// de la clé car ils fixent le nom de la tuile et sa commande.
    /// </summary>
    readonly record struct TileKey(string Kind, Place Place, int Index, DeckButton? Button, string? Label, bool IsFolder, bool IsEdit,
        int Slot = -1, int Cols = 1, int Rows = 1, string? Options = null);

    readonly List<TileKey> _tileKeys = new();

    /// <summary>Emprise d'une page web : « toute la grille » (la grille ramène l'emprise à ses colonnes et à ses lignes).</summary>
    const int WholeGrid = 64;

    /// <summary>
    /// Page web : une seule tuile, le widget web, sur toute la grille (100 % de la largeur et de la hauteur de l'espace central).
    /// Tant que la page et son adresse ne changent pas, la tuile (donc le cadre WebView2 et la page chargée) est gardée.
    /// </summary>
    void BuildWebPageTile(DeckPage page)
    {
        var key = new TileKey("webpage", CurrentPlace, -1, null, page.Name, false, IsEdit, Options: page.Url);
        if (Tiles.Count != 1 || _tileKeys.Count != 1 || _tileKeys[0] != key)
        {
            Tiles.Clear();
            _tileKeys.Clear();
            Tiles.Add(CreateWebPageTile(page));
            _tileKeys.Add(key);
        }
        if (_typing && !Tiles.OfType<WebWidgetViewModel>().Any(w => w.IsTyping)) StopTyping();
        UpdateLiveTimer();
        ApplySelection();
    }

    WebWidgetViewModel CreateWebPageTile(DeckPage page)
    {
        // Bouton de passage, jamais écrit dans le document : il donne au widget web sa couleur, son adresse et son titre
        var button = new DeckButton { Label = page.Name, Widget = WidgetTypes.Web, Color = "#0E7490" };
        button.SetOption(WidgetOptionKeys.Url, page.Url);
        WidgetTypes.IsWebAddress(page.Url, out var uri);
        var web = new WebWidgetViewModel
        {
            Button = button, Edit = new RelayCommand(OpenPageMenu), AccessibleName = $"Page web {page.Name}", Source = uri,
            FrameKey = uri == null ? null : $"page|{CurrentProfile.Name}|{page.Name}|{uri.AbsoluteUri}",
            Suspended = MenuOpen || HasDialog,
            MissingAddressMessage = "Cette page web n'a pas d'adresse valide : mode édition › Page › Adresse de la page web",
            Cell = 0, ColSpan = WholeGrid, RowSpan = WholeGrid,
            ZoomPercent = page.Zoom == 0 ? WebZoom.Normal : WebZoom.Clamp(page.Zoom),
        };
        string profileName = CurrentProfile.Name;
        int pageIndex = _page;
        web.ZoomChanged = z => _doc.UpdateSettings(c =>
        {
            if (ConfigNormalizer.FindProfile(c, profileName) is { } p && pageIndex < p.Pages.Count && p.Pages[pageIndex].IsWeb)
                p.Pages[pageIndex].Zoom = WebZoom.ForStorage(z);
        }, "Zoom de la page web");
        web.Face = TileFaceData.From(button);
        web.TypingRequested = on => SetTyping(on ? web : null, on);
        web.BrowserRequested = OpenInBrowser;
        return web;
    }

    void BuildTiles(List<ViewItem> view)
    {
        if (_folder.Count == 0 && CurrentPage is { IsWeb: true } webPage) { BuildWebPageTile(webPage); return; }
        // Réconciliation tuile par tuile plutôt que Clear + Add : une modification de document (titre tapé dans l'éditeur,
        // réglage) ne régénère plus les 32 conteneurs de la grille, seulement les cases qui ont réellement changé.
        var place = CurrentPlace;
        int offset = _folder.Count > 0 ? 1 : 0;
        int first = _sub * _capacity;
        int cols = Math.Max(1, L.Columns), rows = Math.Max(1, L.Rows);
        bool empty = !view.Any(v => v.Button != null);   // aucune touche : la première case invite à ajouter un bouton
        bool Occupied(int cell) => first + cell < view.Count && (view[first + cell].IsBack || view[first + cell].Button != null);
        var covered = new HashSet<int>();                 // cases recouvertes par un widget : pas de tuile
        int at = 0;                                       // position dans Tiles (une tuile par case non recouverte)
        for (int slot = 0; slot < _capacity; slot++)
        {
            if (covered.Contains(slot)) continue;
            int i = first + slot;
            TileKey key;
            Func<object> create;
            int spanCols = 1, spanRows = 1;
            if (i >= view.Count)
            {
                // Au-delà du contenu : en édition, chaque case libre propose d'ajouter un bouton
                (key, create) = FreeTile(place, i - offset, i == offset && empty);
            }
            else if (view[i].IsBack)
            {
                key = new TileKey("back", place, -1, null, null, false, false);
                create = () => new DeckTileViewModel
                {
                    Face = new TileFaceData
                    {
                        Label = "Retour", Symbol = SymbolRegular.ArrowLeft24, HasSymbol = true,
                        Foreground = Brushes.White, Background = Theme.B(Theme.SurfaceHi),
                    },
                    Label = "Retour", AccessibleName = "Retour au dossier parent", IsEdit = false,
                    Click = new RelayCommand(FolderUp),
                };
            }
            else if (view[i].Button == null)
            {
                (key, create) = FreeTile(place, view[i].ListIndex, i == offset && empty);
            }
            else
            {
                var def = view[i].Button!;
                int idx = view[i].ListIndex;
                bool edit = IsEdit;
                bool widget = def.Widget != null;
                string? signature = null;
                if (widget)
                {
                    var (wantCols, wantRows) = def.Span();
                    (spanCols, spanRows) = TileSpans.Effective(slot, wantCols, wantRows, cols, rows, Occupied);
                    foreach (var cell in TileSpans.Covered(slot, spanCols, spanRows, cols)) covered.Add(cell);
                    signature = WidgetSignature(def);
                }
                // Hors édition, un widget est une tuile d'affichage ; en édition, c'est une touche comme les autres (sélectionnable)
                key = new TileKey(widget && !edit ? "widget" : "button", place, idx, def, def.Label, def.IsFolder, edit, slot, spanCols, spanRows, signature);
                create = widget && !edit
                    ? () => CreateWidgetTile(def, place, idx)
                    : () =>
                    {
                        DeckTileViewModel? tile = null;
                        var menu = new RelayCommand(() => OpenTileMenu(place, idx));                              // clic droit, appui long
                        tile = new DeckTileViewModel
                        {
                            Button = def, Label = def.Label, IsEdit = edit, Edit = menu,
                            Click = edit
                                ? new RelayCommand(() => SelectTile(place, idx))                              // mode édition : sélectionne la touche
                                : def.IsFolder
                                    ? new RelayCommand(() => EnterFolder(idx))
                                    : new AsyncRelayCommand(() => RunTileAsync(tile!), AsyncRelayCommandOptions.AllowConcurrentExecutions),
                        };
                        return tile;
                    };
            }

            if (at < Tiles.Count && _tileKeys[at] == key)
            {
                if (Tiles[at] is DeckTileViewModel kept && key.Kind == "button") UpdateFace(kept);   // couleur, icône, thème…
                else if (Tiles[at] is WidgetTileViewModel keptWidget) keptWidget.Face = TileFaceData.From(keptWidget.Button);
                at++;
                continue;
            }
            var item = create();
            if (item is DeckTileViewModel created && key.Kind == "button") UpdateFace(created);
            if (item is PlacedTile placed) { placed.Cell = slot; placed.ColSpan = spanCols; placed.RowSpan = spanRows; }
            if (at < Tiles.Count) { Tiles[at] = item; _tileKeys[at] = key; }
            else { Tiles.Add(item); _tileKeys.Add(key); }
            at++;
        }
        while (Tiles.Count > at)
        {
            Tiles.RemoveAt(Tiles.Count - 1);
            _tileKeys.RemoveAt(_tileKeys.Count - 1);
        }
        if (_typing && !Tiles.OfType<WebWidgetViewModel>().Any(w => w.IsTyping)) StopTyping();   // la tuile en saisie a disparu (page changée)
        UpdateLiveTimer();
        ApplySelection();
        ShowDiscoveryHintOnce(view.Count(v => v.Button != null));
    }

    /// <summary>Empreinte des réglages d'un widget : une tuile n'est gardée que si rien de ce qui la décrit n'a changé.</summary>
    static string WidgetSignature(DeckButton def) =>
        def.Widget + "|" + def.Color + "|" + string.Join(";", (def.WidgetOptions ?? new()).Where(o => o.Key != WidgetOptionKeys.Zoom)   // le zoom se règle sur la tuile : la garder
            .OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value));

    /// <summary>Tuile d'affichage d'un widget (heure, agenda ou page web).</summary>
    WidgetTileViewModel CreateWidgetTile(DeckButton def, Place place, int index)
    {
        var edit = new RelayCommand(() => OpenTileMenu(place, index));            // clic droit, appui long
        string name = WidgetTypes.Find(def.Widget)?.Name ?? "Widget";
        WidgetTileViewModel tile = def.Widget switch
        {
            _ when _widgets.Find(def.Widget) is { } data => CreateDataWidget(data, def, edit, name),
            WidgetTypes.Agenda => new AgendaWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
            WidgetTypes.Web => CreateWebWidget(def, edit, name, place, index),
            WidgetTypes.Cpu or WidgetTypes.Ram => new SystemMeterWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
            _ => new ClockWidgetViewModel { Button = def, Edit = edit, AccessibleName = name },
        };
        tile.Face = TileFaceData.From(def);
        if (tile is ClockWidgetViewModel clock) clock.Update(DateTime.Now);
        else if (tile is AgendaWidgetViewModel agenda) RefreshAgenda(agenda);
        else if (tile is SystemMeterWidgetViewModel meter) RefreshSystem(meter);
        return tile;
    }

    /// <summary>Tuile d'un widget de données : ses relevés démarrent ici, et s'arrêtent quand la tuile quitte la grille (<see cref="ReleaseDataWidgets"/>).</summary>
    DataWidgetViewModel CreateDataWidget(IWidget widget, DeckButton def, System.Windows.Input.ICommand edit, string name)
    {
        var options = new Dictionary<string, string>(def.WidgetOptions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var context = new WidgetContext { Options = options, Http = _engine.Context.Http, GetValue = _integrations.GetValue };
        var tile = new DataWidgetViewModel(widget, context, WidgetRegistry.IntervalFor(widget.Info, options), a => _ui.BeginInvoke(a))
        {
            Button = def, Edit = edit, AccessibleName = name,
        };
        _dataWidgets.Add(tile);
        tile.SetPaused(!_active);
        tile.Start();
        return tile;
    }

    /// <summary>Fenêtre fermée : tous les relevés de widgets s'arrêtent.</summary>
    public void StopWidgets()
    {
        foreach (var data in _dataWidgets) data.Dispose();
        _dataWidgets.Clear();
    }

    /// <summary>Arrête les relevés des widgets de données dont la tuile n'est plus dans la grille (page changée, widget supprimé, thème changé).</summary>
    void ReleaseDataWidgets()
    {
        if (_dataWidgets.Count == 0) return;
        var shown = Tiles.OfType<DataWidgetViewModel>().ToHashSet();
        foreach (var gone in _dataWidgets.Where(w => !shown.Contains(w)).ToList())
        {
            gone.Dispose();
            _dataWidgets.Remove(gone);
        }
    }

    WebWidgetViewModel CreateWebWidget(DeckButton def, System.Windows.Input.ICommand edit, string name, Place place, int index)
    {
        WidgetTypes.IsWebAddress(def.Option(WidgetOptionKeys.Url), out var uri);   // adresse absente ou refusée : uri reste null, la tuile le dit
        var web = new WebWidgetViewModel
        {
            Button = def, Edit = edit, AccessibleName = name, Source = uri, Suspended = MenuOpen || HasDialog,
            FrameKey = uri == null ? null : $"widget|{place.Profile}|{place.Page}|{string.Join('.', place.Path)}|{index}|{uri.AbsoluteUri}",
            ZoomPercent = def.OptionInt(WidgetOptionKeys.Zoom, WebZoom.Normal, WebZoom.Min, WebZoom.Max),
        };
        web.ZoomChanged = z => _doc.UpdateSettings(c =>
        {
            int stored = WebZoom.ForStorage(z);
            if (_doc.Get(place, index) is { } button) button.SetOption(WidgetOptionKeys.Zoom, stored > 0 ? stored.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }, "Zoom du widget web");
        web.TypingRequested = on => SetTyping(on ? web : null, on);
        web.BrowserRequested = OpenInBrowser;
        return web;
    }

    /// <summary>Ouvre une adresse web dans le navigateur par défaut (issue de secours d'un site qui refuse le cadre intégré).</summary>
    void OpenInBrowser(Uri uri)
    {
        try { _engine.Context.Platform.Launcher.Open(uri.AbsoluteUri, null); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            Log.Warn("Ouverture dans le navigateur impossible : " + ex.Message);
            SetStatus("Impossible d'ouvrir le navigateur");
        }
    }

    // ---------- widget web : saisie au clavier et masquage sous les feuilles ----------

    bool _typing;

    /// <summary>
    /// Saisie au clavier dans un widget web : la fenêtre devient activable comme en mode édition (le clavier va à la page), puis redevient
    /// sans focus et rend le focus à l'application précédente.
    /// </summary>
    void SetTyping(WebWidgetViewModel? tile, bool on)
    {
        if (on && IsEdit) return;   // en édition la fenêtre est déjà activable
        foreach (var w in Tiles.OfType<WebWidgetViewModel>()) w.IsTyping = on && ReferenceEquals(w, tile);
        if (_typing == on) return;
        _typing = on;
        _shell.SetEditing(on);
    }

    void StopTyping()
    {
        if (_typing) SetTyping(null, false);
    }

    /// <summary>Le cadre d'une page web est une fenêtre Win32 : un menu ou une boîte de dialogue ouvert le masque (la page garde son état).</summary>
    void UpdateWebSuspension()
    {
        bool suspended = MenuOpen || HasDialog;
        foreach (var w in Tiles.OfType<WebWidgetViewModel>()) w.Suspended = suspended;
    }

    /// <summary>Contenu d'un widget agenda : les prochains événements de l'intégration Google, ou ce qu'il manque pour en afficher.</summary>
    void RefreshAgenda(AgendaWidgetViewModel agenda)
    {
        var none = Array.Empty<AgendaEntry>();
        var google = _integrations.Find(GoogleId);
        if (google == null || !_integrations.IsEnabled(GoogleId)) { agenda.Show(none, "Active Google Workspace dans Paramètres › Intégrations pour voir ton agenda"); return; }
        if (google.State != ConnectionState.Connected)
        {
            agenda.Show(none, google.State == ConnectionState.Error && google.Error != null ? google.Error : "Connexion à Google en cours…");
            return;
        }
        if (_integrations.GetValue($"{GoogleId}.{CalendarFeed.ErrorKey}") is { Length: > 0 } error) { agenda.Show(none, error); return; }
        var lines = AgendaFormatter.Upcoming(CalendarFeed.Parse(_integrations.GetValue($"{GoogleId}.{CalendarFeed.EventsKey}")), DateTime.Now,
            agenda.Button.OptionInt(WidgetOptionKeys.Days, 7, 1, 60), agenda.Button.OptionInt(WidgetOptionKeys.Count, 4, 1, 20));
        if (lines.Count == 0) agenda.Show(none, "Aucun événement à venir");
        else agenda.Show(lines.Select(l => new AgendaEntry(l.When, l.Title, l.Day, l.IsNow)).ToList(), "");
    }

    const string GoogleId = "google";

    bool _systemEnableRequested;

    /// <summary>
    /// Contenu d'un widget Processeur ou Mémoire : le dernier relevé de l'intégration « Système Windows ». Cette intégration n'a ni compte ni
    /// réglage : si elle est éteinte, le premier widget affiché l'active (une fois, sans toucher aux boutons).
    /// </summary>
    void RefreshSystem(SystemMeterWidgetViewModel meter)
    {
        var sys = _integrations.Find(SystemMeter.IntegrationId);
        if (sys == null) { meter.ShowStatus("Le connecteur Système Windows est indisponible"); return; }
        if (!_integrations.IsEnabled(SystemMeter.IntegrationId))
        {
            meter.ShowStatus("Activation du connecteur Système Windows…");
            EnableSystemIntegration();
            return;
        }
        if (sys.State != ConnectionState.Connected)
        {
            meter.ShowStatus(sys.State == ConnectionState.Error && sys.Error != null ? sys.Error : "Première mesure en cours…");
            return;
        }
        string? Value(string key) => _integrations.GetValue($"{SystemMeter.IntegrationId}.{key}");
        var reading = meter.IsRam ? SystemMeter.Ram(Value("ram.used"), Value("ram.total")) : SystemMeter.Cpu(Value("cpu"));
        if (reading == null) meter.ShowStatus("Première mesure en cours…");
        else meter.Show(reading);
    }

    /// <summary>Active l'intégration « Système Windows » (réglage, hors historique) ; hors de la reconstruction de la grille en cours.</summary>
    void EnableSystemIntegration()
    {
        if (_systemEnableRequested) return;
        _systemEnableRequested = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_integrations.IsEnabled(SystemMeter.IntegrationId))
                _doc.UpdateSettings(c => (c.Integrations ??= new())[SystemMeter.IntegrationId] = new IntegrationConfig { Enabled = true }, "Système Windows");
            _systemEnableRequested = false;
        });
    }

    // ---------- exécution et retour visuel ----------

    /// <summary>Exécute le bouton et affiche son état sur la tuile : en cours (si l'action dure), réussi ou en erreur.</summary>
    public async Task RunTileAsync(DeckTileViewModel tile)
    {
        var def = tile.Button!;
        var run = _engine.RunAsync(def, this);
        if (await Task.WhenAny(run, Task.Delay(RunningDelay)) != run) tile.RunState = TileRunState.Running;
        var result = await run;
        UpdateFace(tile);          // un minuteur vient peut-être de démarrer ou de s'arrêter
        UpdateLiveTimer();
        if (result.Outcome == ActionOutcome.Cancelled) { tile.RunState = TileRunState.Idle; return; }

        var state = result.Ok ? TileRunState.Success : TileRunState.Error;
        tile.RunState = state;
        if (!result.Ok)
        {
            var step = def.Actions.Count > 1 ? $" (étape {result.Step + 1})" : "";
            // Récupérable : « Modifier » ouvre le bouton pour corriger ses réglages (programme, adresse, raccourci…)
            Action? fix = TryGetSlot(tile, out var place, out var index) ? () => StartEditing(place, index) : null;
            SetStatus($"{def.Label}{step} : {result.Message}", actionText: fix == null ? null : "Modifier", action: fix);
            Log.Warn($"Bouton « {def.Label} »{step} : {result.Outcome} — {result.Message}");
        }
        else if (result.Message != null) SetStatus(result.Message, error: false);

        await Task.Delay(result.Ok ? 600 : 1800);
        if (tile.RunState == state) tile.RunState = TileRunState.Idle;
    }

    const string NotifyId = "notify";

    /// <summary>Service de notifications dont une étape « url » du bouton porte l'adresse, ou null.</summary>
    INotificationProvider? NotificationSource(DeckButton def) =>
        def.IsFolder ? null : def.Actions.Where(a => string.Equals(a.Type, ActionTypes.Url, StringComparison.OrdinalIgnoreCase))
            .Select(a => _notifications.ForUrl(a.Value)).FirstOrDefault(p => p != null);

    /// <summary>
    /// Texte du badge rouge d'une adresse suivie (page web du deck), ou null : connecteur éteint, service inconnu, rien de non lu, ou badge masqué pour la page
    /// (<paramref name="enabled"/> : <see cref="DeckPage.BadgeEnabled"/>).
    /// </summary>
    string? NotificationCountFor(string? url, bool enabled = true)
    {
        if (!enabled || _notifications.ForUrl(url) is not { } provider || !_integrations.IsEnabled(NotifyId)) return null;
        return BadgeText(provider);
    }

    /// <summary>
    /// Recalcule <see cref="TotalUnread"/> sur toutes les pages web de tous les profils. Un service suivi par plusieurs pages n'est compté qu'une fois
    /// (même compteur), par la première page non exclue ; une simple activité (point) ne compte pas. À appeler depuis le thread d'interface.
    /// </summary>
    void UpdateTotalUnread()
    {
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(int? Count, bool Excluded)>();
        bool notify = _integrations.IsEnabled(NotifyId);
        foreach (var profile in _doc.Config.Profiles)
            foreach (var page in profile.Pages)
            {
                if (!page.IsWeb) continue;
                var provider = notify ? _notifications.ForUrl(page.Url) : null;
                bool excluded = page.BadgeExcludeFromTotal || !page.BadgeEnabled || provider == null;
                int? count = null;
                if (provider != null && int.TryParse(_integrations.GetValue($"{NotifyId}.{provider.Id}"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) count = n;
                if (!excluded && count is > 0 && !counted.Add(provider!.Id)) excluded = true;   // déjà compté pour ce service
                items.Add((count, excluded));
            }
        TotalUnread = NotificationBadge.Total(items);
    }

    /// <summary>Nombre publié par le connecteur pour ce service, ou le point d'activité à défaut de nombre ; null si rien à afficher.</summary>
    string? BadgeText(INotificationProvider provider)
    {
        int? n = int.TryParse(_integrations.GetValue($"{NotifyId}.{provider.Id}"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : null;
        bool activity = _integrations.GetValue($"{NotifyId}.{StreamDak.Integrations.Notifications.NotificationIntegration.ActivityKey(provider.Id)}") == "1";
        return NotificationBadge.Text(n, activity);
    }

    /// <summary>Texte du badge rouge d'un bouton « site web » (« 3 », « 99+ »), ou null : connecteur éteint, service inconnu, rien de non lu.</summary>
    string? NotificationCount(DeckButton def)
    {
        if (NotificationSource(def) is not { } provider || !_integrations.IsEnabled(NotifyId)) return null;
        return BadgeText(provider);
    }

    void UpdateFace(DeckTileViewModel tile)
    {
        var def = tile.Button;
        if (def == null) return;
        var countdowns = _engine.Context.Countdowns;
        var left = countdowns.Remaining(def);
        var state = def.IsFolder ? null : def.Actions.Select(_integrations.StateOf).FirstOrDefault(s => s != null);
        var badge = NotificationCount(def);
        tile.Face = TileFaceData.From(def, left is { } t ? Countdowns.Format(t) : null, countdowns.Progress(def), _integrations.GetValue, state, badge);
        tile.AccessibleName = def.IsFolder ? $"Dossier {tile.Face.Label}"
            : state is { } on ? $"{tile.Face.Label}, {(on ? "actif" : "inactif")}" : tile.Face.Label;
        if (badge != null) tile.AccessibleName += $", {NotificationBadge.Describe(badge)}";
    }

    /// <summary>Les intégrations notifient depuis leurs threads : on regroupe les changements et on redessine sur le thread d'interface.</summary>
    void QueueIntegrationRefresh()
    {
        lock (_ui)
        {
            if (_integrationRefreshPending) return;
            _integrationRefreshPending = true;
        }
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            lock (_ui) _integrationRefreshPending = false;
            foreach (var t in Tiles.OfType<DeckTileViewModel>())
                if (t.Button is { IsFolder: false } b && (LabelTemplate.ValueKeys(b.Label).Count > 0 || b.Actions.Any(a => _integrations.Find(a.Type) != null) || NotificationSource(b) != null))
                    UpdateFace(t);
            foreach (var agenda in Tiles.OfType<AgendaWidgetViewModel>()) RefreshAgenda(agenda);
            foreach (var meter in Tiles.OfType<SystemMeterWidgetViewModel>()) RefreshSystem(meter);
            if (CurrentProfile.Pages.Any(p => p.IsWeb)) BuildTabs();   // badges des pages web
            UpdateTotalUnread();
        });
    }

    bool IsLive(DeckTileViewModel t) =>
        t.Button is { } b && (LabelTemplate.HasClockTokens(b.Label) || _engine.Context.Countdowns.Remaining(b) != null);

    /// <summary>L'horloge ne tourne que si une tuile visible l'utilise (ou qu'un minuteur est en cours).</summary>
    void UpdateLiveTimer()
    {
        bool needed = _active && (_engine.Context.Countdowns.Any || Tiles.OfType<DeckTileViewModel>().Any(IsLive) || Tiles.OfType<ClockWidgetViewModel>().Any() || Tiles.OfType<AgendaWidgetViewModel>().Any());
        if (needed && !_liveTimer.IsEnabled) _liveTimer.Start();
        else if (!needed) _liveTimer.Stop();
    }

    /// <summary>Met à jour uniquement les tuiles dynamiques ; une horloge sans secondes n'est redessinée qu'au changement de minute.</summary>
    void RefreshLive()
    {
        _engine.Context.Countdowns.Tick();
        var now = DateTime.Now;
        foreach (var clock in Tiles.OfType<ClockWidgetViewModel>()) clock.Update(now);
        var minute = DateTime.Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        bool newMinute = minute != _lastMinute;
        _lastMinute = minute;
        if (newMinute) foreach (var agenda in Tiles.OfType<AgendaWidgetViewModel>()) RefreshAgenda(agenda);   // un événement commence ou se termine
        foreach (var t in Tiles.OfType<DeckTileViewModel>())
        {
            if (t.Button is not { } b) continue;
            if (_engine.Context.Countdowns.Remaining(b) != null || LabelTemplate.NeedsSeconds(b.Label)
                || (newMinute && LabelTemplate.HasClockTokens(b.Label)))
                UpdateFace(t);
        }
        UpdateLiveTimer();
    }

    void OnCountdownFinished(DeckButton button)
    {
        SetStatus($"Minuteur terminé : {button.Label.Replace("{timer}", "").Trim()}", error: false);
        System.Media.SystemSounds.Asterisk.Play();
        foreach (var t in Tiles.OfType<DeckTileViewModel>().Where(t => ReferenceEquals(t.Button, button)))
        {
            UpdateFace(t);
            _ = FlashAsync(t);
        }
    }

    static async Task FlashAsync(DeckTileViewModel tile)
    {
        tile.RunState = TileRunState.Success;
        await Task.Delay(1500);
        if (tile.RunState == TileRunState.Success) tile.RunState = TileRunState.Idle;
    }

    void BuildTabs()
    {
        Tabs.Clear();
        var pages = CurrentProfile.Pages;
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            if (!IsEdit && p.Hidden) continue;
            int target = i;
            Tabs.Add(new TabViewModel
            {
                Text = p.Hidden ? p.Name + " (masquée)" : p.Name, IsActive = i == _page, Badge = p.IsWeb ? NotificationCountFor(p.Url, p.BadgeEnabled) : null,
                Command = new RelayCommand(() => GoTo(target)),
            });
        }
    }

    // ---------- menu ----------

    void BuildMenu()
    {
        if (_specTrail.Count > 0) { RenderSpecMenu(); return; }
        MenuEntries.Clear();
        void Add(SymbolRegular symbol, string text, Action act, bool danger = false, string? trailing = null, bool keepOpen = false) =>
            MenuEntries.Add(new MenuEntryViewModel
            {
                Symbol = symbol, Text = text, IsDanger = danger, Trailing = trailing,
                Command = new RelayCommand(() => { if (!keepOpen) MenuOpen = false; act(); Rebuild(); }),
            });

        if (_profileMenu)
        {
            Add(SymbolRegular.ArrowLeft24, "Retour", () => { _profileMenu = false; BuildMenu(); }, keepOpen: true);
            MenuEntries.Add(new MenuSeparatorViewModel());
            foreach (var p in _doc.Config.Profiles)
            {
                var name = p.Name;
                Add(SymbolRegular.Person24, name, () => SwitchProfile(name), trailing: name.Equals(CurrentProfile.Name, StringComparison.Ordinal) ? "✓" : null);
            }
            return;
        }

        Add(SymbolRegular.Edit24, IsEdit ? "Terminer la modification" : "Modifier le deck", () => IsEdit = !IsEdit);
        if (ProfileCount > 1)
            Add(SymbolRegular.PersonSwap24, "Changer de profil", () => { _profileMenu = true; BuildMenu(); }, trailing: CurrentProfile.Name, keepOpen: true);
        if (_updates.HasUpdate) Add(SymbolRegular.ArrowDownload24, $"Version {_updates.NewVersionText} disponible", _windows.OpenSettings);
        Add(SymbolRegular.Settings24, "Paramètres", _windows.OpenSettings);
        Add(SymbolRegular.DarkTheme24, "Thème", CycleTheme, trailing: _doc.Config.Theme.ToLowerInvariant() switch { "dark" => "Sombre", "amoled" => "AMOLED", "light" => "Clair", _ => "Auto" });
        Add(SymbolRegular.Desktop24, "Changer d'écran", _shell.NextScreen);
        Add(_shell.IsFullscreen ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24, _shell.IsFullscreen ? "Passer en fenêtre" : "Passer en plein écran", _shell.ToggleFullscreen);
        Add(SymbolRegular.Pin24, "Toujours au premier plan", () => _doc.UpdateSettings(c => c.AlwaysOnTop = !c.AlwaysOnTop, "Premier plan"), trailing: _shell.IsOnTop ? "✓" : null);
        Add(SymbolRegular.ArrowSync24, "Recharger la configuration", _persistence.ReloadFromDisk);
        MenuEntries.Add(new MenuSeparatorViewModel());
        Add(SymbolRegular.Power24, "Quitter StreamDak", () => Application.Current.Shutdown(), danger: true);
    }

    void CycleTheme()
    {
        var next = _doc.Config.Theme.ToLowerInvariant() switch { "system" => "dark", "dark" => "amoled", "amoled" => "light", _ => "system" };
        _doc.UpdateSettings(c => c.Theme = next, "Thème");
    }

    // ---------- commandes de la vue ----------

    [RelayCommand] void OpenMenu() { _specTrail.Clear(); _profileMenu = false; BuildMenu(); MenuOpen = true; }
    /// <summary>Pastille du profil : ouvre directement la liste des profils.</summary>
    [RelayCommand] void OpenProfiles() { if (IsEdit) { OpenProfileEditMenu(); return; } if (!HasProfiles) { OpenMenu(); return; } _specTrail.Clear(); _profileMenu = true; BuildMenu(); MenuOpen = true; }
    [RelayCommand] void CloseMenu() => MenuOpen = false;
    [RelayCommand] void EndEdit() { IsEdit = false; Rebuild(); }
    [RelayCommand] void Minimize() => _shell.Minimize();
    [RelayCommand] void ToggleFullscreen() { _shell.ToggleFullscreen(); IsFullscreen = _shell.IsFullscreen; }
    [RelayCommand] void CloseApp() => Application.Current.Shutdown();
    [RelayCommand] void ParentFolder() => FolderUp();

    bool _active = true;

    /// <summary>true si les minuteries du deck tournent (fenêtre visible) ; faux : réduit dans la barre des tâches ou masqué.</summary>
    internal bool IsActive => _active;

    /// <summary>true tant que la minuterie des horloges et minuteurs est en marche (tests).</summary>
    internal bool LiveTimerRunning => _liveTimer.IsEnabled;

    /// <summary>
    /// Fenêtre réduite ou masquée : plus aucune minuterie ne tourne (horloges, minuteurs, suivi de l'application au premier plan),
    /// donc plus de réveil du processeur pour un deck que personne ne voit. Au retour, tout est remis à jour d'un coup.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        _integrations.SetBackground(!active);
        foreach (var data in _dataWidgets) data.SetPaused(!active);
        if (active)
        {
            _appTimer.Start();
            RefreshLive();            // l'heure, les minuteurs et l'agenda ont avancé pendant l'absence
            PollForeground();
        }
        else
        {
            _appTimer.Stop();
            _liveTimer.Stop();
        }
    }

    /// <summary>La fenêtre signale un changement de placement (plein écran / fenêtre) pour mettre à jour l'icône.</summary>
    public void NotifyWindowState() => IsFullscreen = _shell.IsFullscreen;
    [RelayCommand] void PrevSub() { int subs = SubPages(BuildView().Count); _sub = (_sub - 1 + subs) % subs; Rebuild(); }
    [RelayCommand] void NextSub() { int subs = SubPages(BuildView().Count); _sub = (_sub + 1) % subs; Rebuild(); }

    [RelayCommand]
    void AddPage()
    {
        var r = _doc.AddPage(CurrentProfile.Name);
        if (!r.Ok) { SetStatus(r.Message); return; }
        _previousPage = _page;
        _page = r.Index;
        _sub = 0;
        _folder.Clear();
        Rebuild();
    }
}
