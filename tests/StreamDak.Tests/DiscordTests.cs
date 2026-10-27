using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using StreamDak.Integrations;
using StreamDak.Integrations.Discord;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Faux client de bureau Discord : un canal nommé qui parle le protocole RPC local (poignée de main, AUTHORIZE, AUTHENTICATE, réglages vocaux, événements).
/// Il garde l'état vocal comme Discord et peut le changer « de l'extérieur » (l'utilisateur clique dans Discord).
/// </summary>
sealed class FakeDiscord : IAsyncDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;
    readonly SemaphoreSlim _write = new(1, 1);
    NamedPipeServerStream? _current;

    public string Prefix { get; } = $"sdtest-discord-{Guid.NewGuid():N}-";
    public string ClientId { get; set; } = "123";
    public HashSet<string> ValidTokens { get; } = new();
    public ConcurrentQueue<JsonObject> Commands { get; } = new();
    public bool Mute, Deaf;
    public int Input = 80, Output = 100;
    public string? Channel = "Général";

    public FakeDiscord() { _loop = Task.Run(LoopAsync); }

    async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(Prefix + "0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token);
                _current = server;
                await ServeAsync(server);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            finally { _current = null; await server.DisposeAsync(); }
        }
    }

    async Task ServeAsync(Stream stream)
    {
        var header = new byte[8];
        while (!_stop.IsCancellationRequested)
        {
            await stream.ReadExactlyAsync(header, _stop.Token);
            int op = BinaryPrimitives.ReadInt32LittleEndian(header);
            var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
            await stream.ReadExactlyAsync(body, _stop.Token);
            var message = (JsonObject)JsonNode.Parse(body)!;
            if (op == 0)
            {
                if (message["client_id"]?.ToString() != ClientId) { await SendAsync(stream, 2, new JsonObject { ["code"] = 4000, ["message"] = "Invalid Client ID" }); return; }
                await SendAsync(stream, 1, new JsonObject { ["cmd"] = "DISPATCH", ["evt"] = "READY", ["data"] = new JsonObject { ["v"] = 1 } });
                continue;
            }
            Commands.Enqueue(message);
            var cmd = message["cmd"]!.ToString();
            var nonce = message["nonce"]?.ToString();
            var args = message["args"] as JsonObject ?? new JsonObject();
            JsonObject? data = null;
            string? error = null;
            switch (cmd)
            {
                case "AUTHORIZE": data = new JsonObject { ["code"] = "thecode" }; break;
                case "AUTHENTICATE":
                    if (ValidTokens.Contains(args["access_token"]?.ToString() ?? "")) data = new JsonObject { ["user"] = new JsonObject { ["username"] = "moi" } };
                    else error = "Invalid access token";
                    break;
                case "GET_VOICE_SETTINGS": data = Voice(); break;
                case "SET_VOICE_SETTINGS":
                    if (args["mute"] is JsonValue m) Mute = m.GetValue<bool>();
                    if (args["deaf"] is JsonValue d) Deaf = d.GetValue<bool>();
                    if (args["input"]?["volume"] is JsonValue i) Input = i.GetValue<int>();
                    if (args["output"]?["volume"] is JsonValue o) Output = o.GetValue<int>();
                    data = Voice();
                    break;
                case "SUBSCRIBE": data = new JsonObject { ["evt"] = message["evt"]?.ToString() }; break;
                case "GET_SELECTED_VOICE_CHANNEL": data = Channel == null ? null : new JsonObject { ["name"] = Channel }; break;
                case "SELECT_VOICE_CHANNEL": Channel = null; data = null; break;
            }
            var reply = new JsonObject { ["cmd"] = cmd, ["nonce"] = nonce, ["data"] = error != null ? new JsonObject { ["code"] = 4009, ["message"] = error } : data };
            if (error != null) reply["evt"] = "ERROR";
            await SendAsync(stream, 1, reply);
            if (cmd == "SELECT_VOICE_CHANNEL") await SendAsync(stream, 1, new JsonObject { ["cmd"] = "DISPATCH", ["evt"] = "VOICE_CHANNEL_SELECT", ["data"] = new JsonObject { ["channel_id"] = null } });
        }
    }

    JsonObject Voice() => new()
    {
        ["mute"] = Mute, ["deaf"] = Deaf,
        ["input"] = new JsonObject { ["volume"] = Input }, ["output"] = new JsonObject { ["volume"] = Output },
    };

    async Task SendAsync(Stream stream, int op, JsonObject payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await _write.WaitAsync();
        try { await stream.WriteAsync(frame); await stream.FlushAsync(); }
        finally { _write.Release(); }
    }

    /// <summary>L'utilisateur coupe son micro dans Discord lui-même : Discord prévient par un événement.</summary>
    public Task ChangeInDiscordAsync(bool mute)
    {
        Mute = mute;
        return SendAsync(_current!, 1, new JsonObject { ["cmd"] = "DISPATCH", ["evt"] = "VOICE_SETTINGS_UPDATE", ["data"] = Voice() });
    }

    /// <summary>Discord quitte : le canal se ferme.</summary>
    public async Task QuitAsync()
    {
        await _stop.CancelAsync();
        if (_current != null) await _current.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await QuitAsync();
        try { await _loop; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
    }
}

