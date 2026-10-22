using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TouchDeck.ViewModels;

// Boîtes de dialogue du mode édition, affichées par-dessus le deck (pas de fenêtre modale séparée)
public partial class MainViewModel
{
    /// <summary>Boîte de dialogue affichée, ou null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDialog))] DeckDialogViewModel? dialog;

    partial void OnDialogChanged(DeckDialogViewModel? value) => UpdateWebSuspension();

    /// <summary>true si une boîte de dialogue est affichée.</summary>
    public bool HasDialog => Dialog != null;

    /// <summary>Demande un texte ; null si annulé.</summary>
    public Task<string?> AskAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false) =>
        ShowDialogAsync(DeckDialogViewModel.Input(title, message, initial, confirmText, allowEmpty));

    /// <summary>Demande une confirmation ; true si acceptée.</summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false) =>
        await ShowDialogAsync(DeckDialogViewModel.Confirmation(title, message, confirmText, danger)) != null;

    /// <summary>Affiche une information.</summary>
    public Task InfoAsync(string title, string message) => ShowDialogAsync(DeckDialogViewModel.Information(title, message));

    async Task<string?> ShowDialogAsync(DeckDialogViewModel box)
    {
        Dialog?.Abandon();   // une seule boîte à la fois : la précédente est annulée
        Dialog = box;
        try { return await box.Result; }
        finally { if (ReferenceEquals(Dialog, box)) Dialog = null; }
    }
}
