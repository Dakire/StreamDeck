using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using TouchDeck.ViewModels;

namespace TouchDeck.Controls;

/// <summary>
/// Panneau de création rapide d'un bouton. Entrée crée (bouton par défaut), Échap ferme sans écrire, les flèches changent l'action
/// choisie depuis la zone de recherche. La logique est dans <see cref="QuickCreateViewModel"/> ; ici, uniquement les événements WPF.
/// </summary>
public partial class QuickCreateWindow : Wpf.Ui.Controls.FluentWindow
{
    readonly QuickCreateViewModel _vm;
    bool _closing;

    /// <summary>Panneau pour la case <paramref name="index"/> de <paramref name="place"/> ; se ferme à la création, à l'annulation et quand il perd le focus.</summary>
    public QuickCreateWindow(IServiceProvider services, Place place, int index)
    {
        _vm = ActivatorUtilities.CreateInstance<QuickCreateViewModel>(services, place, index);
        DataContext = _vm;
        InitializeComponent();
        Icon = Application.Current.MainWindow?.Icon;
        _vm.CloseRequested += CloseOnce;
        Loaded += (_, _) => { Search.Focus(); Keyboard.Focus(Search); };
        Deactivated += (_, _) => CloseOnce();
        Closed += (_, _) => WindowCleanup.ReleaseTitleBars(this);
    }

    void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up)) return;
        _vm.MoveSelection(e.Key == Key.Down ? 1 : -1);
        e.Handled = true;
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
    }

    void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(List, source) != null)
            _vm.CreateCommand.Execute(null);
    }
}
