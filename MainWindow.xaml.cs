using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.ViewModels;

namespace StreamDak;

/// <summary>
/// Fenêtre principale : uniquement ce qui relève de Win32 et du placement (sans focus, plein écran multi-écran,
/// déplacement) et des boîtes de dialogue. L'interface vit dans MainWindow.xaml et MainViewModel.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow, IShell
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;
    const int WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247, WM_POINTERUPDATE = 0x0245;
    const uint PT_TOUCH = 2, PT_PEN = 3;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const uint SWP_NOACTIVATE = 0x0010;
    // Barre de titre de Windows 11 : le bouton « plein écran » est déclaré à Windows comme bouton agrandir (HTMAXBUTTON), ce qui affiche les
    // dispositions d'accrochage au survol ; la fenêtre est redimensionnable et accrochable (glisser vers un bord ou le haut de l'écran)
    const int WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCMOUSELEAVE = 0x02A2, WM_SYSCOMMAND = 0x0112;
    const int HTCLIENT = 1, HTMAXBUTTON = 9;
    const int SC_MAXIMIZE = 0xF030;
    const int GWL_STYLE = -16, WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000;
    /// <summary>Le doigt n'a pas de survol : après un appui tactile, la barre garde son fonctionnement tactile habituel pendant ce délai (ms).</summary>
    const long TouchQuietMs = 800;
    /// <summary>Largeur (DIP) des bords de la fenêtre plein écran où le redimensionnement est désactivé : le toucher y va aux touches.</summary>
    const double EdgeDip = 8;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetPointerType(uint pointerId, out uint pointerType);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    readonly MainViewModel _vm;

    /// <summary>Navigation du deck (page, profil), pour l'API locale.</summary>
    public INavigator Navigator => _vm;
    readonly DisplayService _displays;
    bool _full;
    bool _onTop;

    /// <summary>Deck sur le document partagé ; la configuration et le thème sont déjà chargés par <see cref="AppHost.Build"/>.</summary>
    public MainWindow(IServiceProvider services)
    {
        _displays = services.GetRequiredService<DisplayService>();
        _vm = ActivatorUtilities.CreateInstance<MainViewModel>(services, this);   // la fenêtre est le « shell » du ViewModel
        DataContext = _vm;
        InitializeComponent();
        _onTop = _vm.Config.AlwaysOnTop;
        Topmost = _onTop;
        Root.SizeChanged += (_, _) => _vm.Resize(Root.ActualWidth, Root.ActualHeight);
        // Écran branché ou débranché : le deck suit (ou revient sur l'écran principal si le sien disparaît)
        _displays.Changed += () => PlaceOnScreen(displayChanged: true);
        _vm.Navigated += AnimateGridIn;
        // Accrochage, agrandissement par le système : l'état « plein écran » et l'icône suivent la taille réelle de la fenêtre
        SizeChanged += (_, _) => SyncFullFlag();
        LocationChanged += (_, _) => SyncFullFlag();
        StateChanged += (_, _) => _vm.NotifyWindowState();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Dialog)) FocusDialog(); };
        Closed += (_, _) =>
        {
            if (_isEditing) { _isEditing = false; _editing = false; }
            // Les cadres web rangés vivent plus longtemps que leurs vues : fermer le deck les libère (après le déchargement des vues)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, Controls.WebFramePool.Clear);
        };   // une fenêtre fermée en pleine édition ne laisse pas le toucher détourné
    }

    /// <summary>Boîte de dialogue ouverte : le champ de saisie (texte sélectionné) ou le bouton de validation reçoit le focus.</summary>
    void FocusDialog() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
    {
        if (_vm.Dialog is not { } box) return;
        if (box.ShowInput) { DialogInput.Focus(); DialogInput.SelectAll(); }
        else DialogOk.Focus();
    });

    /// <summary>Entrée de la nouvelle page : glissement court dans le sens de la navigation. Purement visuel, la page est déjà affichée.</summary>
    void AnimateGridIn(int direction)
    {
        if (!Motion.Enabled) return;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);
        GridShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(36 * direction, 0, duration) { EasingFunction = ease });
        DeckGrid.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, duration) { EasingFunction = ease });
    }

    // ---------- fenêtre : sans focus, dans la barre des tâches, placée sur l'écran tactile ----------

    IntPtr Hwnd => new WindowInteropHelper(this).Handle;
    IntPtr ZOrder => _onTop ? HWND_TOPMOST : HWND_NOTOPMOST;

    /// <summary>Applique WS_EX_NOACTIVATE et le filtre WM_MOUSEACTIVATE (la fenêtre ne prend jamais le focus), puis place le deck sur son écran.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // NOACTIVATE : toucher l'appli ne retire pas le focus de la fenêtre en cours (les raccourcis lui parviennent)
        _ = SetWindowLong(Hwnd, GWL_EXSTYLE, GetWindowLong(Hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        // Cœur du produit : sans ce style, les raccourcis partiraient vers le deck au lieu de l'application active
        if ((GetWindowLong(Hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) == 0) Log.Error("Style WS_EX_NOACTIVATE non appliqué à la fenêtre du deck");
        _touchOwner = this;
        var source = HwndSource.FromHwnd(Hwnd);
        source?.AddHook(WndProc);
        if (source != null)
        {
            _wpfInput = BindWpfInput(source);
            if (_wpfInput == null) Log.Warn("Entrée interne de WPF introuvable : le défilement tactile peut être limité");
        }
        if (TouchTrace)
        {
            AppContext.TryGetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", out bool ptr);
            Log.Info($"[tactile] pile pointeur WPF activée={ptr}, tablettes={System.Windows.Input.Tablet.TabletDevices.Count}");
        }
        PlaceOnScreen();
    }

    /// <summary>Entrée d'un message dans le traitement d'entrée de WPF (HwndSource.InputFilterMessage, interne).</summary>
    delegate IntPtr InputFilter(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);
    static InputFilter? _wpfInput;

    /// <summary>
    /// WM_MOUSEACTIVATE : jamais d'activation. WM_POINTER d'un doigt ou d'un stylet : retiré du traitement par défaut (DefWindowProc le
    /// promouvrait en souris système, ce qui déplace le curseur) et traité par la fenêtre (MainWindow.Touch.cs). WPF reçoit quand même
    /// le message, si son entrée interne est accessible, pour la manipulation et le défilement. La souris (PT_MOUSE) n'est jamais touchée.
    /// </summary>
    static int _touchLogBudget;

    internal static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Mode édition (panneaux avec saisie de texte) : la fenêtre est activable et WPF traite le toucher lui-même
        // Mode édition : la fenêtre est activable (saisie au clavier) mais le toucher reste traité par le deck, comme hors édition :
        // WPF n'active pas les boutons à partir du toucher (ADR 0013), les panneaux d'édition ne répondraient plus au doigt
        if (msg == WM_MOUSEACTIVATE && _editing) return IntPtr.Zero;
        if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
        if (_touchOwner != null && _touchOwner.HandleCaptionMessage(msg, wParam, lParam, out var captionResult)) { handled = true; return captionResult; }
        if (TouchTrace) TraceInput(msg, wParam);
        if (_touchOwner != null && IsTouchOrPenPointerMessage(msg, wParam, PointerTypeOf))
        {
            if (_touchLogBudget > 0) { _touchLogBudget--; Log.Info($"[tactile] message pointeur 0x{msg:X} édition={_editing}"); }
            if (_wpfInput != null) { bool inner = false; _wpfInput(hwnd, msg, wParam, lParam, ref inner); }
            _touchOwner.OnTouchPointer(msg, wParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---------- barre de titre de Windows 11 : disposition d'accrochage, accrochage par glissement ----------

    long _lastTouchTick = long.MinValue / 2;
    bool _maxHover;

    /// <summary>true si un doigt vient de toucher la fenêtre : le survol (et donc la disposition d'accrochage) n'existe pas au toucher.</summary>
    bool RecentTouch => Environment.TickCount64 - _lastTouchTick < TouchQuietMs;

    /// <summary>
    /// Messages de la barre de titre : le bouton plein écran est annoncé à Windows comme bouton agrandir (au survol de la souris, jamais au doigt), son clic est
    /// traité ici ; l'agrandissement demandé par le système (disposition d'accrochage, Win + Haut) passe par le plein écran du deck ; en plein écran les bords
    /// ne redimensionnent pas (le toucher y va aux touches).
    /// </summary>
    internal bool HandleCaptionMessage(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        switch (msg)
        {
            case WM_NCHITTEST:
            {
                long l = lParam.ToInt64();
                int x = (short)(l & 0xFFFF), y = (short)((l >> 16) & 0xFFFF);
                if (_full && OnWindowEdge(x, y)) { result = new IntPtr(HTCLIENT); return true; }
                if (OnMaxButton(x, y)) { SetMaxHover(true); result = new IntPtr(HTMAXBUTTON); return true; }
                SetMaxHover(false);
                return false;
            }
            case WM_NCMOUSEMOVE:
                if (wParam.ToInt64() != HTMAXBUTTON) SetMaxHover(false);
                return false;
            case WM_NCMOUSELEAVE:
                SetMaxHover(false);
                return false;
            case WM_NCLBUTTONDOWN when wParam.ToInt64() == HTMAXBUTTON:
                return true;   // le clic est traité au relâchement : sans cela Windows suivrait lui-même le bouton et agrandirait la fenêtre à sa façon
            case WM_NCLBUTTONUP when wParam.ToInt64() == HTMAXBUTTON:
                _vm.ToggleFullscreenCommand.Execute(null);
                return true;
            case WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE:
                if (!_full) GoFullscreen(CurrentScreen());   // le plein écran du deck (zone de travail en pixels physiques), pas l'état « agrandie » de WPF
                return true;
            default:
                return false;
        }
    }

    /// <summary>Le point (pixels physiques, écran) est sur le bouton plein écran, qui est visible et pas masqué par un menu ou un dialogue.</summary>
    bool OnMaxButton(int x, int y)
    {
        if (RecentTouch || !MaxButton.IsVisible || _vm.HasDialog || _vm.MenuOpen) return false;
        var origin = MaxButton.PointToScreen(new Point(0, 0));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(MaxButton);
        return new Rect(origin.X, origin.Y, MaxButton.ActualWidth * dpi.DpiScaleX, MaxButton.ActualHeight * dpi.DpiScaleY).Contains(x, y);
    }

    /// <summary>Le point (pixels physiques, écran) est dans la bande de bord de la fenêtre.</summary>
    bool OnWindowEdge(int x, int y)
    {
        if (!GetWindowRect(Hwnd, out var r)) return false;
        int edge = (int)Math.Ceiling(EdgeDip * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
        return x < r.Left + edge || x >= r.Right - edge || y >= r.Bottom - edge || y < r.Top + edge;
    }

    /// <summary>Survol du bouton plein écran : la souris est dans la zone non cliente, WPF ne le voit plus, on allume donc la couche de survol du bouton.</summary>
    void SetMaxHover(bool hover)
    {
        if (_maxHover == hover) return;
        _maxHover = hover;
        if (MaxButton.Template?.FindName("Press", MaxButton) is not UIElement press) return;
        if (hover) press.Opacity = 0.08; else press.ClearValue(OpacityProperty);
    }

    /// <summary>
    /// L'état « plein écran » suit la taille réelle : la fenêtre qui occupe exactement la zone de travail de son écran est en plein écran, toute autre
    /// taille (accrochage à une moitié, redimensionnement à la main) ne l'est plus.
    /// </summary>
    void SyncFullFlag()
    {
        if (Hwnd == IntPtr.Zero || WindowState != WindowState.Normal || !GetWindowRect(Hwnd, out var r)) return;
        var wa = CurrentScreen().WorkArea;
        bool full = Math.Abs(r.Left - wa.X) <= 2 && Math.Abs(r.Top - wa.Y) <= 2 && Math.Abs(r.Right - r.Left - wa.Width) <= 2 && Math.Abs(r.Bottom - r.Top - wa.Height) <= 2;
        if (full == _full) return;
        _full = full;
        _vm?.NotifyWindowState();
    }

    /// <summary>Accès à HwndSource.InputFilterMessage (membre interne de WPF) ; null si la version de WPF ne l'expose plus.</summary>
    static InputFilter? BindWpfInput(HwndSource source)
    {
        try
        {
            var method = typeof(HwndSource).GetMethod("InputFilterMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return method == null ? null : (InputFilter)Delegate.CreateDelegate(typeof(InputFilter), source, method);
        }
        catch (Exception ex) when (ex is ArgumentException or MissingMethodException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>Type du pointeur (PT_*), ou null si Windows ne le connaît pas.</summary>
    static uint? PointerTypeOf(uint pointerId) => GetPointerType(pointerId, out uint type) ? type : null;

    /// <summary>Vrai pour un WM_POINTER{DOWN,UPDATE,UP} d'un doigt ou d'un stylet (jamais d'une souris ni d'un type inconnu).</summary>
    internal static bool IsTouchOrPenPointerMessage(int msg, IntPtr wParam, Func<uint, uint?> pointerType)
    {
        if (msg is not (WM_POINTERDOWN or WM_POINTERUP or WM_POINTERUPDATE)) return false;
        uint pointerId = (uint)((long)wParam & 0xFFFF);   // GET_POINTERID_WPARAM
        return pointerType(pointerId) is PT_TOUCH or PT_PEN;
    }

    // Diagnostic : STREAMDAK_TOUCHLOG=1 journalise les messages pointeur / souris et la position du curseur (streamdak.log)
    static readonly bool TouchTrace = Environment.GetEnvironmentVariable("STREAMDAK_TOUCHLOG") == "1";

    static void TraceInput(int msg, IntPtr wParam)
    {
        if (msg is 0x0200 or 0x0245) return;   // mouvements : des centaines de lignes qui noient le reste
        string? name = msg switch { 0x0245 => "WM_POINTERUPDATE", 0x0246 => "WM_POINTERDOWN", 0x0247 => "WM_POINTERUP",
            0x0200 => "WM_MOUSEMOVE", 0x0201 => "WM_LBUTTONDOWN", 0x0202 => "WM_LBUTTONUP", 0x0240 => "WM_TOUCH", _ => null };
        if (name == null) return;
        GetCursorPos(out var c);
        string detail = "";
        if (msg is >= 0x0245 and <= 0x0247)
            detail = GetPointerType((uint)((long)wParam & 0xFFFF), out uint t) ? $" type={t}" : " type=?";
        else
            detail = ((long)GetMessageExtraInfo() & 0xFFFFFF00) == 0xFF515700 ? " depuis-tactile" : " souris-reelle";
        Log.Info($"[tactile] {name}{detail} curseur=({c.X},{c.Y})");
    }

    void PlaceOnScreen(bool displayChanged = false)
    {
        var cfg = _vm.Config;
        if (!cfg.Fullscreen) return;
        var target = _displays.ResolveTarget(cfg);
        if (target == null)
        {
            // Pas d'écran cible : au démarrage on reste en fenêtre ; si l'écran vient de disparaître, on revient sur l'écran principal
            // (il reprendra sa place en plein écran dès que l'écran sera rebranché)
            var all = _displays.All();
            if (displayChanged && _full && all.Count > 0) GoWindowed(all.FirstOrDefault(d => d.Primary) ?? all[0]);
            return;
        }
        GoFullscreen(target);
    }

    void GoFullscreen(DisplayInfo s)
    {
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;   // état « agrandie » de WPF : on repasse par la zone de travail exacte
        // Zone de travail (pixels physiques) : le deck occupe tout l'écran sauf la barre des tâches, qui reste visible et cliquable
        var b = s.WorkArea;
        SetWindowPos(Hwnd, ZOrder, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        // Passer sur un écran de densité différente fait redimensionner la fenêtre par Windows (WM_DPICHANGED) :
        // une seconde pose, une fois sur le bon écran, donne la taille exacte
        SetWindowPos(Hwnd, ZOrder, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        _full = true;
        _vm?.NotifyWindowState();
    }

    /// <summary>Passe en fenêtre 1100×650 (DIP) ; avec cursorPx, la barre du haut est placée sous le doigt.</summary>
    void GoWindowed(DisplayInfo s, POINT? cursorPx = null)
    {
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        int w = (int)(1100 * scale), h = (int)(650 * scale);
        var wa = s.WorkArea;
        int x = wa.X + (wa.Width - w) / 2, y = wa.Y + (wa.Height - h) / 2;
        if (cursorPx is { } c) { x = c.X - w / 2; y = c.Y - 12; }
        SetWindowPos(Hwnd, ZOrder, x, y, w, h, SWP_NOACTIVATE);
        _full = false;
        _vm?.NotifyWindowState();
    }

    /// <summary>Écran qui contient la fenêtre (le plus proche si elle est à cheval).</summary>
    DisplayInfo CurrentScreen()
    {
        var all = _displays.All();
        var device = System.Windows.Forms.Screen.FromHandle(Hwnd).DeviceName;
        return all.FirstOrDefault(d => d.Device == device) ?? all.FirstOrDefault(d => d.Primary) ?? all[0];
    }

    void OnDragBarDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleFullscreen(); return; }
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        if (_full)
        {
            // Position de l'appui (doigt ou souris) en pixels physiques : le curseur ne suit pas le toucher
            var at = PointToScreen(e.GetPosition(this));
            GoWindowed(CurrentScreen(), new POINT { X = (int)at.X, Y = (int)at.Y });
        }
        try { DragMove(); } catch (InvalidOperationException) { }
        FullscreenIfDroppedAtTop();
    }

    /// <summary>
    /// Windows 11 agrandit une fenêtre lâchée en haut d'un écran. Si le déplacement natif ne l'a pas fait (fenêtre sans focus, déplacement au doigt), le deck
    /// passe lui-même en plein écran sur l'écran où la souris est lâchée. Sans effet quand la souris n'a pas bougé (toucher).
    /// </summary>
    void FullscreenIfDroppedAtTop()
    {
        if (_full || WindowState != WindowState.Normal || RecentTouch || !GetCursorPos(out var c)) return;
        var screen = _displays.All().FirstOrDefault(d => c.X >= d.Bounds.X && c.X < d.Bounds.X + d.Bounds.Width && c.Y >= d.Bounds.Y && c.Y < d.Bounds.Y + d.Bounds.Height);
        if (screen != null && c.Y <= screen.Bounds.Y + 2) GoFullscreen(screen);
    }

    // ---------- IShell : ce que le ViewModel demande à la fenêtre ----------

    /// <inheritdoc/>
    public bool IsFullscreen => _full || WindowState == WindowState.Maximized;
    /// <inheritdoc/>
    public bool IsOnTop => _onTop;

    /// <inheritdoc/>
    public void NextScreen()
    {
        var all = _displays.All();
        var cur = CurrentScreen();
        var next = all[(all.ToList().FindIndex(s => s.Device == cur.Device) + 1) % all.Count];
        if (_full) GoFullscreen(next); else GoWindowed(next);
    }

    /// <inheritdoc/>
    public void ToggleFullscreen()
    {
        if (IsFullscreen) GoWindowed(CurrentScreen()); else GoFullscreen(CurrentScreen());
    }

    /// <inheritdoc/>
    public void SetOnTop(bool onTop)
    {
        if (_onTop == onTop) return;
        _onTop = onTop;
        Topmost = _onTop;
        SetWindowPos(Hwnd, ZOrder, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE); // NOSIZE | NOMOVE
    }

    /// <inheritdoc/>
    public void Minimize() => WindowState = WindowState.Minimized;

    /// <inheritdoc/>
    public void ReapplyPlacement()
    {
        SetOnTop(_vm.Config.AlwaysOnTop);
        // Plein écran coupé dans les paramètres : on repasse en fenêtre sans attendre le prochain lancement
        if (!_vm.Config.Fullscreen) { if (_full) GoWindowed(CurrentScreen()); }
        else PlaceOnScreen();
    }
}
