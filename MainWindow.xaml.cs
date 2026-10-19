using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TouchDeck;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    // Palette : voir Theme.cs (clair / sombre)
    static string Surface => Theme.Surface;
    static string SurfaceHi => Theme.SurfaceHi;
    static string Accent => Theme.Accent;
    static string TextDim => Theme.Dim;

    DeckConfig _cfg;
    int _page;
    int _previousPage;
    bool _full;
    bool _onTop;
    bool _menuOpen;
    bool _edit;
    int _sub;        // sous-page courante quand une page dépasse la capacité de la grille
    int _capacity = 1;
    string? _status;
    readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    FileSystemWatcher? _watcher;
    static readonly BitmapImage Logo = new(new Uri("pack://application:,,,/Assets/logo.png"));

    public MainWindow()
    {
        InitializeComponent();
        try { _cfg = ConfigStore.Load(); }
        catch (Exception ex) { _cfg = ConfigStore.DefaultConfig(); _status = "Config invalide : " + ex.Message; }
        _onTop = _cfg.AlwaysOnTop;
        Topmost = _onTop;

        Theme.Apply(_cfg.Theme);
        Theme.Changed += () => Rebuild();
        ApplyBackground();
        Root.SizeChanged += (_, _) => Rebuild();
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); SetStatus(null); };
        StartWatcher();
    }

    // ---------- fenêtre : sans focus, dans la barre des tâches, placée sur l'écran tactile ----------

    IntPtr Hwnd => new WindowInteropHelper(this).Handle;
    IntPtr ZOrder => _onTop ? HWND_TOPMOST : HWND_NOTOPMOST;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // NOACTIVATE : toucher l'appli ne retire pas le focus de la fenêtre en cours (les raccourcis lui parviennent)
        SetWindowLong(Hwnd, GWL_EXSTYLE, GetWindowLong(Hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        PlaceOnScreen();
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
        return IntPtr.Zero;
    }

    void PlaceOnScreen()
    {
        if (!_cfg.Fullscreen) return;
        var screens = System.Windows.Forms.Screen.AllScreens;
        System.Windows.Forms.Screen target;
        if (_cfg.Monitor >= 0 && _cfg.Monitor < screens.Length) target = screens[_cfg.Monitor];
        else
        {
            // Auto : plein écran sur l'écran secondaire ; sans second écran, on reste en fenêtre
            var secondary = screens.FirstOrDefault(s => !s.Primary);
            if (secondary == null) return;
            target = secondary;
        }
        GoFullscreen(target);
    }

    void GoFullscreen(System.Windows.Forms.Screen s)
    {
        var b = s.Bounds; // pixels physiques
        SetWindowPos(Hwnd, ZOrder, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        _full = true;
    }

    /// <summary>Passe en fenêtre 1100×650 (DIP) ; avec cursorPx, la barre du haut est placée sous le doigt.</summary>
    void GoWindowed(System.Windows.Forms.Screen s, POINT? cursorPx = null)
    {
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        int w = (int)(1100 * scale), h = (int)(650 * scale);
        var wa = s.WorkingArea;
        int x = wa.X + (wa.Width - w) / 2, y = wa.Y + (wa.Height - h) / 2;
        if (cursorPx is { } c) { x = c.X - w / 2; y = c.Y - 12; }
        SetWindowPos(Hwnd, ZOrder, x, y, w, h, SWP_NOACTIVATE);
        _full = false;
    }

    System.Windows.Forms.Screen CurrentScreen() => System.Windows.Forms.Screen.FromHandle(Hwnd);

    void NextScreen()
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        var cur = CurrentScreen();
        var next = all[(Array.FindIndex(all, s => s.DeviceName == cur.DeviceName) + 1) % all.Length];
        if (_full) GoFullscreen(next); else GoWindowed(next);
    }

    void ToggleFullscreen()
    {
        if (_full) GoWindowed(CurrentScreen()); else GoFullscreen(CurrentScreen());
    }

    void CycleTheme()
    {
        _cfg.Theme = _cfg.Theme.ToLowerInvariant() switch { "system" => "dark", "dark" => "light", _ => "system" };
        Persist();
        Theme.Apply(_cfg.Theme);
    }

    void ToggleOnTop()
    {
        _onTop = !_onTop;
        Topmost = _onTop;
        SetWindowPos(Hwnd, ZOrder, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE); // NOSIZE | NOMOVE
    }

    void OnDragBarDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleFullscreen(); return; }
        if (_full)
        {
            GetCursorPos(out var p);
            GoWindowed(CurrentScreen(), p);
        }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    // ---------- configuration ----------

    void ApplyBackground()
    {
        // Couleur par défaut : Mica visible ; couleur personnalisée : fond uni par-dessus
        if (string.Equals(_cfg.Background, Theme.DefaultBackground, StringComparison.OrdinalIgnoreCase)) { Root.Background = Brushes.Transparent; return; }
        try { Root.Background = (Brush)new BrushConverter().ConvertFromString(_cfg.Background)!; }
        catch { Root.Background = Brushes.Transparent; /* couleur invalide : Mica */ }
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
            FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
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
            if (_cfg.Fullscreen && _full) PlaceOnScreen();
            SetStatus(null);
        }
        catch (Exception ex) { SetStatus("Config invalide : " + ex.Message); }
        Rebuild();
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

    void SetStatus(string? text)
    {
        _status = text;
        _statusTimer.Stop();
        if (text != null) _statusTimer.Start();
        Rebuild();
    }

    void SetMenu(bool open) { _menuOpen = open; Rebuild(); }

    // ---------- construction de l'interface ----------

    void Rebuild()
    {
        double W = Root.ActualWidth, H = Root.ActualHeight;
        if (W < 50 || H < 50 || _cfg.Pages.Count == 0) return;

        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();

        double scale = Math.Clamp(H / 1080.0, 0.6, 1.6);
        double gap = Math.Max(6, _cfg.Spacing * scale);
        double barH = Math.Clamp(H * 0.085, 52, 92);   // cibles tactiles >= 48 px
        double topH = Math.Clamp(H * 0.05, 36, 56);

        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(topH) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(barH + gap * 1.5) });

        var top = BuildTopBar(topH, gap);
        Grid.SetRow(top, 0);
        Root.Children.Add(top);

        var page = _cfg.Pages[Math.Clamp(_page, 0, _cfg.Pages.Count - 1)];
        double availW = W - gap * 2, availH = H - topH - barH - gap * 1.5 - gap;
        var (gcols, grows) = GridFor(availW, availH);
        _capacity = gcols * grows;
        _sub = Math.Clamp(_sub, 0, SubPages(page) - 1);
        var host = BuildButtons(page, availW, availH, gap);
        host.Margin = new Thickness(gap, gap * 0.5, gap, 0);
        Grid.SetRow(host, 1);
        Root.Children.Add(host);

        var bar = BuildBar(gap, barH);
        Grid.SetRow(bar, 2);
        Root.Children.Add(bar);

        if (_menuOpen)
        {
            var overlay = BuildMenu(W, H, barH, gap);
            Grid.SetRowSpan(overlay, 3);
            Root.Children.Add(overlay);
        }
    }

    Border BuildTopBar(double topH, double gap)
    {
        var grid = new Grid { Margin = new Thickness(gap, 0, gap, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image { Source = Logo, Width = topH * 0.6, Height = topH * 0.6 });
        brand.Children.Add(new TextBlock
        {
            Text = _edit ? "TouchDeck  ·  Mode édition" : "TouchDeck", Foreground = Brush(_edit ? Accent : TextDim), FontSize = Math.Max(13, topH * 0.38),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(topH * 0.25, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        grid.Children.Add(brand);

        // poignée de déplacement (pilule centrée)
        var grip = new Border
        {
            Width = Math.Max(60, topH * 1.8), Height = Math.Max(5, topH * 0.14), CornerRadius = new CornerRadius(10),
            Background = Brush(Theme.Grip), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(grip, 1);
        grid.Children.Add(grip);

        var minimize = new Button
        {
            Style = (Style)FindResource("FlatButton"), Width = topH * 1.3, Height = topH * 0.8,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Tag = new CornerRadius(10),
            Content = new TextBlock { Text = "—", Foreground = Brush(TextDim), FontSize = topH * 0.4 },
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 2);
        grid.Children.Add(minimize);

        var border = new Border { Background = Brush(Theme.Dark ? "#33000000" : "#33FFFFFF"), Child = grid };
        border.MouseLeftButtonDown += OnDragBarDown;
        return border;
    }

    /// <summary>Colonnes × lignes selon la résolution effective (en DIP) : ≈ 20 cases en FHD, moins sur petit écran, plus sur grand.</summary>
    (int cols, int rows) GridFor(double availW, double availH)
    {
        int cols = _cfg.Columns > 0 ? _cfg.Columns : (int)Math.Max(2, Math.Round(availW / 360, MidpointRounding.AwayFromZero));
        int rows = _cfg.Rows > 0 ? _cfg.Rows : (int)Math.Max(2, Math.Round(availH / 230, MidpointRounding.AwayFromZero));
        return (cols, rows);
    }

    /// <summary>Nombre de sous-pages nécessaires quand la page contient plus de boutons que la grille n'a de cases.</summary>
    int SubPages(DeckPage page) =>
        Math.Max(1, (int)Math.Ceiling((page.Buttons.Count + (_edit ? 1 : 0)) / (double)_capacity));

    Grid BuildButtons(DeckPage page, double availW, double availH, double gap)
    {
        var grid = new Grid();
        var (cols, rows) = GridFor(availW, availH);
        int capacity = cols * rows;
        int first = _sub * capacity;
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition());

        double cellMin = Math.Min(availW / cols, availH / rows);
        double pad = gap / 2;
        double radius = Math.Clamp(cellMin * 0.12, 12, 30);

        for (int slot = 0; slot < capacity; slot++)
        {
            int i = first + slot;
            if (i >= page.Buttons.Count) break;
            var def = page.Buttons[i];
            var btn = new Button
            {
                Style = (Style)FindResource("DeckButton"),
                Margin = new Thickness(pad),
                Background = Brush(def.Color, SurfaceHi),
                Tag = new CornerRadius(radius),
            };
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(def.Icon))
                panel.Children.Add(new TextBlock
                {
                    Text = def.Icon, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
                    FontSize = cellMin * 0.36, Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                });
            if (!string.IsNullOrEmpty(def.Label))
                panel.Children.Add(new TextBlock
                {
                    Text = def.Label, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                    FontWeight = FontWeights.SemiBold, FontSize = Math.Max(13, cellMin * 0.095),
                    Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8, cellMin * 0.04, 8, 0),
                });
            if (_edit)
                panel.Children.Add(new TextBlock
                {
                    Text = "✎ Modifier", Foreground = Brushes.White, FontSize = Math.Max(11, cellMin * 0.065),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, cellMin * 0.03, 0, 0),
                });
            btn.Content = panel;
            if (_edit) btn.Click += (_, _) => EditButton(page, i);
            else btn.Click += async (_, _) =>
                await ActionRunner.RunAsync(def, NavigateTo, msg => Dispatcher.Invoke(() => SetStatus(msg)));

            Grid.SetColumn(btn, slot % cols);
            Grid.SetRow(btn, slot / cols);
            grid.Children.Add(btn);
        }

        // En édition, les cases vides deviennent des emplacements « + » pour ajouter un bouton
        if (_edit)
        {
            for (int slot = Math.Max(0, page.Buttons.Count - first); slot < capacity; slot++)
            {
                var add = new Button
                {
                    Style = (Style)FindResource("FlatButton"), Margin = new Thickness(pad),
                    Background = Brush(Theme.Bar), Tag = new CornerRadius(radius),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                var g = new Grid { Width = Math.Max(10, availW / cols - pad * 2), Height = Math.Max(10, availH / rows - pad * 2) };
                g.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    RadiusX = radius, RadiusY = radius, Stroke = Brush(Theme.Placeholder), StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 4 }, Margin = new Thickness(2),
                });
                g.Children.Add(new TextBlock
                {
                    Text = "＋", Foreground = Brush(Theme.Dim), FontSize = cellMin * 0.3,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
                add.Content = g;
                add.Click += (_, _) => EditButton(page, page.Buttons.Count);
                Grid.SetColumn(add, slot % cols);
                Grid.SetRow(add, slot / cols);
                grid.Children.Add(add);
            }
        }
        return grid;
    }

    Grid BuildBar(double gap, double barH)
    {
        var bar = new Grid { Margin = new Thickness(gap, gap * 0.5, gap, gap) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new Grid();
        var tabs = _cfg.Pages.Select((p, i) => (p, i)).Where(t => _edit || !t.p.Hidden).ToList();
        double radius = barH * 0.5;

        if (_status != null)
        {
            // message d'état (erreurs) sous forme de pastille
            left.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius), Background = Brush("#3A1F26"),
                BorderBrush = Brush("#7A3340"), BorderThickness = new Thickness(1),
                Padding = new Thickness(barH * 0.4, 0, barH * 0.4, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "⚠  " + _status, Foreground = Brush("#FFB4BE"), VerticalAlignment = VerticalAlignment.Center,
                    FontSize = Math.Max(14, barH * 0.3), TextTrimming = TextTrimming.CharacterEllipsis,
                },
            });
        }
        else
        {
            // onglets sous forme de contrôle segmenté
            var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var (p, i) in tabs)
            {
                bool active = i == _page;
                var tab = new Button
                {
                    Style = (Style)FindResource("FlatButton"), Height = barH, MinWidth = barH * 2,
                    Background = Brush(active ? Accent : Surface),
                    Tag = new CornerRadius(radius), Margin = new Thickness(0, 0, gap * 0.6, 0),
                    Padding = new Thickness(barH * 0.5, 0, barH * 0.5, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = new TextBlock
                    {
                        Text = p.Hidden ? p.Name + " (masquée)" : p.Name, FontSize = Math.Max(15, barH * 0.33), Foreground = active ? Brushes.White : Brush(Theme.Text),
                        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                        FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    },
                };
                int target = i;
                tab.Click += (_, _) => GoTo(target);
                strip.Children.Add(tab);
            }
            if (_edit)
            {
                strip.Children.Add(BarChip("＋ Page", barH, false, AddPage));
                strip.Children.Add(BarChip("✎ Page", barH, false, () => EditPage(_page)));
            }
            left.Children.Add(strip);
        }
        bar.Children.Add(left);

        var gear = new Button
        {
            Style = (Style)FindResource("FlatButton"), Width = barH, Height = barH,
            Background = Brush(Surface), Tag = new CornerRadius(radius),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new TextBlock { Text = "⚙", FontSize = barH * 0.48, Foreground = Brush(Theme.TextSoft) },
        };
        gear.Click += (_, _) => SetMenu(true);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var cur = _cfg.Pages[Math.Clamp(_page, 0, _cfg.Pages.Count - 1)];
        int subs = SubPages(cur);
        if (subs > 1)
        {
            right.Children.Add(BarChip("‹", barH, false, () => { _sub = (_sub - 1 + subs) % subs; Rebuild(); }));
            right.Children.Add(new TextBlock
            {
                Text = $"{_sub + 1} / {subs}", Foreground = Brush(TextDim), FontSize = Math.Max(14, barH * 0.3),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, barH * 0.2, 0),
            });
            right.Children.Add(BarChip("›", barH, false, () => { _sub = (_sub + 1) % subs; Rebuild(); }));
        }
        if (_edit) right.Children.Add(BarChip("✓ Terminer", barH, true, () => { _edit = false; Rebuild(); }));
        right.Children.Add(gear);
        Grid.SetColumn(right, 1);
        bar.Children.Add(right);
        return bar;
    }

    Button BarChip(string text, double barH, bool accent, Action onClick)
    {
        var b = new Button
        {
            Style = (Style)FindResource("FlatButton"), Height = barH, MinWidth = barH,
            Background = Brush(accent ? Accent : Surface), Tag = new CornerRadius(barH / 2),
            Margin = new Thickness(0, 0, barH * 0.15, 0), Padding = new Thickness(barH * 0.4, 0, barH * 0.4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new TextBlock
            {
                Text = text, FontSize = Math.Max(15, barH * 0.33), Foreground = accent ? Brushes.White : Brush(Theme.Text),
                FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            },
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- édition des boutons et des pages ----------

    void Persist()
    {
        try { ConfigStore.Save(_cfg); }
        catch (Exception ex) { SetStatus("Sauvegarde impossible : " + ex.Message); }
    }

    void EditButton(DeckPage page, int index)
    {
        bool isNew = index >= page.Buttons.Count;
        var source = isNew
            ? new DeckButton { Label = "Nouveau", Icon = "⭐", Color = "#272B38", Actions = { new DeckAction { Type = "hotkey" } } }
            : ConfigStore.Clone(page.Buttons[index]);
        var dlg = new ButtonEditorWindow(this, source, _cfg.Pages.Select(p => p.Name).ToList(), isNew);
        if (dlg.ShowDialog() != true) return;

        if (dlg.Deleted) page.Buttons.RemoveAt(index);
        else
        {
            if (isNew) { page.Buttons.Add(dlg.Result); index = page.Buttons.Count - 1; }
            else page.Buttons[index] = dlg.Result;

            int to = Math.Clamp(index + dlg.Move, 0, page.Buttons.Count - 1);
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

    void AddPage()
    {
        _cfg.Pages.Add(new DeckPage { Name = $"Page {_cfg.Pages.Count + 1}" });
        _previousPage = _page;
        _page = _cfg.Pages.Count - 1;
        _sub = 0;
        Persist();
        EditPage(_page);
    }

    void EditPage(int idx)
    {
        var page = _cfg.Pages[idx];
        var dlg = new PageEditorWindow(this, page, _cfg.Pages.Count > 1);
        if (dlg.ShowDialog() != true) { Rebuild(); return; }

        if (dlg.Deleted)
        {
            _cfg.Pages.RemoveAt(idx);
            _page = Math.Clamp(idx - 1, 0, _cfg.Pages.Count - 1);
            _previousPage = _page;
            _sub = 0;
        }
        else
        {
            var newName = string.IsNullOrWhiteSpace(dlg.PageName) ? page.Name : dlg.PageName.Trim();
            if (!newName.Equals(page.Name, StringComparison.Ordinal))
            {
                // met à jour les boutons qui pointaient vers l'ancien nom
                foreach (var a in _cfg.Pages.SelectMany(p => p.Buttons).SelectMany(b => b.Actions))
                    if (a.Type.Equals("page", StringComparison.OrdinalIgnoreCase) && a.Value.Equals(page.Name, StringComparison.OrdinalIgnoreCase))
                        a.Value = newName;
                page.Name = newName;
            }
            page.Hidden = dlg.Hidden;

            int to = Math.Clamp(idx + dlg.Move, 0, _cfg.Pages.Count - 1);
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

    /// <summary>Feuille de menu tactile affichée par-dessus l'interface ; toucher en dehors la ferme.</summary>
    Grid BuildMenu(double W, double H, double barH, double gap)
    {
        var overlay = new Grid();
        var scrim = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Theme.Scrim)) };
        scrim.MouseLeftButtonDown += (_, _) => SetMenu(false);
        overlay.Children.Add(scrim);

        double rowH = Math.Clamp(H * 0.075, 52, 76);
        double fs = Math.Max(15, rowH * 0.32);
        var list = new StackPanel();

        void Add(string icon, string text, Action act, bool danger = false, string? trailing = null)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rowH * 0.9) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = icon, FontSize = fs * 1.15, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"), Foreground = Brush(Theme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var t = new TextBlock
            {
                Text = text, FontSize = fs, VerticalAlignment = VerticalAlignment.Center,
                Foreground = danger ? Brush(Theme.Danger) : Brush(Theme.Text),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            };
            Grid.SetColumn(t, 1); row.Children.Add(t);
            if (trailing != null)
            {
                var tr = new TextBlock
                {
                    Text = trailing, FontSize = fs, Foreground = Brush(Accent), FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
                };
                Grid.SetColumn(tr, 2); row.Children.Add(tr);
            }
            var b = new Button
            {
                Style = (Style)FindResource("FlatButton"), Height = rowH, Background = Brushes.Transparent,
                Tag = new CornerRadius(14), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row,
            };
            b.Click += (_, _) => { _menuOpen = false; act(); Rebuild(); };
            list.Children.Add(b);
        }

        Add("✏️", _edit ? "Terminer la modification" : "Modifier les boutons", () => _edit = !_edit);
        Add("📝", "Ouvrir le fichier de configuration", () =>
            Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true }));
        Add("🔄", "Recharger la configuration", Reload);
        Add(Theme.Dark ? "🌙" : "☀️", "Thème", CycleTheme, trailing: _cfg.Theme.ToLowerInvariant() switch { "dark" => "Sombre", "light" => "Clair", _ => "Auto" });
        Add("🖥️", "Changer d'écran", NextScreen);
        Add(_full ? "🪟" : "⛶", _full ? "Passer en fenêtre" : "Passer en plein écran", ToggleFullscreen);
        Add("📌", "Toujours au premier plan", ToggleOnTop, trailing: _onTop ? "✓" : null);
        list.Children.Add(new Border { Height = 1, Background = Brush(Theme.Line), Margin = new Thickness(12, 6, 12, 6) });
        Add("⏻", "Quitter TouchDeck", () => Application.Current.Shutdown(), danger: true);

        var card = new Border
        {
            Background = Brush(Surface), BorderBrush = Brush(Theme.Line), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(10),
            Width = Math.Min(W - gap * 2, Math.Max(380, rowH * 7)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, gap, barH + gap * 2),
            Child = list,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.5 },
        };
        overlay.Children.Add(card);
        return overlay;
    }

    static Brush Brush(string color, string? fallback = null)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return (Brush)new BrushConverter().ConvertFromString(fallback ?? "#242835")!; }
    }
}
