using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Hotkeys;

/// <summary>Un raccourci global voulu par la configuration.</summary>
/// <param name="Id">Identifiant d'enregistrement, stable pour une combinaison.</param>
/// <param name="Combo">Combinaison normalisée en minuscules (« ctrl+alt+f1 »).</param>
/// <param name="Button">Bouton déclenché.</param>
public sealed record PlannedHotkey(int Id, string Combo, DeckButton Button);

/// <summary>Un raccourci refusé avant même de le demander au système.</summary>
/// <param name="Combo">Combinaison saisie.</param>
/// <param name="Label">Titre du bouton.</param>
/// <param name="Reason">Raison lisible.</param>
public sealed record RejectedHotkey(string Combo, string Label, string Reason);

/// <summary>
/// Raccourcis globaux voulus par la configuration : combinaison normalisée, validée (syntaxe, un modificateur au moins sauf F13 à F24 : une lettre seule
/// volerait la frappe de toutes les applications) et sans doublon (la première occurrence l'emporte). Logique pure : l'enregistrement auprès du système
/// est fait par <see cref="IHotkeyService"/>.
/// </summary>
public static class GlobalHotkeyPlan
{
    /// <summary>Nombre maximal de raccourcis globaux (chacun consomme une ressource du système).</summary>
    public const int Max = 64;

    /// <summary>Combinaison en minuscules, sans espaces, modificateurs d'abord (« Alt + Ctrl + F1 » → « ctrl+alt+f1 »).</summary>
    public static string Normalize(string combo)
    {
        var parts = HotkeySyntax.Split(combo).Select(p => p.ToLowerInvariant()).Select(p => p == "control" ? "ctrl" : p).ToList();
        var order = new[] { "ctrl", "alt", "shift", "win" };
        var mods = order.Where(parts.Contains);
        var keys = parts.Where(p => !order.Contains(p)).Distinct();
        return string.Join("+", mods.Concat(keys));
    }

    /// <summary>Vérifie une combinaison pour un usage global ; false avec la raison sinon.</summary>
    public static bool TryValidate(string? combo, out string error, Func<char, bool>? canType = null)
    {
        if (!HotkeySyntax.TryValidate(combo, out error, canType)) return false;
        var parts = HotkeySyntax.Split(combo);
        var keys = parts.Where(p => !HotkeySyntax.Modifiers.Contains(p)).ToList();
        if (keys.Count != 1) { error = "Une seule touche en plus des modificateurs"; return false; }
        bool hasModifier = parts.Any(HotkeySyntax.Modifiers.Contains);
        bool freeKey = HotkeySyntax.IsFunctionKey(keys[0], out var n) && n >= 13;
        if (!hasModifier && !freeKey) { error = "Ajoute Ctrl, Alt, Maj ou Win (sans modificateur, seules F13 à F24 sont acceptées)"; return false; }
        if (parts.Contains("win", StringComparer.OrdinalIgnoreCase) && keys[0].Equals("l", StringComparison.OrdinalIgnoreCase)) { error = "Win+L est réservé à Windows"; return false; }
        error = "";
        return true;
    }

    /// <summary>Raccourcis voulus par la configuration, et ceux qui sont refusés.</summary>
    public static (List<PlannedHotkey> Planned, List<RejectedHotkey> Rejected) Build(DeckConfig config, Func<char, bool>? canType = null)
    {
        var planned = new List<PlannedHotkey>();
        var rejected = new List<RejectedHotkey>();
        var seen = new Dictionary<string, string>();
        foreach (var profile in config.Profiles)
            foreach (var page in profile.Pages)
                foreach (var button in DeckDocument.AllButtons(page.Buttons))
                {
                    if (string.IsNullOrWhiteSpace(button.GlobalHotkey) || button.Blank) continue;
                    var label = string.IsNullOrWhiteSpace(button.Label) ? "(sans titre)" : button.Label;
                    if (!TryValidate(button.GlobalHotkey, out var error, canType)) { rejected.Add(new RejectedHotkey(button.GlobalHotkey, label, error)); continue; }
                    var combo = Normalize(button.GlobalHotkey);
                    if (seen.TryGetValue(combo, out var owner)) { rejected.Add(new RejectedHotkey(button.GlobalHotkey, label, $"Déjà utilisé par « {owner} »")); continue; }
                    if (planned.Count >= Max) { rejected.Add(new RejectedHotkey(button.GlobalHotkey, label, $"Plus de {Max} raccourcis globaux")); continue; }
                    seen[combo] = label;
                    planned.Add(new PlannedHotkey(planned.Count + 1, combo, button));
                }
        return (planned, rejected);
    }
}
