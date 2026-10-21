using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchDeck;

/// <summary>Emplacement d'une grille : profil, page et chemin de dossiers (indices successifs) ; chemin vide = racine de la page.</summary>
public sealed class Place : IEquatable<Place>
{
    /// <summary>Nom du profil.</summary>
    public string Profile { get; }
    /// <summary>Index de la page dans le profil.</summary>
    public int Page { get; }
    /// <summary>Index des dossiers ouverts depuis la racine de la page (vide : la page elle-même).</summary>
    public IReadOnlyList<int> Path { get; }

    /// <summary>Emplacement d'une grille.</summary>
    /// <param name="profile">Nom du profil.</param>
    /// <param name="page">Index de la page.</param>
    /// <param name="path">Dossiers ouverts depuis la racine ; null ou vide : la page.</param>
    public Place(string profile, int page, IReadOnlyList<int>? path = null)
    {
        Profile = profile; Page = page; Path = path ?? Array.Empty<int>();
    }

    /// <summary>true pour la grille de la page elle-même (aucun dossier ouvert).</summary>
    public bool IsRoot => Path.Count == 0;
    /// <summary>Emplacement du dossier situé à cet index de la grille.</summary>
    public Place Enter(int folderIndex) => new(Profile, Page, Path.Append(folderIndex).ToArray());
    /// <summary>Dossier parent, ou null à la racine de la page.</summary>
    public Place? Parent => IsRoot ? null : new Place(Profile, Page, Path.Take(Path.Count - 1).ToArray());

    /// <summary>Même profil (sans distinction de casse), même page, même chemin de dossiers.</summary>
    public bool Equals(Place? o) =>
        o != null && Profile.Equals(o.Profile, StringComparison.OrdinalIgnoreCase) && Page == o.Page && Path.SequenceEqual(o.Path);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Place);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Profile.ToLowerInvariant(), Page, Path.Count);
    /// <summary>Forme lisible pour le journal : « profil/page/dossiers ».</summary>
    public override string ToString() => $"{Profile}/{Page}/{string.Join(".", Path)}";
}

/// <summary>Nature d'une modification du document : les vues choisissent ce qu'elles redessinent.</summary>
public enum ChangeKind
{
    /// <summary>Boutons, pages ou profils modifiés (annulable).</summary>
    Content,
    /// <summary>Réglages, favoris, récents (non annulable).</summary>
    Settings,
    /// <summary>Annuler, rétablir, import ou rechargement : tout l'état peut avoir changé.</summary>
    Replaced,
}

/// <summary>Modification signalée par <see cref="DeckDocument.Changed"/>.</summary>
/// <param name="Kind">Nature de la modification.</param>
/// <param name="Label">Libellé affiché dans « Annuler … ».</param>
/// <param name="InPlace">true si un seul bouton a été modifié sur place (propriétés, titre, icône, étapes) : ni le nombre de boutons,
/// ni les pages, ni les profils, ni les réglages n'ont changé, les vues peuvent se contenter d'une mise à jour ciblée.</param>
public sealed record DocumentChange(ChangeKind Kind, string Label, bool InPlace = false);

/// <summary>Résultat d'une opération du document.</summary>
/// <param name="Ok">true si l'opération a été faite.</param>
/// <param name="Message">Raison du refus, à afficher.</param>
/// <param name="Index">Index créé ou déplacé, -1 sinon.</param>
public sealed record OpResult(bool Ok, string? Message = null, int Index = -1)
{
    /// <summary>Opération réussie, avec l'index concerné.</summary>
    public static OpResult Success(int index = -1) => new(true, null, index);
    /// <summary>Opération refusée, avec la raison.</summary>
    public static OpResult Fail(string message) => new(false, message);
}

