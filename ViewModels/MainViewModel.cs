using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchDeck.Actions;
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
    readonly DeckDocument _doc = AppServices.Document;
    readonly ActionEngine _engine = AppServices.Engine;
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
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
    double _w, _h;
    bool _profileMenu;

    public ObservableCollection<object> Tiles { get; } = new();
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<object> MenuEntries { get; } = new();

    [ObservableProperty] LayoutMetrics l = LayoutMetrics.Initial;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrandText))] bool isEdit;
    [ObservableProperty] bool menuOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] string? status;
    [ObservableProperty] bool statusIsError = true;
    [ObservableProperty] Brush rootBackground = Brushes.Transparent;
    [ObservableProperty] bool isFullscreen;
    [ObservableProperty] bool hasSubs;
    [ObservableProperty] string subText = "";
    [ObservableProperty] bool inFolder;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrandText))] string pathText = "";

    public bool HasStatus => Status != null;
    public string BrandText => (IsEdit ? "TouchDeck  ·  Mode édition" : "TouchDeck")
        + (ProfileCount > 1 ? "  ·  " + CurrentProfile.Name : "") + (PathText.Length > 0 ? "  ›  " + PathText : "");

    // ---------- barre du haut et dock ----------

    /// <summary>Profil affiché (pastille de la barre du haut) ; « auto » s'il est imposé par l'application au premier plan.</summary>
    public string ProfileName => CurrentProfile.Name;
    public bool IsAutoProfile => _autoProfile != null;
    public bool HasProfiles => ProfileCount > 1;
    public bool IsSimulated => AppServices.Simulated;
    /// <summary>Points des sous-pages (indicateur du dock).</summary>
    public ObservableCollection<PageDotViewModel> Dots { get; } = new();

    /// <summary>La vue a changé de page, de sous-page ou de dossier (+1 vers l'avant, -1 vers l'arrière) : la fenêtre anime l'entrée de la grille.</summary>
    public event Action<int>? Navigated;
    int _shownPage = -1, _shownSub, _shownDepth;
    string _shownProfile = "";
    public DeckConfig Config => _doc.Config;
    int ProfileCount => _doc.Config.Profiles.Count;

    public MainViewModel(IShell shell)
    {
        _shell = shell;
        ApplyBackground();
        Theme.Changed += () => { ApplyBackground(); TileFaceData.ClearCache(); Rebuild(); };
        _doc.Changed += OnDocumentChanged;
        Notifier.Posted += OnNotice;
        _lastActive = _doc.Config.ActiveProfile;
        _lastPlacement = (_doc.Config.Monitor, _doc.Config.MonitorDevice, _doc.Config.Fullscreen, _doc.Config.AlwaysOnTop);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); SetStatus(null); };
        _appTimer.Tick += (_, _) => PollForeground();
        _appTimer.Start();
        _liveTimer.Tick += (_, _) => RefreshLive();
        _engine.Context.Countdowns.Finished += OnCountdownFinished;
        if (AppServices.StartupMessage != null) SetStatus(AppServices.StartupMessage, error: false);
    }

    /// <summary>Appelé par la fenêtre à chaque changement de taille de la zone de contenu.</summary>
    public void Resize(double width, double height)
    {
        _w = width; _h = height;
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
        Rebuild();
    }

    void OnNotice(Notice n)
    {
        // Le deck affiche les erreurs ; les confirmations restent dans l'éditeur quand il est ouvert
        if (n.Kind is NoticeKind.Error or NoticeKind.Warning) SetStatus(n.Message);
        else if (AppServices.Editor == null) SetStatus(n.Message, error: false);
    }

    void ApplyBackground()
    {
        // Couleur par défaut : fond nuancé du thème (lueur en haut) ; couleur personnalisée : fond uni
        var backdrop = Application.Current.Resources["DeckBackdrop"] as Brush ?? Brushes.Transparent;
        if (string.Equals(_doc.Config.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase)) { RootBackground = backdrop; return; }
        RootBackground = Theme.TryBrush(_doc.Config.Background, out var b) ? b : backdrop;   // couleur invalide : fond du thème
    }

    void SetStatus(string? text, bool error = true)
    {
        StatusIsError = error;
        Status = text;
        _statusTimer.Stop();
        if (text != null) _statusTimer.Start();
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
        int idx = name.Equals("home", StringComparison.OrdinalIgnoreCase) ? 0
            : name.Equals("back", StringComparison.OrdinalIgnoreCase) ? Math.Min(_previousPage, pages.Count - 1)
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

    void Rebuild()
    {
        if (_w < 50 || _h < 50 || _doc.Config.Profiles.Count == 0) return;

        var profile = CurrentProfile;
        var m = LayoutMetrics.Compute(_w, _h, _doc.Config, profile.Columns, profile.Rows);
        _capacity = m.Columns * m.Rows;
        AppServices.AutoGrid = LayoutMetrics.GridSize(_w, _h, _doc.Config);
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

        BuildTiles(view);
        BuildTabs();
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

    void BuildTiles(List<ViewItem> view)
    {
        Tiles.Clear();
        var place = CurrentPlace;
        int offset = _folder.Count > 0 ? 1 : 0;
        int first = _sub * _capacity;
        for (int slot = 0; slot < _capacity; slot++)
        {
            int i = first + slot;
            if (i >= view.Count)
            {
                // Au-delà du contenu : en édition, chaque case libre propose d'ajouter un bouton
                if (IsEdit) { int at = i - offset; Tiles.Add(new AddTileViewModel { Click = new RelayCommand(() => AppServices.OpenEditor(place, at)) }); }
                else Tiles.Add(new SpacerTileViewModel());   // touche libre, dessinée éteinte
                continue;
            }
            var item = view[i];
            if (item.IsBack)
            {
                Tiles.Add(new DeckTileViewModel
                {
                    Face = new TileFaceData
                    {
                        Label = "Retour", Symbol = SymbolRegular.ArrowLeft24, HasSymbol = true,
                        Foreground = Brushes.White, Background = Theme.B(Theme.SurfaceHi),
                    },
                    Label = "Retour", AccessibleName = "Retour au dossier parent", IsEdit = false,
                    Click = new RelayCommand(FolderUp),
                });
                continue;
            }
            if (item.Button == null)
            {
                int at = item.ListIndex;
                if (IsEdit) Tiles.Add(new AddTileViewModel { Click = new RelayCommand(() => AppServices.OpenEditor(place, at)) });
                else Tiles.Add(new SpacerTileViewModel());
                continue;
            }
            var def = item.Button;
            int idx = item.ListIndex;
            DeckTileViewModel? tile = null;
            tile = new DeckTileViewModel
            {
                Button = def, Label = def.Label, IsEdit = IsEdit,
                Click = IsEdit
                    ? new RelayCommand(() => AppServices.OpenEditor(place, idx))
                    : def.IsFolder
                        ? new RelayCommand(() => EnterFolder(idx))
                        : new AsyncRelayCommand(() => RunTileAsync(tile!), AsyncRelayCommandOptions.AllowConcurrentExecutions),
            };
            UpdateFace(tile);
            Tiles.Add(tile);
        }
        UpdateLiveTimer();
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
            SetStatus($"{def.Label}{step} : {result.Message}");
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
        tile.Face = TileFaceData.From(def, left is { } t ? Countdowns.Format(t) : null, countdowns.Progress(def));
        tile.AccessibleName = def.IsFolder ? $"Dossier {tile.Face.Label}" : tile.Face.Label;
    }

    bool IsLive(DeckTileViewModel t) =>
        t.Button is { } b && (LabelTemplate.IsDynamic(b.Label) || _engine.Context.Countdowns.Remaining(b) != null);

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
        var minute = DateTime.Now.ToString("yyyyMMddHHmm");
        bool newMinute = minute != _lastMinute;
        _lastMinute = minute;
        foreach (var t in Tiles.OfType<DeckTileViewModel>())
        {
            if (t.Button is not { } b) continue;
            if (_engine.Context.Countdowns.Remaining(b) != null || LabelTemplate.NeedsSeconds(b.Label)
                || (newMinute && LabelTemplate.IsDynamic(b.Label)))
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

        Add(SymbolRegular.Edit24, "Ouvrir l'éditeur", () => AppServices.OpenEditor(CurrentPlace));
        Add(SymbolRegular.Cursor24, IsEdit ? "Terminer la modification" : "Modifier au toucher", () => IsEdit = !IsEdit);
        if (ProfileCount > 1)
            Add(SymbolRegular.PersonSwap24, "Changer de profil", () => { _profileMenu = true; BuildMenu(); }, trailing: CurrentProfile.Name, keepOpen: true);
        Add(SymbolRegular.Settings24, "Paramètres", AppServices.OpenSettings);
        Add(SymbolRegular.DarkTheme24, "Thème", CycleTheme, trailing: _doc.Config.Theme.ToLowerInvariant() switch { "dark" => "Sombre", "light" => "Clair", _ => "Auto" });
        Add(SymbolRegular.Desktop24, "Changer d'écran", _shell.NextScreen);
        Add(_shell.IsFullscreen ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24, _shell.IsFullscreen ? "Passer en fenêtre" : "Passer en plein écran", _shell.ToggleFullscreen);
        Add(SymbolRegular.Pin24, "Toujours au premier plan", () => _doc.UpdateSettings(c => c.AlwaysOnTop = !c.AlwaysOnTop, "Premier plan"), trailing: _shell.IsOnTop ? "✓" : null);
        Add(SymbolRegular.ArrowSync24, "Recharger la configuration", AppServices.ReloadFromDisk);
        MenuEntries.Add(new MenuSeparatorViewModel());
        Add(SymbolRegular.Power24, "Quitter TouchDeck", () => Application.Current.Shutdown(), danger: true);
    }

    void CycleTheme()
    {
        var next = _doc.Config.Theme.ToLowerInvariant() switch { "system" => "dark", "dark" => "light", _ => "system" };
        _doc.UpdateSettings(c => c.Theme = next, "Thème");
    }

    // ---------- commandes de la vue ----------

    [RelayCommand] void OpenMenu() { _profileMenu = false; BuildMenu(); MenuOpen = true; }
    /// <summary>Pastille du profil : ouvre directement la liste des profils.</summary>
    [RelayCommand] void OpenProfiles() { if (!HasProfiles) { OpenMenu(); return; } _profileMenu = true; BuildMenu(); MenuOpen = true; }
    [RelayCommand] void CloseMenu() => MenuOpen = false;
    [RelayCommand] void EndEdit() { IsEdit = false; Rebuild(); }
    [RelayCommand] void Minimize() => _shell.Minimize();
    [RelayCommand] void ToggleFullscreen() { _shell.ToggleFullscreen(); IsFullscreen = _shell.IsFullscreen; }
    [RelayCommand] void CloseApp() => Application.Current.Shutdown();
    [RelayCommand] void ParentFolder() => FolderUp();
    [RelayCommand] void OpenEditorHere() => AppServices.OpenEditor(CurrentPlace);

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
