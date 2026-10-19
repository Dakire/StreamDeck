namespace TouchDeck.ViewModels;

/// <summary>Ce que le ViewModel demande à la fenêtre : tout ce qui touche à Win32 et au placement.</summary>
public interface IShell
{
    bool IsFullscreen { get; }
    bool IsOnTop { get; }
    void NextScreen();
    void ToggleFullscreen();
    void SetOnTop(bool onTop);
    void Minimize();
    /// <summary>Replace la fenêtre sur l'écran cible après un changement de configuration ou d'affichage.</summary>
    void ReapplyPlacement();
}
