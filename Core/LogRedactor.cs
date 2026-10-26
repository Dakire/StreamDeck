using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>
/// Masque dans un texte de journal ce qui ressemble à un secret : mot de passe ou jeton dans une adresse, en-tête
/// <c>Authorization</c>, valeur après « password= », « token: »… Filet de sécurité : le code ne doit déjà pas journaliser de secret.
/// </summary>
public static partial class LogRedactor
{
    /// <summary>Texte qui remplace une valeur masquée.</summary>
    public const string Mask = "***";

    /// <summary>Le texte sans secret apparent.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        text = UserInfo().Replace(text, "$1" + Mask + "@");
        text = QueryParameter().Replace(text, "$1" + Mask);
        text = AuthScheme().Replace(text, "$1 " + Mask);
        text = Assignment().Replace(text, "$1$2" + Mask);
        return text;
    }

    // https://utilisateur:motdepasse@hôte
    [GeneratedRegex(@"(://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserInfo();

    // ?access_token=…&key=…
    [GeneratedRegex(@"([?&](?:access_token|refresh_token|id_token|token|key|api_?key|apikey|secret|client_secret|password|passwd|pwd|auth|sig|signature|code)=)[^&\s""'#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryParameter();

    // Authorization: Bearer abc.def
    [GeneratedRegex(@"\b(Bearer|Basic|Token)\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthScheme();

    // password=hunter2, "token": "abc", secret: xyz
    [GeneratedRegex(@"(\b(?:password|passwd|pwd|secret|client_secret|api_?key|apikey|token|access_token|refresh_token)\b""?\s*[:=]\s*""?)[^\s,;""'&}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();
}
