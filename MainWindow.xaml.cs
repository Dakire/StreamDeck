using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using TouchDeck.Actions;
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

    // Famille WM_POINTER (Windows 8+) : touch, stylet et souris unifiés
    const int WM_POINTERDOWN   = 0x0246;
    const int WM_POINTERUP     = 0x0247;
    const int WM_POINTERUPDATE = 0x0245;
    // Types de pointeur (POINTER_INPUT_TYPE)
    const uint PT_TOUCH = 2;
    const uint PT_PEN   = 3;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    // GetPointerType : récupère le type du pointeur (touch / pen / mouse) à partir de son identifiant
    [DllImport("user32.dll")] static extern bool GetPointerType(uint pointerId, out uint pointerType);
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
    }

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
        HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        PlaceOnScreen();
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }

        // Bloquer la promotion tactile → souris pour WM_POINTER issus d'un doigt ou d'un stylet.
        // Sans cette interception, DefWindowProc synthétise un WM_MOUSEMOVE qui déplace le curseur
        // avant même que WPF ne reçoive quoi que ce soit. Les vrais messages souris (PT_MOUSE) ne
        // sont pas bloqués : déplacement, clics et survol fonctionnent normalement.
        // WPF continue à traiter le toucher via son propre pipeline Stylus/Manipulation, indépendant
        // de ce chemin Win32. handled = true empêche DefWindowProc : le curseur reste en place.
        if (IsTouchOrPenPointerMessage(msg, wParam, PointerTypeOf)) handled = true;

        return IntPtr.Zero;
    }

    /// <summary>Type du pointeur (PT_*), ou null si Windows ne le connaît pas.</summary>
    static uint? PointerTypeOf(uint pointerId) => GetPointerType(pointerId, out uint type) ? type : null;

    /// <summary>Vrai pour un WM_POINTER{DOWN,UPDATE,UP} d'un doigt ou d'un stylet (jamais d'une souris ni d'un type inconnu).</summary>
    internal static bool IsTouchOrPenPointerMessage(int msg, IntPtr wParam, Func<uint, uint?> pointerType)
    {
        if (msg is not (WM_POINTERDOWN or WM_POINTERUP or WM_POINTERUPDATE)) return false;
        // L'identifiant du pointeur est dans les 16 bits bas de wParam (GET_POINTERID_WPARAM)
        uint pointerId = (uint)((long)wParam & 0xFFFF);
        return pointerType(pointerId) is PT_TOUCH or PT_PEN;
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
        if (_full)
        {
            // Position de l'appui (doigt ou souris) en pixels physiques : le curseur ne suit pas le toucher
            var at = PointToScreen(e.GetPosition(this));
            GoWindowed(CurrentScreen(), new POINT { X = (int)at.X, Y = (int)at.Y });
        }
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    // ---------- IShell : ce que le ViewModel demande à la fenêtre ----------

    /// <inheritdoc/>
    public bool IsFullscreen => _full;
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
        if (_full) GoWindowed(CurrentScreen()); else GoFullscreen(CurrentScreen());
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
