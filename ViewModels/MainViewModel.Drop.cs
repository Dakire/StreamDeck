using System.Collections.Generic;

namespace TouchDeck.ViewModels;

// Glisser-déposer sur le deck : un fichier, un raccourci, un dossier ou une adresse déposé sur une case vide crée un bouton, sans dialogue.
public partial class MainViewModel
{
    /// <summary>Emplacement (grille et index) d'une case de la grille affichée ; false pour la flèche « retour » ou une tuile inconnue.</summary>
    public bool TryGetSlot(object tile, out Place place, out int index)
    {
        int at = Tiles.IndexOf(tile);
        if (at >= 0 && at < _tileKeys.Count && _tileKeys[at].Kind is "button" or "widget" or "spacer" or "add" or "welcome")
        {
            place = _tileKeys[at].Place;
            index = _tileKeys[at].Index;
            return true;
        }
        place = null!;
        index = -1;
        return false;
    }

    /// <summary>true si la tuile est une case vide de la grille : seul endroit où un dépôt est accepté.</summary>
    public bool IsFreeSlot(object tile) => TryGetSlot(tile, out var place, out var index) && _doc.Get(place, index) == null;

    /// <summary>
    /// Crée les boutons déposés : le premier dans la case visée, les suivants dans les cases libres qui suivent. Une seule écriture,
    /// donc un seul pas d'annulation. <paramref name="problems"/> : éléments refusés, signalés dans la barre d'état.
    /// </summary>
    public void DropOnTile(object tile, IReadOnlyList<DeckButton> buttons, IReadOnlyList<string> problems)
    {
        if (!TryGetSlot(tile, out var place, out var index)) { Notifier.Error("Dépose sur une case de la grille."); return; }
        if (_doc.Get(place, index) != null) { Notifier.Error("Cette case est déjà prise : dépose sur une case vide."); return; }
        if (buttons.Count == 0)
        {
            Notifier.Error(problems.Count > 0 ? problems[0] : "Rien à ajouter : dépose un fichier, un dossier ou une adresse web.");
            return;
        }
        var label = buttons.Count == 1 ? $"Déposer « {buttons[0].Label} »" : $"Déposer {buttons.Count} éléments";
        var result = _doc.AddButtons(place, index, buttons, label);
        if (!result.Ok) { Notifier.Error(result.Message ?? "Ajout impossible"); return; }
        var skipped = problems.Count > 0 ? $" ({problems.Count} ignoré{(problems.Count > 1 ? "s" : "")} : {problems[0]})" : "";
        Notifier.Success((buttons.Count == 1 ? $"« {buttons[0].Label} » ajouté" : $"{buttons.Count} boutons ajoutés") + skipped, "Annuler", () => _doc.Undo());
    }
}
