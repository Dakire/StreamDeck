using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDak.ViewModels;

/// <summary>Une case de la grille de l'éditeur. L'objet reste le même quand le contenu change : sélection et focus sont conservés.</summary>
public partial class SlotViewModel : ObservableObject
{
    /// <summary>Position dans la grille affichée (0 = première case).</summary>
    public int Position { get; }

    [ObservableProperty] int _index;           // indice dans la liste de boutons ; -1 pour la case « retour » d'un dossier
    [ObservableProperty] bool _isBack;
    [ObservableProperty] bool _isEmpty = true;
    [ObservableProperty] bool _isFolder;
    [ObservableProperty] TileFaceData _face = TileFaceData.Empty;
    [ObservableProperty] string _automationName = "Case vide";
    [ObservableProperty] string _tooltip = "Case vide";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIssue), nameof(IssueSymbol))] Severity? _issue;
    [ObservableProperty] string _issueText = "";
    [ObservableProperty] bool _isDropTarget;
    [ObservableProperty] bool _isDragSource;
    [ObservableProperty] bool _isOverflow;

    /// <summary>Case à cette position de la grille de l'éditeur.</summary>
    public SlotViewModel(int position) { Position = position; }

    /// <summary>true si la case a un problème de configuration.</summary>
    public bool HasIssue => Issue != null;
    /// <summary>Icône du badge de problème (erreur ou avertissement).</summary>
    public Wpf.Ui.Controls.SymbolRegular IssueSymbol => Issue == Severity.Error
        ? Wpf.Ui.Controls.SymbolRegular.ErrorCircle24 : Wpf.Ui.Controls.SymbolRegular.Warning24;
}
