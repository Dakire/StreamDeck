using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TouchDeck;

public enum Severity { Warning, Error }

public sealed record Issue(Severity Severity, string Message);

/// <summary>Détecte les boutons mal configurés avant qu'ils échouent à l'exécution. Ne lance jamais rien.</summary>
public static class ActionValidator
{
    public static readonly string[] KnownTypes = ActionTypes.All.ToArray();
    public static readonly string[] MediaKeys = { "playpause", "prev", "next", "stop", "mute", "voldown", "volup" };
    /// <summary>Commandes de l'action « audio » ; « volume » prend le niveau (0 à 100) dans Args.</summary>
    public static readonly string[] AudioCommands = TouchDeck.AudioCommands.All.ToArray();
    public static readonly string[] BlockedSchemes = { "javascript", "vbscript", "data", "file" };

    /// <summary>Types qui exécutent du code ou lancent des programmes : à signaler avant d'importer un profil.</summary>
    public static bool IsRisky(DeckAction a) => a.Type.ToLowerInvariant() is ActionTypes.Launch or ActionTypes.Script;

    public static List<Issue> Validate(DeckAction a, DeckConfig cfg, DeckProfile? profile)
    {
        var issues = new List<Issue>();
        void Err(string m) => issues.Add(new Issue(Severity.Error, m));
        void Warn(string m) => issues.Add(new Issue(Severity.Warning, m));

        var type = (a.Type ?? "").ToLowerInvariant();
        var value = a.Value ?? "";
        switch (type)
        {
            case ActionTypes.Hotkey:
                if (!NativeInput.TryValidateHotkey(value, out var why)) Err(why);
                break;
            case ActionTypes.Text:
                if (value.Length == 0) Warn("Le texte à saisir est vide");
                break;
            case ActionTypes.Media:
                if (!MediaKeys.Contains(value.ToLowerInvariant())) Err("Choisis une touche média dans la liste");
                break;
            case ActionTypes.Launch:
                if (string.IsNullOrWhiteSpace(value)) Err("Indique un programme ou un dossier à ouvrir");
                else if (LooksLikePath(value) && !File.Exists(value) && !Directory.Exists(value))
                    Warn("Ce chemin est introuvable sur cet ordinateur");
                break;
            case ActionTypes.Script:
                if (string.IsNullOrWhiteSpace(value)) Err("Indique une commande PowerShell ou un fichier de script");
                else if (IsScriptFile(value) && LooksLikePath(value) && !File.Exists(value))
                    Warn("Ce fichier de script est introuvable");
                break;
            case ActionTypes.Url:
                if (string.IsNullOrWhiteSpace(value) || value.Trim() is "https://" or "http://") Err("Indique une adresse");
                else if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme.Length < 2)
                    Warn("L'adresse devrait commencer par https:// (ou un autre protocole)");
                else if (BlockedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
                    Err($"Le protocole « {uri.Scheme}: » n'est pas autorisé : utilise « Ouvrir un programme » pour un fichier");
                break;
            case ActionTypes.Page:
                if (string.IsNullOrWhiteSpace(value)) Err("Choisis une page");
                else if (profile != null && !value.Equals(PageTargets.Home, StringComparison.OrdinalIgnoreCase) && !value.Equals(PageTargets.Back, StringComparison.OrdinalIgnoreCase)
                         && !profile.Pages.Any(p => p.Name.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    Err($"La page « {value} » n'existe pas dans ce profil");
                break;
            case ActionTypes.Profile:
                if (string.IsNullOrWhiteSpace(value)) Err("Choisis un profil");
                else if (ConfigNormalizer.FindProfile(cfg, value) == null) Err($"Le profil « {value} » n'existe pas");
                break;
            case ActionTypes.Delay:
                if (!int.TryParse(value, out var ms) || ms < 0) Err("La durée doit être un nombre de millisecondes");
                else if (ms > 600_000) Warn("Pause de plus de 10 minutes");
                break;
            case ActionTypes.Focus:
                if (string.IsNullOrWhiteSpace(value)) Err("Indique le programme ou le titre de la fenêtre");
                break;
            case ActionTypes.Http:
                if (string.IsNullOrWhiteSpace(value) || value.Trim() is "https://" or "http://") Err("Indique l'adresse à appeler");
                else if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
                    Err("L'adresse doit commencer par http:// ou https://");
                else if (target.Scheme == "http" && !target.IsLoopback)
                    Warn("Adresse non chiffrée (http://) : le contenu circule en clair sur le réseau");
                break;
            case ActionTypes.Audio:
                if (!AudioCommands.Contains(value.ToLowerInvariant())) Err("Choisis une commande audio dans la liste");
                else if (value.Equals(TouchDeck.AudioCommands.Volume, StringComparison.OrdinalIgnoreCase)
                         && (!int.TryParse(a.Args, out var level) || level is < 0 or > 100))
                    Err("Indique un volume entre 0 et 100");
                break;
            case ActionTypes.Timer:
                if (!int.TryParse(value, out var seconds) || seconds <= 0) Err("La durée doit être un nombre de secondes");
                else if (seconds > 86_400) Warn("Minuteur de plus de 24 heures");
                break;
            default:
                Err($"Type d'action inconnu : « {a.Type} »");
                break;
        }
        return issues;
    }

    public static List<Issue> Validate(DeckButton b, DeckConfig cfg, DeckProfile? profile)
    {
        var issues = new List<Issue>();
        if (b.Blank) return issues;
        if (b.IsFolder) return issues;
        // Une horloge ({time}, {date}) n'a pas besoin d'action : elle sert d'affichage
        if (b.Actions.Count == 0 && !LabelTemplate.IsDynamic(b.Label)) issues.Add(new Issue(Severity.Warning, "Ce bouton n'a aucune action"));
        foreach (var a in b.Actions) issues.AddRange(Validate(a, cfg, profile));
        if (!string.IsNullOrEmpty(b.ImagePath) && IconStore.Resolve(b.ImagePath) is { } path && !File.Exists(path))
            issues.Add(new Issue(Severity.Warning, "L'image du bouton est introuvable"));
        return issues;
    }

    /// <summary>Le pire niveau d'un bouton, ou null s'il est correct.</summary>
    public static Severity? Worst(IEnumerable<Issue> issues) =>
        issues.Any() ? issues.Max(i => i.Severity) : null;

    static bool IsScriptFile(string v)
    {
        var ext = Path.GetExtension(v.Trim()).ToLowerInvariant();
        return ext is ".ps1" or ".bat" or ".cmd";
    }

    // « notepad.exe » n'est pas un chemin : Windows le trouve via le PATH, on ne peut pas le vérifier
    static bool LooksLikePath(string v)
    {
        v = v.Trim();
        return v.Contains('\\') || v.Contains('/') || (v.Length > 1 && v[1] == ':');
    }
}
