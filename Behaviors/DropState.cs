using System.Windows;

namespace StreamDak.Behaviors;

/// <summary>Sur un onglet de page : true pendant qu'une touche ou un onglet glissé le survole (le style de l'onglet s'allume).</summary>
public static class DropState
{
    /// <summary>Propriété attachée : l'élément est la cible actuelle d'un glisser-déposer.</summary>
    public static readonly DependencyProperty IsOverProperty =
        DependencyProperty.RegisterAttached("IsOver", typeof(bool), typeof(DropState), new PropertyMetadata(false));

    /// <summary>Lit <see cref="IsOverProperty"/>.</summary>
    public static bool GetIsOver(DependencyObject d) => (bool)d.GetValue(IsOverProperty);
    /// <summary>Écrit <see cref="IsOverProperty"/>.</summary>
    public static void SetIsOver(DependencyObject d, bool value) => d.SetValue(IsOverProperty, value);
}
