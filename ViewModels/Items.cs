using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

/// <summary>Retour visuel d'un bouton du deck : en cours (après un court délai), réussi ou en erreur, puis retour au repos.</summary>
public enum TileRunState
{
    /// <summary>Au repos.</summary>
    Idle,
    /// <summary>Exécution qui dure plus de <see cref="MainViewModel.RunningDelay"/> : anneau de progression.</summary>
    Running,
    /// <summary>Réussite : coche pendant un instant.</summary>
    Success,
    /// <summary>Échec : alerte, le message est dans la barre d'état.</summary>
    Error,
}

/// <summary>Bouton de la grille du deck. La face et l'état changent sans reconstruire la grille.</summary>
public sealed partial class DeckTileViewModel : ObservableObject
{
    [ObservableProperty] TileFaceData _face = TileFaceData.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsSuccess), nameof(IsError))] TileRunState _runState;
    [ObservableProperty] string _accessibleName = "";

    /// <summary>Bouton de la configuration représenté (null pour la case « retour »).</summary>
    public DeckButton? Button { get; init; }
    /// <summary>Titre enregistré (nom stable de la tuile ; l'affichage est dans <see cref="Face"/>).</summary>
    public string Label { get; init; } = "";
    /// <summary>true en mode « Modifier au toucher » : toucher ouvre l'éditeur au lieu d'exécuter.</summary>
    public bool IsEdit { get; init; }
    /// <summary>Exécute le bouton, ouvre le dossier ou l'éditeur selon le mode.</summary>
    public required ICommand Click { get; init; }

    /// <summary>true pendant une exécution longue.</summary>
    public bool IsRunning => RunState == TileRunState.Running;
    /// <summary>true juste après une réussite.</summary>
    public bool IsSuccess => RunState == TileRunState.Success;
    /// <summary>true juste après un échec.</summary>
    public bool IsError => RunState == TileRunState.Error;
}

/// <summary>Case vide hors édition : réserve la place pour que les voisins gardent leur position.</summary>
public sealed class SpacerTileViewModel { }

/// <summary>Emplacement « + » affiché en mode édition.</summary>
public sealed class AddTileViewModel
{
    /// <summary>Ouvre l'éditeur sur cette case.</summary>
    public required ICommand Click { get; init; }
}

/// <summary>Point de l'indicateur de sous-pages du dock (touchable : va à cette sous-page).</summary>
public sealed class PageDotViewModel
{
    /// <summary>true pour la sous-page affichée.</summary>
    public bool IsActive { get; init; }
    /// <summary>Nom accessible (« Sous-page 2 sur 3 »).</summary>
    public string Text { get; init; } = "";
    /// <summary>Affiche cette sous-page.</summary>
    public required ICommand Command { get; init; }
}

/// <summary>Onglet de page du dock.</summary>
public sealed class TabViewModel
{
    /// <summary>Nom de la page (avec « (masquée) » en mode édition).</summary>
    public string Text { get; init; } = "";
    /// <summary>true pour la page affichée.</summary>
    public bool IsActive { get; init; }
    /// <summary>Affiche cette page.</summary>
    public required ICommand Command { get; init; }
}

/// <summary>Entrée du menu du deck.</summary>
public sealed class MenuEntryViewModel
{
    /// <summary>Icône de l'entrée.</summary>
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; init; }
    /// <summary>Texte de l'entrée.</summary>
    public string Text { get; init; } = "";
    /// <summary>Texte à droite (état courant, coche), ou null.</summary>
    public string? Trailing { get; init; }
    /// <summary>true pour une action destructrice (affichée en rouge).</summary>
    public bool IsDanger { get; init; }
    /// <summary>Action de l'entrée.</summary>
    public required ICommand Command { get; init; }
}

/// <summary>Séparateur du menu du deck.</summary>
public sealed class MenuSeparatorViewModel { }
