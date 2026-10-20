using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations;

/// <summary>
/// Base des services web à jeton (gestionnaires de tâches) : un client HTTP, des requêtes JSON, et des erreurs traduites en messages
/// utiles (jeton refusé, limite de débit avec le délai à attendre, message du service). Pas de relevé périodique : ces services
/// limitent le nombre de requêtes, l'état n'est lu qu'à la connexion et les erreurs apparaissent à l'exécution des commandes.
/// </summary>
public abstract class RestIntegrationBase : IntegrationBase, IDisposable
{
    HttpClient? _http;

    /// <summary>Client de la connexion en cours.</summary>
    protected HttpClient Http => _http ?? throw new InvalidOperationException($"{Name} n'est pas connecté");
    /// <summary>Adresse de base de l'API (le réglage caché « baseurl » la remplace, pour les tests).</summary>
    protected abstract string DefaultBaseUrl { get; }
    /// <summary>Jeton du service, en clair, jamais écrit au journal.</summary>
    protected string Token { get; private set; } = "";

    /// <summary>Prépare les en-têtes d'authentification et de version du service.</summary>
    protected abstract void Configure(HttpClient http, string token);
    /// <summary>Complète une requête juste avant l'envoi (jeton d'accès à renouveler). Par défaut, rien : les en-têtes fixes de <see cref="Configure"/> suffisent.</summary>
    protected virtual Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellation) => Task.CompletedTask;
    /// <summary>Vérifie le jeton et lit ce qu'il faut garder (utilisateur, équipe). Lève une exception à message clair en cas d'échec.</summary>
    protected abstract Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation);

    /// <inheritdoc/>
    protected sealed override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        Token = settings.Secret("token");
        if (Token.Length == 0) throw new InvalidOperationException($"Renseigne le jeton {Name} dans les réglages de l'intégration");
        var baseUrl = settings.Get("baseurl", DefaultBaseUrl).TrimEnd('/') + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
        Configure(_http, Token);
        try { await VerifyAsync(settings, cancellation); }
        catch { _http.Dispose(); _http = null; throw; }
    }

    /// <summary>Envoie une requête JSON et retourne la réponse analysée (objet vide si le service ne répond rien).</summary>
    protected async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : path.TrimStart('/'));
        await AuthorizeAsync(request, cancellation);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await Http.SendAsync(request, cancellation); }
        catch (HttpRequestException ex) { throw new InvalidOperationException($"{Name} est injoignable : {ex.Message}", ex); }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException($"{Name} ne répond pas (délai dépassé)"); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellation);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
                try { return JsonNode.Parse(text) ?? new JsonObject(); }
                catch (JsonException) { throw new InvalidOperationException($"{Name} a répondu autre chose que du JSON"); }
            }
            throw new InvalidOperationException(Describe(response, text));
        }
    }

    /// <summary>Message pour une réponse d'erreur : jeton refusé, limite atteinte, ou message du service.</summary>
    protected virtual string Describe(HttpResponseMessage response, string body)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return $"{Name} refuse le jeton ou l'accès (vérifie le jeton et les droits de l'intégration)";
            case HttpStatusCode.TooManyRequests:
                var wait = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                return wait is > 0 ? $"{Name} limite le nombre de requêtes : réessaie dans {(int)Math.Ceiling(wait.Value)} s" : $"{Name} limite le nombre de requêtes : réessaie dans un instant";
        }
        string detail = "";
        try
        {
            var json = JsonNode.Parse(body);
            detail = json?["err"]?.ToString() ?? json?["error"]?.ToString() ?? json?["message"]?.ToString() ?? "";
        }
        catch (JsonException) { /* corps non JSON */ }
        return detail.Length > 0 ? $"{Name} : {detail}" : $"{Name} a répondu {(int)response.StatusCode}";
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        _http?.Dispose();
        _http = null;
        return Task.CompletedTask;
    }

    /// <summary>Libère le client HTTP.</summary>
    public void Dispose()
    {
        DisconnectCoreAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>Découpe « gauche=droite » (premier signe égal) ou lève une erreur qui montre le format attendu.</summary>
    protected static (string Left, string Right) SplitPair(string? argument, string format)
    {
        var text = RequireArgument(argument, format);
        var eq = text.IndexOf('=');
        if (eq <= 0 || eq == text.Length - 1) throw new ArgumentException($"Écris {format}");
        return (text[..eq].Trim(), text[(eq + 1)..].Trim());
    }
}
