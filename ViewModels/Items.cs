using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDak.ViewModels;

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

/// <summary>Case du deck qui a son propre menu (clic droit, appui long) : bouton, case vide ou emplacement « + ».</summary>
public interface IEditableTile
{
    /// <summary>Ouvre le menu de cette case (créer, modifier, dupliquer…) ; null pour une case sans menu (flèche « retour »).</summary>
    ICommand? Edit { get; }
}

/// <summary>
/// Place d'une case dans la grille affichée : numéro de case (ligne par ligne, 0 = en haut à gauche) et emprise en cases. Un widget
/// peut en occuper plusieurs ; les cases qu'il recouvre n'ont alors pas de tuile. Lue par <c>TileGridPanel</c>.
/// </summary>
public abstract class PlacedTile : ObservableObject
{
    /// <summary>Numéro de la case d'ancrage dans la page affichée.</summary>
    public int Cell { get; set; }
    /// <summary>Largeur en cases.</summary>
    public int ColSpan { get; set; } = 1;
    /// <summary>Hauteur en cases.</summary>
    public int RowSpan { get; set; } = 1;
}

/// <summary>Bouton de la grille du deck. La face et l'état changent sans reconstruire la grille.</summary>
public sealed partial class DeckTileViewModel : PlacedTile, IEditableTile
{
    [ObservableProperty] TileFaceData _face = TileFaceData.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsSuccess), nameof(IsError))] TileRunState _runState;
    [ObservableProperty] string _accessibleName = "";
    /// <summary>true pour la touche sélectionnée dans le mode édition du deck (liseré d'accent).</summary>
    [ObservableProperty] bool _isSelected;
    /// <summary>true pour la touche qu'on est en train de glisser (atténuée).</summary>
    [ObservableProperty] bool _isDragSource;
    /// <summary>true pour la case sous le pointeur pendant un glissement (surbrillance).</summary>
    [ObservableProperty] bool _isDropTarget;

    /// <summary>Bouton de la configuration représenté (null pour la case « retour »).</summary>
    public DeckButton? Button { get; init; }
    /// <summary>Titre enregistré (nom stable de la tuile ; l'affichage est dans <see cref="Face"/>).</summary>
    public string Label { get; init; } = "";
    /// <summary>true en mode « Modifier au toucher » : toucher ouvre l'éditeur au lieu d'exécuter.</summary>
    public bool IsEdit { get; init; }
    /// <summary>Exécute le bouton, ouvre le dossier ou l'éditeur selon le mode.</summary>
    public required ICommand Click { get; init; }
    /// <inheritdoc/>
    public ICommand? Edit { get; init; }

    /// <summary>true pendant une exécution longue.</summary>
    public bool IsRunning => RunState == TileRunState.Running;
    /// <summary>true juste après une réussite.</summary>
    public bool IsSuccess => RunState == TileRunState.Success;
    /// <summary>true juste après un échec.</summary>
    public bool IsError => RunState == TileRunState.Error;
}

/// <summary>Case vide hors édition : réserve la place pour que les voisins gardent leur position. Un clic droit ou un appui long y ouvre le menu de création.</summary>
public sealed class SpacerTileViewModel : PlacedTile, IEditableTile
{
    /// <inheritdoc/>
    public ICommand? Edit { get; init; }
}

/// <summary>Emplacement « + » affiché en mode édition : un toucher le sélectionne, un appui long ouvre le menu de création.</summary>
public sealed partial class AddTileViewModel : PlacedTile, IEditableTile
{
    [ObservableProperty] bool _isSelected;
    /// <summary>true pour la case sous le pointeur pendant un glissement (surbrillance).</summary>
    [ObservableProperty] bool _isDropTarget;
    /// <summary>Sélectionne cette case (le panneau de propriétés propose alors de la remplir).</summary>
    public required ICommand Click { get; init; }
    /// <inheritdoc/>
    public ICommand? Edit { get; init; }
}

/// <summary>
/// Première case d'une page ou d'un dossier encore vide, hors mode édition : invite à ajouter un bouton au toucher (au lieu d'une grille éteinte).
/// Dès qu'un bouton existe, la case redevient une case vide ordinaire.
/// </summary>
public sealed class WelcomeTileViewModel : PlacedTile, IEditableTile
{
    /// <summary>Ouvre le menu de création de cette case.</summary>
    public required ICommand Click { get; init; }
    /// <inheritdoc/>
    public ICommand? Edit => Click;
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
    /// <summary>Texte du badge rouge de notifications (« 3 », « 99+ ») d'une page web de Gmail, Google Chat, Discord…, ou null.</summary>
    public string? Badge { get; init; }
    /// <summary>true si un badge est affiché.</summary>
    public bool HasBadge => Badge != null;
    /// <summary>Valeur de l'InfoBadge : le nombre (« 3 », « 99+ »), ou null pour un simple point d'activité.</summary>
    public string? BadgeValue => Badge == NotificationBadge.ActivityText ? null : Badge;
    /// <summary>Nom accessible du badge seul (« 3 notifications non lues »), ou vide.</summary>
    public string BadgeAutomationName => Badge == null ? "" : NotificationBadge.Describe(Badge);
    /// <summary>Nom accessible de l'onglet.</summary>
    public string AutomationName => Badge == null ? $"Page {Text}" : $"Page {Text}, {NotificationBadge.Describe(Badge)}";
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
    /// <summary>Nom accessible : le texte, suivi de « , activé » quand la coche est affichée (interrupteur).</summary>
    public string AutomationName => Trailing == "✓" ? Text + ", activé" : Text;
    /// <summary>true pour une action destructrice (affichée en rouge).</summary>
    public bool IsDanger { get; init; }
    /// <summary>Action de l'entrée.</summary>
    public required ICommand Command { get; init; }
}

/// <summary>Séparateur du menu du deck.</summary>
public sealed class MenuSeparatorViewModel { }

/// <summary>Titre non cliquable en haut d'un menu de case (« Case vide », nom du bouton, nom de la catégorie).</summary>
public sealed class MenuHeaderViewModel
{
    /// <summary>Texte du titre.</summary>
    public string Text { get; init; } = "";
}
