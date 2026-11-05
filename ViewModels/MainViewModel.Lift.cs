using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDak.ViewModels;

// Déplacer des boutons hors du mode édition : appui long au toucher (la touche se soulève), puis glissement vers une case, une autre page, un dossier ou la corbeille.
// Le geste est traité par MainWindow.Touch.cs ; ce fichier porte l'état et les règles (testés sans fenêtre). Tout passe par les méthodes annulables de DeckDocument.
public partial class MainViewModel
{
    /// <summary>Plage du seuil d'appui long (ms) : assez long pour ne pas gêner un appui, assez court pour rester naturel.</summary>
    public const int MinLiftDelayMs = 300, MaxLiftDelayMs = 1500, DefaultLiftDelayMs = 500;

    /// <summary>
    /// Déplacement au toucher hors édition. Désactivé tant que le réglage n'est pas branché à la configuration (clé de <c>config.json</c> soumise à validation) :
    /// l'appui long ouvre alors l'éditeur de la case, comme avant.
    /// </summary>
    public bool LiftDragEnabled { get; set; }

    int _liftDelayMs = DefaultLiftDelayMs;

    /// <summary>Durée de l'appui long qui soulève une touche (ms), bornée à [<see cref="MinLiftDelayMs"/>, <see cref="MaxLiftDelayMs"/>].</summary>
    public int LiftDelayMs
    {
        get => _liftDelayMs;
        set => _liftDelayMs = System.Math.Clamp(value, MinLiftDelayMs, MaxLiftDelayMs);
    }

    DeckTileViewModel? _liftedTile;

    /// <summary>true si une touche est soulevée (appui long reconnu, doigt encore posé).</summary>
    public bool IsLifting => _liftedTile != null;

    /// <summary>Un glissement de touche est permis : toujours en édition, hors édition seulement après un appui long.</summary>
    bool DragAllowed => IsEdit || (LiftDragEnabled && _liftedTile != null);

    /// <summary>true si cette touche peut être soulevée : hors édition, geste activé, ni menu ni dialogue, et un vrai bouton (pas la flèche « retour »).</summary>
    public bool CanLift(DeckTileViewModel? tile) =>
        LiftDragEnabled && !IsEdit && !MenuOpen && !HasDialog && tile?.Button != null && TryGetSlot(tile, out _, out _);

    /// <summary>Soulève la touche (échelle et ombre) : le doigt qui la tient peut maintenant la glisser. Faux si elle ne peut pas l'être.</summary>
    public bool Lift(DeckTileViewModel tile)
    {
        if (!CanLift(tile)) return false;
        CancelLift();
        _liftedTile = tile;
        tile.IsLifted = true;
        return true;
    }

    /// <summary>Annule le soulèvement et tout glissement en cours (relâché hors de la grille, Échap, doigt perdu) : rien n'est modifié.</summary>
    public void CancelLift()
    {
        if (_liftedTile != null) _liftedTile.IsLifted = false;
        _liftedTile = null;
        IsOverTrash = false;
        EndDrag();
    }

    /// <summary>true pendant un glissement hors édition : la corbeille est proposée.</summary>
    public bool ShowTrash => IsDragging && !IsEdit && _liftedTile != null;

    /// <summary>true si le doigt est sur la corbeille.</summary>
    [ObservableProperty] bool _isOverTrash;

    /// <summary>Supprime le bouton glissé (annulable) ; appelé quand le doigt est relâché sur la corbeille.</summary>
    public void DropOnTrash(DeckTileViewModel tile)
    {
        if (!LiftDragEnabled || IsEdit || !TryGetSlot(tile, out var place, out int index) || _doc.Get(place, index) == null) { CancelLift(); return; }
        var label = _doc.Get(place, index)!.Label;
        CancelLift();
        var result = _doc.ClearButton(place, index);
        if (!result.Ok) { Notifier.Error(result.Message ?? "Suppression impossible"); return; }
        Notifier.Success(string.IsNullOrWhiteSpace(label) ? "Bouton supprimé" : $"« {label} » supprimé", "Annuler", () => _doc.Undo());
    }

    /// <summary>Dépose la touche soulevée sur une case (déplacement, échange ou rangement dans un dossier) puis termine le geste.</summary>
    public void DropLifted(DeckTileViewModel tile, object target)
    {
        MoveTile(tile, target);   // avant l'annulation : le déplacement n'est permis que tant que la touche est soulevée
        CancelLift();
    }

    /// <summary>Même chose vers l'onglet d'une page.</summary>
    public void DropLiftedOnPage(DeckTileViewModel tile, int pageIndex)
    {
        MoveTileToPage(tile, pageIndex);
        CancelLift();
    }

    partial void OnIsDraggingChanged(bool value) => OnPropertyChanged(nameof(ShowTrash));

    partial void OnIsOverTrashChanged(bool value)
    {
        if (value) DragHint = "Supprimer le bouton";
    }
}
