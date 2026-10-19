using System.Collections.Generic;

namespace TouchDeck;

/// <summary>
/// Identifiants des types d'action, tels qu'ils sont écrits dans <c>config.json</c> (clé <c>Type</c>) et dans les
/// fichiers <c>.touchdeck</c>. Ce sont des clés de format : ne jamais en renommer une sans migration (<see cref="ConfigNormalizer"/>).
/// </summary>
public static class ActionTypes
{
    /// <summary>Combinaison de touches (« ctrl+shift+m »).</summary>
    public const string Hotkey = "hotkey";
    /// <summary>Saisie d'un texte dans l'application active.</summary>
    public const string Text = "text";
    /// <summary>Touche multimédia (voir <see cref="ActionValidator.MediaKeys"/>).</summary>
    public const string Media = "media";
    /// <summary>Lancement d'un programme ou d'un dossier.</summary>
    public const string Launch = "launch";
    /// <summary>Commande PowerShell ou fichier .ps1 / .bat / .cmd.</summary>
    public const string Script = "script";
    /// <summary>Ouverture d'une adresse avec l'application associée.</summary>
    public const string Url = "url";
    /// <summary>Navigation vers une page du profil (voir <see cref="PageTargets"/>).</summary>
    public const string Page = "page";
    /// <summary>Affichage d'un autre profil.</summary>
    public const string Profile = "profile";
    /// <summary>Pause en millisecondes entre deux étapes.</summary>
    public const string Delay = "delay";
    /// <summary>Mise au premier plan d'une fenêtre (avec lancement facultatif).</summary>
    public const string Focus = "focus";
    /// <summary>Requête web GET ou POST (webhook).</summary>
    public const string Http = "http";
    /// <summary>Micro et volume (voir <see cref="AudioCommands"/>).</summary>
    public const string Audio = "audio";
    /// <summary>Compte à rebours affiché sur le bouton.</summary>
    public const string Timer = "timer";

    /// <summary>Tous les types connus, dans l'ordre de l'éditeur.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Hotkey, Text, Media, Launch, Script, Url, Page, Profile, Delay, Focus, Http, Audio, Timer,
    };
}

/// <summary>Destinations spéciales de l'action <see cref="ActionTypes.Page"/>, en plus des noms de page.</summary>
public static class PageTargets
{
    /// <summary>Première page du profil.</summary>
    public const string Home = "home";
    /// <summary>Page affichée juste avant.</summary>
    public const string Back = "back";
}

/// <summary>Valeurs de l'action <see cref="ActionTypes.Audio"/>.</summary>
public static class AudioCommands
{
    /// <summary>Coupe ou rétablit le micro par défaut.</summary>
    public const string MicToggle = "mic-toggle";
    /// <summary>Coupe le micro.</summary>
    public const string MicMute = "mic-mute";
    /// <summary>Rétablit le micro.</summary>
    public const string MicUnmute = "mic-unmute";
    /// <summary>Coupe ou rétablit le son de sortie.</summary>
    public const string OutToggle = "out-toggle";
    /// <summary>Règle le volume de sortie ; le niveau (0 à 100) est dans <c>Args</c>.</summary>
    public const string Volume = "volume";

    /// <summary>Toutes les commandes, dans l'ordre de l'éditeur.</summary>
    public static readonly IReadOnlyList<string> All = new[] { MicToggle, MicMute, MicUnmute, OutToggle, Volume };
}
