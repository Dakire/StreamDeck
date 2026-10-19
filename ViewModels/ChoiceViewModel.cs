using System;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Pastille de choix (type d'action, touche média, page, modèle…) ; IsSelected la met en évidence.</summary>
public partial class ChoiceViewModel : ObservableObject
{
    /// <summary>Texte de la pastille.</summary>
    public string Text { get; }
    /// <summary>Sélectionne ce choix.</summary>
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    /// <summary>Pastille de choix ; <paramref name="onSelect"/> est appelé au toucher.</summary>
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
    /// <summary>Couleur en hexadécimal, enregistrée dans le bouton.</summary>
    public string Color { get; }
    /// <summary>Brush de la pastille.</summary>
    public Brush Brush { get; }
    /// <summary>Choisit cette couleur.</summary>
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    /// <summary>Pastille du nuancier ; <paramref name="onSelect"/> reçoit la couleur.</summary>
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
    /// <summary>Icône proposée.</summary>
    public Wpf.Ui.Controls.SymbolRegular Symbol { get; }
    /// <summary>Nom de l'icône (enregistré dans le bouton, lu par les lecteurs d'écran).</summary>
    public string Name => Symbol.ToString();
    /// <summary>Choisit cette icône.</summary>
    public ICommand Command { get; }
    [ObservableProperty] bool _isSelected;

    /// <summary>Icône proposée ; <paramref name="onSelect"/> est appelé au toucher.</summary>
    public IconPickViewModel(Wpf.Ui.Controls.SymbolRegular symbol, Action onSelect)
    {
        Symbol = symbol;
        Command = new RelayCommand(onSelect);
    }
}
