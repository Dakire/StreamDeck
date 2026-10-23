using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

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
            _editCheckpoint = _doc.Checkpoint();
            Editor.GoTo(CurrentPlace, -1);
        }
        else
        {
            Dialog?.Abandon();   // une boîte ouverte ne survit pas à la fin de l'édition
            _editCheckpoint = null;
            EndDrag();
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

    /// <summary>
    /// Glisser-déposer d'une touche sur une autre case, en mode édition (souris ou doigt) : déplacée sur une case vide, rangée dans un dossier,
    /// échangée avec un autre bouton. Une seule modification annulable. Sans effet hors édition ou pour une case de départ vide.
    /// </summary>
    public void MoveTile(object from, object to)
    {
        if (!IsEdit || !TryGetSlot(from, out var sourcePlace, out int sourceIndex) || !TryGetSlot(to, out var targetPlace, out int targetIndex)) return;
        var source = _doc.Get(sourcePlace, sourceIndex);
        if (source == null || (sourcePlace.Equals(targetPlace) && sourceIndex == targetIndex)) return;
        var existing = _doc.Get(targetPlace, targetIndex);
        bool intoFolder = existing is { IsFolder: true };
        var result = intoFolder ? _doc.MoveIntoFolder(sourcePlace, sourceIndex, targetPlace, targetIndex) : _doc.MoveButton(sourcePlace, sourceIndex, targetPlace, targetIndex);
        if (!result.Ok) { Notifier.Error(result.Message ?? "Déplacement impossible"); return; }
        if (!intoFolder) SelectTile(targetPlace, targetIndex);
        Notifier.Success(intoFolder ? "Bouton déplacé dans le dossier" : existing == null ? "Bouton déplacé" : "Boutons échangés", "Annuler", () => _doc.Undo());
    }

    // ---------- glissement : touche atténuée, case visée en surbrillance, étiquette qui suit le pointeur ----------

    DeckTileViewModel? _dragSource;

    /// <summary>true pendant le glissement d'une touche.</summary>
    [ObservableProperty] bool isDragging;
    /// <summary>Position de l'étiquette qui suit le pointeur (DIP, repère de la fenêtre).</summary>
    [ObservableProperty] double dragX;
    /// <summary>Position verticale de l'étiquette (DIP).</summary>
    [ObservableProperty] double dragY;
    /// <summary>Face de la touche glissée.</summary>
    [ObservableProperty] TileFaceData? dragFace;
    /// <summary>Ce que ferait un dépôt ici : « Déplacer ici », « Échanger », « Ranger dans le dossier », ou « Déposer sur une case ».</summary>
    [ObservableProperty] string dragHint = "";

    /// <summary>Début du glissement d'une touche (souris ou doigt).</summary>
    public void BeginDrag(DeckTileViewModel tile)
    {
        if (!IsEdit) return;
        EndDrag();
        _dragSource = tile;
        tile.IsDragSource = true;
        DragFace = tile.Face;
        DragHint = "Déposer sur une case";
        IsDragging = true;
    }

    /// <summary>Le pointeur bouge : l'étiquette le suit et la case visée s'allume si le dépôt y est possible.</summary>
    public void UpdateDrag(double x, double y, object? target)
    {
        if (!IsDragging || _dragSource == null) return;
        DragX = x + 14;
        DragY = y + 14;
        bool valid = target != null && !ReferenceEquals(target, _dragSource) && TryGetSlot(target, out _, out _);
        foreach (var tile in Tiles)
        {
            bool on = valid && ReferenceEquals(tile, target);
            if (tile is DeckTileViewModel deck) deck.IsDropTarget = on;
            else if (tile is AddTileViewModel add) add.IsDropTarget = on;
        }
        DragHint = !valid ? "Déposer sur une case"
            : target is DeckTileViewModel { Button: { IsFolder: true } } ? "Ranger dans le dossier"
            : target is DeckTileViewModel ? "Échanger"
            : "Déplacer ici";
    }

    /// <summary>Fin du glissement (dépôt ou abandon) : tout redevient normal.</summary>
    public void EndDrag()
    {
        if (_dragSource != null) _dragSource.IsDragSource = false;
        _dragSource = null;
        foreach (var tile in Tiles)
        {
            if (tile is DeckTileViewModel deck) deck.IsDropTarget = false;
            else if (tile is AddTileViewModel add) add.IsDropTarget = false;
        }
        IsDragging = false;
    }

    // ---------- quitter sans enregistrer ----------

    string? _editCheckpoint;

    /// <summary>
    /// « Quitter » : sort du mode édition en annulant tout ce qui a été fait depuis son ouverture (après confirmation s'il y a quelque chose
    /// à perdre ; le retour en arrière reste annulable). « Terminer » garde les modifications.
    /// </summary>
    [RelayCommand]
    async Task QuitEdit()
    {
        Editor?.Properties.DiscardDraft();   // une saisie en attente ne doit pas être réécrite après le retour en arrière
        if (_editCheckpoint != null && _doc.HasChangesSince(_editCheckpoint))
        {
            if (!await ConfirmAsync("Quitter sans enregistrer ?", "Les modifications faites depuis l'ouverture du mode édition seront annulées. Tu pourras les rétablir avec « Annuler ».", "Quitter sans enregistrer", danger: true))
                return;
            _doc.RevertTo(_editCheckpoint, "Quitter sans enregistrer");
        }
        IsEdit = false;
        Rebuild();
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
