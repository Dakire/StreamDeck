using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TouchDeck.ViewModels;

/// <summary>Édition d'une page : nom, visibilité dans les onglets, ordre, suppression.</summary>
public partial class PageEditorViewModel : ObservableObject
{
    [ObservableProperty] string _name;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DeleteText))] bool _confirmDelete;

    public bool CanDelete { get; }
    public string DeleteText => ConfirmDelete ? "Confirmer ? (supprime ses boutons)" : "Supprimer la page";
    public ChoiceViewModel VisibleChoice { get; }
    public ChoiceViewModel HiddenChoice { get; }

    public bool Hidden { get; private set; }
    public bool Deleted { get; private set; }
    public int Move { get; private set; }

    public event Action<bool>? CloseRequested;

    public PageEditorViewModel(DeckPage page, bool canDelete)
    {
        _name = page.Name;
        Hidden = page.Hidden;
        CanDelete = canDelete;
        VisibleChoice = new ChoiceViewModel("Visible dans la barre d'onglets", () => SetHidden(false));
        HiddenChoice = new ChoiceViewModel("Masquée (accessible via un bouton)", () => SetHidden(true));
        SetHidden(Hidden);
    }

    void SetHidden(bool hidden)
    {
        Hidden = hidden;
        VisibleChoice.IsSelected = !hidden;
        HiddenChoice.IsSelected = hidden;
    }

    [RelayCommand]
    void Delete()
    {
        if (!ConfirmDelete) { ConfirmDelete = true; return; }
        Deleted = true;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand] void MoveBefore() { Move = -1; CloseRequested?.Invoke(true); }
    [RelayCommand] void MoveAfter() { Move = 1; CloseRequested?.Invoke(true); }
    [RelayCommand] void Save() => CloseRequested?.Invoke(true);
    [RelayCommand] void Cancel() => CloseRequested?.Invoke(false);
}
