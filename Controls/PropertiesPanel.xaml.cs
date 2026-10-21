using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TouchDeck.ViewModels;

namespace TouchDeck.Controls;

/// <summary>
/// Panneau de propriétés d'un bouton (aperçu, titre, apparence, actions), partagé par l'éditeur et le mode édition du deck.
/// Son DataContext est l'<see cref="EditorViewModel"/> ; la saisie est un brouillon validé quand le focus quitte un champ ou sur Entrée.
/// </summary>
public partial class PropertiesPanel : UserControl
{
    /// <summary>Rayon des coins du panneau (0 dans l'éditeur ; le tiroir du deck est arrondi).</summary>
    public static readonly DependencyProperty PanelRadiusProperty =
        DependencyProperty.Register(nameof(PanelRadius), typeof(CornerRadius), typeof(PropertiesPanel), new PropertyMetadata(new CornerRadius(0)));

    /// <summary>Épaisseur de la bordure du panneau (un filet à gauche dans l'éditeur, aucune dans le tiroir du deck).</summary>
    public static readonly DependencyProperty EdgeThicknessProperty =
        DependencyProperty.Register(nameof(EdgeThickness), typeof(Thickness), typeof(PropertiesPanel), new PropertyMetadata(new Thickness(1, 0, 0, 0)));

    /// <summary>Rayon des coins du panneau.</summary>
    public CornerRadius PanelRadius { get => (CornerRadius)GetValue(PanelRadiusProperty); set => SetValue(PanelRadiusProperty, value); }

    /// <summary>Épaisseur de la bordure du panneau.</summary>
    public Thickness EdgeThickness { get => (Thickness)GetValue(EdgeThicknessProperty); set => SetValue(EdgeThicknessProperty, value); }

    /// <summary>Panneau de propriétés.</summary>
    public PropertiesPanel() { InitializeComponent(); }

    EditorViewModel? Editor => DataContext as EditorViewModel;

    /// <summary>Amène le clavier sur la première étape à compléter (raccourci, chemin, adresse), sinon sur le titre.</summary>
    public void FocusFirstField()
    {
        var invalid = Descendants<TextBox>(PropertiesScroll)
            .FirstOrDefault(t => t.DataContext is ActionStepViewModel { HasIssues: true } && t.IsVisible && t.IsEnabled && t != TitleBox);
        var target = invalid ?? (TitleBox.IsVisible ? TitleBox : null);
        if (target != null) { target.Focus(); Keyboard.Focus(target); }
        else PropertiesScroll.Focus();
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    /// <summary>Le focus quitte un champ : la saisie est validée (une seule écriture dans le document).</summary>
    void OnPropertiesLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Editor?.Properties.Flush();

    /// <summary>
    /// Entrée valide la saisie (et, en édition rapide, enregistre et ferme depuis le titre). Échap abandonne la saisie en cours, puis,
    /// en édition rapide, annule la modification et ferme.
    /// </summary>
    void OnPropertiesKeyDown(object sender, KeyEventArgs e)
    {
        if (Editor is not { } vm || e.OriginalSource is not TextBox box) return;
        if (e.Key == Key.Enter && !box.AcceptsReturn)
        {
            vm.Properties.Flush();
            if (vm.QuickEdit && ReferenceEquals(box, TitleBox)) vm.SaveButtonCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!vm.Properties.RevertDraft() && vm.QuickEdit) vm.CancelButtonCommand.Execute(null);
            e.Handled = true;
        }
    }
}
