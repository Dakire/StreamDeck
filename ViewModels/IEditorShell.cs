using System.Threading.Tasks;

namespace StreamDak.ViewModels;

/// <summary>Ce que l'éditeur demande à sa fenêtre : boîtes de dialogue, sélecteurs de fichiers et déplacement du focus.</summary>
public interface IEditorShell : IConfirm
{
    /// <summary>Demande un texte. Retourne null si l'utilisateur annule.</summary>
    Task<string?> PromptAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false);
    /// <summary>Affiche une information avec un seul bouton.</summary>
    Task InfoAsync(string title, string message);
    /// <summary>Choisit un fichier à ouvrir ; null si l'utilisateur annule.</summary>
    string? PickOpenFile(string title, string filter);
    /// <summary>Choisit un fichier à écrire ; null si l'utilisateur annule.</summary>
    string? PickSaveFile(string title, string filter, string defaultName);
    /// <summary>Place le focus sur le panneau de propriétés.</summary>
    void FocusProperties();
    /// <summary>Place le focus sur le titre du bouton, texte sélectionné (F2).</summary>
    void FocusTitle();
    /// <summary>Place le focus sur le champ d'icône.</summary>
    void FocusIcon();
    /// <summary>Place le focus sur la recherche du catalogue (Ctrl+F).</summary>
    void FocusCatalogSearch();
    /// <summary>Redonne le focus à la case d'index de liste donné (-1 : la case sélectionnée).</summary>
    void FocusSlot(int listIndex);
    /// <summary>Ferme l'éditeur (fin d'une modification rapide depuis le deck).</summary>
    void Close();
}