public class DiscordTests : IAsyncLifetime
{
    readonly FakeDiscord _discord = new();
    readonly FakeHttpServer _oauth = new();
    readonly List<string> _grants = new();
    DiscordIntegration _integration = null!;

    public Task InitializeAsync()
    {
        // Échange OAuth2 : le code donne R1 (accès A1) ; R1 donne R2 (accès A2), puis R1 ne vaut plus rien (Discord renouvelle le jeton à chaque usage)
        var used = new HashSet<string>();
        _oauth.Handler = request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            lock (_grants) _grants.Add(form["grant_type"] + ":" + (form["code"] ?? form["refresh_token"]));
            if (form["client_secret"] != "s3cret") return (401, "{\"error\":\"invalid_client\"}");
            if (form["grant_type"] == "authorization_code" && form["code"] == "thecode") { _discord.ValidTokens.Add("A1"); return (200, "{\"access_token\":\"A1\",\"refresh_token\":\"R1\",\"expires_in\":604800}"); }
            if (form["grant_type"] == "refresh_token" && form["refresh_token"] == "R1" && used.Add("R1")) { _discord.ValidTokens.Add("A2"); return (200, "{\"access_token\":\"A2\",\"refresh_token\":\"R2\",\"expires_in\":604800}"); }
            return (400, "{\"error\":\"invalid_grant\",\"error_description\":\"Invalid refresh token\"}");
        };
        _integration = Create();
        return Task.CompletedTask;
    }

    DiscordIntegration Create() => new() { PipePrefix = _discord.Prefix, TokenEndpoint = new Uri($"http://localhost:{_oauth.Port}/api/oauth2/token") };

    public async Task DisposeAsync()
    {
        await _integration.DisconnectAsync();
        _integration.Dispose();
        await _discord.DisposeAsync();
        _oauth.Dispose();
    }

    static IntegrationSettings Settings(string token = "", string clientId = "123") => new()
    {
        Values = new Dictionary<string, string> { ["clientid"] = clientId },
        Secrets = new Dictionary<string, string> { ["clientsecret"] = "s3cret", [DiscordIntegration.TokenField] = token },
    };

    static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task PairingAsksDiscordForTheVoiceScopesAndReturnsTheRefreshToken()
    {
        var token = await _integration.PairAsync(Settings(), CancellationToken.None);
        Assert.Equal("R1", token);
        var authorize = _discord.Commands.Single(c => c["cmd"]!.ToString() == "AUTHORIZE");
        Assert.Equal(new[] { "rpc", "rpc.voice.read", "rpc.voice.write" }, authorize["args"]!["scopes"]!.AsArray().Select(s => s!.ToString()));
        Assert.Equal(new[] { "authorization_code:thecode" }, _grants);
    }

    [Fact]
    public async Task AfterPairingTheConnectionReadsTheVoiceStateWithoutAnotherTokenExchange()
    {
        var token = await _integration.PairAsync(Settings(), CancellationToken.None);
        await _integration.ConnectAsync(Settings(token), CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, _integration.State);
        Assert.Equal("false", _integration.Values["mute"]);
        Assert.Equal("80", _integration.Values["input.volume"]);
        Assert.Equal("true", _integration.Values["voice"]);
        Assert.Equal("Général", _integration.Values["channel"]);
        Assert.Single(_grants);   // le jeton d'accès de l'association sert directement
        Assert.Contains(_discord.Commands, c => c["cmd"]!.ToString() == "SUBSCRIBE" && c["evt"]!.ToString() == "VOICE_SETTINGS_UPDATE");
    }

    [Fact]
    public async Task MuteDeafenAndVolumesGoThroughTheLocalChannelWithoutAnyKeyboard()
    {
        await _integration.ConnectAsync(Settings(await _integration.PairAsync(Settings(), CancellationToken.None)), CancellationToken.None);

        Assert.Equal("Micro coupé", await _integration.ExecuteAsync("togglemute", null, CancellationToken.None));
        Assert.True(_discord.Mute);
        Assert.Equal("true", _integration.Values["mute"]);
        Assert.Equal("Micro rétabli", await _integration.ExecuteAsync("togglemute", null, CancellationToken.None));
        Assert.False(_discord.Mute);

        await _integration.ExecuteAsync("deafen", null, CancellationToken.None);
        Assert.True(_discord.Deaf);
        await _integration.ExecuteAsync("inputvolume", "+5", CancellationToken.None);
        Assert.Equal(85, _discord.Input);
        await _integration.ExecuteAsync("outputvolume", "250", CancellationToken.None);
        Assert.Equal(200, _discord.Output);   // borné à ce que Discord accepte
        Assert.Equal("Salon vocal quitté", await _integration.ExecuteAsync("leave", null, CancellationToken.None));
        Assert.Null(_discord.Channel);
        await Eventually(() => _integration.Values.GetValueOrDefault("voice") == "false");
    }

    [Fact]
    public async Task AChangeMadeInDiscordItselfUpdatesTheButtonsLight()
    {
        await _integration.ConnectAsync(Settings(await _integration.PairAsync(Settings(), CancellationToken.None)), CancellationToken.None);
        var changed = new ConcurrentQueue<string>();
        _integration.ValuesChanged += keys => { foreach (var k in keys) changed.Enqueue(k); };
        await _discord.ChangeInDiscordAsync(mute: true);
        await Eventually(() => changed.Contains("mute"));   // l'événement suit l'écriture de la valeur
        Assert.Equal("true", _integration.Values["mute"]);
    }

    [Fact]
    public async Task TheRotatedRefreshTokenIsSavedEncryptedAndTheStaleOneIsNeverReused()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        var secrets = new MemorySecrets();
        secrets.Set(IntegrationManager.SecretKey("discord", "clientsecret"), "s3cret");
        secrets.Set(IntegrationManager.SecretKey("discord", DiscordIntegration.TokenField), "R1");
        doc.UpdateSettings(c => (c.Integrations ??= new())["discord"] = new IntegrationConfig { Enabled = true, Values = new() { ["clientid"] = "123" } });
        var manager = new IntegrationManager(new IIntegration[] { _integration }, doc, secrets);
        manager.Start();
        await Eventually(() => _integration.State == ConnectionState.Connected);
        Assert.Equal("R2", secrets.Get(IntegrationManager.SecretKey("discord", DiscordIntegration.TokenField)));
        Assert.Equal(new[] { "refresh_token:R1" }, _grants);
        manager.Dispose();

        // Le réglage lu au démarrage (R1) sert encore à une reconnexion : le jeton en mémoire doit l'emporter, R1 n'est plus jamais envoyé
        await _integration.ConnectAsync(Settings("R1"), CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, _integration.State);
        Assert.DoesNotContain("refresh_token:R1", _grants.Skip(1));
    }

    [Fact]
    public async Task WhenDiscordQuitsTheConnectionIsReportedLost()
    {
        await _integration.ConnectAsync(Settings(await _integration.PairAsync(Settings(), CancellationToken.None)), CancellationToken.None);
        await _discord.QuitAsync();
        await Eventually(() => _integration.State == ConnectionState.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _integration.ExecuteAsync("togglemute", null, CancellationToken.None));
    }

    [Fact]
    public async Task ClearMessagesWhenDiscordIsClosedTheClientIdIsWrongOrNothingIsAuthorized()
    {
        var closed = new DiscordIntegration { PipePrefix = "sdtest-nobody-" + Guid.NewGuid().ToString("N") };
        Assert.Contains("n'est pas lancé", (await Assert.ThrowsAsync<InvalidOperationException>(() => closed.ConnectAsync(Settings("R1"), CancellationToken.None))).Message);
        Assert.Contains("Client ID", (await Assert.ThrowsAsync<InvalidOperationException>(() => _integration.ConnectAsync(Settings("R1", clientId: "999"), CancellationToken.None))).Message);
        Assert.Contains("Autoriser dans Discord", (await Assert.ThrowsAsync<InvalidOperationException>(() => Create().ConnectAsync(Settings(), CancellationToken.None))).Message);
        Assert.Contains("Client ID", (await Assert.ThrowsAsync<InvalidOperationException>(() => Create().ConnectAsync(Settings(clientId: ""), CancellationToken.None))).Message);
    }
}
