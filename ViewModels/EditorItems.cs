using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

public sealed partial class PageTabViewModel : ObservableObject
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public bool Hidden { get; init; }
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _isDropTarget;
    public string Display => Hidden ? Name + " (masquée)" : Name;
    public string AutomationName => Hidden ? $"Page {Name}, masquée dans les onglets du deck" : $"Page {Name}";
}

public sealed class CrumbViewModel
{
    public required string Text { get; init; }
    /// <summary>Profondeur de dossier visée (0 = racine de la page).</summary>
    public int Depth { get; init; }
    public bool IsLast { get; init; }
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; init; }
    public bool HasSymbol { get; init; }
    public required ICommand Command { get; init; }
}

public sealed class ProfileItemViewModel
{
    public string Name { get; init; } = "";
    /// <summary>Affiché sur le deck en ce moment.</summary>
    public bool IsActive { get; init; }
    public string? AppMatch { get; init; }
    public string Detail { get; init; } = "";
    public string Display => IsActive ? Name + "  (actif sur le deck)" : Name;
    public override string ToString() => Display;
}

public sealed record FormatChoice(string Text, int Columns, int Rows)
{
    public override string ToString() => Text;
}

public sealed record ScreenChoice(int Monitor, string Text)
{
    public override string ToString() => Text;
}
