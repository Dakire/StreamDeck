using System.Collections.Generic;

namespace StreamDak.Actions;

// Ce que les actions demandent au système d'exploitation. Les implémentations Windows vivent dans Platform/Windows ;
// une future version Linux fournirait les siennes sans toucher au moteur ni aux ViewModels.

/// <summary>Clavier simulé : combinaisons, touches média, saisie de texte.</summary>
public interface IKeyboardService
{
    /// <summary>Envoie une combinaison (« ctrl+shift+m », « win+d », « playpause »). Lève <see cref="System.ArgumentException"/> si une touche est inconnue.</summary>
    void SendHotkey(string combo);
    /// <summary>Tape le texte caractère par caractère dans l'application active (Unicode, sans dépendre de la disposition du clavier).</summary>
    void TypeText(string text);
    /// <summary>Vérifie une combinaison sans l'envoyer ; <paramref name="error"/> : raison en français si invalide.</summary>
    bool TryValidateHotkey(string combo, out string error);
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
    /// <summary>Programmes qui ont une fenêtre ouverte (sans doublon, une quinzaine au plus), pour les proposer dans l'éditeur.</summary>
    IReadOnlyList<string> OpenProcessNames();
}

/// <summary>Périphérique audio par défaut visé par une commande.</summary>
public enum AudioDevice
{
    /// <summary>Sortie (haut-parleurs, casque).</summary>
    Output,
    /// <summary>Micro de communication par défaut.</summary>
    Microphone,
}

/// <summary>Sens d'un périphérique audio.</summary>
public enum AudioFlow
{
    /// <summary>Lecture (haut-parleurs, casque, sortie virtuelle).</summary>
    Render,
    /// <summary>Enregistrement (micro, entrée de carte son).</summary>
    Capture,
}

/// <summary>Un périphérique audio actif du système.</summary>
/// <param name="Id">Identifiant stable du système (stable tant que le périphérique n'est pas réinstallé).</param>
/// <param name="Name">Nom affiché par Windows (« Microphone (Focusrite USB Audio) »).</param>
/// <param name="Flow">Lecture ou enregistrement.</param>
/// <param name="IsDefault">true si c'est le périphérique par défaut de son sens.</param>
public sealed record AudioEndpoint(string Id, string Name, AudioFlow Flow, bool IsDefault);

/// <summary>Niveau et coupure d'un périphérique audio.</summary>
/// <param name="Percent">Niveau de 0 à 100.</param>
/// <param name="Muted">true si le périphérique est coupé.</param>
public sealed record AudioEndpointState(int Percent, bool Muted);

/// <summary>Tous les périphériques audio actifs, par identifiant, sans toucher au périphérique par défaut de Windows.</summary>
public interface IAudioDevices
{
    /// <summary>Périphériques actifs de lecture et d'enregistrement.</summary>
    IReadOnlyList<AudioEndpoint> Endpoints();
    /// <summary>Niveau et coupure d'un périphérique. Lève une exception si le périphérique n'existe plus.</summary>
    AudioEndpointState ReadEndpoint(string id);
    /// <summary>Règle le niveau d'un périphérique (0 à 100).</summary>
    void SetEndpointVolume(string id, int percent);
    /// <summary>Coupe ou rétablit un périphérique.</summary>
    void SetEndpointMute(string id, bool muted);
}

/// <summary>Périphériques audio par défaut (sortie et micro), et tous les périphériques par identifiant.</summary>
public interface IAudioService : IAudioDevices
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
