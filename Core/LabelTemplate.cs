using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TouchDeck;

/// <summary>
/// Titres dynamiques : « {time} », « {date} », « {time:HH:mm:ss} », « {date:dddd d MMMM} » (formats .NET) et « {timer} »
/// (temps restant du minuteur du bouton). Le titre stocké ne change pas : seul l'affichage est calculé.
/// </summary>
public static partial class LabelTemplate
{
    /// <summary>Culture des dates et heures affichées (interface en français).</summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");

    [GeneratedRegex(@"\{(time|date|timer)(?::([^{}]+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    /// <summary>true si le titre contient un modèle ({time}, {date}, {timer}…) : il doit être recalculé à l'affichage.</summary>
    public static bool IsDynamic(string? label) => !string.IsNullOrEmpty(label) && Token().IsMatch(label);

    /// <summary>Le titre a-t-il besoin d'être redessiné chaque seconde (secondes affichées ou minuteur) ?</summary>
    public static bool NeedsSeconds(string? label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        foreach (Match m in Token().Matches(label))
            if (m.Groups[1].Value.Equals("timer", StringComparison.OrdinalIgnoreCase) || m.Groups[2].Value.Contains('s')) return true;
        return false;
    }

    /// <summary>Titre affiché : modèles remplacés par leur valeur à l'instant <paramref name="now"/>.</summary>
    /// <param name="label">Titre enregistré.</param>
    /// <param name="now">Instant à afficher.</param>
    /// <param name="timer">Temps restant du minuteur du bouton, s'il tourne (remplace {timer}).</param>
    public static string Render(string label, DateTime now, string? timer = null)
    {
        if (!IsDynamic(label)) return label;
        return Token().Replace(label, m =>
        {
            var format = m.Groups[2].Success ? m.Groups[2].Value : null;
            try
            {
                return m.Groups[1].Value.ToLowerInvariant() switch
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
