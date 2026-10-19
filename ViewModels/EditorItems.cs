using System.Collections.Generic;
using System.Linq;
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

public sealed record ScreenChoice(int Monitor, string Text, string? Device = null)
{
    public override string ToString() => Text;

    public bool IsCurrent(DeckConfig c) => Monitor == c.Monitor && string.Equals(Device, c.MonitorDevice, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Choix d'écran du deck (automatique, puis chaque écran branché) et le choix correspondant à la configuration.</summary>
    public static (List<ScreenChoice> Choices, ScreenChoice Selected) Build(DeckConfig c, string autoText)
    {
        var list = new List<ScreenChoice> { new(-1, autoText) };
        list.AddRange(DisplayService.All().Select(d => new ScreenChoice(d.Index, d.ToString(), d.Device)));
        // L'écran mémorisé est retrouvé par son identifiant même si son numéro a changé
        var selected = list.FirstOrDefault(s => s.IsCurrent(c))
                       ?? (c.MonitorDevice != null ? list.FirstOrDefault(s => s.Device != null && string.Equals(s.Device, c.MonitorDevice, System.StringComparison.OrdinalIgnoreCase)) : null)
                       ?? (c.MonitorDevice == null ? list.FirstOrDefault(s => s.Monitor == c.Monitor) : null);
        if (selected == null)
        {
            selected = new ScreenChoice(c.Monitor, $"Écran {c.Monitor + 1} (absent)", c.MonitorDevice);
            list.Add(selected);
        }
        return (list, selected);
    }

    public void ApplyTo(DeckConfig c) { c.Monitor = Monitor; c.MonitorDevice = Device; }
}
