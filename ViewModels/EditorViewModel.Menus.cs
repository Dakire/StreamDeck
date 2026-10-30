using System;
using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>Éditeur : menus contextuels des cases, profils, pages et catalogue.</summary>
public sealed partial class EditorViewModel
{
    // ---------- menus contextuels ----------

    /// <summary>Menu contextuel d'une case, selon son contenu et le presse-papiers.</summary>
    public IReadOnlyList<MenuItemSpec> BuildSlotMenu(SlotViewModel slot)
    {
        if (slot.IsBack)
            return new[] { new MenuItemSpec("Retour au dossier parent", GoUp, SymbolRegular.ArrowLeft24) };

        var place = Place;
        bool occupied = !slot.IsEmpty;
        bool folder = slot.IsFolder;

        // Menu à plat : le catalogue, l'inspecteur et le glisser-déposer sont toujours à l'écran, donc plus de sous-menus d'ajout, de déplacement ni de propriétés.
        // Les raccourcis clavier (Suppr, Ctrl+C / X / V / D, F2, Alt+flèches) sont affichés en face des entrées.
        var items = new List<MenuItemSpec>();
        if (!occupied)
        {
            items.Add(new("Coller", () => { SelectIndex(slot.Index); PasteCommand.Execute(null); }, SymbolRegular.ClipboardPaste24, "Ctrl+V", Enabled: _doc.HasClipboard));
            items.Add(new("Créer un dossier", () => { SelectIndex(slot.Index); CreateFolderHere(); }, SymbolRegular.FolderAdd24));
            return items;
        }
        if (folder) items.Add(new("Ouvrir le dossier", OpenSelectedFolder, SymbolRegular.FolderOpen24, "Entrée"));
        items.Add(new("Modifier le titre", () => _shell.FocusTitle(), SymbolRegular.TextT24, "F2"));
        items.Add(MenuItemSpec.Separator);
        items.Add(new("Copier", () => CopyCommand.Execute(null), SymbolRegular.Copy24, "Ctrl+C"));
        items.Add(new("Couper", () => CutCommand.Execute(null), SymbolRegular.Cut24, "Ctrl+X"));
        items.Add(new("Coller", () => PasteCommand.Execute(null), SymbolRegular.ClipboardPaste24, "Ctrl+V", Enabled: _doc.HasClipboard));
        items.Add(new("Dupliquer", () => DuplicateCommand.Execute(null), SymbolRegular.CopyAdd24, "Ctrl+D"));

        var otherPages = Pages.Where(p => p.Index != _page).ToList();
        var folders = Slots.Where(s => s.IsFolder && s.Index != slot.Index).ToList();
        if (otherPages.Count > 0 || folders.Count > 0 || _folder.Count > 0) items.Add(MenuItemSpec.Separator);
        if (otherPages.Count > 0)
            items.Add(new("Vers la page", Icon: SymbolRegular.Document24,
                Children: otherPages.Select(p => new MenuItemSpec(p.Name, () => MoveToPage(slot.Index, p))).ToList()));
        if (folders.Count > 0)
            items.Add(new("Vers le dossier", Icon: SymbolRegular.Folder24,
                Children: folders.Select(f => new MenuItemSpec(_doc.Get(place, f.Index)?.Label is { Length: > 0 } t ? t : "Dossier", () => MoveToFolder(slot.Index, f.Index))).ToList()));
        if (_folder.Count > 0)
            items.Add(new("Sortir du dossier", () => MoveOut(slot.Index), SymbolRegular.ArrowUpLeft24));

        items.Add(MenuItemSpec.Separator);
        items.Add(new("Réinitialiser", () => ResetButtonCommand.Execute(null), SymbolRegular.ArrowReset24));
        items.Add(new("Supprimer", () => DeleteSelection(), SymbolRegular.Delete24, "Suppr", Danger: true));
        return items;
    }

    void MoveToPage(int index, PageTabViewModel page)
    {
        var dst = new Place(_profile, page.Index);
        var r = _doc.MoveButton(Place, index, dst, _doc.FirstEmptyIndex(dst));
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success($"Bouton déplacé vers « {page.Name} »", "Annuler", () => _doc.Undo());
    }

    void MoveToFolder(int index, int folderIndex)
    {
        var r = _doc.MoveIntoFolder(Place, index, Place, folderIndex);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success("Bouton déplacé dans le dossier", "Annuler", () => _doc.Undo());
    }

    void MoveOut(int index)
    {
        var r = _doc.MoveToParent(Place, index);
        if (!r.Ok) { Notifier.Error(r.Message ?? "Déplacement impossible"); return; }
        SelectIndex(-1);
        Notifier.Success("Bouton sorti du dossier", "Annuler", () => _doc.Undo());
    }

