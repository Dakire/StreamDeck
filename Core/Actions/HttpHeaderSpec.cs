using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StreamDak.Actions;

/// <summary>
/// En-têtes personnalisés d'une requête web : un « Nom: valeur » par ligne. Les en-têtes que le client pose lui-même (Host, Content-Length…) sont refusés,
/// ainsi que tout saut de ligne dans une valeur (injection d'en-tête). Au plus 20 en-têtes, 4 096 caractères au total.
/// </summary>
public static partial class HttpHeaderSpec
{
    /// <summary>Nombre maximal d'en-têtes.</summary>
    public const int MaxHeaders = 20;
    /// <summary>Taille maximale du texte des en-têtes.</summary>
    public const int MaxLength = 4096;
    /// <summary>Méthodes acceptées.</summary>
    public static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

    static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade", "Expect", "Proxy-Authorization", "TE", "Trailer",
    };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]*$")]
    private static partial Regex NamePattern();

    /// <summary>Lit les lignes d'en-têtes ; false avec un message lisible si une ligne est invalide.</summary>
    public static bool TryParse(string? text, out List<(string Name, string Value)> headers, out string error)
    {
        headers = new List<(string, string)>();
        error = "";
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (text.Length > MaxLength) { error = $"Les en-têtes dépassent {MaxLength} caractères"; return false; }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) { error = $"« {Shorten(line)} » : écris « Nom: valeur »"; return false; }
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!NamePattern().IsMatch(name)) { error = $"« {Shorten(name)} » n'est pas un nom d'en-tête valide"; return false; }
            if (Forbidden.Contains(name)) { error = $"L'en-tête « {name} » est géré par StreamDak"; return false; }
            foreach (var c in value) if (c < ' ' && c != '\t' || c == '\u007f') { error = $"L'en-tête « {name} » contient un caractère de contrôle"; return false; }
            if (headers.Count >= MaxHeaders) { error = $"Au plus {MaxHeaders} en-têtes"; return false; }
            headers.Add((name, value));
        }
        return true;
    }

    /// <summary>true si la méthode est vide (automatique) ou l'une de <see cref="Methods"/>.</summary>
    public static bool IsValidMethod(string? method) =>
        string.IsNullOrWhiteSpace(method) || Array.Exists(Methods, m => m.Equals(method.Trim(), StringComparison.OrdinalIgnoreCase));

    static string Shorten(string s) => s.Length <= 30 ? s : s[..30] + "…";
}
