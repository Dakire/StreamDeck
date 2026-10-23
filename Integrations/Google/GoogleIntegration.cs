using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Google;

/// <summary>
/// Google Workspace avec le projet Google Cloud de l'utilisateur : ajouter une ligne ou écrire une cellule dans Google Sheets,
/// ajouter du texte à un document Google Docs, créer un événement Agenda en langage naturel. Connexion OAuth 2.0 « application de
/// bureau » (navigateur, redirection locale 127.0.0.1, PKCE) ; seul le jeton de renouvellement est conservé, chiffré (DPAPI).
/// StreamDak n'embarque aucune application Google : l'écran « application non vérifiée » est normal pour un usage personnel.
/// </summary>
public sealed class GoogleIntegration : RestIntegrationBase, IPairableIntegration
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "google";
    /// <summary>Droits demandés : tableurs, documents et événements d'agenda, rien d'autre (ni Drive complet ni Gmail).</summary>
    public const string Scopes = "https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/documents https://www.googleapis.com/auth/calendar.events";
    static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);

    string _apiUrl = "", _clientId = "", _clientSecret = "", _spreadsheet = "", _range = "A:A", _document = "";
    string _accessToken = "";
    DateTime _accessExpiry = DateTime.MinValue;
    readonly SemaphoreSlim _refresh = new(1, 1);

    /// <summary>Ouvre l'adresse de connexion dans le navigateur ; remplacé dans les tests.</summary>
    public Func<Uri, Task> OpenBrowser { get; set; } = url =>
    {
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    };

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Google Workspace";
    /// <inheritdoc/>
    public override string Category => "Google Workspace";
    /// <inheritdoc/>
    public override string Icon => "TableSimple24";
    /// <inheritdoc/>
    public override string Help =>
        "1) Console Google Cloud : crée un projet, active les API Google Sheets, Docs et Calendar. 2) Écran de consentement : type « Externe », puis passe le projet en « Production » (sinon Google invalide la connexion tous les 7 jours). 3) Identifiants : crée un ID client « Application de bureau », copie son identifiant et son secret ici. 4) Clique sur « Se connecter à Google » et accepte l'écran « application non vérifiée » (Paramètres avancés › Accéder à…).";
    /// <inheritdoc/>
    protected override string DefaultBaseUrl => "https://www.googleapis.com";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("clientid", "ID client OAuth", SettingKind.Text, null, "Se termine par .apps.googleusercontent.com."),
        new SettingField("clientsecret", "Secret client", SettingKind.Secret, null, "Celui de l'ID client « Application de bureau »."),
        new SettingField("token", "Autorisation", SettingKind.Secret, null, "Remplie par le bouton « Se connecter à Google »."),
        new SettingField("spreadsheet", "Feuille de calcul", SettingKind.Text, null, "Identifiant du tableur (la partie longue de son adresse, entre /d/ et /edit)."),
        new SettingField("range", "Plage d'ajout", SettingKind.Text, "A:A", "Où ajouter les lignes ; avec le nom de l'onglet si besoin (Feuille 1!A:C)."),
        new SettingField("document", "Document", SettingKind.Text, null, "Identifiant du document Google Docs (entre /d/ et /edit)."),
    };

    /// <inheritdoc/>
    public string PairLabel => "Se connecter à Google";
    /// <inheritdoc/>
    public string PairHelp => "Renseigne l'ID client et le secret, puis clique : ton navigateur s'ouvre pour autoriser StreamDak (3 minutes).";
    /// <inheritdoc/>
    public string PairedSecretField => "token";

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("sheet-append", "Ajouter une ligne (Sheets)", "Ajoute une ligne au tableur ; {date}, {time} et {datetime} sont remplacés.", "valeurs séparées par « ; » (Café;{datetime})", true, null, "TableSimple24", "#107C41"),
        new IntegrationCommand("sheet-set", "Écrire une cellule (Sheets)", "Écrit une valeur dans une cellule.", "« cellule=valeur » (B2=Terminé)", true, null, "TableEdit24", "#107C41"),
        new IntegrationCommand("doc-append", "Ajouter du texte (Docs)", "Ajoute un paragraphe à la fin du document ; {date}, {time} et {datetime} sont remplacés.", "le texte", true, null, "DocumentEdit24", "#185ABD"),
        new IntegrationCommand("calendar-add", "Événement (Agenda)", "Crée un événement en langage naturel dans l'agenda principal.", "« Dentiste demain à 15h »", true, null, "CalendarAdd24", "#1A73E8"),
    };

    /// <inheritdoc/>
    protected override void Configure(HttpClient http, string token) { /* le jeton d'accès est ajouté requête par requête */ }

    static string AuthUrl(IntegrationSettings s) => s.Get("apiurl").Length > 0 ? s.Get("apiurl").TrimEnd('/') + "/auth" : "https://accounts.google.com/o/oauth2/v2/auth";
    string TokenUrl() => _apiUrl.Length > 0 ? _apiUrl + "/token" : "https://oauth2.googleapis.com/token";
    string Sheets() => _apiUrl.Length > 0 ? _apiUrl + "/sheets" : "https://sheets.googleapis.com";
    string Docs() => _apiUrl.Length > 0 ? _apiUrl + "/docs" : "https://docs.googleapis.com";
    string Calendar() => _apiUrl.Length > 0 ? _apiUrl + "/calendar" : "https://www.googleapis.com/calendar/v3";

    /// <inheritdoc/>
    protected override async Task VerifyAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _apiUrl = settings.Get("apiurl").TrimEnd('/');
        _clientId = settings.Get("clientid");
        _clientSecret = settings.Secret("clientsecret");
        _spreadsheet = settings.Get("spreadsheet");
        _range = settings.Get("range", "A:A");
        _document = settings.Get("document");
        if (_clientId.Length == 0 || _clientSecret.Length == 0) throw new InvalidOperationException("Renseigne l'ID client et le secret de ton projet Google Cloud");
        _accessToken = "";
        _accessExpiry = DateTime.MinValue;
        await EnsureAccessTokenAsync(cancellation);   // vérifie que l'autorisation est encore valable
        StartCalendar();
    }

    // ---------- lecture de l'agenda (widget agenda) ----------

    /// <summary>Jours d'agenda lus à chaque rafraîchissement (le widget en affiche au plus 14).</summary>
    const int CalendarDays = 14;
    CancellationTokenSource? _calendar;

    /// <summary>Délai entre deux lectures de l'agenda ; réduit dans les tests.</summary>
    public TimeSpan CalendarRefresh { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>false : l'agenda n'est pas lu en arrière-plan (tests des autres commandes, qui comptent les requêtes).</summary>
    public bool ReadCalendar { get; set; } = true;

    void StartCalendar()
    {
        StopCalendar();
        if (!ReadCalendar) return;
        var cts = new CancellationTokenSource();
        _calendar = cts;
        _ = Task.Run(() => CalendarLoopAsync(cts.Token), CancellationToken.None);
    }

    void StopCalendar() => Interlocked.Exchange(ref _calendar, null)?.Cancel();

    async Task CalendarLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await RefreshCalendarAsync(token);
            try { await Task.Delay(CalendarRefresh, token); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Lit les événements de l'agenda principal pour les prochains jours et les publie (<see cref="CalendarFeed.EventsKey"/>). Un échec
    /// (autorisation expirée, réseau) est publié dans <see cref="CalendarFeed.ErrorKey"/> et n'interrompt jamais l'intégration : les actions
    /// Sheets, Docs et Agenda continuent de marcher. Ne lève pas d'exception.
    /// </summary>
    public async Task RefreshCalendarAsync(CancellationToken cancellation)
    {
        try
        {
            var from = Now().Date;
            var window = $"timeMin={Uri.EscapeDataString(new DateTimeOffset(from).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))}"
                + $"&timeMax={Uri.EscapeDataString(new DateTimeOffset(from.AddDays(CalendarDays)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))}";
            var reply = await SendAsync(HttpMethod.Get, $"{Calendar()}/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=50&{window}", null, cancellation);
            SetValue(CalendarFeed.EventsKey, CalendarFeed.Serialize(CalendarFeed.FromGoogle(reply)));
            RemoveValue(CalendarFeed.ErrorKey);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { /* arrêt demandé */ }
        catch (InvalidOperationException ex)
        {
            Log.Warn($"Google Agenda : lecture impossible ({ex.Message})");
            SetValue(CalendarFeed.ErrorKey, ex.Message);
        }
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        StopCalendar();
        RemoveValues("calendar.");
        return base.DisconnectCoreAsync();
    }

    /// <inheritdoc/>
    protected override async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        await EnsureAccessTokenAsync(cancellation);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _accessToken);
    }

    async Task EnsureAccessTokenAsync(CancellationToken cancellation)
    {
        if (_accessToken.Length > 0 && DateTime.UtcNow < _accessExpiry.AddSeconds(-60)) return;
        await _refresh.WaitAsync(cancellation);
        try
        {
            if (_accessToken.Length > 0 && DateTime.UtcNow < _accessExpiry.AddSeconds(-60)) return;
            var reply = await PostFormAsync(TokenUrl(), new Dictionary<string, string>
            {
                ["client_id"] = _clientId, ["client_secret"] = _clientSecret, ["refresh_token"] = Token, ["grant_type"] = "refresh_token",
            }, cancellation);
            _accessToken = reply["access_token"]?.ToString() ?? throw new InvalidOperationException("Google n'a pas renvoyé de jeton d'accès");
            _accessExpiry = DateTime.UtcNow.AddSeconds(reply["expires_in"]?.GetValue<int>() ?? 3600);
        }
        finally { _refresh.Release(); }
    }

    // Échange avec le point de terminaison OAuth : les erreurs ont la forme {"error":"invalid_grant","error_description":"…"}
    static async Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        HttpResponseMessage response;
        try { response = await http.PostAsync(url, new FormUrlEncodedContent(form), cancellation); }
        catch (HttpRequestException ex) { throw new InvalidOperationException($"Google est injoignable : {ex.Message}", ex); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellation);
            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { /* corps non JSON */ }
            if (response.IsSuccessStatusCode && json != null) return json;
            var error = json?["error"]?.ToString();
            throw new InvalidOperationException(error switch
            {
                "invalid_grant" => "L'autorisation Google a expiré ou été révoquée : clique de nouveau sur « Se connecter à Google » (un projet resté en mode Test l'invalide au bout de 7 jours)",
                "invalid_client" => "Google refuse l'ID client ou le secret : vérifie-les dans la console Google Cloud",
                _ => $"Google : {json?["error_description"] ?? error ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}",
            });
        }
    }

    /// <inheritdoc/>
    public async Task<string> PairAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var clientId = settings.Get("clientid");
        var secret = settings.Secret("clientsecret");
        if (clientId.Length == 0 || secret.Length == 0) throw new InvalidOperationException("Renseigne d'abord l'ID client et le secret de ton projet Google Cloud");
        _apiUrl = settings.Get("apiurl").TrimEnd('/');

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = StartLoopback(out int port);
        var redirect = $"http://127.0.0.1:{port}/";
        var url = new Uri(AuthUrl(settings) + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scopes,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state,
            ["access_type"] = "offline", ["prompt"] = "consent",
        }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(SignInTimeout);
        var waiting = WaitForCodeAsync(listener, state, timeout.Token);   // l'écoute démarre avant l'ouverture du navigateur
        string code;
        try { await OpenBrowser(url); code = await waiting; }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Aucune réponse de Google dans les 3 minutes : réessaie et accepte l'autorisation dans le navigateur");
        }

        var reply = await PostFormAsync(TokenUrl(), new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = clientId, ["client_secret"] = secret, ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
        }, cancellation);
        return reply["refresh_token"]?.ToString()
            ?? throw new InvalidOperationException("Google n'a pas renvoyé d'autorisation durable : révoque l'accès de StreamDak sur myaccount.google.com/permissions puis recommence");
    }

    static HttpListener StartLoopback(out int port)
    {
        for (int attempt = 0; ; attempt++)
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); return listener; }
            catch (HttpListenerException) when (attempt < 5) { listener.Close(); }
        }
    }

    static async Task<string> WaitForCodeAsync(HttpListener listener, string state, CancellationToken cancellation)
    {
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(cancellation);
            var query = context.Request.QueryString;
            string? code = query["code"], error = query["error"];
            bool valid = query["state"] == state && (code != null || error != null);
            var page = valid
                ? (error == null ? "StreamDak est connecté à Google. Tu peux fermer cet onglet." : "Connexion refusée. Tu peux fermer cet onglet.")
                : "Requête ignorée.";
            var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>StreamDak</title><p style=\"font:16px Segoe UI\">{WebUtility.HtmlEncode(page)}</p>");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellation);
            context.Response.Close();
            if (!valid) continue;   // un autre programme a frappé à la porte (mauvais « state ») : on continue d'attendre
            if (error != null) throw new InvalidOperationException(error == "access_denied" ? "Tu as refusé l'autorisation dans le navigateur" : $"Google : {error}");
            return code!;
        }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Remplace {date}, {time} et {datetime} par l'instant présent (jj/MM/aaaa, HH:mm, les deux).</summary>
    public static string Expand(string text, DateTime now) => text
        .Replace("{datetime}", now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{date}", now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    /// <summary>Horloge utilisée pour les jetons {date} ; remplacée dans les tests.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        switch (command.ToLowerInvariant())
        {
            case "sheet-append":
            {
                var cells = Expand(RequireArgument(argument, "les valeurs séparées par « ; »"), Now()).Split(';', StringSplitOptions.TrimEntries);
                var id = Need(_spreadsheet, "la feuille de calcul");
                var url = $"{Sheets()}/v4/spreadsheets/{Uri.EscapeDataString(id)}/values/{Uri.EscapeDataString(_range)}:append?valueInputOption=USER_ENTERED&insertDataOption=INSERT_ROWS";
                await SendAsync(HttpMethod.Post, url, new JsonObject { ["values"] = new JsonArray(new JsonArray(cells.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())) }, cancellation);
                return $"Ligne ajoutée ({cells.Length} cellule{(cells.Length > 1 ? "s" : "")})";
            }
            case "sheet-set":
            {
                var (cell, value) = SplitPair(argument, "cellule=valeur");
                var id = Need(_spreadsheet, "la feuille de calcul");
                var url = $"{Sheets()}/v4/spreadsheets/{Uri.EscapeDataString(id)}/values/{Uri.EscapeDataString(cell)}?valueInputOption=USER_ENTERED";
                await SendAsync(HttpMethod.Put, url, new JsonObject { ["values"] = new JsonArray(new JsonArray(JsonValue.Create(Expand(value, Now())))) }, cancellation);
                return $"{cell} = {value}";
            }
            case "doc-append":
            {
                var text = Expand(RequireArgument(argument, "le texte"), Now());
                var id = Need(_document, "le document");
                var body = new JsonObject
                {
                    ["requests"] = new JsonArray(new JsonObject
                    {
                        ["insertText"] = new JsonObject { ["text"] = text + "\n", ["endOfSegmentLocation"] = new JsonObject() },
                    }),
                };
                await SendAsync(HttpMethod.Post, $"{Docs()}/v1/documents/{Uri.EscapeDataString(id)}:batchUpdate", body, cancellation);
                return "Texte ajouté au document";
            }
            case "calendar-add":
            {
                var text = RequireArgument(argument, "l'événement");
                var created = await SendAsync(HttpMethod.Post, $"{Calendar()}/calendars/primary/events/quickAdd?text={Uri.EscapeDataString(text)}", null, cancellation);
                return $"Événement créé : {created["summary"] ?? text}";
            }
            default: throw new ArgumentException($"Commande Google inconnue : « {command} »");
        }
    }

    static string Need(string value, string what) =>
        value.Length > 0 ? value : throw new InvalidOperationException($"Renseigne {what} dans les réglages Google Workspace");
}
