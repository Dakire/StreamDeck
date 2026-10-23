using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StreamDak.ViewModels;
using Wpf.Ui.Controls;

namespace StreamDak.Controls;

/// <summary>
/// Transforme une description de menu (<see cref="MenuItemSpec"/>) en menu WPF-UI. WPF gère le reste de façon native :
/// fermeture au clic extérieur et sur Échap, navigation aux flèches, sous-menus placés dans l'écran, touche Menu / Maj+F10.
/// </summary>
public static class ContextMenuFactory
{
    /// <summary>Construit un menu WPF à partir de sa description par le ViewModel (sous-menus, séparateurs, raccourcis affichés).</summary>
    public static ContextMenu Build(IEnumerable<MenuItemSpec> specs)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        Fill(menu, specs);
        return menu;
    }

    /// <summary>Ouvre le menu sous un bouton (bouton « ⋯ » de la barre d'outils).</summary>
    public static void ShowBelow(FrameworkElement anchor, IEnumerable<MenuItemSpec> specs)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        Fill(menu, specs);
        menu.IsOpen = true;
    }

    static void Fill(ItemsControl parent, IEnumerable<MenuItemSpec> specs)
    {
        foreach (var spec in specs)
        {
            if (spec.IsSeparator) { parent.Items.Add(new Separator()); continue; }

            var item = new System.Windows.Controls.MenuItem { Header = spec.Header, IsEnabled = spec.Enabled, MinHeight = 44 };
            if (spec.Icon is { } icon) item.Icon = new SymbolIcon { Symbol = icon };
            if (spec.Gesture != null) item.InputGestureText = spec.Gesture;
            if (spec.Checked) { item.IsCheckable = true; item.IsChecked = true; }
            if (spec.Danger) item.SetResourceReference(Control.ForegroundProperty, "DeckDanger");
            System.Windows.Automation.AutomationProperties.SetName(item, spec.Header);

            if (spec.Children is { Count: > 0 }) Fill(item, spec.Children);
            else if (spec.Execute != null)
            {
                var run = spec.Execute;
                item.Click += (_, _) => run();
            }
            parent.Items.Add(item);
        }
    }
}
