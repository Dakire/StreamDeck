using System.Windows.Controls;

namespace StreamDak.Controls;

/// <summary>Curseur vertical d'un canal audio (icône qui coupe, niveau, nom) ; tout est lié au <see cref="ViewModels.LevelWidgetViewModel"/> du DataContext.</summary>
public partial class VerticalSlider : UserControl
{
    /// <summary>Crée le contrôle.</summary>
    public VerticalSlider() => InitializeComponent();
}
