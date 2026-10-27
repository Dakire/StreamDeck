using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>
/// Conflits de raccourcis. StreamDak n'enregistre aucun raccourci global : il <b>envoie</b> des combinaisons à l'application au premier plan, et un raccourci
/// global d'une autre application (Discord) l'intercepte au passage. Une même combinaison peut donc agir deux fois : dans l'application qui l'a déclarée
/// en global, et dans celle qui a le focus si elle l'utilise aussi (Ctrl+Maj+M coupe le micro de Teams). Une combinaison précédée d'une mise au premier
/// plan (étape « focus ») vise une application précise : elle n'est pas concernée.
/// </summary>
public static class HotkeyConflicts
{
    static readonly string[] ModifierOrder = { "ctrl", "alt", "shift", "win" };

    /// <summary>Raccourcis locaux connus (ils n'agissent que si l'application a le focus), en plus de ceux des applications cibles du catalogue.</summary>
    static readonly (string Combo, string Use)[] LocalShortcuts =
    {
        ("ctrl+shift+m", "Discord : micro (raccourci local, seulement quand Discord a le focus)"),
        ("ctrl+shift+d", "Discord : sourdine (raccourci local, seulement quand Discord a le focus)"),
    };

    static readonly Lazy<Dictionary<string, List<string>>> Known = new(BuildKnown);

    static bool Is(DeckAction action, string type) => string.Equals(action.Type, type, StringComparison.OrdinalIgnoreCase);

    static Dictionary<string, List<string>> BuildKnown()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string combo, string use)
        {
            var key = Normalize(combo);
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
            if (!list.Contains(use)) list.Add(use);
        }
        foreach (var preset in AppShortcutPresets.Entries)
        {
            // Seuls les raccourcis envoyés à une application précise sont « locaux » : ceux des raccourcis globaux (Discord) n'entrent pas dans la table
            if (!preset.Actions.Any(a => Is(a, ActionTypes.Focus))) continue;
            foreach (var step in preset.Actions.Where(a => Is(a, ActionTypes.Hotkey))) Add(step.Value, preset.Name);
        }
        foreach (var (combo, use) in LocalShortcuts) Add(combo, use);
        return map;
    }

    /// <summary>Forme canonique : minuscules, modificateurs dans l'ordre ctrl, alt, shift, win, puis les touches (« Shift + Ctrl+M » → « ctrl+shift+m »).</summary>
    public static string Normalize(string? combo)
    {
        var parts = HotkeySyntax.Split(combo).Select(p => p.Equals("control", StringComparison.OrdinalIgnoreCase) ? "ctrl" : p.ToLowerInvariant()).ToList();
        var mods = ModifierOrder.Where(parts.Contains);
        var keys = parts.Where(p => !HotkeySyntax.Modifiers.Contains(p));
        return string.Join("+", mods.Concat(keys));
    }

    /// <summary>Combinaison lisible : « Ctrl+Maj+M ».</summary>
    public static string Display(string? combo) =>
        string.Join("+", Normalize(combo).Split('+', StringSplitOptions.RemoveEmptyEntries).Select(p => p switch
        {
            "ctrl" => "Ctrl", "alt" => "Alt", "shift" => "Maj", "win" => "Win",
            _ => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..],
        }));

    /// <summary>Combinaisons que le bouton envoie à l'application au premier plan (étapes « hotkey » qui ne suivent aucune mise au premier plan).</summary>
    public static IEnumerable<string> UntargetedHotkeys(DeckButton button)
    {
        foreach (var step in button.Actions)
        {
            if (Is(step, ActionTypes.Focus)) yield break;
            if (Is(step, ActionTypes.Hotkey) && HotkeySyntax.Split(step.Value).Length > 0) yield return step.Value;
        }
    }

    /// <summary>Autres usages connus d'une combinaison (raccourcis des applications cibles, raccourcis locaux de Discord).</summary>
    public static IReadOnlyList<string> KnownUses(string combo) =>
        Known.Value.TryGetValue(Normalize(combo), out var uses) ? uses : Array.Empty<string>();

    /// <summary>
    /// Avertissements d'un bouton : combinaison déjà utilisée par une application connue, ou envoyée telle quelle par un autre bouton du deck (titre différent).
    /// <paramref name="self"/> : le bouton tel qu'il est dans la configuration (exclu de la recherche ; le bouton examiné peut être un brouillon).
    /// </summary>
    public static List<string> Warnings(DeckButton button, DeckConfig config, DeckButton? self = null)
    {
        var warnings = new List<string>();
        foreach (var combo in UntargetedHotkeys(button).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = Normalize(combo);
            var uses = KnownUses(combo);
            if (uses.Count > 0)
                warnings.Add($"{Display(combo)} part vers l'application au premier plan, et c'est aussi : {string.Join(" ; ", uses)}. Choisis une combinaison libre " +
                             "(par exemple Ctrl+Alt+Maj+une lettre) et déclare-la dans l'application visée.");
            var others = config.Profiles.SelectMany(p => p.Pages.SelectMany(page => DeckDocument.AllButtons(page.Buttons).Select(b => (Profile: p.Name, Page: page.Name, Button: b))))
                .Where(x => !ReferenceEquals(x.Button, self) && !ReferenceEquals(x.Button, button)
                            && !string.Equals(x.Button.Label, button.Label, StringComparison.Ordinal)
                            && UntargetedHotkeys(x.Button).Any(c => Normalize(c) == key))
                .Take(3).ToList();
            if (others.Count > 0)
                warnings.Add($"{Display(combo)} est aussi envoyé par {string.Join(", ", others.Select(o => $"« {o.Button.Label} » (page {o.Page})"))} : les deux boutons feront la même chose.");
        }
        return warnings;
    }
}
