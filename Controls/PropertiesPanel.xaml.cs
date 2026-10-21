using System.Windows.Controls;
using System.Windows.Input;
using TouchDeck.ViewModels;

namespace TouchDeck.Controls;

/// <summary>
/// Panneau de propriétés d'un bouton (aperçu, titre, apparence, actions), partagé par l'éditeur et le mode édition du deck.
/// Son DataContext est l'<see cref="EditorViewModel"/> ; la saisie est un brouillon validé quand le focus quitte un champ ou sur Entrée.
/// </summary>
public partial class PropertiesPanel : UserControl
{
    /// <summary>Panneau de propriétés.</summary>
    public PropertiesPanel() { InitializeComponent(); }

    EditorViewModel? Editor => DataContext as EditorViewModel;

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
