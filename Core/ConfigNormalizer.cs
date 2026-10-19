using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchDeck;

/// <summary>Résultat d'une normalisation : la configuration a-t-elle été migrée, et ce qui a été corrigé.</summary>
public sealed record NormalizeResult(bool Migrated, IReadOnlyList<string> Warnings);

/// <summary>
/// Migre l'ancien format (version 1, <c>Pages</c> à la racine) vers les profils, puis répare ce qui pourrait corrompre
/// l'application : listes nulles, noms vides ou en double, bornes. Ne supprime jamais de bouton.
/// </summary>
public static class ConfigNormalizer
{
    /// <summary>Version du format écrite par cette version de TouchDeck.</summary>
    public const int CurrentVersion = 2;
    /// <summary>Profondeur maximale des dossiers imbriqués : au-delà, le contenu est remonté dans le dernier dossier autorisé (avec un avertissement).</summary>
    public const int MaxFolderDepth = 8;
    /// <summary>Nombre d'actions récentes conservées dans le catalogue.</summary>
    public const int MaxRecents = 12;
    /// <summary>Valeurs acceptées pour <see cref="DeckConfig.Theme"/>.</summary>
    public static readonly string[] Themes = { "system", "dark", "light" };

    /// <summary>Migre l'ancien format et répare une configuration lue sur disque, sans rien supprimer de valide ; décrit ce qui a changé.</summary>
    public static NormalizeResult Normalize(DeckConfig cfg)
    {
        var warnings = new List<string>();
        bool migrated = false;

        cfg.Profiles ??= new List<DeckProfile>();
        cfg.Favorites ??= new List<string>();
        cfg.Recents ??= new List<string>();

        if (cfg.Version < CurrentVersion)
        {
            if (cfg.Profiles.Count == 0)
                cfg.Profiles.Add(new DeckProfile { Name = "Principal", Pages = cfg.Pages ?? new List<DeckPage>() });
            else if (cfg.Pages is { Count: > 0 })
                warnings.Add("La clé « Pages » est ignorée parce que « Profiles » est présente.");
            cfg.Pages = null;
            cfg.Version = CurrentVersion;
            migrated = true;
        }
        else if (cfg.Pages != null)
        {
            if (cfg.Pages.Count > 0 && cfg.Profiles.Count == 0)
                cfg.Profiles.Add(new DeckProfile { Name = "Principal", Pages = cfg.Pages });
            else if (cfg.Pages.Count > 0)
                warnings.Add("La clé « Pages » est ignorée parce que « Profiles » est présente.");
            cfg.Pages = null;
        }

        if (cfg.Profiles.Count == 0) cfg.Profiles.Add(new DeckProfile { Name = "Principal" });

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var profile in cfg.Profiles)
        {
            n++;
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = $"Profil {n}";
            profile.Name = Unique(profile.Name.Trim(), names, warnings, "profil");
            profile.Columns = Math.Clamp(profile.Columns, 0, 20);
            profile.Rows = Math.Clamp(profile.Rows, 0, 20);
            if (string.IsNullOrWhiteSpace(profile.AppMatch)) profile.AppMatch = null;
            NormalizePages(profile, warnings);
        }

        if (cfg.Monitor < -1) cfg.Monitor = -1;
        if (string.IsNullOrWhiteSpace(cfg.MonitorDevice)) cfg.MonitorDevice = null;
        cfg.Columns = Math.Clamp(cfg.Columns, 0, 20);
        cfg.Rows = Math.Clamp(cfg.Rows, 0, 20);
        cfg.Spacing = double.IsFinite(cfg.Spacing) ? Math.Clamp(cfg.Spacing, 0, 80) : 14;
        var theme = (cfg.Theme ?? "").ToLowerInvariant();
        cfg.Theme = Themes.Contains(theme) ? theme : "system";
        cfg.Background = string.IsNullOrWhiteSpace(cfg.Background) ? Theme.DefaultBackground : cfg.Background;
        cfg.IconStyle = string.Equals(cfg.IconStyle, "emoji", StringComparison.OrdinalIgnoreCase) ? "emoji" : "fluent";

        if (string.IsNullOrEmpty(cfg.ActiveProfile) || FindProfile(cfg, cfg.ActiveProfile) == null)
            cfg.ActiveProfile = cfg.Profiles[0].Name;
        else
            cfg.ActiveProfile = FindProfile(cfg, cfg.ActiveProfile)!.Name;