/// <summary>
/// État éditable de la configuration, partagé par le deck et l'éditeur : toutes les modifications passent ici,
/// avec historique annuler / rétablir, presse-papiers interne et notification des changements. Aucune dépendance à WPF.
/// </summary>
public sealed partial class DeckDocument
{
    const int MaxHistory = 100;
    static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1.5);

    readonly List<(string Json, string Label)> _undo = new();
    readonly List<(string Json, string Label)> _redo = new();
    string? _lastCoalesceKey;
    DateTime _lastCoalesceAt;

    /// <summary>Configuration courante ; remplacée par <see cref="Reset"/>, modifiée par les méthodes du document.</summary>
    public DeckConfig Config { get; private set; }
    /// <summary>Presse-papiers interne (copie indépendante d'un bouton), partagé par le deck et l'éditeur.</summary>
    public DeckButton? Clipboard { get; private set; }

    /// <summary>Chaque modification, après coup : sauvegarde automatique, deck, éditeur et paramètres s'y abonnent.</summary>
    public event Action<DocumentChange>? Changed;

    /// <summary>Document sur une configuration déjà normalisée.</summary>
    public DeckDocument(DeckConfig config) { Config = config; }

    /// <summary>true s'il y a une modification à annuler.</summary>
    public bool CanUndo => _undo.Count > 0;
    /// <summary>true s'il y a une modification à rétablir.</summary>
    public bool CanRedo => _redo.Count > 0;
    /// <summary>Libellé de la modification que « Annuler » défera, ou null.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    /// <summary>Libellé de la modification que « Rétablir » refera, ou null.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;
    /// <summary>true si le presse-papiers interne contient un bouton.</summary>
    public bool HasClipboard => Clipboard != null;

    // ---------- historique ----------

    string Snapshot() => JsonSerializer.Serialize(Config, ConfigStore.Options);
    static DeckConfig Restore(string json) => JsonSerializer.Deserialize<DeckConfig>(json, ConfigStore.Options)!;

    void Push(string snapshot, string label, string? coalesceKey, bool coalesce)
    {
        if (coalesce)
        {
            _lastCoalesceAt = DateTime.UtcNow;   // même série de frappes : on garde l'état d'avant la première
            _redo.Clear();
            return;
        }
        _undo.Add((snapshot, label));
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
        _lastCoalesceKey = coalesceKey;
        _lastCoalesceAt = DateTime.UtcNow;
    }

    /// <summary>true si cette modification prolonge la série de frappes précédente : l'état d'avant la série est déjà dans l'historique.</summary>
    bool Coalesces(string? coalesceKey) =>
        coalesceKey != null && coalesceKey == _lastCoalesceKey && DateTime.UtcNow - _lastCoalesceAt < CoalesceWindow && _undo.Count > 0;

    /// <summary>Applique une modification annulable. <paramref name="mutate"/> retourne false (sans rien modifier) pour refuser.</summary>
    OpResult Edit(string label, Func<DeckConfig, OpResult> mutate, string? coalesceKey = null, bool inPlace = false)
    {
        // Le cliché (sérialisation de toute la configuration) ne sert qu'au début d'une série : on l'évite pour les frappes suivantes
        bool coalesce = Coalesces(coalesceKey);
        var snapshot = coalesce ? "" : Snapshot();
        var result = mutate(Config);
        if (!result.Ok) return result;
        Push(snapshot, label, coalesceKey, coalesce);
        Changed?.Invoke(new DocumentChange(ChangeKind.Content, label, inPlace));
        return result;
    }

    /// <summary>Annule la dernière modification de contenu ; false s'il n'y a rien à annuler.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var (json, label) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((Snapshot(), label));
        Config = Restore(json);
        _lastCoalesceKey = null;
        Changed?.Invoke(new DocumentChange(ChangeKind.Replaced, label));
        return true;
    }

    /// <summary>Rétablit la dernière modification annulée ; false s'il n'y a rien à rétablir.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var (json, label) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((Snapshot(), label));
        Config = Restore(json);
        _lastCoalesceKey = null;
        Changed?.Invoke(new DocumentChange(ChangeKind.Replaced, label));
        return true;
    }

    /// <summary>Remplace tout le contenu (import, restauration) ; l'opération est annulable.</summary>
    public void ReplaceAll(DeckConfig cfg, string label)
    {
        var snapshot = Snapshot();
        Config = cfg;
        Push(snapshot, label, null, false);
        Changed?.Invoke(new DocumentChange(ChangeKind.Replaced, label));
    }

    /// <summary>Rechargement depuis le disque (modification externe) : l'historique n'a plus de sens.</summary>
    public void Reset(DeckConfig cfg)
    {
        Config = cfg;
        _undo.Clear(); _redo.Clear(); _lastCoalesceKey = null;
        Changed?.Invoke(new DocumentChange(ChangeKind.Replaced, "Rechargement"));
    }

    /// <summary>Modification hors historique (réglages, favoris).</summary>
    public void UpdateSettings(Action<DeckConfig> change, string label = "Réglages")
    {
        change(Config);
        ConfigNormalizer.Normalize(Config);
        Changed?.Invoke(new DocumentChange(ChangeKind.Settings, label));
    }

    // ---------- lecture ----------

    /// <summary>Profil de ce nom (sans distinction de casse), ou null.</summary>
    public DeckProfile? FindProfile(string? name) => ConfigNormalizer.FindProfile(Config, name);
    /// <summary>Profil actif, ou le premier si le nom enregistré n'existe plus.</summary>
    public DeckProfile ActiveProfile => FindProfile(Config.ActiveProfile) ?? Config.Profiles[0];

    /// <summary>Page d'un profil, ou null si le profil ou l'index n'existe pas.</summary>
    public DeckPage? FindPage(string profile, int page)
    {
        var p = FindProfile(profile);
        return p != null && page >= 0 && page < p.Pages.Count ? p.Pages[page] : null;
    }

    /// <summary>Liste des boutons de la grille désignée (page ou contenu d'un dossier), ou null si le chemin n'existe plus.</summary>
    public List<DeckButton>? Container(Place place)
    {
        var page = FindPage(place.Profile, place.Page);
        if (page == null) return null;
        var list = page.Buttons;
        foreach (var i in place.Path)
        {
            if (i < 0 || i >= list.Count || !list[i].IsFolder) return null;
            list = list[i].Children ??= new List<DeckButton>();
        }
        return list;
    }

    /// <summary>Le dossier désigné par un chemin non vide.</summary>
    public DeckButton? FolderAt(Place folderPlace)
    {
        if (folderPlace.IsRoot) return null;
        var parent = Container(folderPlace.Parent!);
        int i = folderPlace.Path[^1];
        return parent != null && i >= 0 && i < parent.Count && parent[i].IsFolder ? parent[i] : null;
    }

    /// <summary>Bouton à cet index de la grille, ou null (index hors liste ou case vide).</summary>
    public DeckButton? Get(Place place, int index)
    {
        var list = Container(place);
        if (list == null || index < 0 || index >= list.Count) return null;
        var b = list[index];
        return b.Blank ? null : b;
    }

    /// <summary>Premier index libre (case vide ou fin de liste) à partir de <paramref name="from"/>.</summary>
    public int FirstEmptyIndex(Place place, int from = 0)
    {
        var list = Container(place);
        if (list == null) return -1;
        for (int i = Math.Max(0, from); i < list.Count; i++) if (list[i].Blank) return i;
        return Math.Max(list.Count, from);
    }

    /// <summary>true si <paramref name="candidate"/> est le dossier désigné par (<paramref name="folderParent"/>, <paramref name="folderIndex"/>) ou se trouve dedans.</summary>
    static bool IsInsideFolder(Place candidate, Place folderParent, int folderIndex) =>
        candidate.Profile.Equals(folderParent.Profile, StringComparison.OrdinalIgnoreCase) && candidate.Page == folderParent.Page
        && candidate.Path.Count > folderParent.Path.Count
        && candidate.Path.Take(folderParent.Path.Count).SequenceEqual(folderParent.Path)
        && candidate.Path[folderParent.Path.Count] == folderIndex;

    static void Trim(List<DeckButton> list)
    {
        while (list.Count > 0 && list[^1].Blank) list.RemoveAt(list.Count - 1);
    }

    static void Pad(List<DeckButton> list, int index)
    {
        while (list.Count <= index) list.Add(new DeckButton { Blank = true });
    }

    static DeckButton? Take(List<DeckButton> list, int index) =>
        index >= 0 && index < list.Count && !list[index].Blank ? list[index] : null;

    static void Put(List<DeckButton> list, int index, DeckButton? b)
    {
        if (b == null)
        {
            if (index < list.Count) list[index] = new DeckButton { Blank = true };
            return;
        }
        Pad(list, index);
        list[index] = b;
    }

    // ---------- boutons ----------

    /// <summary>Place un bouton (copie) à cet index, en remplaçant le contenu éventuel ; annulable.</summary>
    public OpResult PutButton(Place place, int index, DeckButton button, string label = "Modifier le bouton") =>
        Edit(label, _ =>
        {
            var list = Container(place);
            if (list == null || index < 0) return OpResult.Fail("Emplacement introuvable");
            Put(list, index, button);
            return OpResult.Success(index);
        });

    /// <summary>Modifie le bouton en place (panneau de propriétés). <paramref name="coalesceKey"/> regroupe les frappes successives en une seule annulation.</summary>
    public OpResult UpdateButton(Place place, int index, Action<DeckButton> edit, string label, string? coalesceKey = null) =>
        Edit(label, _ =>
        {
            var b = Get(place, index);
            if (b == null) return OpResult.Fail("Aucun bouton à modifier");
            edit(b);
            return OpResult.Success(index);
        }, coalesceKey, inPlace: true);

    /// <summary>Vide la case (les voisines gardent leur position) ; annulable.</summary>
    public OpResult ClearButton(Place place, int index, string label = "Supprimer le bouton") =>
        Edit(label, _ =>
        {
            var list = Container(place);
            if (list == null || Get(place, index) == null) return OpResult.Fail("Rien à supprimer");
            Put(list, index, null);
            Trim(list);
            return OpResult.Success(index);
        });

    /// <summary>Déplace le bouton ; si la cible est occupée, les deux boutons échangent leur place.</summary>
    public OpResult MoveButton(Place src, int srcIndex, Place dst, int dstIndex)
    {
        if (src.Equals(dst) && srcIndex == dstIndex) return OpResult.Fail("Même emplacement");
        return Edit("Déplacer le bouton", _ =>
        {
            var from = Container(src); var to = Container(dst);
            if (from == null || to == null || dstIndex < 0) return OpResult.Fail("Emplacement introuvable");
            var moving = Take(from, srcIndex);
            if (moving == null) return OpResult.Fail("Aucun bouton à déplacer");
            var displaced = Take(to, dstIndex);
            if (moving.IsFolder && IsInsideFolder(dst, src, srcIndex))
                return OpResult.Fail("Un dossier ne peut pas être déplacé dans lui-même");
            if (displaced is { IsFolder: true } && IsInsideFolder(src, dst, dstIndex))
                return OpResult.Fail("Cet échange placerait un dossier dans lui-même");
            Put(from, srcIndex, null);
            Put(to, dstIndex, moving);
            if (displaced != null) Put(from, srcIndex, displaced);
            Trim(from); Trim(to);
            return OpResult.Success(dstIndex);
        });
    }

    /// <summary>Range le bouton dans un dossier (à la première case libre).</summary>
    public OpResult MoveIntoFolder(Place src, int srcIndex, Place folderContainer, int folderIndex)
    {
        return Edit("Déplacer dans le dossier", _ =>
        {
            var from = Container(src); var parent = Container(folderContainer);
            if (from == null || parent == null) return OpResult.Fail("Emplacement introuvable");
            var moving = Take(from, srcIndex);
            var folder = Take(parent, folderIndex);
            if (moving == null) return OpResult.Fail("Aucun bouton à déplacer");
            if (folder is not { IsFolder: true }) return OpResult.Fail("La cible n'est pas un dossier");
            if (ReferenceEquals(moving, folder) || (moving.IsFolder && IsInsideFolder(folderContainer.Enter(folderIndex), src, srcIndex)))
                return OpResult.Fail("Un dossier ne peut pas être déplacé dans lui-même");
            var target = folderContainer.Enter(folderIndex);
            Put(from, srcIndex, null);
            Trim(from);
            var children = folder.Children ??= new List<DeckButton>();
            int at = children.FindIndex(c => c.Blank);
            if (at < 0) at = children.Count;
            Put(children, at, moving);
            return OpResult.Success(at);
        });
    }

    /// <summary>Remonte le bouton d'un dossier vers la grille parente (première case libre).</summary>
    public OpResult MoveToParent(Place src, int srcIndex)
    {
        var parentPlace = src.Parent;
        if (parentPlace == null) return OpResult.Fail("Ce bouton n'est pas dans un dossier");
        return Edit("Sortir du dossier", _ =>
        {
            var from = Container(src); var to = Container(parentPlace);
            var moving = from == null ? null : Take(from, srcIndex);
            if (from == null || to == null || moving == null) return OpResult.Fail("Aucun bouton à déplacer");
            Put(from, srcIndex, null);
            Trim(from);
            int at = to.FindIndex(c => c.Blank);
            if (at < 0) at = to.Count;
            Put(to, at, moving);
            return OpResult.Success(at);
        });
    }

    /// <summary>Crée un dossier vide à cet index ; annulable.</summary>
    public OpResult CreateFolder(Place place, int index, string name = "Dossier") =>
        Edit("Créer un dossier", _ =>
        {
            var list = Container(place);
            if (list == null || index < 0) return OpResult.Fail("Emplacement introuvable");
            if (Take(list, index) != null) return OpResult.Fail("La case n'est pas vide");
            Put(list, index, NewFolder(name));
            return OpResult.Success(index);
        });

    /// <summary>Nouveau bouton dossier (icône et couleur par défaut, aucun enfant).</summary>
    public static DeckButton NewFolder(string name = "Dossier") => new()
    {
        Label = name, Icon = "Folder24", Color = "#B45309", IsFolder = true, Children = new List<DeckButton>(),
    };

    // ---------- presse-papiers ----------

    /// <summary>Copie le bouton dans le presse-papiers interne ; false si la case est vide.</summary>
    public bool Copy(Place place, int index)
    {
        var b = Get(place, index);
        if (b == null) return false;
        Clipboard = ConfigStore.Clone(b);
        return true;
    }

    /// <summary>Copie le bouton dans le presse-papiers interne puis vide la case ; annulable.</summary>
    public OpResult Cut(Place place, int index)
    {
        var b = Get(place, index);
        if (b == null) return OpResult.Fail("Rien à couper");
        var copy = ConfigStore.Clone(b);
        var result = ClearButton(place, index, "Couper le bouton");
        if (result.Ok) Clipboard = copy;
        return result;
    }

    /// <summary>Colle une copie du presse-papiers à cet index ; annulable.</summary>
    public OpResult Paste(Place place, int index)
    {
        if (Clipboard == null) return OpResult.Fail("Le presse-papiers est vide");
        var copy = ConfigStore.Clone(Clipboard);
        return PutButton(place, index, copy, "Coller le bouton");
    }

    /// <summary>Copie le bouton dans la prochaine case libre ; annulable.</summary>
    public OpResult Duplicate(Place place, int index)
    {
        var b = Get(place, index);
        if (b == null) return OpResult.Fail("Rien à dupliquer");
        return Edit("Dupliquer le bouton", _ =>
        {
            var list = Container(place)!;
            int at = FirstEmptyIndex(place, index + 1);
            Put(list, at, ConfigStore.Clone(b));
            return OpResult.Success(at);
        });
    }

    /// <summary>Crée un bouton dans une case vide (refuse une case occupée) ; une seule modification annulable.</summary>
    public OpResult AddButton(Place place, int index, DeckButton button, string label = "Ajouter un bouton") =>
        AddButtons(place, index, new[] { button }, label);

    /// <summary>Crée un bouton dans la première case libre de la grille désignée ; une seule modification annulable.</summary>
    public OpResult AddButtonFirstFree(Place place, DeckButton button, string label = "Ajouter un bouton") =>
        AddButtons(place, FirstEmptyIndex(place), new[] { button }, label);

    /// <summary>
    /// Crée plusieurs boutons : le premier dans la case vide <paramref name="startIndex"/>, les suivants dans les cases libres qui suivent.
    /// Une seule modification annulable, quel que soit le nombre de boutons ; le résultat porte l'index du premier.
    /// </summary>
    public OpResult AddButtons(Place place, int startIndex, IReadOnlyList<DeckButton> buttons, string label = "Ajouter des boutons")
    {
        if (buttons.Count == 0 || buttons.Any(b => b == null || b.Blank)) return OpResult.Fail("Aucun bouton à ajouter");
        return Edit(label, _ =>
        {
            var list = Container(place);
            if (list == null || startIndex < 0) return OpResult.Fail("Emplacement introuvable");
            if (Take(list, startIndex) != null) return OpResult.Fail("La case n'est pas vide");
            int at = startIndex;
            foreach (var b in buttons)
            {
                Put(list, at, b);
                at = FirstEmptyIndex(place, at + 1);
            }
            return OpResult.Success(startIndex);
        });
    }

    // ---------- favoris et récents ----------

    /// <summary>Ajoute ou retire une action du catalogue des favoris (réglage, hors historique).</summary>
    public void ToggleFavorite(string id) => UpdateSettings(c =>
    {
        if (!c.Favorites.Remove(id)) c.Favorites.Add(id);
    }, "Favoris");

    /// <summary>Place une action du catalogue en tête des récents (réglage, hors historique).</summary>
    public void AddRecent(string id) => UpdateSettings(c =>
    {
        c.Recents.Remove(id);
        c.Recents.Insert(0, id);
        if (c.Recents.Count > ConfigNormalizer.MaxRecents) c.Recents.RemoveRange(ConfigNormalizer.MaxRecents, c.Recents.Count - ConfigNormalizer.MaxRecents);
    }, "Récents");

    // ---------- utilitaires ----------

    /// <summary>Toutes les actions des boutons donnés, dossiers compris.</summary>
    public static IEnumerable<DeckAction> AllActions(IEnumerable<DeckButton> buttons)
    {
        foreach (var b in buttons)
        {
            foreach (var a in b.Actions) yield return a;
            if (b.IsFolder && b.Children != null)
                foreach (var a in AllActions(b.Children)) yield return a;
        }
    }

    /// <summary>Tous les boutons de la liste, contenu des dossiers compris (parcours en profondeur).</summary>
    public static IEnumerable<DeckButton> AllButtons(IEnumerable<DeckButton> buttons)
    {
        foreach (var b in buttons)
        {
            if (b.Blank) continue;
            yield return b;
            if (b.IsFolder && b.Children != null)
                foreach (var c in AllButtons(b.Children)) yield return c;
        }
    }
}
