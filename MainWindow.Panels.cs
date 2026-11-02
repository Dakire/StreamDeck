using System.Windows.Controls.Primitives;
using StreamDak.ViewModels;

namespace StreamDak;

// Poignées du catalogue et de l'inspecteur (mode édition) : glisser redimensionne, un simple toucher replie ou déploie.
public partial class MainWindow
{
    // En dessous de cette distance (DIP) un glissement est un toucher
    const double TapDistance = 6;
    double _handleTravel;

    MainViewModel? PanelModel => DataContext as MainViewModel;

    void OnCatalogHandleDelta(object sender, DragDeltaEventArgs e)
    {
        _handleTravel += System.Math.Abs(e.HorizontalChange);
        PanelModel?.ResizeCatalog(-e.HorizontalChange);   // la poignée est à gauche du catalogue : vers la gauche = plus large
    }

    void OnCatalogHandleCompleted(object sender, DragCompletedEventArgs e)
    {
        bool tap = !e.Canceled && _handleTravel < TapDistance;
        _handleTravel = 0;
        if (tap) PanelModel?.ToggleCatalog(); else PanelModel?.CommitPanelSizes();
    }

    void OnInspectorHandleDelta(object sender, DragDeltaEventArgs e)
    {
        _handleTravel += System.Math.Abs(e.VerticalChange);
        PanelModel?.ResizeInspector(-e.VerticalChange);   // la poignée est au-dessus de l'inspecteur : vers le haut = plus haut
    }

    void OnInspectorHandleCompleted(object sender, DragCompletedEventArgs e)
    {
        bool tap = !e.Canceled && _handleTravel < TapDistance;
        _handleTravel = 0;
        if (tap) PanelModel?.ToggleInspector(); else PanelModel?.CommitPanelSizes();
    }
}
