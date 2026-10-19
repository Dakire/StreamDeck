using System.Collections.Generic;

namespace TouchDeck.ViewModels;

public record ButtonEditResult(DeckButton Button, bool Deleted, int Move);
public record PageEditResult(string Name, bool Hidden, bool Deleted, int Move);

/// <summary>Ce que le ViewModel demande à la fenêtre : tout ce qui touche à Win32, au placement et aux boîtes de dialogue.</summary>
public interface IShell
{
    bool IsFullscreen { get; }
    bool IsOnTop { get; }
    void NextScreen();
    void ToggleFullscreen();
    void ToggleOnTop();
    void Minimize();
    /// <summary>Replace la fenêtre sur l'écran cible après un rechargement de config.</summary>
    void ReapplyPlacement();
    ButtonEditResult? EditButton(DeckButton source, List<string> pageNames, bool isNew);
    PageEditResult? EditPage(DeckPage page, bool canDelete);
}
