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

    /// <summary>Appui de la souris sur une touche en mode édition : le glisser-déposer pour la déplacer peut commencer.</summary>
    void OnDeckMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragSource = _vm.IsEdit && !IsSyntheticFromTouch() ? TileAt(e.OriginalSource as DependencyObject) as DeckTileViewModel : null;
        _dragStart = e.GetPosition(null);
    }

    void OnDeckMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var source = _dragSource;
        _dragSource = null;
        _vm.BeginDrag(source);
        try { DragDrop.DoDragDrop(DeckGrid, new DataObject(TileFormat, source), DragDropEffects.Move); }
        finally { _vm.EndDrag(); }
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
