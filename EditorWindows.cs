using System;
using System.Collections.Generic;
using System.Windows;
using TouchDeck.ViewModels;

namespace TouchDeck;

/// <summary>Édition d'un bouton. Toute la logique est dans ButtonEditorViewModel ; la fenêtre ne fait que se fermer.</summary>
public partial class ButtonEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    readonly ButtonEditorViewModel _vm;

    public DeckButton Result => _vm.Result;
    public bool Deleted => _vm.Deleted;
    /// <summary>-1 / +1 : déplace le bouton d'une case avant / après.</summary>
    public int Move => _vm.Move;

    public ButtonEditorWindow(Window owner, DeckButton source, List<string> pageNames, bool isNew)
    {
        _vm = new ButtonEditorViewModel(source, pageNames, isNew);
        _vm.CloseRequested += ok => { DialogResult = ok; Close(); };
        DataContext = _vm;
        InitializeComponent();
        Owner = owner;
        Icon = owner.Icon;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height * 0.94);
    }
}

/// <summary>Édition d'une page : nom, visibilité dans les onglets, ordre, suppression.</summary>
public partial class PageEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    readonly PageEditorViewModel _vm;

    public string PageName => _vm.Name;
    public bool Hidden => _vm.Hidden;
    public bool Deleted => _vm.Deleted;
    public int Move => _vm.Move;

    public PageEditorWindow(Window owner, DeckPage page, bool canDelete)
    {
        _vm = new PageEditorViewModel(page, canDelete);
        _vm.CloseRequested += ok => { DialogResult = ok; Close(); };
        DataContext = _vm;
        InitializeComponent();
        Owner = owner;
        Icon = owner.Icon;
    }
}
