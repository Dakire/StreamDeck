using System;
using System.Collections.Generic;
using Wpf.Ui.Controls;

namespace StreamDak.ViewModels;

/// <summary>
/// Description d'un menu contextuel, indépendante de WPF : les ViewModels décrivent le menu selon l'état courant,
/// <c>ContextMenuFactory</c> le transforme en vrai menu. Un seul composant pour les cases, profils, pages et catalogue.
/// </summary>
public sealed record MenuItemSpec(
    string Header,
    Action? Execute = null,
    SymbolRegular? Icon = null,
    string? Gesture = null,
    bool Enabled = true,
    bool Danger = false,
    bool IsSeparator = false,
    IReadOnlyList<MenuItemSpec>? Children = null,
    bool Checked = false)
{
    /// <summary>Séparateur de menu.</summary>
    public static MenuItemSpec Separator { get; } = new("", IsSeparator: true);
}
