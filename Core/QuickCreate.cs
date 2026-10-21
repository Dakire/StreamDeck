using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchDeck;

/// <summary>Modèle de bouton proposé en un clic dans le menu d'une case vide : une entrée du catalogue d'actions.</summary>
public sealed record QuickTemplate(string Title, string CatalogId, string Icon);

/// <summary>
/// Création rapide d'un bouton depuis le deck : modèles en un clic, recherche assistée dans le catalogue d'actions (natives,
/// applications cibles, intégrations) et contrôle de ce qui reste à compléter. Aucune écriture : le document reçoit le bouton fini.
/// </summary>
public static class QuickCreate
{
    /// <summary>Nombre de suggestions affichées sous la zone de recherche.</summary>
    public const int MaxSuggestions = 8;

    /// <summary>Modèles du menu d'une case vide, dans l'ordre d'affichage.</summary>
    public static readonly IReadOnlyList<QuickTemplate> Templates = new QuickTemplate[]
    {
        new("Raccourci clavier", "key.hotkey", "Keyboard24"),
        new("Lancer une application", "app.launch", "Rocket24"),
        new("Ouvrir un site", "web.url", "Link24"),
        new("Volume / muet", "audio.mute", "SpeakerMute24"),
        new("Média : lecture / pause", "media.playpause", "Play24"),
        new("Média : suivant", "media.next", "Next24"),
        new("Script", "auto.script", "Flash24"),
        new("Dossier", "nav.folder", "Folder24"),
        new("Page", "nav.page", "Document24"),
    };

    /// <summary>Bouton neuf d'après un identifiant du catalogue, ou null s'il n'existe plus (extension retirée).</summary>
    public static DeckButton? CreateFromCatalog(string? catalogId) => ActionCatalog.Find(catalogId)?.CreateButton();

    /// <summary>
    /// Actions du catalogue qui répondent à une saisie, la plus proche d'abord : nom exact, début du nom, début d'un mot du nom,
    /// puis simple présence (nom, description, catégorie, mots-clés). « chrome » propose Chrome, « vol » propose Volume.
    /// Une saisie vide ne propose rien (l'appelant affiche alors favoris et récents).
    /// </summary>
    public static IReadOnlyList<CatalogAction> Rank(string? query, int max = MaxSuggestions, IEnumerable<CatalogAction>? source = null)
    {
        var folded = ActionCatalog.Fold(query ?? "").Trim();
        if (folded.Length == 0) return Array.Empty<CatalogAction>();
        return ActionCatalog.Search(query, source)
            .Select((a, order) => (Action: a, Score: Score(ActionCatalog.Fold(a.Name), folded), order))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Action.Name.Length)
            .ThenBy(x => x.order)
            .Take(Math.Max(1, max))
            .Select(x => x.Action)
            .ToList();
    }

    static int Score(string name, string query)
    {
        if (name == query) return 100;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 80;
        if (name.Split(' ', ':', '-', '/').Any(w => w.StartsWith(query, StringComparison.Ordinal))) return 60;
        return name.Contains(query, StringComparison.Ordinal) ? 40 : 20;
    }

    /// <summary>true si le bouton créé doit encore être complété (raccourci, chemin, adresse…) : l'appelant ouvre alors l'éditeur sur sa case.</summary>
    public static bool NeedsSetup(DeckButton button, DeckConfig config, DeckProfile? profile) =>
        !button.IsFolder && ActionValidator.Validate(button, config, profile).Any(i => i.Severity == Severity.Error);
}
