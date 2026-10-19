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
    public CatalogAction Action { get; }
    public SymbolRegular Symbol { get; }
    public Brush Color { get; }
    public string Name => Action.Name;
    public string Description => Action.Description;
    public string Category => Action.Category;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FavoriteGlyph), nameof(FavoriteName))] bool _isFavorite;
    public SymbolRegular FavoriteGlyph => IsFavorite ? SymbolRegular.Star24 : SymbolRegular.StarOff24;
    public string FavoriteName => IsFavorite ? $"Retirer {Name} des favoris" : $"Ajouter {Name} aux favoris";
    public string AutomationName => $"{Name} — {Description}";
    public ICommand ToggleFavoriteCommand { get; }

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
    public string Title { get; }
    public SymbolRegular Symbol { get; }
    public ObservableCollection<CatalogItemViewModel> Items { get; } = new();
    [ObservableProperty] bool _isExpanded;
    public string CountText => Items.Count == 1 ? "1 action" : $"{Items.Count} actions";
    public string AutomationName => $"{Title}, {CountText}";

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

    public ObservableCollection<CatalogSectionViewModel> Sections { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSearching))] string _search = "";
    [ObservableProperty] bool _hasResults = true;
    [ObservableProperty] string _resultsText = "";

    public bool IsSearching => !string.IsNullOrWhiteSpace(Search);

    public CatalogViewModel(DeckDocument doc)
    {
        _doc = doc;
        Refresh();
    }

    partial void OnSearchChanged(string value) => Refresh();

    [RelayCommand] void ClearSearch() => Search = "";

    public CatalogItemViewModel? FindItem(string id) =>
        Sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Action.Id == id);

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
