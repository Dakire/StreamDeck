using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

/// <summary>Retour visuel d'un bouton du deck : en cours (après un court délai), réussi ou en erreur, puis retour au repos.</summary>
public enum TileRunState { Idle, Running, Success, Error }

/// <summary>Bouton de la grille du deck. La face et l'état changent sans reconstruire la grille.</summary>
public sealed partial class DeckTileViewModel : ObservableObject
{
    [ObservableProperty] TileFaceData _face = TileFaceData.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsSuccess), nameof(IsError))] TileRunState _runState;
    [ObservableProperty] string _accessibleName = "";

    /// <summary>Bouton de la configuration représenté (null pour la case « retour »).</summary>
    public DeckButton? Button { get; init; }
    public string Label { get; init; } = "";
    public bool IsEdit { get; init; }
    public required ICommand Click { get; init; }

    public bool IsRunning => RunState == TileRunState.Running;
    public bool IsSuccess => RunState == TileRunState.Success;
    public bool IsError => RunState == TileRunState.Error;
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
