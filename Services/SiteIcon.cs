using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace StreamDak;

/// <summary>
/// Icône d'un site web (favicon) enregistrée comme image de bouton, sur demande de l'utilisateur seulement. Seules des adresses http et https sans identifiants
/// sont jointes, la page et l'icône sont limitées en taille, et l'image est décodée puis réencodée en PNG par <see cref="IconStore"/> (jamais de SVG ni de code).
/// </summary>
public static partial class SiteIcon
{
    /// <summary>Taille maximale de la page lue pour y chercher ses icônes.</summary>
    public const int MaxPageBytes = 512 * 1024;
    /// <summary>Taille maximale d'une icône téléchargée.</summary>
    public const int MaxIconBytes = 1024 * 1024;

    static readonly HttpClient Shared = Create();

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 4, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StreamDak/1.0 (button-icon)");
        return client;
    }

    /// <summary>true pour une adresse de page acceptée : http ou https, avec un hôte, sans identifiants.</summary>
    public static bool IsPageAddress(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048) return false;
        var candidate = text.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;
        uri = parsed;
        return true;
    }

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex LinkTag();

    [GeneratedRegex("([a-zA-Z-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Attribute();

    /// <summary>
    /// Adresses des icônes déclarées par la page, de la meilleure à la moins bonne (icône tactile d'abord, puis la plus grande taille annoncée), suivies de
    /// <c>/favicon.ico</c> de l'origine. Les SVG et les adresses qui ne sont pas http(s) sont ignorés.
    /// </summary>
    public static IReadOnlyList<Uri> Candidates(string html, Uri page)
    {
        var found = new List<(Uri Uri, int Score)>();
        try
        {
            foreach (Match tag in LinkTag().Matches(html.Length > MaxPageBytes ? html[..MaxPageBytes] : html))
            {
                var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match a in Attribute().Matches(tag.Value))
                    attributes[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
                if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href)) continue;
                var rels = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool touch = rels.Contains("apple-touch-icon") || rels.Contains("apple-touch-icon-precomposed");
                if (!touch && !rels.Contains("icon")) continue;
                if (!Uri.TryCreate(page, System.Net.WebUtility.HtmlDecode(href.Trim()), out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) continue;
                if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || (attributes.TryGetValue("type", out var type) && type.Contains("svg", StringComparison.OrdinalIgnoreCase))) continue;
                int size = 0;
                if (attributes.TryGetValue("sizes", out var sizes) && sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('x', 'X')[0])
                        .Select(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).DefaultIfEmpty(0).Max() is var biggest) size = biggest;
                found.Add((uri, (touch ? 10_000 : 0) + Math.Min(size, 4096)));
            }
        }
        catch (RegexMatchTimeoutException) { /* page pathologique : on retombe sur favicon.ico */ }
        var list = found.OrderByDescending(f => f.Score).Select(f => f.Uri).DistinctBy(u => u.AbsoluteUri).ToList();
        var fallback = new Uri(page, "/favicon.ico");
        if (!list.Contains(fallback)) list.Add(fallback);
        return list;
    }

    /// <summary>
    /// Récupère l'icône du site de <paramref name="page"/> et l'enregistre (nom d'image de <see cref="IconStore"/>). Lève <see cref="InvalidDataException"/>
    /// avec un message affichable si le site n'en donne aucune.
    /// </summary>
    public static async Task<string> FetchAsync(Uri page, HttpClient? client = null, CancellationToken cancellation = default)
    {
        client ??= Shared;
        string html = "";
        try { html = System.Text.Encoding.UTF8.GetString(await ReadAsync(client, page, MaxPageBytes, cancellation)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException) { /* la page est illisible : /favicon.ico peut encore exister */ }

        string problem = "Ce site ne donne pas d'icône";
        foreach (var candidate in Candidates(html, page))
        {
            try
            {
                var bytes = await ReadAsync(client, candidate, MaxIconBytes, cancellation);
                using var stream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = IconStore.LargestFrame(decoder);
                if (frame == null) continue;
                return IconStore.Store(frame);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or NotSupportedException or FileFormatException
                                           or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            {
                if (cancellation.IsCancellationRequested) throw;
                problem = ex is HttpRequestException { StatusCode: not null } ? "Ce site ne donne pas d'icône"
                    : ex is HttpRequestException or TaskCanceledException ? "Le site ne répond pas" : "L'icône du site n'est pas lisible";
            }
        }
        throw new InvalidDataException(problem);
    }

    static async Task<byte[]> ReadAsync(HttpClient client, Uri uri, int limit, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Réponse trop volumineuse");
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellation)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit) throw new InvalidDataException("Réponse trop volumineuse");
        }
        return buffer.ToArray();
    }
}
