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
using StreamDak.ViewModels;

namespace StreamDak;

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

    [StructLayout(LayoutKind.Sequential)]
    struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags, touchMask;
        public RECT rcContact, rcContactRaw;
        public uint orientation, pressure;
    }

    [DllImport("user32.dll")] static extern bool GetPointerInfo(uint pointerId, out POINTER_INFO info);
    [DllImport("user32.dll")] static extern bool GetPointerTouchInfo(uint pointerId, out POINTER_TOUCH_INFO info);
    [DllImport("user32.dll")] static extern bool GetPointerDeviceRects(IntPtr device, out RECT pointerDeviceRect, out RECT displayRect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004;
    const int DoubleTapMs = 450;
    const uint POINTER_FLAG_INCONTACT = 0x0004;

    static MainWindow? _touchOwner;
    internal readonly TapRecognizer _tap = new();
    bool _tapOnBar;
    bool _dragging;
    DeckTileViewModel? _touchDragTile;   // touche saisie au doigt en mode édition
    bool _touchDragging, _touchDraggingTab;
    (int X, int Y) _dragOffset;
    long _lastBarTapTick;
    TabViewModel? _touchDragTab;   // onglet de page tenu par un doigt en mode édition : le lâcher sur un autre onglet change son rang
    Controls.LevelControl? _levelTouch;   // roue ou curseur de niveau audio tenu par un doigt (le doigt est routé vers lui, pas vers le test d'appui)
    uint _levelPointer;
    readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    long _edgeSince;                      // instant d'arrivée du doigt dans la zone d'un bord (0 : hors zone)
    int _edgeSide;                        // -1 gauche, +1 droite
    int _lastDragX;
    DeckTileViewModel? _liftCandidate;    // touche sous le doigt qui sera soulevée par l'appui long (déplacement hors édition)
    DeckTileViewModel? _lifted;           // touche soulevée, doigt encore posé
    ICommand? _liftFallbackEdit;          // relâchée sans avoir glissé, la touche soulevée ouvre l'éditeur de sa case (comportement de l'appui long d'avant)
    ICommand? _pressEdit;                 // édition de la case sous le doigt (appui long), null si la case ne s'édite pas
    readonly DispatcherTimer _longPressTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool _longPressWired;

    /// <summary>Oublie le doigt suivi (fin ou début du mode édition : son relâchement est traité par WPF, pas par le deck).</summary>
    internal void ResetTouchTracking()
    {
        CancelWebTouches();
        _levelTouch?.PointerCancel();
        _levelTouch = null;
        _tap.Cancel();
        _longPressTimer.Stop();
        _pressEdit = null;
        _dragging = false;
        _touchDragTile = null;
        _touchDragging = _touchDraggingTab = false;
        _liftCandidate = _lifted = null;
        _liftFallbackEdit = null;
        StopEdgeTracking();
        EndSwipe(0);
        _vm.CancelLift();
        _vm.EndDrag();
    }

    /// <summary>Échap pendant un déplacement : annulation propre, rien n'est modifié. Retourne true si un déplacement était en cours.</summary>
    internal bool CancelTouchLift()
    {
        if (_lifted == null && !_vm.IsLifting) return false;
        _tap.Cancel();
        ResetTouchTracking();
        return true;
    }


    /// <summary>Vrai tant qu'un doigt est suivi par le deck (posé, pas encore relâché).</summary>
    internal bool IsTouchTracking => _tap.Tracking;

    /// <summary>Un message WM_POINTER d'un doigt ou d'un stylet (déjà retiré du traitement par défaut de Windows).</summary>
    void OnTouchPointer(int msg, IntPtr wParam)
    {
        uint id = (uint)((long)wParam & 0xFFFF);
        if (!GetPointerInfo(id, out var info)) return;
        if (RouteToWebFrame(msg, WebTouchOf(id, info))) { _lastTouchTick = Environment.TickCount64; return; }
        HandleTouch(msg, id, info.ptPixelLocation.X, info.ptPixelLocation.Y, (info.pointerFlags & POINTER_FLAG_INCONTACT) != 0);
    }

    /// <summary>Contact du doigt tel que Windows le décrit (position, drapeaux, zone de contact, pression, surfaces du périphérique), pour la page web.</summary>
    static Controls.WebTouchPoint WebTouchOf(uint id, POINTER_INFO info)
    {
        Int32Rect contact = default, device = default, display = default;
        uint pressure = 0, orientation = 0, mask = 0;
        if (GetPointerTouchInfo(id, out var touch))
        {
            contact = new Int32Rect(touch.rcContact.Left, touch.rcContact.Top, touch.rcContact.Right - touch.rcContact.Left, touch.rcContact.Bottom - touch.rcContact.Top);
            pressure = touch.pressure;
            orientation = touch.orientation;
            mask = touch.touchMask;
        }
        if (GetPointerDeviceRects(info.sourceDevice, out var d, out var s))
        {
            device = new Int32Rect(d.Left, d.Top, d.Right - d.Left, d.Bottom - d.Top);
            display = new Int32Rect(s.Left, s.Top, s.Right - s.Left, s.Bottom - s.Top);
        }
        return new Controls.WebTouchPoint(id, info.frameId, info.pointerFlags, new Point(info.ptPixelLocation.X, info.ptPixelLocation.Y),
            info.ptHimetricLocation.X, info.ptHimetricLocation.Y, info.dwTime, contact, pressure, orientation, mask, device, display);
    }

    /// <summary>Doigts posés sur une page web active : chacun appartient à son cadre jusqu'au relâchement (défilement, pincement à deux doigts).</summary>
    readonly System.Collections.Generic.Dictionary<uint, (Controls.DeckWebView Frame, Controls.WebTouchPoint Last)> _webTouches = new();

    /// <summary>
    /// Doigt sur une page web en interaction (cadre activé) : il est transmis à la page comme un vrai toucher, avec les données de Windows, et ne passe ni par
    /// le test d'appui, ni par l'appui long, ni par le balayage des pages (la page garde son défilement). Retourne true si le message a été transmis.
    /// Par défaut la page est toujours en interaction : le premier toucher lui va (ADR 0037). En mode « toucher pour activer », au repos, le bouclier transparent
    /// reçoit ce premier toucher (ADR 0032). Édition, menu ou dialogue ouverts : rien n'est transmis.
    /// </summary>
    internal bool RouteToWebFrame(int msg, Controls.WebTouchPoint p)
    {
        if (_webTouches.TryGetValue(p.Id, out var held))
        {
            held.Frame.Forward(msg, p);
            if (msg == WM_POINTERUP) _webTouches.Remove(p.Id);
            else _webTouches[p.Id] = (held.Frame, p);
            return true;
        }
        if (msg != WM_POINTERDOWN || !Controls.DeckWebView.CanForwardTouch || _vm.IsEdit || _vm.MenuOpen || _vm.HasDialog) return false;
        if (WebFrameAt((int)p.Screen.X, (int)p.Screen.Y) is not { } web || !web.Forward(msg, p)) return false;
        _webTouches[p.Id] = (web, p);
        return true;
    }

    /// <summary>Doigts encore posés sur une page web : la page reçoit un relâchement annulé (passage en édition, fenêtre fermée), rien ne reste « appuyé ».</summary>
    void CancelWebTouches()
    {
        foreach (var (id, (frame, last)) in _webTouches)
            frame.Forward(WM_POINTERUP, last with { Flags = (last.Flags & ~POINTER_FLAG_INCONTACT) | 0x8000 | 0x40000 });   // CANCELED | UP
        _webTouches.Clear();
    }

    /// <summary>Cadre web activé (en interaction, affiché) sous le point (pixels écran), ou null.</summary>
    Controls.DeckWebView? WebFrameAt(int x, int y)
    {
        for (DependencyObject? d = HitVisual(x, y); d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is not Controls.WebWidgetView view) continue;
            if (view.Frame is not { IsEnabled: true, IsVisible: true } web) return null;
            var at = web.PointFromScreen(new Point(x, y));
            return at.X >= 0 && at.Y >= 0 && at.X < web.ActualWidth && at.Y < web.ActualHeight ? web : null;
        }
        return null;
    }

    /// <summary>true si ce doigt touche encore l'écran (Windows peut annuler un toucher sans relâchement : geste système, rejet de paume).</summary>
    internal Func<uint, bool> PointerInContact { get; set; } = id => GetPointerInfo(id, out var info) && (info.pointerFlags & POINTER_FLAG_INCONTACT) != 0;

    /// <summary>
    /// Traitement d'un doigt (pixels physiques, écran) : contrôles de niveau et poignées d'abord, puis balayage de la grille, appui, appui long, glissés.
    /// Séparé de la lecture du message pour que les tests puissent rejouer un geste hors écran (<paramref name="nowMs"/> : horloge du geste, sinon celle du système).
    /// </summary>
    internal void HandleTouch(int msg, uint id, int x, int y, bool inContact, long? nowMs = null)
    {
        if (!_longPressWired)
        {
            _longPressWired = true;
            _longPressTimer.Tick += (_, _) => OnLongPressTick();
            _edgeTimer.Tick += (_, _) => OnEdgeTick();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && CancelTouchLift()) e.Handled = true; };   // Échap annule un déplacement en cours
        }
        long now = nowMs ?? Environment.TickCount64;
        _lastTouchTick = Environment.TickCount64;
        if (RouteToLevelControl(msg, id, x, y, inContact)) return;
        if (RouteToResizeHandle(msg, x, y)) return;
        if (RouteToSwipe(msg, id, x, y, now)) return;
        switch (msg)
        {
            case WM_POINTERDOWN:
                // Un doigt suivi qui ne touche plus l'écran (relâchement jamais reçu) ne doit pas bloquer les appuis suivants
                if (_tap.Tracking && _tap.TrackedId is { } old && (old == id || !PointerInContact(old)))
                    ResetTouchTracking();
                // Un toucher sur le cadre actif lui a été transmis plus haut : celui-ci est ailleurs. Hors de la tuile web, la page redevient passive
                if (!IsInsideWebTile(HitVisual(x, y))) _vm.EndWebInteractions();
                bool tracked = _tap.Tracking;
                BeginSwipe(id, x, y, now, tracked);
                _tap.Slop = (int)Math.Ceiling(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
                _tap.Down(id, x, y, now);
                _tapOnBar = Hit(x, y).bar;
                _dragging = false;
                _touchDragging = _touchDraggingTab = false;
                _touchDragTile = _vm.IsEdit && !tracked ? TileAt(HitVisual(x, y)) as DeckTileViewModel : null;   // glisser une touche pour la déplacer
                _touchDragTab = _vm.IsEdit && !tracked && _touchDragTile == null ? TabAt(HitVisual(x, y)) : null;
                // Appui long : seulement pour le doigt suivi (un second doigt est ignoré), sur une case qui s'édite
                _pressEdit = tracked ? null : EditTargetAt(HitVisual(x, y));
                // Hors édition, appui long sur un bouton : il se soulève (seuil réglable) au lieu d'ouvrir l'éditeur tout de suite
                _liftCandidate = tracked ? null : TileAt(HitVisual(x, y)) as DeckTileViewModel;
                if (_liftCandidate != null && _vm.CanLift(_liftCandidate)) _tap.LongPressThresholdMs = _vm.LiftDelayMs; else _liftCandidate = null;
                if (_pressEdit != null || _liftCandidate != null) _longPressTimer.Start(); else _longPressTimer.Stop();
                break;
            case WM_POINTERUPDATE:
                if (_tap.Move(id, x, y))
                {
                    _longPressTimer.Stop();   // le doigt glisse : défilement ou déplacement, pas un appui long
                    if (_tapOnBar) DragBarTo(x, y);
                    if (_touchDragTile != null)
                    {
                        if (!_touchDragging) _vm.BeginDrag(_touchDragTile);
                        _touchDragging = true;
                    }
                    if (_touchDragging)
                    {
                        var at = Root.PointFromScreen(new Point(x, y));
                        _vm.UpdateDrag(at.X, at.Y, TileAt(HitVisual(x, y)));
                        if (_lifted != null) { _vm.IsOverTrash = IsOverTrashZone(x, y); TrackEdge(x); }
                    }
                    if (_touchDragTab != null) _touchDraggingTab = true;
                }
                break;
            case WM_POINTERUP:
                _longPressTimer.Stop();
                if (_touchDraggingTab && _touchDragTab is { } movedTab)
                {
                    // Doigt relâché après un glissement depuis un onglet : la page prend le rang de l'onglet sous le doigt
                    var dropTab = TabAt(HitVisual(x, y));
                    _tap.Up(id, x, y);
                    _touchDraggingTab = false;
                    _touchDragTab = null;
                    if (dropTab != null && dropTab.Index != movedTab.Index) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.MoveTab(movedTab.Index, dropTab.Index));
                    break;
                }
                _touchDragTab = null;
                bool wasDragging = _dragging;
                _dragging = false;
                if (_lifted is { } lifted)
                {
                    // Touche soulevée (appui long hors édition) : déposée sur une case, un onglet ou la corbeille ; relâchée ailleurs, le geste est annulé sans rien modifier
                    StopEdgeTracking();
                    bool moved = _touchDragging;
                    bool trash = moved && IsOverTrashZone(x, y);
                    var dropAt = moved ? TileAt(HitVisual(x, y)) : null;
                    var dropTab = moved ? TabAt(HitVisual(x, y)) : null;
                    var fallback = _liftFallbackEdit;
                    _tap.Up(id, x, y);
                    _touchDragging = false;
                    _touchDragTile = _lifted = null;
                    _liftFallbackEdit = null;
                    if (!moved) { _vm.CancelLift(); if (fallback != null && fallback.CanExecute(null)) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => fallback.Execute(null)); }
                    else if (trash) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.DropOnTrash(lifted));
                    else if (dropAt != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.DropLifted(lifted, dropAt));
                    else if (dropTab != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.DropLiftedOnPage(lifted, dropTab.Index));
                    else _vm.CancelLift();
                    break;
                }
                if (_touchDragging && _touchDragTile is { } dragged)
                {
                    // Doigt relâché après un glissement depuis une touche : déposée sur la case sous le doigt
                    var dropAt = TileAt(HitVisual(x, y));
                    var dropTab = TabAt(HitVisual(x, y));
                    _tap.Up(id, x, y);
                    _touchDragging = false;
                    _touchDragTile = null;
                    _vm.EndDrag();
                    if (dropAt != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.MoveTile(dragged, dropAt));
                    else if (dropTab != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _vm.MoveTileToPage(dragged, dropTab.Index));   // lâchée sur un onglet : change de page
                    break;
                }
                if (!_tap.Up(id, x, y) || wasDragging) break;   // un appui long consommé n'est pas un appui : l'action ne s'exécute pas
                var (target, bar, binding) = Hit(x, y);
                var field = FieldAt(HitVisual(x, y));
                if (TouchTrace)
                    Log.Info($"[tactile] appui ({x},{y}) visuel={Chain(HitVisual(x, y))} bouton={target?.GetType().Name ?? "-"} actif={target?.IsEnabled} liaison={binding != null} barre={bar} menu={_vm.MenuOpen} édition={_vm.IsEdit} dialogue={_vm.HasDialog}");
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

    readonly SwipeGesture _swipe = new();
    uint _swipeId;

    /// <summary>
    /// Un doigt se pose : il peut devenir un balayage s'il est sur la grille (pas sur un cadre web, qui garde son défilement), hors édition, sans menu ni
    /// dialogue, et si c'est le seul doigt suivi. Le geste reste « indécis » tant qu'il bouge peu : l'appui et l'appui long gardent leur comportement.
    /// </summary>
    void BeginSwipe(uint id, int x, int y, long now, bool tracked)
    {
        _swipe.Cancel();
        if (tracked || !_vm.CanSwipe || !IsOverDeckGrid(x, y) || IsInsideWebTile(HitVisual(x, y))) return;
        var at = Root.PointFromScreen(new Point(x, y));   // la grille elle-même est décalée pendant le geste : repère fixe de la fenêtre
        _swipe.Begin(at.X, at.Y, now, DeckGrid.ActualWidth, _vm.CanSwipeBack);
        _swipeId = id;
    }

    /// <summary>
    /// Doigt du balayage : tant qu'il est indécis, le reste du traitement continue ; reconnu, la page suit le doigt, et la touche d'origine ne fait rien
    /// (ni action, ni appui long, ni menu). Retourne true si le message a été traité ici.
    /// </summary>
    bool RouteToSwipe(int msg, uint id, int x, int y, long now)
    {
        if (!_swipe.Active || id != _swipeId || msg == WM_POINTERDOWN) return false;
        var at = Root.PointFromScreen(new Point(x, y));
        if (msg == WM_POINTERUPDATE)
        {
            bool was = _swipe.State is SwipeState.Swiping or SwipeState.SwipingDown;
            var state = _swipe.Move(at.X, at.Y, now);
            if (state is not (SwipeState.Swiping or SwipeState.SwipingDown)) return false;
            if (!_vm.CanSwipe) { EndSwipe(0); return false; }   // un menu s'est ouvert entre-temps
            if (!was)
            {
                // Balayage reconnu : l'état « touche enfoncée » est abandonné proprement (ni action, ni appui long, ni menu)
                _tap.Cancel();
                _longPressTimer.Stop();
                _pressEdit = null;
                _liftCandidate = null;
                _touchDragTile = null;
                _tapOnBar = false;
            }
            if (state == SwipeState.Swiping) ShowSwipeOffset(_swipe.DeltaX);
            return true;
        }
        if (msg != WM_POINTERUP) return false;
        bool swiping = _swipe.State is SwipeState.Swiping or SwipeState.SwipingDown;
        int direction = _swipe.End(at.X, at.Y, now);
        if (!swiping) return false;
        if (direction == SwipeGesture.Back) { _swipe.Cancel(); _vm.SwipeBack(); return true; }
        EndSwipe(direction);
        return true;
    }

    /// <summary>true si le point (pixels écran) est dans le rectangle de la grille des touches (espaces entre les touches compris).</summary>
    bool IsOverDeckGrid(int x, int y)
    {
        if (!DeckGrid.IsVisible || DeckGrid.ActualWidth <= 0) return false;
        var at = Root.PointFromScreen(new Point(x, y));
        var origin = DeckGrid.TranslatePoint(new Point(0, 0), Root);
        var bounds = new Rect(new Point(origin.X - GridShift.X, origin.Y), new Size(DeckGrid.ActualWidth, DeckGrid.ActualHeight));
        return bounds.Contains(at);
    }

    /// <summary>Distance d'entrée de la page après un balayage (DIP) : l'animation d'entrée part de plus loin que pour un toucher d'onglet.</summary>
    double _swipeEntryOffset;

    /// <summary>La grille suit le doigt (avec résistance s'il n'y a pas de page dans ce sens). Sans animations, la grille ne bouge pas pendant le geste.</summary>
    void ShowSwipeOffset(double dx)
    {
        if (!Motion.Enabled) return;
        GridShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        GridShift.X = SwipeGesture.Follow(dx, DeckGrid.ActualWidth, _vm.HasSwipeTarget(-Math.Sign(dx)));
    }

    /// <summary>
    /// Fin du balayage : change de page par la commande des onglets (la nouvelle page entre du côté où va le doigt), ou revient en place, avec un rebond élastique
    /// au bout de la liste. <paramref name="direction"/> 0 : rien ne change.
    /// </summary>
    void EndSwipe(int direction)
    {
        _swipe.Cancel();
        if (direction != 0)
        {
            _swipeEntryOffset = Math.Clamp(Math.Abs(GridShift.X), 36, 160);
            bool moved = _vm.SwipePage(direction);
            _swipeEntryOffset = 0;
            if (moved) return;
        }
        if (GridShift.X == 0 && !GridShift.HasAnimatedProperties) return;
        if (!Motion.Enabled) { GridShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null); GridShift.X = 0; return; }
        bool edge = !_vm.HasSwipeTarget(-Math.Sign(GridShift.X));
        System.Windows.Media.Animation.IEasingFunction ease = edge
            ? new System.Windows.Media.Animation.ElasticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }
            : new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        double from = GridShift.X;
        GridShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        GridShift.X = 0;   // valeur de base à 0 : l'animation part du décalage du doigt et y revient
        GridShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(edge ? 380 : 200)) { EasingFunction = ease });
    }

    bool _touchResizing;

    /// <summary>
    /// Poignée de coin d'un widget sélectionné (mode édition) : le doigt posé dessus lui appartient jusqu'au relâchement, la taille suit le doigt sur la grille ;
    /// ni appui, ni appui long, ni glissement de touche. Retourne true si le message a été traité ici.
    /// </summary>
    bool RouteToResizeHandle(int msg, int x, int y)
    {
        if (_touchResizing)
        {
            if (msg == WM_POINTERUPDATE) ResizeTo(DeckGrid.PointFromScreen(new Point(x, y)));
            else if (msg == WM_POINTERUP) { _touchResizing = false; _longPressTimer.Stop(); }
            return true;
        }
        if (msg != WM_POINTERDOWN || !_vm.IsEdit || !IsResizeHandle(HitVisual(x, y), out var cell)) return false;
        _touchResizing = true;
        _resizeCell = cell;
        _longPressTimer.Stop();
        return true;
    }

    /// <summary>
    /// Roue ou curseur de niveau audio : un doigt posé dessus lui appartient jusqu'à son relâchement (rotation continue, pas d'appui long, pas de test d'appui).
    /// Retourne true si le message a été traité ici. Un second doigt pendant le geste est ignoré.
    /// </summary>
    bool RouteToLevelControl(int msg, uint id, int x, int y, bool inContact)
    {
        if (_levelTouch != null && id != _levelPointer)
        {
            // Un autre doigt : ignoré tant que le premier tient le contrôle ; si le premier a été perdu (relâchement jamais reçu), le contrôle est libéré
            if (GetPointerInfo(_levelPointer, out var held) && (held.pointerFlags & POINTER_FLAG_INCONTACT) != 0) return true;
            _levelTouch.PointerCancel();
            _levelTouch = null;
        }
        if (_levelTouch == null)
        {
            if (msg != WM_POINTERDOWN || _vm.IsEdit || _vm.MenuOpen || _vm.HasDialog || _tap.Tracking) return false;
            if (LevelControlAt(HitVisual(x, y)) is not { IsEnabled: true } level) return false;
            _levelTouch = level;
            _levelPointer = id;
            _longPressTimer.Stop();
            _pressEdit = null;
            level.PointerDown(level.PointFromScreen(new Point(x, y)));
            return true;
        }
        var at = _levelTouch.PointFromScreen(new Point(x, y));
        if (msg == WM_POINTERUPDATE && inContact) _levelTouch.PointerMove(at);
        else if (msg == WM_POINTERUP || !inContact)
        {
            _levelTouch.PointerUp(at);
            _levelTouch = null;
        }
        return true;
    }

    /// <summary>Largeur (DIP) de la zone de bord qui change de page, et délais : première bascule après 500 ms, puis une page toutes les 900 ms tant que le doigt reste.</summary>
    const double EdgeZoneDip = 72;
    const long EdgeFirstMs = 500, EdgeRepeatMs = 900;

    /// <summary>Le doigt qui tient un bouton soulevé bouge : note sa position et démarre le suivi d'un bord si besoin.</summary>
    void TrackEdge(int x)
    {
        _lastDragX = x;
        if (!_edgeTimer.IsEnabled) _edgeTimer.Start();
    }

    void StopEdgeTracking()
    {
        _edgeTimer.Stop();
        _edgeSince = 0;
        _edgeSide = 0;
    }

    /// <summary>Côté de la zone de bord où se trouve le doigt (-1, 0, +1), d'après sa position en pixels écran.</summary>
    internal static int EdgeSide(double xDip, double widthDip) => xDip <= EdgeZoneDip ? -1 : xDip >= widthDip - EdgeZoneDip ? 1 : 0;

    /// <summary>Bouton soulevé tenu près d'un bord : change de page après un court délai, puis régulièrement tant que le doigt y reste.</summary>
    void OnEdgeTick()
    {
        if (_lifted == null && !_vm.IsLifting) { StopEdgeTracking(); return; }
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var at = Root.PointFromScreen(new Point(_lastDragX, 0));
        int side = EdgeSide(at.X, Root.ActualWidth);
        long now = Environment.TickCount64;
        if (side == 0 || side != _edgeSide) { _edgeSide = side; _edgeSince = side == 0 ? 0 : now; return; }
        if (now - _edgeSince < (_edgeFlipped ? EdgeRepeatMs : EdgeFirstMs)) return;
        _edgeSince = now;
        _edgeFlipped = true;
        _vm.FlipPageWhileLifting(side);
    }

    bool _edgeFlipped;

    /// <summary>true si le point (pixels écran) est sur la corbeille proposée pendant un déplacement hors édition.</summary>
    bool IsOverTrashZone(int x, int y)
    {
        if (!_vm.ShowTrash || !TrashZone.IsVisible) return false;
        var local = TrashZone.PointFromScreen(new Point(x, y));
        return local.X >= 0 && local.Y >= 0 && local.X <= TrashZone.ActualWidth && local.Y <= TrashZone.ActualHeight;
    }

    /// <summary>true si l'élément est dans la tuile d'une page web (bande de commandes comprise).</summary>
    internal static bool IsInsideWebTile(DependencyObject? hit)
    {
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Controls.WebWidgetView) return true;
        return false;
    }

    /// <summary>Contrôle de niveau audio qui contient cet élément, ou null.</summary>
    static Controls.LevelControl? LevelControlAt(DependencyObject? hit)
    {
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Controls.LevelControl level) return level;
        return null;
    }

    /// <summary>Le doigt est resté immobile assez longtemps sur une case : l'éditeur s'ouvre dessus (une seule fois par appui).</summary>
    void OnLongPressTick()
    {
        if (!_tap.Tracking) { _longPressTimer.Stop(); return; }
        // Toucher annulé par Windows (geste système, rejet de paume) : aucun relâchement n'arrive, l'appui long ne doit pas se déclencher doigt levé
        if (_tap.TrackedId is { } held && !PointerInContact(held))
        {
            ResetTouchTracking();
            return;
        }
        if (!_tap.TryLongPress(Environment.TickCount64)) return;
        _longPressTimer.Stop();
        var edit = _pressEdit;
        _pressEdit = null;
        _swipe.Cancel();   // doigt tenu : appui long, plus de balayage
        if (_liftCandidate != null && _vm.Lift(_liftCandidate))
        {
            // La touche se soulève ; si le doigt se relève sans glisser, l'éditeur de la case s'ouvre alors (voir le relâchement)
            _lifted = _touchDragTile = _liftCandidate;
            _liftCandidate = null;
            _liftFallbackEdit = edit;
            return;
        }
        if (edit != null && edit.CanExecute(null)) edit.Execute(null);
    }

    /// <summary>Élément visuel sous le point (pixels écran), ou null.</summary>
    DependencyObject? HitVisual(int x, int y)
    {
        DependencyObject? hit = null;
        // Un élément invisible n'intercepte jamais un appui ; le fond du menu (seul élément à MouseBinding) ne l'intercepte que menu ouvert
        VisualTreeHelper.HitTest(this,
            d => d is UIElement ue && (!ue.IsVisible || (!_vm.MenuOpen && ue.InputBindings.OfType<MouseBinding>().Any()))
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            r => { hit = r.VisualHit; return HitTestResultBehavior.Stop; },
            new PointHitTestParameters(PointFromScreen(new Point(x, y))));
        return hit;
    }

    /// <summary>
    /// Commande d'édition de la case qui contient cet élément (bouton, case vide, emplacement « + »), ou null : barre, dock, menu,
    /// flèche « retour ». Le contexte de données d'un élément de la case est la case elle-même.
    /// </summary>
    internal static ICommand? EditTargetAt(DependencyObject? hit) => TileAt(hit)?.Edit ?? TabAt(hit)?.Edit;

    /// <summary>Onglet de page qui contient cet élément, ou null.</summary>
    internal static TabViewModel? TabAt(DependencyObject? hit)
    {
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: TabViewModel tab }) return tab;
        return null;
    }

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
        if (TouchTrace) Log.Info($"[tactile] clic droit synthétique={IsSyntheticFromTouch()} source={e.OriginalSource?.GetType().Name} menu={_vm.MenuOpen}");
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
        var hit = HitVisual(x, y);
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

    /// <summary>Types des 8 premiers ancêtres visuels d'un élément (diagnostic).</summary>
    static string Chain(DependencyObject? d)
    {
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; d != null && i < 8; i++, d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d)) names.Add(d.GetType().Name);
        return names.Count == 0 ? "aucun" : string.Join("<", names);
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
