using System;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace TouchDeck.ViewModels;

public sealed partial class CatalogItemViewModel : ObservableObject
{
    /// <summary>Action du catalogue représentée.</summary>
    public CatalogAction Action { get; }
    /// <summary>Icône Fluent de l'action.</summary>
    public SymbolRegular Symbol { get; }
    /// <summary>Couleur de la pastille (celle du bouton créé).</summary>
    public Brush Color { get; }
    /// <summary>Nom affiché.</summary>
    public string Name => Action.Name;
    /// <summary>Description courte affichée sous le nom.</summary>
    public string Description => Action.Description;
    /// <summary>Catégorie d'origine (affichée dans les sections Favoris et Récents).</summary>
    public string Category => Action.Category;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FavoriteGlyph), nameof(FavoriteName))] bool _isFavorite;
    /// <summary>Étoile pleine ou vide selon l'état favori.</summary>
    public SymbolRegular FavoriteGlyph => IsFavorite ? SymbolRegular.Star24 : SymbolRegular.StarOff24;
    /// <summary>Nom accessible du bouton favori, qui dit ce que fait le prochain appui.</summary>
    public string FavoriteName => IsFavorite ? $"Retirer {Name} des favoris" : $"Ajouter {Name} aux favoris";
    /// <summary>Nom accessible de la ligne : nom et description.</summary>
    public string AutomationName => $"{Name} — {Description}";
    /// <summary>Ajoute ou retire l'action des favoris.</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Ligne du catalogue.</summary>
    /// <param name="action">Action représentée.</param>
    /// <param name="favorite">État favori initial.</param>
    /// <param name="toggle">Appelé par l'étoile : le catalogue met à jour les favoris du document.</param>
    public CatalogItemViewModel(CatalogAction action, bool favorite, Action<CatalogItemViewModel> toggle)
    {
        Action = action;
        _isFavorite = favorite;
        Symbol = IconCatalog.TryResolve(action.Icon, out var s) ? s : SymbolRegular.Apps24;
        Color = Theme.TryBrush(action.Color, out var b) ? b : Theme.B(Theme.SurfaceHi);
        ToggleFavoriteCommand = new RelayCommand(() => toggle(this));
    }
}

public sealed partial class CatalogSectionViewModel : ObservableObject
{
    /// <summary>Nom de la catégorie (ou « Favoris », « Récents », « Résultats »).</summary>
    public string Title { get; }
    /// <summary>Icône de la section.</summary>
    public SymbolRegular Symbol { get; }
    /// <summary>Actions de la section.</summary>
    public ObservableCollection<CatalogItemViewModel> Items { get; } = new();
    [ObservableProperty] bool _isExpanded;
    /// <summary>Nombre d'actions, affiché à côté du titre.</summary>
    public string CountText => Items.Count == 1 ? "1 action" : $"{Items.Count} actions";
    /// <summary>Nom accessible de l'en-tête : titre et nombre d'actions.</summary>
    public string AutomationName => $"{Title}, {CountText}";

    /// <summary>Section repliable ; <paramref name="expanded"/> : état initial (mémorisé par la vue).</summary>
    public CatalogSectionViewModel(string title, SymbolRegular symbol, bool expanded)
    {
        Title = title; Symbol = symbol; _isExpanded = expanded;
    }
}

/// <summary>Catalogue d'actions : favoris, récents, catégories repliables et recherche instantanée.</summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    const string FavoritesTitle = "Favoris";
    const string RecentsTitle = "Récemment utilisées";

    readonly DeckDocument _doc;
    // État replié / déplié conservé pendant la session, par section
    readonly Dictionary<string, bool> _expanded = new() { [FavoritesTitle] = true, [RecentsTitle] = true };
    bool _refreshing;

    /// <summary>Sections affichées : favoris, récents puis catégories, ou résultats de recherche.</summary>
    public ObservableCollection<CatalogSectionViewModel> Sections { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSearching))] string _search = "";
    [ObservableProperty] bool _hasResults = true;
    [ObservableProperty] string _resultsText = "";

    /// <summary>true pendant une recherche (une seule section de résultats).</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(Search);

    /// <summary>Catalogue de l'éditeur ; les favoris et récents sont lus dans le document.</summary>
    public CatalogViewModel(DeckDocument doc)
    {
        _doc = doc;
        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    [RelayCommand] void ClearSearch() => Search = "";

    /// <summary>Ligne affichée pour cette action, ou null.</summary>
    public CatalogItemViewModel? FindItem(string id) =>
        Sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Action.Id == id);

    /// <summary>Reconstruit les sections (favoris, récents ou recherche modifiés).</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            Sections.Clear();
            var cfg = _doc.Config;
            var matches = ActionCatalog.Search(Search).ToList();
            bool searching = IsSearching;

            CatalogItemViewModel Make(CatalogAction a) => new(a, cfg.Favorites.Contains(a.Id), Toggle);

            var favs = cfg.Favorites.Select(ActionCatalog.Find).Where(a => a != null && matches.Contains(a!)).Select(a => a!).ToList();
            if (favs.Count > 0) AddSection(FavoritesTitle, SymbolRegular.Star24, favs.Select(Make), searching);

            if (!searching)
            {
                var recents = cfg.Recents.Select(ActionCatalog.Find).Where(a => a != null).Select(a => a!).ToList();
                if (recents.Count > 0) AddSection(RecentsTitle, SymbolRegular.History24, recents.Select(Make), false);
            }

            foreach (var cat in ActionCatalog.Categories)
            {
                var items = matches.Where(a => a.Category == cat).Select(Make).ToList();
                if (items.Count == 0) continue;
                var icon = IconCatalog.TryResolve(ActionCatalog.CategoryIcons.GetValueOrDefault(cat, "Apps24"), out var s) ? s : SymbolRegular.Apps24;
                AddSection(cat, icon, items, searching);
            }

            HasResults = Sections.Count > 0;
            ResultsText = !searching ? "" : matches.Count == 0 ? $"Aucune action ne correspond à « {Search.Trim()} »"
                : matches.Count == 1 ? "1 action trouvée" : $"{matches.Count} actions trouvées";
        }
        finally { _refreshing = false; }
    }

    void AddSection(string title, SymbolRegular icon, IEnumerable<CatalogItemViewModel> items, bool forceOpen)
    {
        bool open = forceOpen || (_expanded.TryGetValue(title, out var e) && e);
        var section = new CatalogSectionViewModel(title, icon, open);
        foreach (var i in items) section.Items.Add(i);
        section.PropertyChanged += (_, args) =>
        {
            // Pendant une recherche, tout est ouvert d'office : on ne retient pas ce réglage
            if (args.PropertyName == nameof(CatalogSectionViewModel.IsExpanded) && !_refreshing && !IsSearching)
                _expanded[title] = section.IsExpanded;
        };
        Sections.Add(section);
    }

    void Toggle(CatalogItemViewModel item) => _doc.ToggleFavorite(item.Action.Id);
}
