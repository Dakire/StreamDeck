using System.Threading.Tasks;

namespace TouchDeck.ViewModels;

/// <summary>Ce que l'éditeur demande à sa fenêtre : boîtes de dialogue, sélecteurs de fichiers et déplacement du focus.</summary>
public interface IEditorShell : IConfirm
{
    /// <summary>Demande un texte. Retourne null si l'utilisateur annule.</summary>
    Task<string?> PromptAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false);
    Task InfoAsync(string title, string message);
    string? PickOpenFile(string title, string filter);
    string? PickSaveFile(string title, string filter, string defaultName);
    void FocusProperties();
    void FocusTitle();
    void FocusIcon();
    void FocusCatalogSearch();
    /// <summary>Redonne le focus à la case d'index de liste donné (-1 : la case sélectionnée).</summary>
    void FocusSlot(int listIndex);
}
