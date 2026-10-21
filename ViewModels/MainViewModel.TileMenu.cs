using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

// Menu d'une case du deck (clic droit, appui long) : décrit en MenuItemSpec comme ceux de l'éditeur, mais affiché dans la feuille
// du deck. Un ContextMenu WPF est une fenêtre popup séparée : le toucher n'y serait plus traité par le deck (ADR 0013).
public partial class MainViewModel
{
    sealed record SpecLevel(string Title, IReadOnlyList<MenuItemSpec> Items);

    /// <summary>Niveaux de menu ouverts (le dernier est affiché) ; vide quand la feuille montre le menu général.</summary>
    readonly List<SpecLevel> _specTrail = new();
    bool _hintShown;

    partial void OnMenuOpenChanged(bool value)
    {
        if (!value) _specTrail.Clear();
    }

    /// <summary>
    /// Clé et fabrique d'une case sans bouton : emplacement « + » en édition, invitation sur la première case d'une page vide,
    /// sinon case éteinte qui ouvre le menu de création (clic droit, appui long).
    /// </summary>
    (TileKey Key, Func<object> Create) FreeTile(Place place, int at, bool welcome)
    {
        if (IsEdit)
            return (new TileKey("add", place, at, null, null, false, true),
                () => new AddTileViewModel { Click = new RelayCommand(() => SelectTile(place, at)), Edit = new RelayCommand(() => OpenTileMenu(place, at)) });
        if (welcome) return (new TileKey("welcome", place, at, null, null, false, false), () => new WelcomeTileViewModel { Click = new RelayCommand(() => OpenTileMenu(place, at)) });
        return (new TileKey("spacer", place, at, null, null, false, false), () => new SpacerTileViewModel { Edit = new RelayCommand(() => OpenTileMenu(place, at)) });
    }

    /// <summary>Indice affiché une fois par lancement, tant que la page est vide ou presque : jamais en permanence, jamais par-dessus un autre message.</summary>
    void ShowDiscoveryHintOnce(int buttons)
    {
        if (_hintShown || buttons > 2 || Status != null || MenuOpen) return;
        _hintShown = true;
        SetStatus("Astuce : appui long (ou clic droit) sur une case vide pour ajouter un bouton", error: false);
    }

    /// <summary>Ouvre le menu de la case : création sur une case vide, actions sur un bouton.</summary>
    void OpenTileMenu(Place place, int index)
    {
        var button = _doc.Get(place, index);
        var title = button == null ? "Case vide" : button.Label.Length > 0 ? button.Label : "Bouton";
        _profileMenu = false;
        _specTrail.Clear();
        _specTrail.Add(new SpecLevel(title, BuildTileMenu(place, index)));
        RenderSpecMenu();
        MenuOpen = true;
    }

    /// <summary>
    /// Menu d'une case. Case vide : Nouveau bouton, modèles en un clic, Coller, Éditeur complet. Bouton : Modifier, Dupliquer,
    /// Copier, Couper, Supprimer, Éditeur complet. Chaque entrée écrit une seule fois dans le document (un seul pas d'annulation).
    /// </summary>
    public IReadOnlyList<MenuItemSpec> BuildTileMenu(Place place, int index) =>
        _doc.Get(place, index) is { } button ? BuildButtonMenu(place, index, button) : BuildEmptyTileMenu(place, index);

    List<MenuItemSpec> BuildEmptyTileMenu(Place place, int index)
    {
        var items = new List<MenuItemSpec>
        {
            new("Nouveau bouton…", () => _windows.OpenQuickCreate(place, index), SymbolRegular.Add24),
            new("Parcourir les actions", Icon: SymbolRegular.Apps24, Children: CatalogMenu.Categories(a => CreateFromCatalog(a, place, index))),
            MenuItemSpec.Separator,
        };
        foreach (var template in QuickCreate.Templates)
        {
            if (ActionCatalog.Find(template.CatalogId) is not { } action) continue;
            items.Add(new(template.Title, () => CreateFromCatalog(action, place, index), IconCatalog.TryResolve(template.Icon, out var symbol) ? symbol : SymbolRegular.Apps24));
        }
        items.Add(MenuItemSpec.Separator);
        if (_doc.HasClipboard) items.Add(new("Coller", () => PasteHere(place, index), SymbolRegular.ClipboardPaste24));
        items.Add(new("Éditeur complet…", () => _windows.OpenEditor(place, index), SymbolRegular.WindowEdit20));
        return items;
    }

