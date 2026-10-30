using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Google;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Google Workspace contre un faux Google : connexion OAuth (navigateur simulé, PKCE), renouvellement du jeton, Sheets, Docs, Agenda.</summary>
public class GoogleTests
{
    const string ClientId = "123.apps.googleusercontent.com", ClientSecret = "secret-client", Refresh = "refresh-1", Access = "access-1";

    sealed class FakeGoogle
    {
        public int TokenCalls;
        public string? LastVerifier, LastRedirect;
        public string RefreshReply = "ok";   // ok | invalid_grant | invalid_client
        public bool OmitRefreshToken;
        public string? EventsReply;   // null : l'agenda répond 404
        public int EventsCalls;
        public string? LastEventsQuery;

        public (int, string) Handle(SeenRequest r)
        {
            var path = r.PathAndQuery.Split('?')[0];
            if (path == "/g/token")
            {
                TokenCalls++;
                var form = System.Web.HttpUtility.ParseQueryString(r.Body);
                if (form["grant_type"] == "authorization_code")
                {
                    LastVerifier = form["code_verifier"];
                    LastRedirect = form["redirect_uri"];
                    if (form["code"] != "code-42" || form["client_secret"] != ClientSecret) return (400, """{"error":"invalid_grant"}""");
                    return (200, OmitRefreshToken ? """{"access_token":"a","expires_in":3600}""" : $$$"""{"access_token":"{{{Access}}}","expires_in":3600,"refresh_token":"{{{Refresh}}}"}""");
                }
                return RefreshReply switch
                {
                    "invalid_grant" => (400, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}"""),
                    "invalid_client" => (401, """{"error":"invalid_client"}"""),
                    _ => form["refresh_token"] == Refresh ? (200, $$$"""{"access_token":"{{{Access}}}","expires_in":3600}""") : (400, """{"error":"invalid_grant"}"""),
                };
            }
            if (r.Headers["Authorization"] != "Bearer " + Access) return (401, """{"error":{"message":"bad token"}}""");
            if (path.StartsWith("/g/sheets/v4/spreadsheets/SHEET/values/", StringComparison.Ordinal)) return (200, "{}");
            if (path == "/g/docs/v1/documents/DOC:batchUpdate") return (200, "{}");
            if (path == "/g/calendar/calendars/primary/events/quickAdd") return (200, """{"summary":"Dentiste"}""");
            if (path == "/g/calendar/calendars/primary/events")
            {
                EventsCalls++;
                LastEventsQuery = r.PathAndQuery;
                return EventsReply == null ? (404, "{}") : (200, EventsReply);
            }
            return (404, "{}");
        }
    }

    static IntegrationSettings For(FakeHttpServer server, string refresh = Refresh, params (string, string)[] more)
    {
        var values = new Dictionary<string, string>
        {
            ["apiurl"] = $"http://localhost:{server.Port}/g", ["clientid"] = ClientId, ["spreadsheet"] = "SHEET", ["document"] = "DOC", ["range"] = "Feuille 1!A:C",
        };
        foreach (var (k, v) in more) values[k] = v;
        var secrets = new Dictionary<string, string> { ["clientsecret"] = ClientSecret };
        if (refresh.Length > 0) secrets["token"] = refresh;
        return new IntegrationSettings { Values = values, Secrets = secrets };
    }

    static GoogleIntegration NewGoogle() => new() { Now = () => new DateTime(2026, 10, 21, 9, 5, 0), ReadCalendar = false };

    [Fact]
    public async Task SignInRunsTheWholeAuthorizationCodeFlowWithPkceAndReturnsTheRefreshToken()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle();
        server.Handler = google.Handle;
        using var integration = NewGoogle();
        Uri? opened = null;
        using var http = new HttpClient();
        integration.OpenBrowser = async url =>
        {
            opened = url;
            // Le « navigateur » : Google redirige vers 127.0.0.1 avec le code et l'état reçu
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var callback = $"{query["redirect_uri"]}?code=code-42&state={Uri.EscapeDataString(query["state"]!)}";
            await http.GetStringAsync(callback);
        };

        var token = await integration.PairAsync(For(server, ""), CancellationToken.None);

        Assert.Equal(Refresh, token);
        var q = System.Web.HttpUtility.ParseQueryString(opened!.Query);
        Assert.Equal(ClientId, q["client_id"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal(GoogleIntegration.Scopes, q["scope"]);
        Assert.StartsWith("http://127.0.0.1:", q["redirect_uri"]);
        Assert.DoesNotContain("gmail", q["scope"]!, StringComparison.Ordinal);   // droits minimaux
        // PKCE : le défi envoyé est bien l'empreinte du vérificateur donné ensuite
        var expected = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(google.LastVerifier!))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, q["code_challenge"]);
        Assert.Equal(q["redirect_uri"], google.LastRedirect);
    }

    [Fact]
    public async Task SignInIgnoresACallbackWithAWrongStateAndReportsARefusal()
    {
        using var server = new FakeHttpServer { Handler = new FakeGoogle().Handle };
        using var integration = NewGoogle();
        using var http = new HttpClient();
        integration.OpenBrowser = async url =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            await http.GetStringAsync($"{query["redirect_uri"]}?code=pirate&state=faux");               // ignoré
            await http.GetStringAsync($"{query["redirect_uri"]}?error=access_denied&state={Uri.EscapeDataString(query["state"]!)}");
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => integration.PairAsync(For(server, ""), CancellationToken.None));
        Assert.Contains("refusé l'autorisation", ex.Message);
    }

    [Fact]
    public async Task SignInNeedsCredentialsAndAnOfflineGrant()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle { OmitRefreshToken = true };
        server.Handler = google.Handle;
        using var integration = NewGoogle();
        var empty = new IntegrationSettings();
        Assert.Contains("ID client", (await Assert.ThrowsAsync<InvalidOperationException>(() => integration.PairAsync(empty, default))).Message);

        using var http = new HttpClient();
        integration.OpenBrowser = async url =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            await http.GetStringAsync($"{query["redirect_uri"]}?code=code-42&state={Uri.EscapeDataString(query["state"]!)}");
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => integration.PairAsync(For(server, ""), CancellationToken.None));
        Assert.Contains("autorisation durable", ex.Message);
    }

    [Fact]
    public async Task ConnectingRenewsTheAccessTokenOnceAndCommandsUseIt()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle();
        server.Handler = google.Handle;
        using var integration = NewGoogle();
        await integration.ConnectAsync(For(server), CancellationToken.None);

        Assert.Equal("Ligne ajoutée (3 cellules)", await integration.ExecuteAsync("sheet-append", "Café; {datetime} ;12", default));
        var append = server.Requests.Last();
        Assert.Equal("POST", append.Method);
        Assert.Contains("/values/Feuille 1!A:C:append", Uri.UnescapeDataString(append.PathAndQuery), StringComparison.Ordinal);
        Assert.Contains("valueInputOption=USER_ENTERED", append.PathAndQuery, StringComparison.Ordinal);
        var row = JsonNode.Parse(append.Body)!["values"]![0]!.AsArray().Select(n => n!.ToString()).ToArray();
        Assert.Equal(new[] { "Café", "21/10/2026 09:05", "12" }, row);

        await integration.ExecuteAsync("sheet-set", "B2=Terminé le {date}", default);
        var put = server.Requests.Last();
        Assert.Equal("PUT", put.Method);
        Assert.Contains("/values/B2?", put.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal("Terminé le 21/10/2026", JsonNode.Parse(put.Body)!["values"]![0]![0]!.ToString());

        await integration.ExecuteAsync("doc-append", "Réunion à {time}", default);
        var insert = JsonNode.Parse(server.Requests.Last().Body)!["requests"]![0]!["insertText"]!;
        Assert.Equal("Réunion à 09:05\n", insert["text"]!.ToString());
        Assert.NotNull(insert["endOfSegmentLocation"]);

        Assert.Equal("Événement créé : Dentiste", await integration.ExecuteAsync("calendar-add", "Dentiste demain à 15h", default));
        Assert.Contains("text=Dentiste%20demain%20%C3%A0%2015h", server.Requests.Last().PathAndQuery, StringComparison.Ordinal);

        Assert.Equal(1, google.TokenCalls);   // un seul renouvellement pour toute la série
    }

    [Fact]
    public async Task ARevokedOrExpiredGrantTellsToSignInAgain()
    {
        using var server = new FakeHttpServer();
        server.Handler = new FakeGoogle { RefreshReply = "invalid_grant" }.Handle;
        using var integration = NewGoogle();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => integration.ConnectAsync(For(server), CancellationToken.None));
        Assert.Contains("Se connecter à Google", ex.Message);
        Assert.Contains("7 jours", ex.Message);
        Assert.Equal(ConnectionState.Error, integration.State);

        server.Handler = new FakeGoogle { RefreshReply = "invalid_client" }.Handle;
        using var other = NewGoogle();
        var bad = await Assert.ThrowsAsync<InvalidOperationException>(() => other.ConnectAsync(For(server), CancellationToken.None));
        Assert.Contains("ID client", bad.Message);
        Assert.DoesNotContain(ClientSecret, bad.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Refresh, bad.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSettingsAndBadArgumentsAreExplainedBeforeAnyRequest()
    {
        using var server = new FakeHttpServer { Handler = new FakeGoogle().Handle };
        using var integration = NewGoogle();
        await integration.ConnectAsync(For(server, Refresh, ("spreadsheet", " "), ("document", " ")), CancellationToken.None);
        int before = server.Requests.Count;

        Assert.Contains("feuille de calcul", (await Assert.ThrowsAsync<InvalidOperationException>(() => integration.ExecuteAsync("sheet-append", "a", default))).Message);
        Assert.Contains("document", (await Assert.ThrowsAsync<InvalidOperationException>(() => integration.ExecuteAsync("doc-append", "a", default))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => integration.ExecuteAsync("sheet-set", "sans-egal", default));
        await Assert.ThrowsAsync<ArgumentException>(() => integration.ExecuteAsync("calendar-add", " ", default));
        await Assert.ThrowsAsync<ArgumentException>(() => integration.ExecuteAsync("drive", "x", default));
        Assert.Equal(before, server.Requests.Count);

        using var noClient = NewGoogle();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => noClient.ConnectAsync(new IntegrationSettings { Secrets = new Dictionary<string, string> { ["token"] = "t" } }, default));
        Assert.Contains("ID client", ex.Message);
    }

    [Fact]
    public async Task ApiErrorsFromTheServiceAreReported()
    {
        using var server = new FakeHttpServer { Handler = new FakeGoogle().Handle };
        using var integration = NewGoogle();
        await integration.ConnectAsync(For(server, Refresh, ("spreadsheet", "AUTRE")), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => integration.ExecuteAsync("sheet-append", "a", default));
        Assert.Contains("Google Workspace a répondu 404", ex.Message);
    }

    [Fact]
    public void ThePlaceholdersAreExpandedAndTheScopesStayMinimal()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 0);
        Assert.Equal("02/01/2026 03:04 | 02/01/2026 | 03:04", GoogleIntegration.Expand("{DATETIME} | {date} | {time}", now));
        Assert.Equal(3, GoogleIntegration.Scopes.Split(' ').Length);
        Assert.All(new GoogleIntegration().Fields.Where(f => f.Key is "clientsecret" or "token"), f => Assert.Equal(SettingKind.Secret, f.Kind));
    }

    // ---------- lecture de l'agenda (widget agenda) ----------

    /// <summary>Décalage de la machine ce jour-là : les heures du test sont celles de la machine, quel que soit son fuseau (le runner de la CI est en UTC).</summary>
    static string LocalOffset()
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 21, 12, 0, 0));
        return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString("hh':'mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    static readonly string Events = EventsTemplate.Replace("+02:00", LocalOffset(), StringComparison.Ordinal);

    const string EventsTemplate = """
        {"items":[
          {"summary":"Réunion d'équipe","status":"confirmed","start":{"dateTime":"2026-10-21T10:00:00+02:00"},"end":{"dateTime":"2026-10-21T11:00:00+02:00"}},
          {"summary":"Férié","start":{"date":"2026-10-22"},"end":{"date":"2026-10-23"}},
          {"summary":"Annulé","status":"cancelled","start":{"dateTime":"2026-10-21T12:00:00+02:00"},"end":{"dateTime":"2026-10-21T13:00:00+02:00"}},
          {"start":{"dateTime":"2026-10-21T15:30:00+02:00"},"end":{"dateTime":"2026-10-21T16:00:00+02:00"}}
        ]}
        """;

    static async Task<string?> WaitForValueAsync(GoogleIntegration integration, string key)
    {
        for (int i = 0; i < 100; i++)
        {
            if (integration.Values.TryGetValue(key, out var v)) return v;
            await Task.Delay(30);
        }
        return null;
    }

    [Fact]
    public async Task ConnectingStartsReadingTheCalendarAndPublishesTheEvents()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle { EventsReply = Events };
        server.Handler = google.Handle;
        using var integration = new GoogleIntegration { Now = () => new DateTime(2026, 10, 21, 9, 5, 0), CalendarRefresh = TimeSpan.FromMinutes(30) };
        await integration.ConnectAsync(For(server), CancellationToken.None);

        var json = await WaitForValueAsync(integration, CalendarFeed.EventsKey);
        Assert.NotNull(json);
        var events = CalendarFeed.Parse(json);
        Assert.Equal(3, events.Count);                                          // l'événement annulé est ignoré
        Assert.Contains(events, e => e.Title == "Réunion d'équipe" && !e.AllDay);
        Assert.Contains(events, e => e.Title == "Férié" && e.AllDay);
        Assert.Contains(events, e => e.Title == "(sans titre)");
        Assert.DoesNotContain(CalendarFeed.ErrorKey, integration.Values.Keys);
        var query = System.Web.HttpUtility.ParseQueryString(google.LastEventsQuery!.Split('?')[1]);
        Assert.Equal("true", query["singleEvents"]);
        Assert.StartsWith("2026-10-21T00:00:00", query["timeMin"]);
        Assert.StartsWith("2026-11-04T00:00:00", query["timeMax"]);               // 14 jours
        await integration.DisconnectAsync();
        Assert.DoesNotContain(CalendarFeed.EventsKey, integration.Values.Keys);   // déconnecté : plus d'événements affichés
    }

    [Fact]
    public async Task AFailingCalendarReportsItsErrorButTheOtherCommandsKeepWorking()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle();                                          // l'agenda répond 404
        server.Handler = google.Handle;
        using var integration = new GoogleIntegration { Now = () => new DateTime(2026, 10, 21, 9, 5, 0), CalendarRefresh = TimeSpan.FromMinutes(30) };
        await integration.ConnectAsync(For(server), CancellationToken.None);

        var error = await WaitForValueAsync(integration, CalendarFeed.ErrorKey);
        Assert.Contains("Google Workspace a répondu 404", error);
        Assert.Equal(ConnectionState.Connected, integration.State);
        Assert.Equal("Ligne ajoutée (1 cellule)", await integration.ExecuteAsync("sheet-append", "a", default));
    }

    [Fact]
    public async Task TheCalendarIsReadAgainAfterTheRefreshDelayAndStopsWhenDisconnected()
    {
        using var server = new FakeHttpServer();
        var google = new FakeGoogle { EventsReply = Events };
        server.Handler = google.Handle;
        using var integration = new GoogleIntegration { Now = () => new DateTime(2026, 10, 21, 9, 5, 0), CalendarRefresh = TimeSpan.FromMilliseconds(80) };
        await integration.ConnectAsync(For(server), CancellationToken.None);
        for (int i = 0; i < 100 && google.EventsCalls < 3; i++) await Task.Delay(30);
        Assert.True(google.EventsCalls >= 3, $"{google.EventsCalls} lectures");
        await integration.DisconnectAsync();
        int after = google.EventsCalls;
        await Task.Delay(250);
        Assert.True(google.EventsCalls <= after + 1, "la lecture continue après la déconnexion");
    }

    [Fact]
    public void GoogleEventsAreParsedAndFormattedForTheAgendaWidget()
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var events = CalendarFeed.FromGoogle(JsonNode.Parse(Events));
        var now = new DateTime(2026, 10, 21, 10, 30, 0);
        var lines = AgendaFormatter.Upcoming(events, now, 7, 10, fr);

        Assert.Equal(new[] { "Réunion d'équipe", "(sans titre)", "Férié" }, lines.Select(l => l.Title));
        Assert.True(lines[0].IsNow);                                              // 10:00-11:00 : en cours à 10:30
        Assert.Equal("Aujourd'hui", lines[0].Day);
        Assert.Equal("", lines[1].Day);                                           // même jour : pas répété
        Assert.Equal("15:30", lines[1].When);
        Assert.Equal("Demain", lines[2].Day);
        Assert.Equal("Journée", lines[2].When);
        Assert.Equal(2, AgendaFormatter.Upcoming(events, now, 7, 2, fr).Count);   // limité au nombre demandé
        var evening = new DateTime(2026, 10, 21, 17, 0, 0);
        Assert.Empty(AgendaFormatter.Upcoming(events, evening, 1, 10, fr));          // un jour : les événements d'aujourd'hui sont finis, « Férié » est demain
        Assert.Equal(new[] { "Férié" }, AgendaFormatter.Upcoming(events, evening, 2, 10, fr).Select(l => l.Title));
    }

    [Fact]
    public void AnUnreadableEventsValueGivesAnEmptyList()
    {
        Assert.Empty(CalendarFeed.Parse(null));
        Assert.Empty(CalendarFeed.Parse("pas du json"));
        Assert.Empty(CalendarFeed.Parse("{\"a\":1}"));
    }
}
