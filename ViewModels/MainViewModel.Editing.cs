using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

// Mode édition intégré au deck : un panneau d'actions à gauche et un panneau de propriétés à droite, dans la fenêtre du deck, à la place
// de l'éditeur séparé. Le moteur est l'EditorViewModel (catalogue, sélection, brouillon, annuler / rétablir), sans sa fenêtre.
public partial class MainViewModel
{
    /// <summary>Éditeur intégré (catalogue, sélection, propriétés), non nul pendant le mode édition.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEditor))] EditorViewModel? editor;

    /// <summary>Largeur du panneau d'actions (gauche), 0 hors mode édition.</summary>
    [ObservableProperty] double leftPanelWidth;
    /// <summary>Largeur du panneau de propriétés (droite), 0 hors mode édition.</summary>
    [ObservableProperty] double rightPanelWidth;

    /// <summary>true pendant le mode édition (les panneaux sont affichés).</summary>
    public bool HasEditor => Editor != null;

    /// <summary>Panneaux proportionnels à l'écran, bornés pour garder des cibles tactiles confortables et une grille lisible.</summary>
    void ApplyPanelWidths()
    {
        if (IsEdit && _rawW > 0)
        {
            LeftPanelWidth = Math.Min(Math.Clamp(_rawW * 0.2, 260, 380), _rawW * 0.28);
            RightPanelWidth = Math.Min(Math.Clamp(_rawW * 0.26, 320, 460), _rawW * 0.34);
        }
        else { LeftPanelWidth = 0; RightPanelWidth = 0; }
        _w = _rawW - LeftPanelWidth - RightPanelWidth;
        _h = _rawH;
    }

    partial void OnIsEditChanged(bool value)
    {
        StopTyping();   // la saisie au clavier d'un widget web et le mode édition se disputent l'activation de la fenêtre
        if (value)
        {
            if (Editor == null)
            {
                Editor = _editorFactory(_shell.EditorShell);
                Editor.SelectionChanged += ApplySelection;
            }
            _shell.SetEditing(true);
            Editor.GoTo(CurrentPlace, -1);
        }
        else
        {
            Dialog?.Abandon();   // une boîte ouverte ne survit pas à la fin de l'édition
            if (Editor is { } e)
            {
                e.Properties.Flush();
                e.SelectionChanged -= ApplySelection;
                Editor = null;
                e.Dispose();
            }
            _shell.SetEditing(false);
        }
        ApplyPanelWidths();
        Rebuild();
    }

    /// <summary>Sélectionne une case dans le panneau de propriétés (un toucher sur une touche en mode édition).</summary>
    void SelectTile(Place place, int index) => Editor?.GoTo(place, index);

    /// <summary>
    /// Entre en mode édition sur cette case : le panneau de propriétés s'ouvre à droite, le titre (bouton) ou la recherche d'actions
    /// (case vide) reçoit le clavier. <paramref name="addCatalogId"/> : action du catalogue posée aussitôt dans la case vide (« Annuler » la retire).
    /// </summary>
    public void StartEditing(Place place, int index, string? addCatalogId = null)
    {
        if (!IsEdit) IsEdit = true;
        if (Editor is not { } editor) return;
        editor.GoTo(place, index);
        if (addCatalogId != null) editor.AddCatalogActionById(addCatalogId, index);
        if (_doc.Get(place, index) != null) _shell.EditorShell.FocusTitle(); else if (addCatalogId == null) _shell.EditorShell.FocusCatalogSearch();
    }

    /// <summary>Met en évidence la touche sélectionnée (liseré d'accent) sans reconstruire la grille.</summary>
    void ApplySelection()
    {
        var place = Editor?.Place;
        int selected = Editor?.SelectedIndex ?? -1;
        foreach (var tile in Tiles)
        {
            bool on = selected >= 0 && place != null && TryGetSlot(tile, out var p, out var i) && i == selected && p.Equals(place);
            if (tile is DeckTileViewModel deck) deck.IsSelected = on;
            else if (tile is AddTileViewModel add) add.IsSelected = on;
        }
    }
}
