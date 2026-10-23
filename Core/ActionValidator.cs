using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StreamDak.Actions;

namespace StreamDak;

/// <summary>Gravité d'un problème de configuration.</summary>
public enum Severity
{
    /// <summary>Le bouton fonctionne probablement, mais quelque chose est à vérifier.</summary>
    Warning,
    /// <summary>Le bouton échouera à l'exécution.</summary>
    Error,
}

/// <summary>Problème de configuration d'une étape ou d'un bouton.</summary>
/// <param name="Severity">Gravité : une erreur empêche l'exécution, un avertissement non.</param>
/// <param name="Message">Explication affichée dans l'éditeur.</param>
public sealed record Issue(Severity Severity, string Message);

/// <summary>Détecte les boutons mal configurés avant qu'ils échouent à l'exécution. Ne lance jamais rien.</summary>
public static class ActionValidator
{
    /// <summary>
    /// Clavier de la plateforme, pour valider les raccourcis exactement comme ils seront envoyés (caractères présents sur la
    /// disposition courante). Renseigné par la racine de composition ; sans lui, la syntaxe commune seule est vérifiée.
    /// </summary>
    public static IKeyboardService? Keyboard { get; set; }

    /// <summary>Types d'action reconnus (voir <see cref="ActionTypes"/>) : tout autre type est refusé à l'import.</summary>
    public static IReadOnlyList<string> KnownTypes => ActionTypes.All.Concat(External.Keys).ToList();

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<DeckAction, IEnumerable<Issue>>> External = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Déclare un type d'action fourni par une intégration : il devient valide (import, éditeur) et sa validation est déléguée
    /// à <paramref name="validate"/>. Appelé au démarrage par la racine de composition.
    /// </summary>
    public static void RegisterExternalType(string type, Func<DeckAction, IEnumerable<Issue>> validate) => External[type] = validate;
    /// <summary>Valeurs acceptées par l'action « media ».</summary>
    public static readonly string[] MediaKeys = { "playpause", "prev", "next", "stop", "mute", "voldown", "volup" };
    /// <summary>Commandes de l'action « audio » ; « volume » prend le niveau (0 à 100) dans Args.</summary>
    public static readonly string[] AudioCommands = StreamDak.AudioCommands.All.ToArray();
    /// <summary>Protocoles jamais ouverts par l'action « url » : ils permettent d'exécuter du code ou de lire des fichiers par détour.</summary>
    public static readonly string[] BlockedSchemes = { "javascript", "vbscript", "data", "file" };

    /// <summary>Types qui exécutent du code ou lancent des programmes : à signaler avant d'importer un profil.</summary>
    public static bool IsRisky(DeckAction a) => a.Type.ToLowerInvariant() is ActionTypes.Launch or ActionTypes.Script;

    /// <summary>Problèmes d'une étape, sans rien exécuter ; <paramref name="profile"/> sert à vérifier les noms de page (null : non vérifiés).</summary>
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
                if (!(Keyboard?.TryValidateHotkey(value, out var why) ?? HotkeySyntax.TryValidate(value, out why))) Err(why);
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
                else if (value.Equals(StreamDak.AudioCommands.Volume, StringComparison.OrdinalIgnoreCase)
                         && (!int.TryParse(a.Args, out var level) || level is < 0 or > 100))
                    Err("Indique un volume entre 0 et 100");
                break;
            case ActionTypes.Timer:
                if (!int.TryParse(value, out var seconds) || seconds <= 0) Err("La durée doit être un nombre de secondes");
                else if (seconds > 86_400) Warn("Minuteur de plus de 24 heures");
                break;
            default:
                if (External.TryGetValue(type, out var validateExternal)) issues.AddRange(validateExternal(a));
                else Err($"Type d'action inconnu : « {a.Type} »");
                break;
        }
        return issues;
    }

    /// <summary>Problèmes d'un bouton : ses étapes, une image manquante, l'absence d'action (sauf horloge).</summary>
    public static List<Issue> Validate(DeckButton b, DeckConfig cfg, DeckProfile? profile)
    {
        var issues = new List<Issue>();
        if (b.Blank) return issues;
        if (b.IsFolder) return issues;
        if (b.Widget != null)
        {
            if (b.Widget == WidgetTypes.Web && !WidgetTypes.IsWebAddress(b.Option(WidgetOptionKeys.Url), out _))
                issues.Add(new Issue(Severity.Warning, "Renseigne l'adresse de la page (https://…)"));
            return issues;
        }
        // Une horloge ({time}, {date}) n'a pas besoin d'action : elle sert d'affichage
        if (b.Actions.Count == 0 && !LabelTemplate.IsDynamic(b.Label)) issues.Add(new Issue(Severity.Warning, "Ce bouton n'a aucune action"));
        foreach (var a in b.Actions) issues.AddRange(Validate(a, cfg, profile));
        if (!string.IsNullOrEmpty(b.ImagePath) && IconNames.Resolve(b.ImagePath) is { } path && !File.Exists(path))
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
