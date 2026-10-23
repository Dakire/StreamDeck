using System.Windows.Controls;
using System.Windows.Input;
using StreamDak.ViewModels;

namespace StreamDak.Controls;

/// <summary>
/// Panneau d'actions du mode édition du deck : recherche et catalogue ; toucher une action la pose dans la case sélectionnée.
/// Son DataContext est l'<see cref="EditorViewModel"/>.
/// </summary>
public partial class CatalogPanel : UserControl
{
    /// <summary>Panneau d'actions.</summary>
    public CatalogPanel() { InitializeComponent(); }

    /// <summary>Entrée ajoute la première action trouvée ; Échap efface la recherche ; flèche bas passe à la liste.</summary>
    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel vm) return;
        if (e.Key == Key.Enter && vm.Catalog.IsSearching) { vm.AddFirstCatalogResult(); e.Handled = true; }
        else if (e.Key == Key.Escape && vm.Catalog.IsSearching) { vm.Catalog.Search = ""; e.Handled = true; }
        else if (e.Key == Key.Down) { SearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
    }
}
