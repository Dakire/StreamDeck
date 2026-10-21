using System.Windows;
using System.Windows.Media;
using TouchDeck.ViewModels;

namespace TouchDeck;

/// <summary>
/// Glisser-déposer sur la grille du deck (fichier, raccourci, exécutable, dossier ou adresse web) : seule une case vide accepte le dépôt.
/// Le dépôt OLE ne demande pas l'activation de la fenêtre : le deck ne prend toujours pas le focus.
/// </summary>
public partial class MainWindow
{
    /// <summary>Case de la grille sous le pointeur d'un glisser-déposer, ou null.</summary>
    IEditableTile? TileUnder(DragEventArgs e) =>
        TileAt(VisualTreeHelper.HitTest(DeckGrid, e.GetPosition(DeckGrid))?.VisualHit);

    void OnDeckDragOver(object sender, DragEventArgs e)
    {
        bool accepted = DropPayload.CanAccept(e.Data) && TileUnder(e) is { } tile && _vm.IsFreeSlot(tile);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;   // le curseur « interdit » dit que la case est prise
        e.Handled = true;
    }

    void OnDeckDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TileUnder(e) is not { } tile) return;
        var dropped = DropPayload.Read(e.Data);
        _vm.DropOnTile(tile, dropped.Buttons, dropped.Problems);
    }
}
