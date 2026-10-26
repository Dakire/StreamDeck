using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>
/// Cherche une version plus récente dans les releases GitHub du dépôt configuré. HTTPS seulement, <c>User-Agent</c> obligatoire, requêtes conditionnelles
/// (<c>If-None-Match</c> : un « 304 » ne compte pas dans la limite de l'API et rejoue la réponse gardée), limite de l'API respectée (<c>Retry-After</c>,
/// <c>X-RateLimit-Reset</c>). Ne lève jamais d'exception pour un incident réseau : le résultat est <see cref="UpdateStatus.Error"/> avec un message affichable.
/// Aucun jeton GitHub : les releases d'un dépôt public se lisent sans compte.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>Délai d'une requête à l'API.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    readonly HttpClient _http;
    readonly Func<DateTime> _utcNow;
    readonly string _userAgent;

    /// <summary>Vérificateur ; <paramref name="currentVersion"/> sert au <c>User-Agent</c> (« StreamDak/1.0.0 »).</summary>
    public UpdateChecker(HttpClient http, SemVer currentVersion, Func<DateTime>? utcNow = null)
    {
        _http = http;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _userAgent = "StreamDak/" + currentVersion;
    }

    /// <summary>
    /// Une vérification. <paramref name="state"/> est mis à jour (ETag, corps gardé, dernière vérification, limite) ; l'appelant l'enregistre.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(SemVer current, UpdateSettings settings, UpdateState state, CancellationToken cancellation)
    {
        if (settings.Repository is not { } repo) return new UpdateCheck(UpdateStatus.Disabled, null, "Aucun dépôt de mises à jour n'est configuré (update.json : owner, repo).");
        var now = _utcNow();
        if (state.NextAllowedUtc is { } wait && wait > now)
            return new UpdateCheck(UpdateStatus.Error, null, $"La limite de requêtes de GitHub est atteinte : réessaie vers {wait.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}.");

        string channel = settings.Beta ? "beta" : "stable", repoName = $"{repo.Owner}/{repo.Repo}".ToLowerInvariant();
        bool sameRequest = state.ETag != null && state.ETagChannel == channel && state.ETagRepo == repoName && state.CachedBody != null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, settings.Beta ? repo.ListUrl : repo.LatestUrl);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (sameRequest) request.Headers.TryAddWithoutValidation("If-None-Match", state.ETag);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (response.StatusCode == HttpStatusCode.NotModified && sameRequest)
            {
                state.LastCheckUtc = now;
                return Evaluate(current, settings, repo, state.CachedBody!, fromCache: true);
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                state.LastCheckUtc = now;
                state.ETag = state.CachedBody = null;
                return new UpdateCheck(UpdateStatus.UpToDate, null, "Aucune version publiée pour l'instant.");
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var until = RateLimitReset(response, now);
                state.NextAllowedUtc = until;
                return new UpdateCheck(UpdateStatus.Error, null, $"La limite de requêtes de GitHub est atteinte : réessaie vers {until.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}.");
            }
            if (!response.IsSuccessStatusCode)
                return new UpdateCheck(UpdateStatus.Error, null, $"GitHub a répondu {(int)response.StatusCode} : réessaie plus tard.");

            var body = await ReadBoundedAsync(response, timeout.Token);
            var result = Evaluate(current, settings, repo, body, fromCache: false);
            if (result.Status == UpdateStatus.Error) return result;
            state.LastCheckUtc = now;
            state.NextAllowedUtc = null;
            state.ETag = response.Headers.ETag?.ToString();
            (state.ETagChannel, state.ETagRepo, state.CachedBody) = state.ETag == null ? (null, null, null) : (channel, repoName, body);
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new UpdateCheck(UpdateStatus.Error, null, "GitHub ne répond pas (délai dépassé) : vérifie ta connexion.");
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"Mises à jour : réseau indisponible ({ex.Message})");
            return new UpdateCheck(UpdateStatus.Error, null, "Impossible de joindre GitHub : vérifie ta connexion.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException)
        {
            Log.Warn($"Mises à jour : réponse illisible ({ex.Message})");
            return new UpdateCheck(UpdateStatus.Error, null, "La réponse de GitHub est illisible.");
        }
    }

    static UpdateCheck Evaluate(SemVer current, UpdateSettings settings, UpdateRepo repo, string body, bool fromCache)
    {
        var release = GitHubReleases.Newest(JsonNode.Parse(body), repo, settings.Beta);
        if (release == null) return new UpdateCheck(UpdateStatus.UpToDate, null, "Aucune version publiée pour l'instant.", fromCache);
        return release.Version > current ? new UpdateCheck(UpdateStatus.Available, release, null, fromCache) : new UpdateCheck(UpdateStatus.UpToDate, release, null, fromCache);
    }

    static async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength > GitHubReleases.MaxResponseBytes) throw new InvalidOperationException("Réponse trop volumineuse");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > GitHubReleases.MaxResponseBytes) throw new InvalidOperationException("Réponse trop volumineuse");
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Instant de reprise indiqué par GitHub : <c>Retry-After</c> (secondes), sinon <c>X-RateLimit-Reset</c> (époque Unix), sinon une heure.</summary>
    static DateTime RateLimitReset(HttpResponseMessage response, DateTime now)
    {
        if (response.Headers.RetryAfter is { } retry)
        {
            if (retry.Delta is { } delta) return now + delta;
            if (retry.Date is { } date) return date.UtcDateTime;
        }
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch) && epoch > 0)
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return now.AddHours(1);
    }

    /// <summary>true si une vérification automatique est due : jamais faite, ou faite il y a plus de <paramref name="interval"/>.</summary>
    public static bool IsDue(UpdateState state, DateTime utcNow, TimeSpan interval) =>
        state.LastCheckUtc is not { } last || utcNow - last >= interval || last > utcNow.AddMinutes(5);   // horloge reculée : on revérifie
}
