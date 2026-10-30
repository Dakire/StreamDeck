using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StreamDak.ViewModels;

namespace StreamDak;

/// <summary>
/// Glisser-déposer sur la grille du deck (fichier, raccourci, exécutable, dossier ou adresse web) : seule une case vide accepte le dépôt.
/// Le dépôt OLE ne demande pas l'activation de la fenêtre : le deck ne prend toujours pas le focus.
/// </summary>
public partial class MainWindow
{
    /// <summary>Case de la grille sous le pointeur d'un glisser-déposer, ou null.</summary>
    IEditableTile? TileUnder(DragEventArgs e) =>
        TileAt(VisualTreeHelper.HitTest(DeckGrid, e.GetPosition(DeckGrid))?.VisualHit);

    const string TileFormat = "StreamDak.Tile";
    Point _dragStart;
    DeckTileViewModel? _dragSource;

    // ---------- redimensionnement d'un widget par sa poignée de coin ----------

    bool _resizing;
    int _resizeCell;

    /// <summary>La poignée de redimensionnement (marque « ResizeHandle ») qui contient cet élément, et la case d'ancrage de son widget.</summary>
    internal static bool IsResizeHandle(DependencyObject? hit, out int cell)
    {
        cell = -1;
        for (DependencyObject? d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Tag: "ResizeHandle", DataContext: DeckTileViewModel tile })
            {
                cell = tile.Cell;
                return tile.CanResize;
            }
        return false;
    }

    /// <summary>Taille en cases du widget ancré à <paramref name="cell"/> pour un pointeur en <paramref name="p"/> (repère de la grille) : sa case la plus basse à droite est celle du pointeur.</summary>
    internal static (int Cols, int Rows) SizeAt(Point p, int cell, double gridW, double gridH, int columns, int rows)
    {
        columns = Math.Max(1, columns); rows = Math.Max(1, rows);
        double cw = gridW / columns, ch = gridH / rows;
        int col = cell % columns, row = cell / columns;
        int cols = (int)Math.Floor(p.X / Math.Max(1, cw)) - col + 1, spanRows = (int)Math.Floor(p.Y / Math.Max(1, ch)) - row + 1;
        return (Math.Clamp(cols, 1, columns - col), Math.Clamp(spanRows, 1, Math.Max(1, rows - row)));
    }

    void ResizeTo(Point pointInGrid)
    {
        var (cols, rows) = SizeAt(pointInGrid, _resizeCell, DeckGrid.ActualWidth, DeckGrid.ActualHeight, _vm.L.Columns, _vm.L.Rows);
        _vm.ResizeSelectedWidget(cols, rows);
    }

    void OnDeckMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        DeckGrid.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Appui de la souris sur une touche en mode édition : le glisser-déposer pour la déplacer peut commencer.</summary>
    void OnDeckMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsEdit && !IsSyntheticFromTouch() && IsResizeHandle(e.OriginalSource as DependencyObject, out var cell))
        {
            _resizing = true;
            _resizeCell = cell;
            _dragSource = null;
            DeckGrid.CaptureMouse();   // la touche est reconstruite à chaque changement de taille : la souris reste à la grille
            e.Handled = true;
            return;
        }
        _dragSource = _vm.IsEdit && !IsSyntheticFromTouch() ? TileAt(e.OriginalSource as DependencyObject) as DeckTileViewModel : null;
        _dragStart = e.GetPosition(null);
    }

    void OnDeckMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizing)
        {
            if (e.LeftButton == MouseButtonState.Pressed) ResizeTo(e.GetPosition(DeckGrid));
            else { _resizing = false; DeckGrid.ReleaseMouseCapture(); }
            return;
        }
        if (_dragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _dragSource;
        _dragSource = null;
        _vm.BeginDrag(source);
        try { DragDrop.DoDragDrop(DeckGrid, new DataObject(TileFormat, source), DragDropEffects.Move); }
        finally { _vm.EndDrag(); }
    }

    // ---------- onglets de page (mode édition) : clic droit = menu de la page, glisser-déposer = changer son rang ----------

    const string TabFormat = "StreamDak.Tab";
    TabViewModel? _tabSource;
    Point _tabStart;

    static TabViewModel? TabOf(object sender) => (sender as FrameworkElement)?.DataContext as TabViewModel;

    /// <summary>Clic droit sur un onglet en mode édition : menu de la page (le clic droit fabriqué par un appui long du doigt est ignoré : l'appui long l'a déjà ouvert).</summary>
    void OnTabRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.IsEdit || IsSyntheticFromTouch()) { e.Handled = IsSyntheticFromTouch(); return; }
        if (TabOf(sender) is { Edit: { } edit } && edit.CanExecute(null)) { edit.Execute(null); e.Handled = true; }
    }

    void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        _tabSource = _vm.IsEdit && !IsSyntheticFromTouch() ? TabOf(sender) : null;
        _tabStart = e.GetPosition(null);
    }

    void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _tabStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - _tabStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _tabSource;
        _tabSource = null;
        var element = (UIElement)sender;
        element.Opacity = 0.4;   // l'onglet saisi est atténué tant qu'on le glisse
        try { DragDrop.DoDragDrop(element, new DataObject(TabFormat, source), DragDropEffects.Move); }
        finally { element.ClearValue(UIElement.OpacityProperty); }
    }

    static bool AcceptsOnTab(DragEventArgs e) => e.Data.GetDataPresent(TabFormat) || e.Data.GetDataPresent(TileFormat);

    void OnTabDragEnter(object sender, DragEventArgs e)
    {
        if (_vm.IsEdit && AcceptsOnTab(e)) Behaviors.DropState.SetIsOver((DependencyObject)sender, true);
    }

    void OnTabDragLeave(object sender, DragEventArgs e) => Behaviors.DropState.SetIsOver((DependencyObject)sender, false);

    void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _vm.IsEdit && AcceptsOnTab(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void OnTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        Behaviors.DropState.SetIsOver((DependencyObject)sender, false);
        if (TabOf(sender) is not { } target) return;
        if (e.Data.GetData(TabFormat) is TabViewModel moved) _vm.MoveTab(moved.Index, target.Index);
        else if (e.Data.GetData(TileFormat) is DeckTileViewModel tile) _vm.MoveTileToPage(tile, target.Index);   // une touche lâchée sur un onglet change de page
    }

    void OnDeckDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(TileFormat))
        {
            var over = TileUnder(e);
            var at = e.GetPosition(Root);
            _vm.UpdateDrag(at.X, at.Y, over);
            e.Effects = _vm.IsEdit && over != null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        bool accepted = DropPayload.CanAccept(e.Data) && TileUnder(e) is { } tile && _vm.IsFreeSlot(tile);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;   // le curseur « interdit » dit que la case est prise
        e.Handled = true;
    }

    void OnDeckDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(TileFormat) is DeckTileViewModel moved)
        {
            if (TileUnder(e) is { } target) _vm.MoveTile(moved, target);
            return;
        }
        if (TileUnder(e) is not { } tile) return;
        var dropped = DropPayload.Read(e.Data);
        _vm.DropOnTile(tile, dropped.Buttons, dropped.Problems);
    }
}
