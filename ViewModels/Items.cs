using System.Windows.Input;
using System.Windows.Media;

namespace TouchDeck.ViewModels;

/// <summary>Bouton de la grille du deck.</summary>
public sealed class DeckTileViewModel
{
    public required TileFaceData Face { get; init; }
    public string Label { get; init; } = "";
    public string AccessibleName { get; init; } = "";
    public bool IsEdit { get; init; }
    public required ICommand Click { get; init; }
}

/// <summary>Case vide hors édition : réserve la place pour que les voisins gardent leur position.</summary>
public sealed class SpacerTileViewModel { }

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
