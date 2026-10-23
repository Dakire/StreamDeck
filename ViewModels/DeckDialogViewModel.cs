using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamDak.ViewModels;

/// <summary>
/// Boîte de dialogue affichée dans la fenêtre du deck (saisie d'un nom, confirmation, information), à la place d'une fenêtre modale :
/// une fenêtre séparée pouvait s'ouvrir derrière le deck ou sur un autre écran et laisser le deck inerte.
/// </summary>
public sealed partial class DeckDialogViewModel : ObservableObject
{
    readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly bool _allowEmpty;

    /// <summary>Titre de la boîte.</summary>
    public string Title { get; }
    /// <summary>Message explicatif.</summary>
    public string Message { get; }
    /// <summary>Libellé du bouton de validation.</summary>
    public string ConfirmText { get; }
    /// <summary>true : un champ de saisie est affiché.</summary>
    public bool ShowInput { get; }
    /// <summary>true : le bouton « Annuler » est affiché (faux pour une simple information).</summary>
    public bool ShowCancel { get; }
    /// <summary>true : bouton de validation rouge (action destructrice).</summary>
    public bool IsDanger { get; }

    /// <summary>Texte saisi.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanConfirm), nameof(ErrorText), nameof(HasError))] string text;

    /// <summary>Résultat : le texte validé (chaîne vide pour une confirmation), null si la boîte est annulée.</summary>
    public Task<string?> Result => _done.Task;

    DeckDialogViewModel(string title, string message, string confirmText, bool showInput, bool showCancel, bool danger, string initial, bool allowEmpty)
    {
        Title = title; Message = message; ConfirmText = confirmText; ShowInput = showInput; ShowCancel = showCancel; IsDanger = danger;
        text = initial;
        _allowEmpty = allowEmpty;
    }

    /// <summary>Boîte de saisie d'un texte.</summary>
    public static DeckDialogViewModel Input(string title, string message, string initial, string confirmText, bool allowEmpty) =>
        new(title, message, confirmText, true, true, false, initial, allowEmpty);

    /// <summary>Boîte de confirmation.</summary>
    public static DeckDialogViewModel Confirmation(string title, string message, string confirmText, bool danger) =>
        new(title, message, confirmText, false, true, danger, "", true);

    /// <summary>Boîte d'information à un seul bouton.</summary>
    public static DeckDialogViewModel Information(string title, string message) =>
        new(title, message, "Fermer", false, false, false, "", true);

    /// <summary>true si le bouton de validation est utilisable (un nom ne peut pas être vide).</summary>
    public bool CanConfirm => !ShowInput || _allowEmpty || !string.IsNullOrWhiteSpace(Text);
    /// <summary>Message d'erreur du champ.</summary>
    public string ErrorText => CanConfirm ? "" : "Le nom ne peut pas être vide";
    /// <summary>true si <see cref="ErrorText"/> est affiché.</summary>
    public bool HasError => !CanConfirm;

    [RelayCommand(CanExecute = nameof(CanConfirm))] void Confirm() => _done.TrySetResult(ShowInput ? Text.Trim() : "");

    [RelayCommand] void Cancel() => _done.TrySetResult(ShowCancel ? null : "");

    partial void OnTextChanged(string value) => ConfirmCommand.NotifyCanExecuteChanged();

    /// <summary>Ferme la boîte sans réponse (une autre la remplace, ou le mode édition se termine).</summary>
    internal void Abandon() => _done.TrySetResult(null);
}
