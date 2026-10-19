using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TouchDeck;

/// <summary>
/// Titres dynamiques : « {time} », « {date} », « {time:HH:mm:ss} », « {date:dddd d MMMM} » (formats .NET), « {timer} »
/// (temps restant du minuteur du bouton) et valeurs d'état des intégrations « {obs.scene} », « {sys.cpu} » (le texte après
/// les deux-points est affiché tant que la valeur est inconnue : « {obs.scene:hors ligne} »). Le titre stocké ne change pas :
/// seul l'affichage est calculé.
/// </summary>
public static partial class LabelTemplate
{
    /// <summary>Culture des dates et heures affichées (interface en français).</summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Texte affiché pour une valeur inconnue quand le modèle n'en propose pas.</summary>
    public const string Unknown = "—";

    [GeneratedRegex(@"\{(?:(?<kind>time|date|timer)|(?<key>[a-z][a-z0-9]*\.[a-z0-9_.\-]+))(?::(?<fmt>[^{}]+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    /// <summary>true si le titre contient un modèle ({time}, {date}, {timer}, {intégration.valeur}) : il doit être recalculé à l'affichage.</summary>
    public static bool IsDynamic(string? label) => !string.IsNullOrEmpty(label) && Token().IsMatch(label);

    /// <summary>Le titre a-t-il besoin d'être redessiné chaque seconde (secondes affichées ou minuteur) ?</summary>
    public static bool NeedsSeconds(string? label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        foreach (Match m in Token().Matches(label))
        {
            if (!m.Groups["kind"].Success) continue;
            if (m.Groups["kind"].Value.Equals("timer", StringComparison.OrdinalIgnoreCase) || m.Groups["fmt"].Value.Contains('s')) return true;
        }
        return false;
    }

    /// <summary>true si le titre contient une heure, une date ou un minuteur (à redessiner avec l'horloge), pas seulement des valeurs d'intégration.</summary>
    public static bool HasClockTokens(string? label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        foreach (Match m in Token().Matches(label)) if (m.Groups["kind"].Success) return true;
        return false;
    }

    /// <summary>Clés des valeurs d'intégration citées par le titre (« obs.scene »).</summary>
    public static IReadOnlyList<string> ValueKeys(string? label)
    {
        var keys = new List<string>();
        if (string.IsNullOrEmpty(label)) return keys;
        foreach (Match m in Token().Matches(label))
            if (m.Groups["key"].Success) keys.Add(m.Groups["key"].Value.ToLowerInvariant());
        return keys;
    }

    /// <summary>Titre affiché : modèles remplacés par leur valeur à l'instant <paramref name="now"/>.</summary>
    /// <param name="label">Titre enregistré.</param>
    /// <param name="now">Instant à afficher.</param>
    /// <param name="timer">Temps restant du minuteur du bouton, s'il tourne (remplace {timer}).</param>
    /// <param name="values">Valeur d'état d'une intégration d'après sa clé (« obs.scene »), ou null si elle est inconnue.</param>
    public static string Render(string label, DateTime now, string? timer = null, Func<string, string?>? values = null)
    {
        if (!IsDynamic(label)) return label;
        return Token().Replace(label, m =>
        {
            var format = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            if (m.Groups["key"].Success)
                return values?.Invoke(m.Groups["key"].Value.ToLowerInvariant()) ?? format ?? Unknown;
            try
            {
                return m.Groups["kind"].Value.ToLowerInvariant() switch
                {
                    "time" => now.ToString(format ?? "HH:mm", Culture),
                    "date" => now.ToString(format ?? "ddd d MMM", Culture),
                    _ => timer ?? "",
                };
            }
            catch (FormatException) { return m.Value; }   // format invalide : affiché tel quel
        });
    }
}