    /// <summary>Menu contextuel d'un profil.</summary>
    public IReadOnlyList<MenuItemSpec> BuildProfileMenu(string name)
    {
        bool active = name.Equals(_doc.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase);
        bool canDelete = _doc.Config.Profiles.Count > 1;
        return new List<MenuItemSpec>
        {
            new("Définir comme profil actif", () => SetActive(name), SymbolRegular.Checkmark24, Enabled: !active, Checked: active),
            MenuItemSpec.Separator,
            new("Renommer", async () => await RenameProfileAsync(name), SymbolRegular.Rename24, "F2"),
            new("Dupliquer", () => DuplicateProfile(name), SymbolRegular.CopyAdd24),
            new("Application associée…", async () => await SetAppMatchAsync(name), SymbolRegular.Window24),
            MenuItemSpec.Separator,
            new("Exporter…", () => ExportProfile(name), SymbolRegular.ArrowExportUp24),
            new("Importer…", () => ImportProfileCommand.Execute(null), SymbolRegular.ArrowImport24),
            new("Nouveau profil depuis un modèle", Icon: SymbolRegular.DocumentAdd24, Children: ProfileTemplates.All.Select(t => new MenuItemSpec(t.Name, () => NewProfileFromTemplate(t), Enum.TryParse(t.Icon, out SymbolRegular icon) ? icon : null)).ToList()),
            MenuItemSpec.Separator,
            new("Supprimer", async () => await DeleteProfileAsync(name), SymbolRegular.Delete24, Enabled: canDelete, Danger: true),
        };
    }

    /// <summary>Menu contextuel d'un onglet de page.</summary>
    public IReadOnlyList<MenuItemSpec> BuildPageMenu(PageTabViewModel page)
    {
        int count = Pages.Count;
        return new List<MenuItemSpec>
        {
            new("Renommer", async () => await RenamePageAsync(page), SymbolRegular.Rename24),
            new("Dupliquer", () => DuplicatePage(page), SymbolRegular.CopyAdd24),
            page.IsWeb
                ? new("Adresse de la page web…", async () => await SetPageWebAsync(page), SymbolRegular.Globe24)
                : new("Afficher une page web…", async () => await SetPageWebAsync(page), SymbolRegular.Globe24),
            new("Afficher une grille de boutons", () => SetPageGrid(page), SymbolRegular.Grid24, Enabled: page.IsWeb),
            new(page.Hidden ? "Afficher dans les onglets" : "Masquer des onglets", () => TogglePageHidden(page), page.Hidden ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24),
            // Notifications : seulement pour une page web (interrupteurs cochés quand ils sont actifs)
            new("Interaction avec la page", Icon: SymbolRegular.CursorClick24, Enabled: page.IsWeb, Children: WebInputModes.All
                .Select(m => new MenuItemSpec(m.Label, () => SetPageWebInput(page, m.Mode), Checked: page.WebInput == m.Mode)).ToList()),
            new("Afficher le badge", () => TogglePageBadge(page), SymbolRegular.Alert24, Enabled: page.IsWeb, Checked: page.IsWeb && page.BadgeEnabled),
            new("Exclure du total", () => TogglePageBadgeExcluded(page), SymbolRegular.AlertOff24, Enabled: page.IsWeb && page.BadgeEnabled, Checked: page.IsWeb && page.BadgeExcludeFromTotal),
            MenuItemSpec.Separator,
            new("Supprimer la page", async () => await DeletePageAsync(page), SymbolRegular.Delete24, Enabled: count > 1, Danger: true),
        };
    }

    /// <summary>Menu contextuel d'une action du catalogue.</summary>
    public IReadOnlyList<MenuItemSpec> BuildCatalogMenu(CatalogItemViewModel item)
    {
        var a = item.Action;
        bool selectedFree = _selectedIndex >= 0 && _doc.Get(Place, _selectedIndex) == null;
        return new List<MenuItemSpec>
        {
            new(_selectedIndex >= 0 ? "Ajouter à la case sélectionnée" : "Ajouter à la première case libre", () => AddCatalogAction(a), SymbolRegular.Add24),
            new("Ajouter à la première case libre", () => AddCatalogAction(a, FirstFree()), SymbolRegular.AddSquare24, Enabled: _selectedIndex >= 0 && !selectedFree),
            new("Configurer", () => { AddCatalogAction(a); _shell.FocusProperties(); }, SymbolRegular.Settings24),
            MenuItemSpec.Separator,
            new(item.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", () => item.ToggleFavoriteCommand.Execute(null),
                item.IsFavorite ? SymbolRegular.StarOff24 : SymbolRegular.Star24),
            new("Afficher les informations", async () => await ShowInfoAsync(a), SymbolRegular.Info24),
        };
    }

    /// <summary>Décrit une action du catalogue (description, catégorie, étapes).</summary>
    public async System.Threading.Tasks.Task ShowInfoAsync(CatalogAction a)
    {
        var steps = a.IsFolder ? "Ouvre une grille de sous-boutons." : string.Join("\n", a.Actions.Select((s, i) =>
            $"{i + 1}. {s.Type}{(string.IsNullOrEmpty(s.Value) ? "" : " : " + s.Value)}"));
        var text = $"{a.Description}\n\nCatégorie : {a.Category}\n\nÉtapes :\n{steps}" + (a.Note != null ? $"\n\n{a.Note}" : "");
        await _shell.InfoAsync(a.Name, text);
    }
}
