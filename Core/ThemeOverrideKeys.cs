using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>Clés et bornes des réglages de thème personnalisé, de délai des pages web et de déplacement au toucher (ADR 0034). Sans WPF.</summary>
public static class ThemeOverrideKeys
{
    /// <summary>Couleurs modifiables, dans l'ordre de l'éditeur.</summary>
    public static readonly IReadOnlyList<string> All = new[] { "accent", "background", "surface", "text", "success", "danger" };

    /// <summary>Bornes du délai des pages web (s) et du seuil d'appui long (ms).</summary>
    public const int MinWebIdleSeconds = 5, MaxWebIdleSeconds = 300, DefaultWebIdleSeconds = 30;
    /// <summary>Bornes du seuil d'appui long (ms).</summary>
    public const int MinLiftDelayMs = 300, MaxLiftDelayMs = 1500, DefaultLiftDelayMs = 500;

    static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>true pour #RRGGBB.</summary>
    public static bool IsHex(string? value) => value != null && Hex.IsMatch(value);

    /// <summary>Garde les clés connues à valeur #RRGGBB (en majuscules) ; null s'il n'en reste aucune (rien n'est écrit).</summary>
    public static Dictionary<string, string>? Sanitize(Dictionary<string, string>? overrides)
    {
        if (overrides == null) return null;
        var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in All)
            if (overrides.FirstOrDefault(p => string.Equals(p.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)) is { Value: { } v } && IsHex(v.Trim()))
                clean[key] = v.Trim().ToUpperInvariant();
        return clean.Count == 0 ? null : clean;
    }

    /// <summary>Délai effectif d'une page web activée.</summary>
    public static TimeSpan WebIdle(DeckConfig c) => TimeSpan.FromSeconds(c.WebIdleExitSeconds == 0 ? DefaultWebIdleSeconds : c.WebIdleExitSeconds);

    /// <summary>Seuil effectif de l'appui long qui soulève un bouton (ms).</summary>
    public static int LiftDelay(DeckConfig c) => c.LiftDelayMs == 0 ? DefaultLiftDelayMs : c.LiftDelayMs;
}
