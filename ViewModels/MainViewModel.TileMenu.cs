using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

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
        UpdateWebSuspension();
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

    /// <summary>
    /// Ouvre le menu de la case : création sur une case vide, actions sur un bouton. En mode édition, c'est le menu complet de l'éditeur
    /// (copier, déplacer, dossier, réinitialiser…) : les panneaux autour du deck remplacent l'ancienne fenêtre d'édition.
    /// </summary>
    void OpenTileMenu(Place place, int index)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var button = _doc.Get(place, index);
        var title = button == null ? "Case vide" : button.Label.Length > 0 ? button.Label : "Bouton";
        IReadOnlyList<MenuItemSpec>? items = null;
        if (IsEdit && Editor is { } editor)
        {
            // Même grille : une simple sélection (la saisie en cours est validée), rien n'est reconstruit ; c'était un rechargement complet de l'éditeur
            // (cases, pages, écrans, propriétés) à chaque clic droit, 150 à 230 ms mesurés. Autre grille : l'éditeur s'y place.
            if (!editor.Place.Equals(place)) editor.GoTo(place, index);
            else if (editor.SelectedIndex != index) editor.SelectIndex(index);
            if (editor.Slots.FirstOrDefault(s => s.Index == index && !s.IsBack) is { } slot) items = editor.BuildSlotMenu(slot);
        }
        OpenSpecMenu(title, items ?? BuildTileMenu(place, index));
        if (started.ElapsedMilliseconds > 50) Log.Info($"Menu de la case « {title} » ouvert en {started.ElapsedMilliseconds} ms");
    }

    /// <summary>Affiche une description de menu dans la feuille du deck.</summary>
    void OpenSpecMenu(string title, IReadOnlyList<MenuItemSpec> items)
    {
        _profileMenu = false;
        _specTrail.Clear();
        _specTrail.Add(new SpecLevel(title, items));
        RenderSpecMenu();
        MenuOpen = true;
    }

    /// <summary>
    /// Menu de la case sélectionnée en mode édition (bouton « Case » du dock) : le même que le clic droit ou l'appui long, accessible d'un simple toucher.
    /// </summary>
    [RelayCommand]
    void OpenSelectedTileMenu()
    {
        if (Editor is not { SelectedIndex: >= 0 } editor) { Notifier.Info("Touche d'abord une case de la grille"); return; }
        OpenTileMenu(editor.Place, editor.SelectedIndex);
    }

    /// <summary>Options de la page affichée (renommer, dupliquer, masquer, supprimer…), en mode édition.</summary>
    [RelayCommand]
    void OpenPageMenu() => OpenPageMenuAt(_page);

    /// <summary>Menu d'une page (clic droit ou appui long sur son onglet), en mode édition.</summary>
    void OpenPageMenuAt(int index)
    {
        if (Editor is not { } editor || editor.Pages.FirstOrDefault(p => p.Index == index) is not { } page) return;
        OpenSpecMenu($"Page « {page.Name} »", editor.BuildPageMenu(page));
    }

    /// <summary>Glisser-déposer d'un onglet : la page <paramref name="from"/> prend le rang <paramref name="to"/> (annulable) ; la page affichée reste la même.</summary>
    public void MoveTab(int from, int to)
    {
        if (!IsEdit || from == to) return;
        int shown = _page;
        var result = _doc.MovePage(CurrentProfile.Name, from, to - from);
        if (!result.Ok) return;
        _page = shown == from ? to : from < shown && shown <= to ? shown - 1 : to <= shown && shown < from ? shown + 1 : shown;
        Editor?.GoTo(CurrentPlace, -1);
        Rebuild();
    }

    /// <summary>
    /// Options du profil affiché, en mode édition : changer de profil, format de la grille, puis renommer, dupliquer, exporter, importer,
    /// nouveau profil depuis un modèle et suppression.
    /// </summary>
    void OpenProfileEditMenu()
    {
        if (Editor is not { } editor) return;
        var items = new List<MenuItemSpec>();
        if (ProfileCount > 1)
            items.Add(new("Changer de profil", Icon: SymbolRegular.PersonSwap24, Children: _doc.Config.Profiles
                .Select(p => new MenuItemSpec(p.Name, () => SwitchProfile(p.Name), SymbolRegular.Person24, Checked: p.Name.Equals(CurrentProfile.Name, StringComparison.Ordinal))).ToList()));
        items.Add(new("Format de la grille", Icon: SymbolRegular.Grid24, Children: editor.Formats
            .Select(f => new MenuItemSpec(f.Text, () => editor.SelectedFormat = f, Checked: f.Equals(editor.SelectedFormat))).ToList()));
        items.Add(MenuItemSpec.Separator);
        items.AddRange(editor.BuildProfileMenu(CurrentProfile.Name));
        OpenSpecMenu($"Profil « {CurrentProfile.Name} »", items);
    }

    /// <summary>
    /// Menu d'une case. Case vide : Nouveau bouton, modèles en un clic, Coller. Bouton : Modifier, Dupliquer,
    /// Copier, Couper, Supprimer. Chaque entrée écrit une seule fois dans le document (un seul pas d'annulation).
    /// </summary>
    public IReadOnlyList<MenuItemSpec> BuildTileMenu(Place place, int index) =>
        _doc.Get(place, index) is { } button ? BuildButtonMenu(place, index, button) : BuildEmptyTileMenu(place, index);

    List<MenuItemSpec> BuildEmptyTileMenu(Place place, int index)
    {
        var items = new List<MenuItemSpec>
        {
            new("Nouveau bouton…", () => StartEditing(place, index), SymbolRegular.Add24),
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
        return items;
    }

    /// <summary>
    /// Menu d'un bouton, selon ce qu'il est : un dossier s'ouvre, un widget se règle, un bouton se modifie ou se remplace par une autre action.
    /// Les entrées d'édition ouvrent le mode édition sur la case, le bon champ prêt à la saisie.
    /// </summary>
    List<MenuItemSpec> BuildButtonMenu(Place place, int index, DeckButton button)
    {
        var widget = WidgetTypes.Find(button.Widget);
        var items = new List<MenuItemSpec>();
        if (button.IsFolder) items.Add(new("Ouvrir le dossier", () => { if (place.Equals(CurrentPlace)) EnterFolder(index); }, SymbolRegular.FolderOpen24));
        items.Add(new(widget != null ? $"Régler le widget « {widget.Name} »…" : button.IsFolder ? "Propriétés du dossier…" : "Modifier l'action…",
            () => StartEditing(place, index, focus: TileEditFocus.Properties), SymbolRegular.Edit24));
        items.Add(new("Renommer…", () => StartEditing(place, index), SymbolRegular.Rename24));
        if (widget == null) items.Add(new("Changer l'icône…", () => StartEditing(place, index, focus: TileEditFocus.Icon), SymbolRegular.Image24));
        items.Add(MenuItemSpec.Separator);
        items.Add(new("Dupliquer", () => DuplicateHere(place, index), SymbolRegular.CopyAdd24));
        items.Add(new("Copier", () => CopyHere(place, index), SymbolRegular.Copy24));
        items.Add(new("Couper", () => CutHere(place, index), SymbolRegular.Cut24));
        items.Add(new("Déplacer…", () =>
        {
            StartEditing(place, index, focus: TileEditFocus.None);
            Notifier.Info("Glisse la touche vers une autre case (ou Alt + flèches)");
        }, SymbolRegular.ArrowMove24));
        if (!button.IsFolder)
            items.Add(new("Remplacer par", Icon: SymbolRegular.ArrowSwap24, Children: CatalogMenu.Categories(a => ReplaceFromCatalog(a, place, index))));
        items.Add(MenuItemSpec.Separator);
        items.Add(new(button.IsFolder ? "Supprimer le dossier" : widget != null ? "Supprimer le widget" : "Supprimer", () => DeleteHere(place, index, button.IsFolder),
            SymbolRegular.Delete24, Danger: true));
        return items;
    }

    /// <summary>Remplace le bouton par une action du catalogue, en une modification annulable ; si l'action demande un réglage, l'édition s'ouvre dessus.</summary>
    void ReplaceFromCatalog(CatalogAction action, Place place, int index)
    {
        var button = action.CreateButton();
        var result = _doc.PutButton(place, index, button, $"Remplacer par « {action.Name} »");
        if (!result.Ok) { Notifier.Error(result.Message ?? "Remplacement impossible"); return; }
        _doc.AddRecent(action.Id);
        if (QuickCreate.NeedsSetup(button, _doc.Config, _doc.FindProfile(place.Profile)))
        {
            StartEditing(place, index, focus: TileEditFocus.Properties);
            Notifier.Info($"« {action.Name} » : complète ses réglages à droite");
            return;
        }
        Notifier.Success($"Remplacé par « {action.Name} »", "Annuler", () => _doc.Undo());
    }

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
