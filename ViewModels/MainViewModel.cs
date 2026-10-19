using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

public partial class MainViewModel : ObservableObject
{
    readonly IShell _shell;
    readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    FileSystemWatcher? _watcher;
    DeckConfig _cfg;
    int _page, _previousPage;
    int _sub;        // sous-page courante quand une page dépasse la capacité de la grille
    int _capacity = 1;
    double _w, _h;

    public ObservableCollection<object> Tiles { get; } = new();
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<object> MenuEntries { get; } = new();

    [ObservableProperty] LayoutMetrics l = LayoutMetrics.Initial;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrandText))] bool isEdit;
    [ObservableProperty] bool menuOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] string? status;
    [ObservableProperty] Brush rootBackground = Brushes.Transparent;
    [ObservableProperty] bool hasSubs;
    [ObservableProperty] string subText = "";

    public bool HasStatus => Status != null;
    public string BrandText => IsEdit ? "TouchDeck  ·  Mode édition" : "TouchDeck";
    public DeckConfig Config => _cfg;

    public MainViewModel(IShell shell)
    {
        _shell = shell;
        string? error = null;
        try { _cfg = ConfigStore.Load(); }
        catch (Exception ex) { _cfg = ConfigStore.DefaultConfig(); error = "Config invalide : " + ex.Message; }

        Theme.Apply(_cfg.Theme);
        Theme.Changed += () => { ApplyBackground(); Rebuild(); };
        ApplyBackground();
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); SetStatus(null); };
        StartWatcher();
        if (error != null) SetStatus(error);
    }

    /// <summary>Appelé par la fenêtre à chaque changement de taille de la zone de contenu.</summary>
    public void Resize(double width, double height)
    {
        _w = width; _h = height;
        Rebuild();
    }

    // ---------- configuration ----------

    void ApplyBackground()
    {
        // Couleur par défaut : Mica visible ; couleur personnalisée : fond uni par-dessus
        if (string.Equals(_cfg.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase)) { RootBackground = Brushes.Transparent; return; }
        try { RootBackground = (Brush)new BrushConverter().ConvertFromString(_cfg.Background)!; }
        catch { RootBackground = Brushes.Transparent; /* couleur invalide : Mica */ }
    }

    void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(ConfigStore.Dir, Path.GetFileName(ConfigStore.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler h = (_, _) => Application.Current.Dispatcher.BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
            _watcher.Changed += h; _watcher.Created += h; _watcher.Renamed += (_, _) => h(null!, null!);
        }
        catch { /* pas de rechargement automatique */ }
    }

    void Reload()
    {
        try
        {
            _cfg = ConfigStore.Load();
            _page = Math.Min(_page, _cfg.Pages.Count - 1);
            Theme.Apply(_cfg.Theme);
            ApplyBackground();
            if (_cfg.Fullscreen && _shell.IsFullscreen) _shell.ReapplyPlacement();
            SetStatus(null);
        }
        catch (Exception ex) { SetStatus("Config invalide : " + ex.Message); }
        Rebuild();
    }

    void Persist()
    {
        try { ConfigStore.Save(_cfg); }
        catch (Exception ex) { SetStatus("Sauvegarde impossible : " + ex.Message); }
    }

    void SetStatus(string? text)
    {
        Status = text;
        _statusTimer.Stop();
        if (text != null) _statusTimer.Start();
    }

    // ---------- navigation ----------

    void NavigateTo(string name)
    {
        int idx = name.Equals("home", StringComparison.OrdinalIgnoreCase) ? 0
            : name.Equals("back", StringComparison.OrdinalIgnoreCase) ? _previousPage
            : _cfg.Pages.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) { SetStatus($"Page introuvable : {name}"); return; }
        GoTo(idx);
    }

    void GoTo(int idx)
    {
        if (idx == _page) return;
        _previousPage = _page;
        _page = idx;
        _sub = 0;
        Rebuild();
    }

    DeckPage CurrentPage => _cfg.Pages[Math.Clamp(_page, 0, _cfg.Pages.Count - 1)];

    int SubPages(DeckPage page) =>
        Math.Max(1, (int)Math.Ceiling((page.Buttons.Count + (IsEdit ? 1 : 0)) / (double)_capacity));

    // ---------- état d'affichage ----------

    void Rebuild()
    {
        if (_w < 50 || _h < 50 || _cfg.Pages.Count == 0) return;

        var m = LayoutMetrics.Compute(_w, _h, _cfg);
        _capacity = m.Columns * m.Rows;
        L = m;

        var page = CurrentPage;
        int subs = SubPages(page);
        _sub = Math.Clamp(_sub, 0, subs - 1);
        HasSubs = subs > 1;
        SubText = $"{_sub + 1} / {subs}";

        BuildTiles(page);
        BuildTabs();
    }

    void BuildTiles(DeckPage page)
    {
        Tiles.Clear();
        int first = _sub * _capacity;
        for (int slot = 0; slot < _capacity; slot++)
        {
            int i = first + slot;
            if (i >= page.Buttons.Count) break;
            var def = page.Buttons[i];
            Tiles.Add(new DeckTileViewModel
            {
                Icon = def.Icon, Label = def.Label, IsEdit = IsEdit,
                Background = ToBrush(def.Color, Theme.SurfaceHi),
                Click = IsEdit
                    ? new RelayCommand(() => EditButton(page, i))
                    : new AsyncRelayCommand(
                        () => ActionRunner.RunAsync(def, NavigateTo, msg => Application.Current.Dispatcher.Invoke(() => SetStatus(msg))),
                        AsyncRelayCommandOptions.AllowConcurrentExecutions),
            });
        }

        // En édition, les cases vides deviennent des emplacements « + » pour ajouter un bouton
        if (IsEdit)
            for (int slot = Math.Max(0, page.Buttons.Count - first); slot < _capacity; slot++)
                Tiles.Add(new AddTileViewModel { Click = new RelayCommand(() => EditButton(page, page.Buttons.Count)) });
    }

    void BuildTabs()
    {
        Tabs.Clear();
        for (int i = 0; i < _cfg.Pages.Count; i++)
        {
            var p = _cfg.Pages[i];
            if (!IsEdit && p.Hidden) continue;
            int target = i;
            Tabs.Add(new TabViewModel
            {
                Text = p.Hidden ? p.Name + " (masquée)" : p.Name, IsActive = i == _page,
                Command = new RelayCommand(() => GoTo(target)),
            });
        }
    }

    void BuildMenu()
    {
        MenuEntries.Clear();
        void Add(SymbolRegular symbol, string text, Action act, bool danger = false, string? trailing = null) =>
            MenuEntries.Add(new MenuEntryViewModel
            {
                Symbol = symbol, Text = text, IsDanger = danger, Trailing = trailing,
                Command = new RelayCommand(() => { MenuOpen = false; act(); Rebuild(); }),
            });

        Add(SymbolRegular.Edit24, IsEdit ? "Terminer la modification" : "Modifier les boutons", () => IsEdit = !IsEdit);
        Add(SymbolRegular.Document24, "Ouvrir le fichier de configuration", () =>
            Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true }));
        Add(SymbolRegular.ArrowSync24, "Recharger la configuration", Reload);
        Add(SymbolRegular.DarkTheme24, "Thème", CycleTheme, trailing: _cfg.Theme.ToLowerInvariant() switch { "dark" => "Sombre", "light" => "Clair", _ => "Auto" });
        Add(SymbolRegular.Desktop24, "Changer d'écran", _shell.NextScreen);
        Add(_shell.IsFullscreen ? SymbolRegular.FullScreenMinimize24 : SymbolRegular.FullScreenMaximize24, _shell.IsFullscreen ? "Passer en fenêtre" : "Passer en plein écran", _shell.ToggleFullscreen);
        Add(SymbolRegular.Pin24, "Toujours au premier plan", _shell.ToggleOnTop, trailing: _shell.IsOnTop ? "✓" : null);
        MenuEntries.Add(new MenuSeparatorViewModel());
        Add(SymbolRegular.Power24, "Quitter TouchDeck", () => Application.Current.Shutdown(), danger: true);
    }

    void CycleTheme()
    {
        _cfg.Theme = _cfg.Theme.ToLowerInvariant() switch { "system" => "dark", "dark" => "light", _ => "system" };
        Persist();
        Theme.Apply(_cfg.Theme);
    }

    // ---------- commandes de la vue ----------

    [RelayCommand] void OpenMenu() { BuildMenu(); MenuOpen = true; }
    [RelayCommand] void CloseMenu() => MenuOpen = false;
    [RelayCommand] void EndEdit() { IsEdit = false; Rebuild(); }
    [RelayCommand] void Minimize() => _shell.Minimize();
    [RelayCommand] void PrevSub() { int subs = SubPages(CurrentPage); _sub = (_sub - 1 + subs) % subs; Rebuild(); }
    [RelayCommand] void NextSub() { int subs = SubPages(CurrentPage); _sub = (_sub + 1) % subs; Rebuild(); }
    [RelayCommand] void EditCurrentPage() => EditPage(_page);

    [RelayCommand]
    void AddPage()
    {
        _cfg.Pages.Add(new DeckPage { Name = $"Page {_cfg.Pages.Count + 1}" });
        _previousPage = _page;
        _page = _cfg.Pages.Count - 1;
        _sub = 0;
        Persist();
        EditPage(_page);
    }

    // ---------- édition des boutons et des pages ----------

    void EditButton(DeckPage page, int index)
    {
        bool isNew = index >= page.Buttons.Count;
        var source = isNew
            ? new DeckButton { Label = "Nouveau", Icon = "⭐", Color = "#272B38", Actions = { new DeckAction { Type = "hotkey" } } }
            : ConfigStore.Clone(page.Buttons[index]);
        var result = _shell.EditButton(source, _cfg.Pages.Select(p => p.Name).ToList(), isNew);
        if (result == null) return;

        if (result.Deleted) page.Buttons.RemoveAt(index);
        else
        {
            if (isNew) { page.Buttons.Add(result.Button); index = page.Buttons.Count - 1; }
            else page.Buttons[index] = result.Button;

            int to = Math.Clamp(index + result.Move, 0, page.Buttons.Count - 1);
            if (to != index)
            {
                var b = page.Buttons[index];
                page.Buttons.RemoveAt(index);
                page.Buttons.Insert(to, b);
            }
        }
        Persist();
        Rebuild();
    }

    void EditPage(int idx)
    {
        var page = _cfg.Pages[idx];
        var result = _shell.EditPage(page, _cfg.Pages.Count > 1);
        if (result == null) { Rebuild(); return; }

        if (result.Deleted)
        {
            _cfg.Pages.RemoveAt(idx);
            _page = Math.Clamp(idx - 1, 0, _cfg.Pages.Count - 1);
            _previousPage = _page;
            _sub = 0;
        }
        else
        {
            var newName = string.IsNullOrWhiteSpace(result.Name) ? page.Name : result.Name.Trim();
            if (!newName.Equals(page.Name, StringComparison.Ordinal))
            {
                // met à jour les boutons qui pointaient vers l'ancien nom
                foreach (var a in _cfg.Pages.SelectMany(p => p.Buttons).SelectMany(b => b.Actions))
                    if (a.Type.Equals("page", StringComparison.OrdinalIgnoreCase) && a.Value.Equals(page.Name, StringComparison.OrdinalIgnoreCase))
                        a.Value = newName;
                page.Name = newName;
            }
            page.Hidden = result.Hidden;

            int to = Math.Clamp(idx + result.Move, 0, _cfg.Pages.Count - 1);
            if (to != idx)
            {
                _cfg.Pages.RemoveAt(idx);
                _cfg.Pages.Insert(to, page);
                _page = to;
            }
        }
        Persist();
        Rebuild();
    }

    static Brush ToBrush(string color, string fallback)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return (Brush)new BrushConverter().ConvertFromString(fallback)!; }
    }
}
