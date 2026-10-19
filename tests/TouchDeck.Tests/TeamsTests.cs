using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Integrations;
using TouchDeck.Integrations.Teams;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Faux serveur de l'API locale de Teams : accord d'association, jeton, état de réunion, actions.</summary>
sealed class FakeTeamsServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly CancellationTokenSource _stop = new();
    readonly Task _accept;
    WebSocket? _client;

    public const string ValidToken = "jeton-teams";
    public int Port { get; }
    public bool UserAccepts = true;
    public string? Refuse;   // réponse d'échec aux actions
    public ConcurrentQueue<string> Actions { get; } = new();
    public ConcurrentQueue<string> Queries { get; } = new();
    public bool InMeeting = true, Muted;

    public FakeTeamsServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _accept = Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            _ = Task.Run(() => Serve(context));
        }
    }

    string StateJson() => "{\"meetingUpdate\":{\"meetingState\":{\"isMuted\":" + Json(Muted) + ",\"isCameraOn\":false,\"isHandRaised\":false,\"isInMeeting\":" + Json(InMeeting) + ",\"isRecordingOn\":false,\"isBackgroundBlurred\":false}}}";

    static string Json(bool value) => value ? "true" : "false";

    public Task PushState() => _client is { State: WebSocketState.Open } c ? SendAsync(c, StateJson()) : Task.CompletedTask;

    static Task SendAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    async Task Serve(HttpListenerContext context)
    {
        var token = context.Request.QueryString["token"] ?? "";
        if (token.Length > 0 && token != ValidToken) { context.Response.StatusCode = 401; context.Response.Close(); return; }
        if (token.Length == 0 && !UserAccepts) { context.Response.StatusCode = 403; context.Response.Close(); return; }
        var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
        _client = ws;
        if (token.Length == 0)
        {
            await SendAsync(ws, $$"""{"tokenRefresh":"{{ValidToken}}"}""");
            return;
        }
        var buffer = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!;
                var action = message["action"]!.GetValue<string>();
                int id = message["requestId"]!.GetValue<int>();
                if (action == "query-meeting-state") { Queries.Enqueue(action); await SendAsync(ws, StateJson()); continue; }
                Actions.Enqueue(action + (message["parameters"]?["type"] is JsonNode t ? ":" + t : ""));
                if (action == "toggle-mute" && Refuse == null) Muted = !Muted;
                await SendAsync(ws, $$"""{"requestId":{{id}},"response":"{{Refuse ?? "Success"}}"}""");
                if (action == "toggle-mute") await SendAsync(ws, StateJson());
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { /* client parti */ }
    }

    public async Task DropClient()
    {
        if (_client is { State: WebSocketState.Open } c) await c.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        try { _accept.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { /* arrêt */ }
    }
}

public class TeamsTests
{
    static IntegrationSettings For(FakeTeamsServer server, string token = FakeTeamsServer.ValidToken) => new()
    {
        Values = new Dictionary<string, string> { ["port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Secrets = token.Length == 0 ? new Dictionary<string, string>() : new Dictionary<string, string> { ["token"] = token },
    };

    static async Task Until(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte"); await Task.Delay(10); }
    }

    [Fact]
    public async Task PairingReturnsTheTokenOnceTheUserAccepts()
    {
        using var server = new FakeTeamsServer();
        using var teams = new TeamsIntegration();
        Assert.Equal(FakeTeamsServer.ValidToken, await teams.PairAsync(For(server, ""), CancellationToken.None));
    }

    [Fact]
    public async Task PairingExplainsAnExplicitRefusal()
    {
        using var server = new FakeTeamsServer { UserAccepts = false };
        using var teams = new TeamsIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => teams.PairAsync(For(server, ""), CancellationToken.None));
        Assert.Contains("Teams", ex.Message);
    }

    [Fact]
    public async Task ConnectingWithoutATokenTellsToPairFirst()
    {
        using var server = new FakeTeamsServer();
        using var teams = new TeamsIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => teams.ConnectAsync(For(server, ""), CancellationToken.None));
        Assert.Contains("Associer à Teams", ex.Message);
    }

    [Fact]
    public async Task APairedConnectionPublishesTheMeetingState()
    {
        using var server = new FakeTeamsServer { Muted = true };
        using var teams = new TeamsIntegration();
        await teams.ConnectAsync(For(server), CancellationToken.None);
        await Until(() => teams.Values.ContainsKey("muted"));

        Assert.Equal("true", teams.Values["muted"]);
        Assert.Equal("true", teams.Values["inMeeting"]);
        Assert.Equal("false", teams.Values["camera"]);
    }

    [Fact]
    public async Task CommandsMapToTeamsActionsAndTheStateFollows()
    {
        using var server = new FakeTeamsServer();
        using var teams = new TeamsIntegration();
        await teams.ConnectAsync(For(server), CancellationToken.None);
        await Until(() => teams.Values.ContainsKey("muted"));

        await teams.ExecuteAsync("mute", null, default);
        await Until(() => teams.Values["muted"] == "true");
        await teams.ExecuteAsync("react", "Like", default);
        await teams.ExecuteAsync("leave", null, default);

        Assert.Equal(new[] { "toggle-mute", "react:like", "leave-call" }, server.Actions.ToArray());
    }

    [Fact]
    public async Task CommandsExplainWhyTheyCannotRun()
    {
        using var server = new FakeTeamsServer { InMeeting = false };
        using var teams = new TeamsIntegration();
        await teams.ConnectAsync(For(server), CancellationToken.None);
        await Until(() => teams.Values.ContainsKey("inMeeting"));

        var noMeeting = await Assert.ThrowsAsync<InvalidOperationException>(() => teams.ExecuteAsync("mute", null, default));
        Assert.Contains("Aucune réunion", noMeeting.Message);
        Assert.Empty(server.Actions);

        server.InMeeting = true;
        await server.PushState();
        await Until(() => teams.Values["inMeeting"] == "true");
        server.Refuse = "Not allowed";
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => teams.ExecuteAsync("hand", null, default));
        Assert.Contains("Not allowed", refused.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => teams.ExecuteAsync("react", "confetti", default));
        await Assert.ThrowsAsync<ArgumentException>(() => teams.ExecuteAsync("dance", null, default));
    }

    [Fact]
    public async Task LosingTheConnectionIsReportedForReconnection()
    {
        using var server = new FakeTeamsServer();
        using var teams = new TeamsIntegration();
        await teams.ConnectAsync(For(server), CancellationToken.None);
        await Until(() => teams.Values.ContainsKey("muted"));

        await server.DropClient();
        await Until(() => teams.State == ConnectionState.Error);

        Assert.Empty(teams.Values);
    }

    [Fact]
    public async Task ATeamsThatIsNotRunningGivesActionableAdvice()
    {
        using var server = new FakeTeamsServer();
        var settings = For(server);
        server.Dispose();
        using var teams = new TeamsIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => teams.ConnectAsync(settings, CancellationToken.None));
        Assert.Contains("Gérer l'API", ex.Message);
        Assert.Equal(ConnectionState.Error, teams.State);
    }
}
