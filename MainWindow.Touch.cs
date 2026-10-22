using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TouchDeck.ViewModels;

namespace TouchDeck;

/// <summary>
/// Toucher du deck sans souris système. WPF n'active pas les boutons à partir du toucher : il attend la souris synthétique de Windows,
/// qui déplace le curseur. Le deck avale donc les WM_POINTER du doigt (voir WndProc) et traite lui-même l'appui : bouton visé trouvé
/// par test de collision, activé par l'automatisation WPF (même chemin qu'un clic, commande comprise), barre du haut déplaçable
/// au doigt, double appui sur la barre = plein écran. Un appui long (immobile) sur un bouton ou une case vide ouvre l'éditeur sur
/// cette case, sans exécuter l'action ; le clic droit de la souris fait de même (voir <see cref="OnDeckRightClick"/>).
/// </summary>
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_INFO
    {
        public uint pointerType, pointerId, frameId, pointerFlags;
        public IntPtr sourceDevice, hwndTarget;
        public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int inputData;
        public uint dwKeyStates;
        public ulong performanceCount;
        public int buttonChangeType;
    }

    [DllImport("user32.dll")] static extern bool GetPointerInfo(uint pointerId, out POINTER_INFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004;
    const int DoubleTapMs = 450;
    const uint POINTER_FLAG_INCONTACT = 0x0004;

    static MainWindow? _touchOwner;
    internal readonly TapRecognizer _tap = new();
    bool _tapOnBar;
    bool _dragging;
    (int X, int Y) _dragOffset;
    long _lastBarTapTick;
    ICommand? _pressEdit;                 // édition de la case sous le doigt (appui long), null si la case ne s'édite pas
    readonly DispatcherTimer _longPressTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool _longPressWired;

    /// <summary>Oublie le doigt suivi (fin ou début du mode édition : son relâchement est traité par WPF, pas par le deck).</summary>
    internal void ResetTouchTracking()
    {
        _tap.Cancel();
        _longPressTimer.Stop();
        _pressEdit = null;
        _dragging = false;
    }

    /// <summary>Vrai tant qu'un doigt est suivi par le deck (posé, pas encore relâché).</summary>
    internal bool IsTouchTracking => _tap.Tracking;

    /// <summary>Un message WM_POINTER d'un doigt ou d'un stylet (déjà retiré du traitement par défaut de Windows).</summary>
    void OnTouchPointer(int msg, IntPtr wParam)
    {
        if (!_longPressWired)
        {
            _longPressWired = true;
            _longPressTimer.Tick += (_, _) => OnLongPressTick();
        }
        uint id = (uint)((long)wParam & 0xFFFF);
        if (!GetPointerInfo(id, out var info)) return;
        int x = info.ptPixelLocation.X, y = info.ptPixelLocation.Y;
        switch (msg)
        {
            case WM_POINTERDOWN:
                // Un doigt suivi qui ne touche plus l'écran (relâchement jamais reçu) ne doit pas bloquer les appuis suivants
                if (_tap.Tracking && _tap.TrackedId is { } old && (old == id || !(GetPointerInfo(old, out var oldInfo) && (oldInfo.pointerFlags & POINTER_FLAG_INCONTACT) != 0)))
                    ResetTouchTracking();
                bool tracked = _tap.Tracking;
                _tap.Slop = (int)Math.Ceiling(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
                _tap.Down(id, x, y, Environment.TickCount64);
                _tapOnBar = Hit(x, y).bar;
                _dragging = false;
                // Appui long : seulement pour le doigt suivi (un second doigt est ignoré), sur une case qui s'édite
                _pressEdit = tracked ? null : EditTargetAt(HitVisual(x, y));
                if (_pressEdit != null) _longPressTimer.Start(); else _longPressTimer.Stop();
                break;
            case WM_POINTERUPDATE:
                if (_tap.Move(id, x, y))
                {
                    _longPressTimer.Stop();   // le doigt glisse : défilement ou déplacement, pas un appui long
                    if (_tapOnBar) DragBarTo(x, y);
                }
                break;
            case WM_POINTERUP:
                _longPressTimer.Stop();
                bool wasDragging = _dragging;
                _dragging = false;
                if (!_tap.Up(id, x, y) || wasDragging) break;   // un appui long consommé n'est pas un appui : l'action ne s'exécute pas
                var (target, bar, binding) = Hit(x, y);
                var field = FieldAt(HitVisual(x, y));
                if (TouchTrace)
                    Log.Info($"[tactile] appui ({x},{y}) visuel={HitVisual(x, y)?.GetType().Name ?? "aucun"} bouton={target?.GetType().Name ?? "-"} actif={target?.IsEnabled} liaison={binding != null} barre={bar} menu={_vm.MenuOpen} édition={_vm.IsEdit} dialogue={_vm.HasDialog}");
                // L'action part APRÈS le message : une commande qui ouvre une boîte de dialogue (boucle modale) ou démarre un traitement
                // ne doit pas tourner dans le WM_POINTERUP, sinon Windows n'a pas fini de traiter ce toucher et le deck semble figé.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (field != null) field.Focus();   // champ de saisie des panneaux d'édition : le toucher lui donne le focus
                    else if (target != null) { if (target.IsEnabled) Activate(target); }
                    else if (binding != null) { if (binding.CanExecute(null)) binding.Execute(null); }
                    else if (bar) OnBarTap();
                });
                break;
        }
    }

    /// <summary>Le doigt est resté immobile assez longtemps sur une case : l'éditeur s'ouvre dessus (une seule fois par appui).</summary>
    void OnLongPressTick()
    {
        if (!_tap.Tracking) { _longPressTimer.Stop(); return; }
        if (!_tap.TryLongPress(Environment.TickCount64)) return;
        _longPressTimer.Stop();
        var edit = _pressEdit;
        _pressEdit = null;
        if (edit != null && edit.CanExecute(null)) edit.Execute(null);
    }

    /// <summary>Élément visuel sous le point (pixels écran), ou null.</summary>
    DependencyObject? HitVisual(int x, int y) => VisualTreeHelper.HitTest(this, PointFromScreen(new Point(x, y)))?.VisualHit;

    /// <summary>
    /// Commande d'édition de la case qui contient cet élément (bouton, case vide, emplacement « + »), ou null : barre, dock, menu,
    /// flèche « retour ». Le contexte de données d'un élément de la case est la case elle-même.
    /// </summary>
    internal static ICommand? EditTargetAt(DependencyObject? hit) => TileAt(hit)?.Edit;

    /// <summary>Case (bouton, case vide, emplacement « + », flèche « retour ») qui contient cet élément, ou null.</summary>
    internal static IEditableTile? TileAt(DependencyObject? hit)
    {
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: IEditableTile tile }) return tile;
        return null;
    }

    /// <summary>
    /// Clic droit de la souris sur un bouton ou une case vide : l'éditeur s'ouvre directement dessus. Le clic droit que Windows fabrique
    /// depuis un appui long du doigt est ignoré (l'appui long est déjà traité par <see cref="OnLongPressTick"/>) : pas de double ouverture.
    /// </summary>
    void OnDeckRightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsSyntheticFromTouch()) { e.Handled = true; return; }
        if (EditTargetAt(e.OriginalSource as DependencyObject) is { } edit && edit.CanExecute(null))
        {
            edit.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Vrai si le message souris en cours est fabriqué par Windows à partir du toucher (signature 0xFF515700 dans les informations supplémentaires).</summary>
    static bool IsSyntheticFromTouch() => ((long)GetMessageExtraInfo() & 0xFFFFFF00) == 0xFF515700;

    /// <summary>Élément activable sous le point (pixels écran) : bouton, commande d'un MouseBinding, ou barre du haut.</summary>
    (ButtonBase? button, bool bar, ICommand? binding) Hit(int x, int y)
    {
        var p = PointFromScreen(new Point(x, y));
        var hit = VisualTreeHelper.HitTest(this, p)?.VisualHit;
        bool bar = false;
        ICommand? binding = null;
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase { IsEnabled: true } b) return (b, false, null);
            if (d == DragBar) bar = true;
            if (binding == null && d is UIElement ue)
                binding = ue.InputBindings.OfType<MouseBinding>().FirstOrDefault(m => m.MouseAction == MouseAction.LeftClick)?.Command;
        }
        return (null, bar, binding);
    }

    /// <summary>Champ de saisie qui contient cet élément, ou null.</summary>
    static TextBoxBase? FieldAt(DependencyObject? hit)
    {
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBoxBase { IsEnabled: true } box) return box;
        return null;
    }

    /// <summary>Active un bouton comme le ferait un clic (commande, bascule), sans événement souris.</summary>
    static void Activate(ButtonBase button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
    }

    /// <summary>Double appui sur la barre : plein écran / fenêtre.</summary>
    void OnBarTap()
    {
        long now = Environment.TickCount64;
        bool second = now - _lastBarTapTick <= DoubleTapMs;
        _lastBarTapTick = second ? 0 : now;
        if (second) ToggleFullscreen();
    }

    /// <summary>Déplace la fenêtre sous le doigt depuis la barre du haut (sort du plein écran au premier mouvement).</summary>
    void DragBarTo(int x, int y)
    {
        if (!_dragging)
        {
            if (_full) GoWindowed(CurrentScreen(), new POINT { X = x, Y = y });
            GetWindowRect(Hwnd, out var r);
            _dragOffset = (x - r.Left, y - r.Top);
            _dragging = true;
        }
        SetWindowPos(Hwnd, IntPtr.Zero, x - _dragOffset.X, y - _dragOffset.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
