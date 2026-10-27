using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Mise en forme et lecture des réglages des widgets de données (pur, testé).</summary>
public static partial class WidgetFormat
{
    /// <summary>Taille maximale lue dans une réponse web : un widget n'a besoin que de quelques valeurs.</summary>
    public const int MaxResponseBytes = 256 * 1024;

    /// <summary>Délai d'une requête web d'un widget.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Octets en « 512 Mo », « 12,4 Go », « 1,2 To » (une décimale sous 100, sans au-dessus).</summary>
    public static string Bytes(double bytes, CultureInfo culture)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To", "Po" };
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1) { bytes /= 1024; unit++; }
        return (bytes >= 100 || unit == 0 ? bytes.ToString("0", culture) : bytes.ToString("0.0", culture)) + " " + units[unit];
    }

    /// <summary>Durée en « 45 min », « 3 h 20 », « 2 j 4 h ».</summary>
    public static string Duration(TimeSpan span, CultureInfo culture)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalDays >= 1) return $"{((int)span.TotalDays).ToString(culture)} j {span.Hours.ToString(culture)} h";
        if (span.TotalHours >= 1) return $"{((int)span.TotalHours).ToString(culture)} h {span.Minutes.ToString("00", culture)}";
        return $"{Math.Max(0, span.Minutes).ToString(culture)} min";
    }

    /// <summary>Débit en « 830 Ko/s », « 12,4 Mo/s ».</summary>
    public static string Rate(double bytesPerSecond, CultureInfo culture) => Bytes(Math.Max(0, bytesPerSecond), culture) + "/s";

    /// <summary>Débit moyen entre deux relevés d'un compteur d'octets ; 0 si le compteur a été remis à zéro (interface relancée) ou si le temps n'a pas avancé.</summary>
    public static double BytesPerSecond(long previous, long current, double seconds) =>
        seconds <= 0 || current < previous ? 0 : (current - previous) / seconds;

    static readonly char[] ListSeparators = { ';', ',', '\n', '\r', ' ', '\t' };

    /// <summary>Éléments d'une liste saisie (séparés par « ; », « , », espaces ou sauts de ligne), sans doublon ni vide, au plus <paramref name="max"/>.</summary>
    public static IReadOnlyList<string> SplitList(string? text, int max = 8) =>
        (text ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();

    /// <summary>true si le texte est un nom d'hôte ou une adresse IP plausible (lettres, chiffres, point, tiret, deux-points et crochets d'IPv6).</summary>
    public static bool IsHostName(string? text) => !string.IsNullOrEmpty(text) && HostPattern().IsMatch(text);

    [GeneratedRegex(@"^[A-Za-z0-9._\-:\[\]]{1,253}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>
    /// Valeur d'un chemin « a.b.0.c » dans un document JSON, lue comme un nombre (un texte numérique est accepté) ; false si le chemin ne mène à aucun nombre.
    /// </summary>
    public static bool TryGetNumber(JsonNode? root, string path, out double value)
    {
        value = 0;
        var node = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (node is JsonObject obj) node = obj.TryGetPropertyValue(part, out var child) ? child : null;
            else if (node is JsonArray array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count) node = array[index];
            else return false;
            if (node == null) return false;
        }
        if (node is not JsonValue json) return false;
        if (json.TryGetValue<double>(out var d)) { value = d; return double.IsFinite(d); }
        if (json.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { value = d; return double.IsFinite(d); }
        return false;
    }

    /// <summary>GET d'un document JSON : http ou https seulement, délai de 10 s, réponse limitée à <see cref="MaxResponseBytes"/>.</summary>
    public static async Task<JsonNode?> GetJsonAsync(WidgetContext context, Uri uri, CancellationToken cancellation)
    {
        var bytes = await GetBytesAsync(context, uri, cancellation);
        try { return JsonNode.Parse(bytes); }
        catch (System.Text.Json.JsonException) { throw new InvalidOperationException("La réponse n'est pas du JSON"); }
    }

    /// <summary>GET d'un document : http ou https seulement, délai de 10 s, réponse limitée à <paramref name="limit"/> octets (par défaut <see cref="MaxResponseBytes"/>).</summary>
    public static async Task<byte[]> GetBytesAsync(WidgetContext context, Uri uri, CancellationToken cancellation, int limit = MaxResponseBytes)
    {
        if (uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("L'adresse doit commencer par http:// ou https://");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await context.Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Le serveur a répondu {(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Réponse trop volumineuse");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > limit) throw new InvalidOperationException("Réponse trop volumineuse");
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException("Le serveur ne répond pas (délai dépassé)"); }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Serveur injoignable : " + ex.Message, ex); }
    }
}
