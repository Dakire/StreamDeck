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
        text = Assignment().Replace(text, "$1" + Mask);
        text = Header().Replace(text, "$1" + Mask);
        text = Webhook().Replace(text, "$1" + Mask);
        text = PathKey().Replace(text, "$1" + Mask);
        text = BareCode().Replace(text, "$1" + Mask);
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
    [GeneratedRegex(@"(\b(?:password|passwd|pwd|mot de passe|mdp|jeton|secret|client_secret|api[_-]?key|apikey|token|access_token|refresh_token)\b""?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s;""'&}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();

    // En-têtes dont la valeur entière est secrète, y compris sans schéma (« Authorization: pk_… ») : Cookie: a=b; c=d
    [GeneratedRegex(@"(\b(?:Authorization|Proxy-Authorization|Cookie|Set-Cookie|X-Api-Key|X-Auth-Token|X-Access-Token|hue-application-key)""?\s*[:=]\s*)(?:""[^""]*""|[^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Header();

    // Webhooks Discord (/api/webhooks/id/jeton) et Slack (/services/T…/B…/x) : l'adresse entière donne le droit d'écrire
    [GeneratedRegex(@"(/api/webhooks/\d+/|/services/T[A-Za-z0-9]+/B[A-Za-z0-9]+/)[A-Za-z0-9_\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Webhook();

    // Hue : /api/<clé d'application>/lights
    [GeneratedRegex(@"(/api/)[A-Za-z0-9_\-]{20,}(?=/|\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex PathKey();

    // Code d'autorisation OAuth hors paramètre d'adresse : code=4/0AbCdEf…
    [GeneratedRegex(@"(\bcode=)[A-Za-z0-9._~/\-]{10,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareCode();
}
