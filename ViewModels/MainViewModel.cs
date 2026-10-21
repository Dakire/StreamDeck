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
using TouchDeck.Actions;
using TouchDeck.Integrations;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

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
    public string BrandText => (IsEdit ? "TouchDeck  ·  Mode édition" : "TouchDeck")
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
    int _shownPage = -1, _shownSub, _shownDepth;
    string _shownProfile = "";
    /// <summary>Configuration courante (placement de la fenêtre).</summary>
    public DeckConfig Config => _doc.Config;
    int ProfileCount => _doc.Config.Profiles.Count;

    /// <summary>Deck sur le document partagé ; <paramref name="shell"/> fournit le placement Win32.</summary>
    public MainViewModel(IShell shell, DeckDocument doc, ActionEngine engine, AppOptions options, StartupResult startup,
        GridState grid, IAppWindows windows, ConfigPersistence persistence, IntegrationManager integrations,
        Func<IEditorShell, EditorViewModel> editorFactory)
    {
        _editorFactory = editorFactory;
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

    Place CurrentPlace => new(CurrentProfile.Name, _page, _folder.ToArray());

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
        Rebuild(navigation: !change.InPlace);   // un bouton modifié sur place ne change ni les pages ni les onglets
    }

    void OnNotice(Notice n)
    {
        // Le deck affiche les erreurs ; les confirmations restent dans l'éditeur quand il est ouvert
        if (n.Kind is NoticeKind.Error or NoticeKind.Warning) SetStatus(n.Message);
        else if (!_windows.IsEditorOpen) SetStatus(n.Message, error: false, n.ActionText, n.Action);
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
        int subs = SubPages(view.Count);
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
    readonly record struct TileKey(string Kind, Place Place, int Index, DeckButton? Button, string? Label, bool IsFolder, bool IsEdit);

    readonly List<TileKey> _tileKeys = new();

    void BuildTiles(List<ViewItem> view)
    {
        // Réconciliation case par case plutôt que Clear + Add : une modification de document (titre tapé dans l'éditeur,
        // réglage) ne régénère plus les 32 conteneurs de la grille, seulement les cases qui ont réellement changé.
        var place = CurrentPlace;
        int offset = _folder.Count > 0 ? 1 : 0;
        int first = _sub * _capacity;
        bool empty = !view.Any(v => v.Button != null);   // aucune touche : la première case invite à ajouter un bouton
        for (int slot = 0; slot < _capacity; slot++)
        {
            int i = first + slot;
            TileKey key;
            Func<object> create;
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
                key = new TileKey("button", place, idx, def, def.Label, def.IsFolder, edit);
                create = () =>
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

            if (slot < Tiles.Count && _tileKeys[slot] == key)
            {
                if (Tiles[slot] is DeckTileViewModel kept && key.Kind == "button") UpdateFace(kept);   // couleur, icône, thème…
                continue;
            }
            var item = create();
            if (item is DeckTileViewModel created && key.Kind == "button") UpdateFace(created);
            if (slot < Tiles.Count) { Tiles[slot] = item; _tileKeys[slot] = key; }
            else { Tiles.Add(item); _tileKeys.Add(key); }
        }
        while (Tiles.Count > _capacity)
        {
            Tiles.RemoveAt(Tiles.Count - 1);
            _tileKeys.RemoveAt(_tileKeys.Count - 1);
        }
        UpdateLiveTimer();
        ApplySelection();
        ShowDiscoveryHintOnce(view.Count(v => v.Button != null));
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

    void UpdateFace(DeckTileViewModel tile)
    {
        var def = tile.Button;
        if (def == null) return;
        var countdowns = _engine.Context.Countdowns;
        var left = countdowns.Remaining(def);
        var state = def.IsFolder ? null : def.Actions.Select(_integrations.StateOf).FirstOrDefault(s => s != null);
        tile.Face = TileFaceData.From(def, left is { } t ? Countdowns.Format(t) : null, countdowns.Progress(def), _integrations.GetValue, state);
        tile.AccessibleName = def.IsFolder ? $"Dossier {tile.Face.Label}"
            : state is { } on ? $"{tile.Face.Label}, {(on ? "actif" : "inactif")}" : tile.Face.Label;
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
                if (t.Button is { IsFolder: false } b && (LabelTemplate.ValueKeys(b.Label).Count > 0 || b.Actions.Any(a => _integrations.Find(a.Type) != null)))
                    UpdateFace(t);
        });
    }

    bool IsLive(DeckTileViewModel t) =>
        t.Button is { } b && (LabelTemplate.HasClockTokens(b.Label) || _engine.Context.Countdowns.Remaining(b) != null);

    /// <summary>L'horloge ne tourne que si une tuile visible l'utilise (ou qu'un minuteur est en cours).</summary>
    void UpdateLiveTimer()
    {
        bool needed = _engine.Context.Countdowns.Any || Tiles.OfType<DeckTileViewModel>().Any(IsLive);
        if (needed && !_liveTimer.IsEnabled) _liveTimer.Start();
        else if (!needed) _liveTimer.Stop();
    }

    /// <summary>Met à jour uniquement les tuiles dynamiques ; une horloge sans secondes n'est redessinée qu'au changement de minute.</summary>
    void RefreshLive()
    {
        _engine.Context.Countdowns.Tick();
        var minute = DateTime.Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        bool newMinute = minute != _lastMinute;
        _lastMinute = minute;
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
                Text = p.Hidden ? p.Name + " (masquée)" : p.Name, IsActive = i == _page,
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

        Add(SymbolRegular.Edit24, "Ouvrir l'éditeur", () => _windows.OpenEditor(CurrentPlace));
        Add(SymbolRegular.Cursor24, IsEdit ? "Terminer la modification" : "Modifier au toucher", () => IsEdit = !IsEdit);
        if (ProfileCount > 1)
            Add(SymbolRegular.PersonSwap24, "Changer de profil", () => { _profileMenu = true; BuildMenu(); }, trailing: CurrentProfile.Name, keepOpen: true);
        Add(SymbolRegular.Settings24, "Paramètres", _windows.OpenSettings);
        Add(SymbolRegular.DarkTheme24, "Thème", CycleTheme, trailing: _doc.Config.Theme.ToLowerInvariant() switch { "dark" => "Sombre", "amoled" => "AMOLED", "light" => "Clair", _ => "Auto" });
        Add(SymbolRegular.Desktop24, "Changer d'écran", _shell.NextScreen);
        Add(_shell.IsFullscreen ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24, _shell.IsFullscreen ? "Passer en fenêtre" : "Passer en plein écran", _shell.ToggleFullscreen);
        Add(SymbolRegular.Pin24, "Toujours au premier plan", () => _doc.UpdateSettings(c => c.AlwaysOnTop = !c.AlwaysOnTop, "Premier plan"), trailing: _shell.IsOnTop ? "✓" : null);
        Add(SymbolRegular.ArrowSync24, "Recharger la configuration", _persistence.ReloadFromDisk);
        MenuEntries.Add(new MenuSeparatorViewModel());
        Add(SymbolRegular.Power24, "Quitter TouchDeck", () => Application.Current.Shutdown(), danger: true);
    }

    void CycleTheme()
    {
        var next = _doc.Config.Theme.ToLowerInvariant() switch { "system" => "dark", "dark" => "amoled", "amoled" => "light", _ => "system" };
        _doc.UpdateSettings(c => c.Theme = next, "Thème");
    }

    // ---------- commandes de la vue ----------

    [RelayCommand] void OpenMenu() { _specTrail.Clear(); _profileMenu = false; BuildMenu(); MenuOpen = true; }
    /// <summary>Pastille du profil : ouvre directement la liste des profils.</summary>
    [RelayCommand] void OpenProfiles() { if (!HasProfiles) { OpenMenu(); return; } _specTrail.Clear(); _profileMenu = true; BuildMenu(); MenuOpen = true; }
    [RelayCommand] void CloseMenu() => MenuOpen = false;
    [RelayCommand] void EndEdit() { IsEdit = false; Rebuild(); }
    [RelayCommand] void Minimize() => _shell.Minimize();
    [RelayCommand] void ToggleFullscreen() { _shell.ToggleFullscreen(); IsFullscreen = _shell.IsFullscreen; }
    [RelayCommand] void CloseApp() => Application.Current.Shutdown();
    [RelayCommand] void ParentFolder() => FolderUp();
    [RelayCommand] void OpenEditorHere() => _windows.OpenEditor(CurrentPlace);

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
