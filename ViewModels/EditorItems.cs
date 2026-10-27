using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDak.ViewModels;

public sealed partial class PageTabViewModel : ObservableObject
{
    /// <summary>Index de la page dans le profil.</summary>
    public int Index { get; init; }
    /// <summary>Nom de la page.</summary>
    public string Name { get; init; } = "";
    /// <summary>true si la page est masquée dans les onglets du deck.</summary>
    public bool Hidden { get; init; }
    /// <summary>true pour une page web plein cadre (pas de grille de boutons).</summary>
    public bool IsWeb { get; init; }
    /// <summary>Adresse de la page web, ou null.</summary>
    public string? Url { get; init; }
    /// <summary>true si le badge de notifications de la page web est affiché dans l'onglet.</summary>
    public bool BadgeEnabled { get; init; } = true;
    /// <summary>true si la page est exclue du total de non lus (pastille de la barre des tâches).</summary>
    public bool BadgeExcludeFromTotal { get; init; }
    /// <summary>Interaction de la page web avec la souris et le doigt (<see cref="WebInputModes"/>).</summary>
    public string WebInput { get; init; } = WebInputModes.Default;
    [ObservableProperty] bool _isSelected;
    [ObservableProperty] bool _isDropTarget;
    /// <summary>Texte de l'onglet.</summary>
    public string Display => Hidden ? Name + " (masquée)" : Name;
    /// <summary>Nom accessible de l'onglet.</summary>
    public string AutomationName => Hidden ? $"Page {Name}, masquée dans les onglets du deck" : $"Page {Name}";
}

/// <summary>Élément du fil d'Ariane (profil › page › dossiers) au-dessus de la grille de l'éditeur.</summary>
public sealed class CrumbViewModel
{
    /// <summary>Texte affiché.</summary>
    public required string Text { get; init; }
    /// <summary>Profondeur de dossier visée (0 = racine de la page).</summary>
    public int Depth { get; init; }
    /// <summary>true pour l'emplacement courant (non cliquable, sans séparateur après).</summary>
    public bool IsLast { get; init; }
    /// <summary>Icône facultative (dossier).</summary>
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; init; }
    /// <summary>true si <see cref="Symbol"/> est à afficher.</summary>
    public bool HasSymbol { get; init; }
    /// <summary>Remonte à cet emplacement.</summary>
    public required ICommand Command { get; init; }
}

/// <summary>Profil dans la liste de l'éditeur.</summary>
public sealed class ProfileItemViewModel
{
    /// <summary>Nom du profil.</summary>
    public string Name { get; init; } = "";
    /// <summary>Affiché sur le deck en ce moment.</summary>
    public bool IsActive { get; init; }
    /// <summary>Programme associé, ou null.</summary>
    public string? AppMatch { get; init; }
    /// <summary>Détail affiché sous le nom (format de grille, application associée).</summary>
    public string Detail { get; init; } = "";
    /// <summary>Texte de la liste, avec la mention du profil actif.</summary>
    public string Display => IsActive ? Name + "  (actif sur le deck)" : Name;
    /// <summary>Texte affiché par la liste déroulante.</summary>
    public override string ToString() => Display;
}

/// <summary>Format de grille proposé dans l'éditeur.</summary>
/// <param name="Text">Libellé (« 5 × 3 », « Automatique »).</param>
/// <param name="Columns">Colonnes (0 : automatique).</param>
/// <param name="Rows">Lignes (0 : automatique).</param>
public sealed record FormatChoice(string Text, int Columns, int Rows)
{
    /// <summary>Libellé affiché par la liste déroulante.</summary>
    public override string ToString() => Text;
}

/// <summary>Écran proposé pour le deck.</summary>
/// <param name="Monitor">Index de l'écran (-1 : automatique).</param>
/// <param name="Text">Libellé affiché.</param>
/// <param name="Device">Identifiant Windows de l'écran, prioritaire sur l'index.</param>
public sealed record ScreenChoice(int Monitor, string Text, string? Device = null)
{
    /// <summary>Libellé affiché par la liste déroulante.</summary>
    public override string ToString() => Text;

    /// <summary>true si ce choix est celui de la configuration.</summary>
    public bool IsCurrent(DeckConfig c) => Monitor == c.Monitor && string.Equals(Device, c.MonitorDevice, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Choix d'écran du deck (automatique, puis chaque écran branché) et le choix correspondant à la configuration.</summary>
    public static (List<ScreenChoice> Choices, ScreenChoice Selected) Build(DeckConfig c, IEnumerable<DisplayInfo> displays, string autoText)
    {
        var list = new List<ScreenChoice> { new(-1, autoText) };
        list.AddRange(displays.Select(d => new ScreenChoice(d.Index, d.ToString(), d.Device)));
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

    /// <summary>Enregistre ce choix dans la configuration (index et identifiant).</summary>
    public void ApplyTo(DeckConfig c) { c.Monitor = Monitor; c.MonitorDevice = Device; }
}
