using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDak.ViewModels;

// Déplacer des boutons hors du mode édition : appui long au toucher (la touche se soulève), puis glissement vers une case, une autre page, un dossier ou la corbeille.
// Le geste est traité par MainWindow.Touch.cs ; ce fichier porte l'état et les règles (testés sans fenêtre). Tout passe par les méthodes annulables de DeckDocument.
public partial class MainViewModel
{
    /// <summary>Plage du seuil d'appui long (ms) : assez long pour ne pas gêner un appui, assez court pour rester naturel.</summary>
    public const int MinLiftDelayMs = 300, MaxLiftDelayMs = 1500, DefaultLiftDelayMs = 500;

    /// <summary>Déplacement au toucher hors édition : activé par défaut, désactivable (<see cref="DeckConfig.LiftDragDisabled" />, relu à chaque reconstruction de la grille).</summary>
    public bool LiftDragEnabled { get; set; } = true;

    int _liftDelayMs = DefaultLiftDelayMs;

    /// <summary>Durée de l'appui long qui soulève une touche (ms), bornée à [<see cref="MinLiftDelayMs"/>, <see cref="MaxLiftDelayMs"/>].</summary>
    public int LiftDelayMs
    {
        get => _liftDelayMs;
        set => _liftDelayMs = System.Math.Clamp(value, MinLiftDelayMs, MaxLiftDelayMs);
    }

    DeckTileViewModel? _liftedTile;
    (Place Place, int Index)? _liftSource;   // case d'origine : la tuile soulevée disparaît si la page change pendant le glissé

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
        if (TryGetSlot(tile, out var place, out int index)) _liftSource = (place, index);
        tile.IsLifted = true;
        return true;
    }

    /// <summary>Annule le soulèvement et tout glissement en cours (relâché hors de la grille, Échap, doigt perdu) : rien n'est modifié.</summary>
    public void CancelLift()
    {
        if (_liftedTile != null) _liftedTile.IsLifted = false;
        _liftedTile = null;
        _liftSource = null;
        IsOverTrash = false;
        EndDrag();
    }

    /// <summary>true pendant un glissement hors édition : la corbeille est proposée.</summary>
    public bool ShowTrash => IsDragging && !IsEdit && _liftedTile != null;

    /// <summary>true si le doigt est sur la corbeille.</summary>
    [ObservableProperty] bool _isOverTrash;

    /// <summary>Supprime le bouton glissé (annulable) ; appelé quand le doigt est relâché sur la corbeille.</summary>
    public void DropOnTrash(DeckTileViewModel? tile = null)
    {
        if (!LiftDragEnabled || IsEdit || _liftSource is not var (place, index) || _doc.Get(place, index) == null) { CancelLift(); return; }
        var label = _doc.Get(place, index)!.Label;
        CancelLift();
        var result = _doc.ClearButton(place, index);
        if (!result.Ok) { Notifier.Error(result.Message ?? "Suppression impossible"); return; }
        Notifier.Success(string.IsNullOrWhiteSpace(label) ? "Bouton supprimé" : $"« {label} » supprimé", "Annuler", () => _doc.Undo());
    }

    /// <summary>Dépose la touche soulevée sur une case (déplacement, échange ou rangement dans un dossier) puis termine le geste.</summary>
    public void DropLifted(DeckTileViewModel? tile, object target)
    {
        // Avant l'annulation : le déplacement n'est permis que tant que la touche est soulevée. L'origine mémorisée vaut même si la page a changé entre-temps.
        if (DragAllowed && _liftSource is var (place, index)) MoveFrom(place, index, target);
        CancelLift();
    }

    /// <summary>Même chose vers l'onglet d'une page.</summary>
    public void DropLiftedOnPage(DeckTileViewModel? tile, int pageIndex)
    {
        if (DragAllowed && _liftSource is var (place, index)) MoveFromToPage(place, index, pageIndex);
        CancelLift();
    }

    /// <summary>
    /// Doigt tenu près d'un bord de la grille pendant un déplacement : passe à la page visible suivante (+1) ou précédente (-1), sans lâcher le bouton.
    /// Les pages masquées et les pages web sont sautées (on ne dépose rien dessus). Retourne false s'il n'y a pas de page dans ce sens.
    /// </summary>
    public bool FlipPageWhileLifting(int direction)
    {
        if (!IsLifting || IsEdit || direction == 0) return false;
        var pages = CurrentProfile.Pages;
        for (int i = _page + Math.Sign(direction); i >= 0 && i < pages.Count; i += Math.Sign(direction))
        {
            if (pages[i].Hidden || pages[i].IsWeb) continue;
            GoTo(i);
            return true;
        }
        return false;
    }

    partial void OnIsDraggingChanged(bool value) => OnPropertyChanged(nameof(ShowTrash));

    partial void OnIsOverTrashChanged(bool value)
    {
        if (value) DragHint = "Supprimer le bouton";
    }
}
