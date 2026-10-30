using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

// Mode édition intégré au deck : le catalogue d'actions à droite et l'inspecteur de la case sous la grille, dans la fenêtre du deck, à la place
// de l'éditeur séparé. Le moteur est l'EditorViewModel (catalogue, sélection, brouillon, annuler / rétablir), sans sa fenêtre.
public partial class MainViewModel
{
    /// <summary>Éditeur intégré (catalogue, sélection, propriétés), non nul pendant le mode édition.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEditor))] EditorViewModel? editor;

    /// <summary>Largeur du catalogue d'actions (colonne de droite), 0 hors mode édition.</summary>
    [ObservableProperty] double catalogWidth;
    /// <summary>Hauteur de l'inspecteur de la case (sous la grille), 0 hors mode édition.</summary>
    [ObservableProperty] double inspectorHeight;

    /// <summary>true pendant le mode édition (les panneaux sont affichés).</summary>
    public bool HasEditor => Editor != null;

    /// <summary>Panneaux proportionnels à l'écran, bornés pour garder des cibles tactiles confortables et une grille lisible.</summary>
    void ApplyPanelWidths()
    {
        if (IsEdit && _rawW > 0 && _rawH > 0)
        {
            CatalogWidth = Math.Min(Math.Clamp(_rawW * 0.22, 300, 420), _rawW * 0.3);
            InspectorHeight = Math.Min(Math.Clamp(_rawH * 0.4, 320, 460), _rawH * 0.5);
        }
        else { CatalogWidth = 0; InspectorHeight = 0; }
        _w = _rawW - CatalogWidth;
        _h = _rawH - InspectorHeight;
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
    public void StartEditing(Place place, int index, string? addCatalogId = null, TileEditFocus focus = TileEditFocus.Title)
    {
        if (!IsEdit) IsEdit = true;
        if (Editor is not { } editor) return;
        editor.GoTo(place, index);
        if (addCatalogId != null) editor.AddCatalogActionById(addCatalogId, index);
        if (_doc.Get(place, index) == null) { if (addCatalogId == null) _shell.EditorShell.FocusCatalogSearch(); return; }
        switch (focus)
        {
            case TileEditFocus.Title: _shell.EditorShell.FocusTitle(); break;
            case TileEditFocus.Icon: _shell.EditorShell.FocusIcon(); break;
            case TileEditFocus.Properties: _shell.EditorShell.FocusProperties(); break;
        }
    }

    /// <summary>
    /// Glisser-déposer d'une touche sur une autre case, en mode édition (souris ou doigt) : déplacée sur une case vide, rangée dans un dossier,
    /// échangée avec un autre bouton. Une seule modification annulable. Sans effet hors édition ou pour une case de départ vide.
    /// </summary>
    public void MoveTile(object from, object to)
    {
        if (!IsEdit || !TryGetSlot(from, out var sourcePlace, out int sourceIndex)) return;
        if (IsBackTile(to))
        {
            var up = _doc.MoveToParent(sourcePlace, sourceIndex);
            if (!up.Ok) { Notifier.Error(up.Message ?? "Déplacement impossible"); return; }
            Notifier.Success("Bouton sorti du dossier", "Annuler", () => _doc.Undo());
            return;
        }
        if (!TryGetSlot(to, out var targetPlace, out int targetIndex)) return;
        var source = _doc.Get(sourcePlace, sourceIndex);
        if (source == null || (sourcePlace.Equals(targetPlace) && sourceIndex == targetIndex)) return;
        var existing = _doc.Get(targetPlace, targetIndex);
        bool intoFolder = existing is { IsFolder: true };
        var result = intoFolder ? _doc.MoveIntoFolder(sourcePlace, sourceIndex, targetPlace, targetIndex) : _doc.MoveButton(sourcePlace, sourceIndex, targetPlace, targetIndex);
        if (!result.Ok) { Notifier.Error(result.Message ?? "Déplacement impossible"); return; }
        if (!intoFolder) SelectTile(targetPlace, targetIndex);
        Notifier.Success(intoFolder ? "Bouton déplacé dans le dossier" : existing == null ? "Bouton déplacé" : "Boutons échangés", "Annuler", () => _doc.Undo());
    }

    /// <summary>Poignée d'un widget glissée sur la grille : le widget sélectionné prend cette taille (ramenée à ce qui tient).</summary>
    public void ResizeSelectedWidget(int cols, int rows) => Editor?.Properties.ResizeTo(cols, rows);

    /// <summary>true pour la case « retour » d'un dossier (elle accepte une touche : elle la sort du dossier).</summary>
    bool IsBackTile(object tile) { int at = Tiles.IndexOf(tile); return at >= 0 && at < _tileKeys.Count && _tileKeys[at].Kind == "back"; }

    /// <summary>Touche glissée puis lâchée sur l'onglet d'une page : elle y est déplacée (première case libre) ; annulable.</summary>
    public void MoveTileToPage(DeckTileViewModel moved, int pageIndex)
    {
        if (!IsEdit || pageIndex == _page || !TryGetSlot(moved, out var sourcePlace, out int sourceIndex) || _doc.Get(sourcePlace, sourceIndex) == null) return;
        var dst = new Place(CurrentProfile.Name, pageIndex);
        var result = _doc.MoveButton(sourcePlace, sourceIndex, dst, _doc.FirstEmptyIndex(dst));
        if (!result.Ok) { Notifier.Error(result.Message ?? "Déplacement impossible"); return; }
        Editor?.SelectIndex(-1);
        var name = CurrentProfile.Pages[pageIndex].Name;
        Notifier.Success($"Bouton déplacé vers « {name} »", "Annuler", () => _doc.Undo());
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
        bool back = target != null && IsBackTile(target);
        bool valid = target != null && !ReferenceEquals(target, _dragSource) && (back || TryGetSlot(target, out _, out _));
        foreach (var tile in Tiles)
        {
            bool on = valid && ReferenceEquals(tile, target);
            if (tile is DeckTileViewModel deck) deck.IsDropTarget = on;
            else if (tile is AddTileViewModel add) add.IsDropTarget = on;
        }
        DragHint = !valid ? "Déposer sur une case"
            : back ? "Sortir du dossier"
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

/// <summary>Champ qui reçoit le clavier quand le menu d'une case ouvre l'édition : titre, icône, réglages de l'action, ou aucun (déplacement).</summary>
public enum TileEditFocus
{
    /// <summary>Titre du bouton, texte sélectionné.</summary>
    Title,
    /// <summary>Choix de l'icône.</summary>
    Icon,
    /// <summary>Premier réglage de l'action ou du widget.</summary>
    Properties,
    /// <summary>Rien : la touche est seulement sélectionnée.</summary>
    None,
}
