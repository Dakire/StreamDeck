namespace TouchDeck.Actions;

// Ce que les actions demandent au système d'exploitation. Les implémentations Windows vivent dans Platform/Windows ;
// une future version Linux fournirait les siennes sans toucher au moteur ni aux ViewModels.

/// <summary>Clavier simulé : combinaisons, touches média, saisie de texte.</summary>
public interface IKeyboardService
{
    /// <summary>Envoie une combinaison (« ctrl+shift+m », « win+d », « playpause »). Lève <see cref="System.ArgumentException"/> si une touche est inconnue.</summary>
    void SendHotkey(string combo);
    /// <summary>Tape le texte caractère par caractère dans l'application active (Unicode, sans dépendre de la disposition du clavier).</summary>
    void TypeText(string text);
}

/// <summary>Lancement de programmes, dossiers, adresses et scripts.</summary>
public interface IAppLauncher
{
    /// <summary>Ouvre un programme, un dossier ou une adresse avec l'application associée.</summary>
    void Open(string target, string? arguments);
    /// <summary>Exécute un fichier de script ou une commande en ligne.</summary>
    void RunScript(string scriptOrCommand, string? arguments);
}

/// <summary>Fenêtres des autres applications.</summary>
public interface IWindowService
{
    /// <summary>Met au premier plan la fenêtre dont le programme ou le titre correspond ; false si aucune.</summary>
    bool Activate(string query);
    /// <summary>Nom du programme au premier plan (sans .exe), ou null.</summary>
    string? ForegroundProcessName();
}

/// <summary>Périphérique audio par défaut visé par une commande.</summary>
public enum AudioDevice
{
    /// <summary>Sortie (haut-parleurs, casque).</summary>
    Output,
    /// <summary>Micro de communication par défaut.</summary>
    Microphone,
}

/// <summary>Périphériques audio par défaut (sortie et micro).</summary>
public interface IAudioService
{
    /// <summary>true si le périphérique par défaut est coupé.</summary>
    bool IsMuted(AudioDevice device);
    /// <summary>Coupe ou rétablit le périphérique par défaut.</summary>
    void SetMute(AudioDevice device, bool muted);
    /// <summary>Volume en pourcentage (0 à 100).</summary>
    void SetVolume(AudioDevice device, int percent);
}

/// <summary>Ensemble des services d'une plateforme.</summary>
public sealed record PlatformServices(IKeyboardService Keyboard, IAppLauncher Launcher, IWindowService Windows, IAudioService Audio);
