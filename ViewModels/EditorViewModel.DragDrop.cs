namespace TouchDeck.ViewModels;

/// <summary>Éditeur : glisser-déposer sur la grille (déplacer, échanger, copier, ranger dans un dossier, ajouter depuis le catalogue).</summary>
public sealed partial class EditorViewModel
{
    // ---------- glisser-déposer ----------

    /// <summary>Que se passerait-il si on lâchait ici ? Aucune modification : sert à l'effet du curseur et à la surbrillance.</summary>
    public DropKind EvaluateDrop(DragPayload payload, SlotViewModel target, bool copy)
    {
        var place = Place;
        if (target.IsBack)
            return !payload.IsCatalog && payload.Source != null && payload.Source.Equals(place) ? DropKind.ToParent : DropKind.None;
        var existing = _doc.Get(place, target.Index);
        if (payload.IsCatalog)
        {
            if (existing is { IsFolder: true } && ActionCatalog.Find(payload.CatalogId)?.IsFolder != true) return DropKind.IntoFolder;
            return existing == null ? DropKind.Move : DropKind.Replace;
        }
        if (payload.Source == null || _doc.Get(payload.Source, payload.SourceIndex) == null) return DropKind.None;
        bool same = payload.Source.Equals(place) && payload.SourceIndex == target.Index;
        if (same) return DropKind.None;
        if (existing == null) return copy ? DropKind.Copy : DropKind.Move;
        if (copy) return DropKind.None;                 // copier sur une case occupée écraserait un bouton : refusé
        if (existing.IsFolder && !(payload.Source.Equals(place) && payload.SourceIndex == target.Index)) return DropKind.IntoFolder;
        return DropKind.Swap;
    }

    /// <summary>Applique un glisser-déposer sur une case (voir <see cref="DropKind"/>) ; <paramref name="copy"/> : Ctrl enfoncé.</summary>
    public void Drop(DragPayload payload, SlotViewModel target, bool copy)
    {
        var kind = EvaluateDrop(payload, target, copy);
        var place = Place;
        OpResult r;
        string done;
        switch (kind)
        {
            case DropKind.None: return;
            case DropKind.ToParent:
                r = _doc.MoveToParent(payload.Source!, payload.SourceIndex); done = "Bouton sorti du dossier"; break;
            case DropKind.IntoFolder when payload.IsCatalog:
            {
                var a = ActionCatalog.Find(payload.CatalogId)!;
                var folderPlace = place.Enter(target.Index);
                int at = _doc.FirstEmptyIndex(folderPlace);
                r = _doc.PutButton(folderPlace, at, a.CreateButton(), $"Ajouter « {a.Name} »");
                if (r.Ok) _doc.AddRecent(a.Id);
                done = $"« {a.Name} » ajouté au dossier"; break;
            }
            case DropKind.IntoFolder:
                r = _doc.MoveIntoFolder(payload.Source!, payload.SourceIndex, place, target.Index); done = "Bouton déplacé dans le dossier"; break;
            case DropKind.Copy:
            {
                var b = _doc.Get(payload.Source!, payload.SourceIndex)!;
                r = _doc.PutButton(place, target.Index, ConfigStore.Clone(b), "Copier le bouton"); done = "Bouton copié"; break;
            }
            case DropKind.Move when payload.IsCatalog:
            case DropKind.Replace when payload.IsCatalog:
                AddCatalogActionById(payload.CatalogId!, target.Index);
                return;
            default:
                r = _doc.MoveButton(payload.Source!, payload.SourceIndex, place, target.Index);
                done = kind == DropKind.Swap ? "Boutons échangés" : "Bouton déplacé"; break;
        }
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        if (kind is not (DropKind.IntoFolder or DropKind.ToParent)) SelectIndex(target.Index);
        else SelectIndex(-1);
        Notifier.Success(done, "Annuler", () => _doc.Undo());
    }

    /// <summary>Dépôt sur un onglet de page : le bouton va à la première case libre de cette page.</summary>
    public bool CanDropOnPage(DragPayload payload, PageTabViewModel page) =>
        !payload.IsCatalog && payload.Source != null && _doc.Get(payload.Source, payload.SourceIndex) != null
        && !(payload.Source.Equals(new Place(_profile, page.Index)));

    /// <summary>Envoie l'élément glissé dans une autre page (première case libre).</summary>
    public void DropOnPage(DragPayload payload, PageTabViewModel page)
    {
        if (!CanDropOnPage(payload, page)) return;
        var dst = new Place(_profile, page.Index);
        var r = _doc.MoveButton(payload.Source!, payload.SourceIndex, dst, _doc.FirstEmptyIndex(dst));
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success($"Bouton déplacé vers la page « {page.Name} »", "Annuler", () => _doc.Undo());
    }
}
