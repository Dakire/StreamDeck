using System;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Pastille de choix (type d'action, touche média, page, modèle…) ; IsSelected la met en évidence.</summary>
public partial class ChoiceViewModel : ObservableObject
{
    public string Text { get; }
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    public ChoiceViewModel(string text, Action onSelect, bool selected = false)
    {
        Text = text;
        Command = new RelayCommand(onSelect);
        _isSelected = selected;
    }
}

/// <summary>Pastille de couleur de la palette.</summary>
public partial class SwatchViewModel : ObservableObject
{
    public string Color { get; }
    public Brush Brush { get; }
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    public SwatchViewModel(string color, Action<string> onSelect)
    {
        Color = color;
        Brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        Command = new RelayCommand(() => onSelect(color));
    }
}

/// <summary>Icône Fluent proposée dans le sélecteur d'icônes de l'éditeur.</summary>
public partial class IconPickViewModel : ObservableObject
{
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; }
    public string Name => Symbol.ToString();
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    public IconPickViewModel(Wpf.Ui.Controls.SymbolRegular symbol, Action onSelect)
    {
        Symbol = symbol;
        Command = new RelayCommand(onSelect);
    }
}
