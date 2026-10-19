using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TouchDeck.ViewModels;

namespace TouchDeck;

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
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    readonly MainViewModel _vm;
    bool _full;
    bool _onTop;

    public MainWindow()
    {
        _vm = new MainViewModel(this);   // la configuration et le thème sont déjà chargés par AppServices
        DataContext = _vm;
        InitializeComponent();
        _onTop = _vm.Config.AlwaysOnTop;
        Topmost = _onTop;
        Root.SizeChanged += (_, _) => _vm.Resize(Root.ActualWidth, Root.ActualHeight);
        // Écran branché ou débranché : le deck suit (ou revient sur l'écran principal si le sien disparaît)
        DisplayService.Start();
        DisplayService.Changed += () => PlaceOnScreen(displayChanged: true);
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

    void PlaceOnScreen(bool displayChanged = false)
    {
        var cfg = _vm.Config;
        if (!cfg.Fullscreen) return;
        var target = DisplayService.ResolveTarget(cfg.Monitor);
        if (target == null)
        {
            // Pas d'écran cible : au démarrage on reste en fenêtre ; si l'écran vient de disparaître, on revient sur l'écran principal
            if (displayChanged && _full) GoWindowed(System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]);
            return;
        }
        GoFullscreen(target);
    }

    void GoFullscreen(System.Windows.Forms.Screen s)
    {
        var b = s.Bounds; // pixels physiques
        SetWindowPos(Hwnd, ZOrder, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
        _full = true;
        _vm?.NotifyWindowState();
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
        _vm?.NotifyWindowState();
    }

    System.Windows.Forms.Screen CurrentScreen() => System.Windows.Forms.Screen.FromHandle(Hwnd);

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

    // ---------- IShell : ce que le ViewModel demande à la fenêtre ----------

    public bool IsFullscreen => _full;
    public bool IsOnTop => _onTop;

    public void NextScreen()
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        var cur = CurrentScreen();
        var next = all[(Array.FindIndex(all, s => s.DeviceName == cur.DeviceName) + 1) % all.Length];
        if (_full) GoFullscreen(next); else GoWindowed(next);
    }

    public void ToggleFullscreen()
    {
        if (_full) GoWindowed(CurrentScreen()); else GoFullscreen(CurrentScreen());
    }

    public void SetOnTop(bool onTop)
    {
        if (_onTop == onTop) return;
        _onTop = onTop;
        Topmost = _onTop;
        SetWindowPos(Hwnd, ZOrder, 0, 0, 0, 0, 0x0001 | 0x0002 | SWP_NOACTIVATE); // NOSIZE | NOMOVE
    }

    public void Minimize() => WindowState = WindowState.Minimized;

    public void ReapplyPlacement()
    {
        SetOnTop(_vm.Config.AlwaysOnTop);
        // Plein écran coupé dans les paramètres : on repasse en fenêtre sans attendre le prochain lancement
        if (!_vm.Config.Fullscreen) { if (_full) GoWindowed(CurrentScreen()); }
        else PlaceOnScreen();
    }
}
