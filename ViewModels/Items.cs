using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

/// <summary>Bouton de la grille.</summary>
public sealed class DeckTileViewModel
{
    public string Icon { get; init; } = "";
    public string Label { get; init; } = "";
    public Brush Background { get; init; } = Brushes.Gray;
    public bool IsEdit { get; init; }
    public bool HasIcon => Icon.Length > 0;
    public bool HasLabel => Label.Length > 0;
    public required ICommand Click { get; init; }
}

/// <summary>Emplacement « + » affiché en mode édition.</summary>
public sealed class AddTileViewModel
{
    public required ICommand Click { get; init; }
}

public sealed class TabViewModel
{
    public string Text { get; init; } = "";
    public bool IsActive { get; init; }
    public required ICommand Command { get; init; }
}

public sealed class MenuEntryViewModel
{
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; init; }
    public string Text { get; init; } = "";
    public string? Trailing { get; init; }
    public bool IsDanger { get; init; }
    public required ICommand Command { get; init; }
}

public sealed class MenuSeparatorViewModel { }
