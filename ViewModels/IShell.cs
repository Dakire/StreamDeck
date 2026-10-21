namespace TouchDeck.ViewModels;

/// <summary>Ce que le ViewModel demande à la fenêtre : tout ce qui touche à Win32 et au placement.</summary>
public interface IShell
{
    /// <summary>true si le deck occupe tout l'écran (zone de travail).</summary>
    bool IsFullscreen { get; }
    /// <summary>true si le deck reste au-dessus des autres fenêtres.</summary>
    bool IsOnTop { get; }
    /// <summary>Passe le deck sur l'écran suivant, en gardant le mode (plein écran ou fenêtre).</summary>
    void NextScreen();
    /// <summary>Bascule entre plein écran et fenêtre sur l'écran courant.</summary>
    void ToggleFullscreen();
    /// <summary>Garde ou non le deck au-dessus des autres fenêtres.</summary>
    void SetOnTop(bool onTop);
    /// <summary>Réduit le deck dans la barre des tâches.</summary>
    void Minimize();
    /// <summary>Replace la fenêtre sur l'écran cible après un changement de configuration ou d'affichage.</summary>
    void ReapplyPlacement();
    /// <summary>
    /// Mode édition du deck : la fenêtre devient activable (les champs de saisie reçoivent le clavier) et le toucher redevient celui de WPF ;
    /// en sortant, elle ne prend de nouveau jamais le focus et le rend à l'application qui l'avait.
    /// </summary>
    void SetEditing(bool editing);
    /// <summary>Dialogues et focus des panneaux d'édition intégrés au deck (importer une image, saisir un nom…).</summary>
    IEditorShell EditorShell { get; }
}