        cfg.Favorites = cfg.Favorites.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        cfg.Recents = cfg.Recents.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(MaxRecents).ToList();
        return new NormalizeResult(migrated, warnings);
    }

    /// <summary>Profil de ce nom (sans distinction de casse), ou null.</summary>
    public static DeckProfile? FindProfile(DeckConfig cfg, string? name) =>
        name == null ? null : cfg.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    static void NormalizePages(DeckProfile profile, List<string> warnings)
    {
        profile.Pages ??= new List<DeckPage>();
        if (profile.Pages.Count == 0) profile.Pages.Add(new DeckPage { Name = "Page 1" });
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var page in profile.Pages)
        {
            n++;
            if (string.IsNullOrWhiteSpace(page.Name)) page.Name = $"Page {n}";
            page.Name = Unique(page.Name.Trim(), names, warnings, "page");
            page.Buttons = NormalizeButtons(page.Buttons, 0, warnings);
        }
    }

    /// <summary>Répare une liste de boutons (valeurs nulles, positions de titre ou d'image inconnues, dossiers trop profonds dont le contenu est remonté) ; <paramref name="depth"/> : profondeur du dossier courant.</summary>
    public static List<DeckButton> NormalizeButtons(List<DeckButton>? buttons, int depth, List<string> warnings)
    {
        var list = (buttons ?? new List<DeckButton>()).Where(b => b != null).ToList();
        List<DeckButton>? overflow = null;
        foreach (var b in list)
        {
            b.Label ??= "";
            b.Icon ??= "";
            b.Color = string.IsNullOrWhiteSpace(b.Color) ? "#334155" : b.Color;
            b.Actions = (b.Actions ?? new List<DeckAction>()).Where(a => a != null).ToList();
            foreach (var a in b.Actions) { a.Type ??= ""; a.Value ??= ""; }
            if (b.TitlePosition is not (null or "top" or "middle" or "bottom")) b.TitlePosition = null;
            if (b.ImageFit is not (null or "cover" or "contain")) b.ImageFit = null;
            if (string.IsNullOrWhiteSpace(b.ImagePath)) b.ImagePath = null;
            if (string.IsNullOrWhiteSpace(b.TextColor)) b.TextColor = null;

            if (b.IsFolder)
            {
                if (depth >= MaxFolderDepth)
                {
                    // Rien ne se perd : le contenu est remonté à la fin de ce dossier (les positions existantes ne bougent pas)
                    var moved = Flatten(b.Children);
                    if (moved.Count > 0) (overflow ??= new List<DeckButton>()).AddRange(moved);
                    warnings.Add($"Dossier « {b.Label} » trop profond : ses {moved.Count} bouton(s) ont été remontés dans le dossier qui le contient.");
                    b.Children = new List<DeckButton>();
                }
                else b.Children = NormalizeButtons(b.Children, depth + 1, warnings);
            }
            else b.Children = null;
        }
        if (overflow != null) list.AddRange(NormalizeButtons(overflow, MaxFolderDepth, warnings));
        // Les cases vides de fin ne servent à rien
        while (list.Count > 0 && list[^1].Blank) list.RemoveAt(list.Count - 1);
        return list;
    }

    /// <summary>Tous les boutons descendants d'un dossier, à plat ; les sous-dossiers sont gardés, vidés de leur contenu.</summary>
    static List<DeckButton> Flatten(List<DeckButton>? children)
    {
        var result = new List<DeckButton>();
        foreach (var c in children ?? new List<DeckButton>())
        {
            if (c == null || c.Blank) continue;
            var inner = c.IsFolder ? c.Children : null;
            if (c.IsFolder) c.Children = new List<DeckButton>();
            result.Add(c);
            if (inner != null) result.AddRange(Flatten(inner));
        }
        return result;
    }

    /// <summary>Rend un nom unique (insensible à la casse) en ajoutant « (2) », « (3 ) »…</summary>
    public static string Unique(string name, ISet<string> taken, List<string>? warnings = null, string kind = "élément")
    {
        string result = name;
        int i = 2;
        while (!taken.Add(result))
        {
            result = $"{name} ({i++})";
        }
        if (result != name) warnings?.Add($"Nom de {kind} en double : « {name} » renommé en « {result} ».");
        return result;
    }
}