    List<MenuItemSpec> BuildButtonMenu(Place place, int index, DeckButton button) => new()
    {
        new("Modifier…", () => StartEditing(place, index), SymbolRegular.Edit24),
        new("Dupliquer", () => DuplicateHere(place, index), SymbolRegular.CopyAdd24),
        new("Copier", () => CopyHere(place, index), SymbolRegular.Copy24),
        new("Couper", () => CutHere(place, index), SymbolRegular.Cut24),
        MenuItemSpec.Separator,
        new(button.IsFolder ? "Supprimer le dossier" : "Supprimer", () => DeleteHere(place, index, button.IsFolder), SymbolRegular.Delete24, Danger: true),
        MenuItemSpec.Separator,
        new("Éditeur complet…", () => _windows.OpenEditor(place, index), SymbolRegular.WindowEdit20),
    };

    // ---------- création et opérations : une écriture, un pas d'annulation, « Annuler » touchable ----------

    /// <summary>
    /// Pose l'action du catalogue dans la case. Si le bouton demande encore un réglage (raccourci, chemin, adresse), l'éditeur rapide
    /// s'ouvre sur la case et la pose elle-même : « Annuler » y rend la case vide.
    /// </summary>
    void CreateFromCatalog(CatalogAction action, Place place, int index)
    {
        var button = action.CreateButton();
        if (QuickCreate.NeedsSetup(button, _doc.Config, _doc.FindProfile(place.Profile)))
        {
            StartEditing(place, index, action.Id);   // les réglages à compléter se font dans le panneau de propriétés du deck
            return;
        }
        var result = _doc.AddButton(place, index, button, $"Ajouter « {action.Name} »");
        if (!result.Ok) { Notifier.Error(result.Message ?? "Ajout impossible"); return; }
        _doc.AddRecent(action.Id);
        Notifier.Success($"« {action.Name} » ajouté", "Annuler", () => _doc.Undo());
    }

    void DuplicateHere(Place place, int index)
    {
        var result = _doc.Duplicate(place, index);
        if (result.Ok) Notifier.Success("Bouton dupliqué dans la prochaine case libre", "Annuler", () => _doc.Undo());
        else Notifier.Error(result.Message ?? "Duplication impossible");
    }

    void CopyHere(Place place, int index)
    {
        if (_doc.Copy(place, index)) Notifier.Info("Bouton copié : appui long sur une case vide pour le coller");
    }

    void CutHere(Place place, int index)
    {
        var result = _doc.Cut(place, index);
        if (result.Ok) Notifier.Success("Bouton coupé : appui long sur une case vide pour le coller", "Annuler", () => _doc.Undo());
        else Notifier.Error(result.Message ?? "Coupe impossible");
    }

    void PasteHere(Place place, int index)
    {
        var result = _doc.Paste(place, index);
        if (result.Ok) Notifier.Success("Bouton collé", "Annuler", () => _doc.Undo());
        else Notifier.Error(result.Message ?? "Collage impossible");
    }

    void DeleteHere(Place place, int index, bool folder)
    {
        var result = _doc.ClearButton(place, index);
        if (result.Ok) Notifier.Success(folder ? "Dossier supprimé" : "Bouton supprimé", "Annuler", () => _doc.Undo());
        else Notifier.Error(result.Message ?? "Suppression impossible");
    }

    // ---------- affichage dans la feuille ----------

    void RenderSpecMenu()
    {
        var level = _specTrail[^1];
        MenuEntries.Clear();
        MenuEntries.Add(new MenuHeaderViewModel { Text = level.Title });
        if (_specTrail.Count > 1)
        {
            MenuEntries.Add(SpecEntry(SymbolRegular.ArrowLeft24, "Retour", () => { _specTrail.RemoveAt(_specTrail.Count - 1); RenderSpecMenu(); }));
            MenuEntries.Add(new MenuSeparatorViewModel());
        }
        foreach (var spec in level.Items)
        {
            if (spec.IsSeparator)
            {
                if (MenuEntries[^1] is not (MenuSeparatorViewModel or MenuHeaderViewModel)) MenuEntries.Add(new MenuSeparatorViewModel());
                continue;
            }
            var icon = spec.Icon ?? SymbolRegular.Empty;
            if (spec.Children is { Count: > 0 } children)
            {
                var title = spec.Header.TrimEnd('…');
                MenuEntries.Add(SpecEntry(icon, spec.Header, () => { _specTrail.Add(new SpecLevel(title, children)); RenderSpecMenu(); }, trailing: "›"));
            }
            else
            {
                var run = spec.Execute;
                MenuEntries.Add(SpecEntry(icon, spec.Header, () => { MenuOpen = false; run?.Invoke(); },
                    enabled: spec.Enabled && run != null, danger: spec.Danger, trailing: spec.Checked ? "✓" : null));
            }
        }
    }

    static MenuEntryViewModel SpecEntry(SymbolRegular symbol, string text, Action run, bool enabled = true, bool danger = false, string? trailing = null) =>
        new() { Symbol = symbol, Text = text, IsDanger = danger, Trailing = trailing, Command = new RelayCommand(run, () => enabled) };
}
