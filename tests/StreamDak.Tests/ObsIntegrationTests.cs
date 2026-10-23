using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Obs;
using Xunit;

namespace StreamDak.Tests;

/// <summary>OBS : protocole obs-websocket v5 contre un faux serveur, commandes, états en temps réel, erreurs lisibles.</summary>
public class ObsIntegrationTests
{
    static IntegrationSettings Settings(FakeObsServer server, string password = "") => new()
    {
        Values = new System.Collections.Generic.Dictionary<string, string> { ["host"] = "localhost", ["port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Secrets = new System.Collections.Generic.Dictionary<string, string> { ["password"] = password },
    };

    static async Task Until(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte");
            await Task.Delay(10);
        }
    }

    static async Task<(ObsIntegration Obs, FakeObsServer Server)> ConnectedAsync(string? password = null)
    {
        var server = new FakeObsServer(password);
        var obs = new ObsIntegration();
        await obs.ConnectAsync(Settings(server, password ?? ""), CancellationToken.None);
        return (obs, server);
    }

    [Fact]
    public void AuthenticationMatchesTheVectorOfTheProtocolDocumentation()
    {
        // Exemple publié dans la documentation du protocole obs-websocket v5
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsClient.Authenticate("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
    }

    [Fact]
    public async Task ConnectingReadsTheInitialState()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            Assert.Equal(ConnectionState.Connected, obs.State);
            var v = obs.Values;
            Assert.Equal("Jeu", v["scene"]);
            Assert.Equal("true", v["scene.Jeu"]);
            Assert.Equal("false", v["scene.Discussion"]);
            Assert.Equal("false", v["streaming"]);
            Assert.Equal("false", v["recording"]);
            Assert.Equal("false", v["virtualcam"]);
            Assert.Equal("false", v["mute.Micro"]);
            Assert.Equal("true", v["mute.Bureau"]);
            Assert.False(v.ContainsKey("mute.Image"));   // source sans audio : ignorée sans erreur
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task AWorkingPasswordConnectsAndAWrongOneIsExplained()
    {
        using var server = new FakeObsServer("sésame");
        var good = new ObsIntegration();
        await good.ConnectAsync(Settings(server, "sésame"), CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, good.State);
        await good.DisconnectAsync();

        var bad = new ObsIntegration();
        var wrong = await Assert.ThrowsAsync<InvalidOperationException>(() => bad.ConnectAsync(Settings(server, "faux"), CancellationToken.None));
        Assert.Contains("Mot de passe OBS refusé", wrong.Message);
        Assert.Equal(ConnectionState.Error, bad.State);
        Assert.Equal(wrong.Message, bad.Error);

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => new ObsIntegration().ConnectAsync(Settings(server), CancellationToken.None));
        Assert.Contains("demande un mot de passe", missing.Message);
    }

    [Fact]
    public async Task ObsNotRunningGivesAnActionableMessage()
    {
        int free;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)) { probe.Start(); free = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        var obs = new ObsIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => obs.ConnectAsync(new IntegrationSettings
        {
            Values = new System.Collections.Generic.Dictionary<string, string> { ["port"] = free.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        }, CancellationToken.None));
        Assert.Contains("Vérifie qu'OBS est lancé", ex.Message);
        Assert.Contains("WebSocket", ex.Message);
    }

    [Fact]
    public async Task CommandsDriveObsAndReturnAReadableMessage()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            Assert.Equal("Scène : Pause", await obs.ExecuteAsync("scene", "Pause", default));
            Assert.Equal("Pause", server.Scene);
            Assert.Equal("Micro coupé", await obs.ExecuteAsync("mute", "Micro", default));
            Assert.Equal("Micro rétabli", await obs.ExecuteAsync("mute", "Micro", default));
            Assert.Equal("Direct démarré", await obs.ExecuteAsync("stream", null, default));
            Assert.Equal("Direct arrêté", await obs.ExecuteAsync("stream", null, default));
            Assert.Equal("Enregistrement démarré", await obs.ExecuteAsync("record", null, default));
            Assert.Equal("Caméra virtuelle active", await obs.ExecuteAsync("virtualcam", null, default));
            Assert.Equal("Relecture sauvegardée", await obs.ExecuteAsync("replay", null, default));
            Assert.Equal("Webcam masquée", await obs.ExecuteAsync("source", "Webcam", default));
            Assert.False(server.Items["Webcam"]);
            Assert.Equal("Webcam affichée", await obs.ExecuteAsync("source", "Webcam", default));
            Assert.Contains(server.Requests, r => r.StartsWith("SetSceneItemEnabled", StringComparison.Ordinal) && r.Contains("\"sceneItemId\":7"));
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task ObsErrorsAndMissingArgumentsBecomeDisplayableMessages()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            var unknownScene = await Assert.ThrowsAsync<InvalidOperationException>(() => obs.ExecuteAsync("scene", "Inconnue", default));
            Assert.Equal("OBS : Scène introuvable", unknownScene.Message);
            await Assert.ThrowsAsync<ArgumentException>(() => obs.ExecuteAsync("scene", "  ", default));
            await Assert.ThrowsAsync<ArgumentException>(() => obs.ExecuteAsync("mute", null, default));
            await Assert.ThrowsAsync<ArgumentException>(() => obs.ExecuteAsync("danser", null, default));
            var noSource = await Assert.ThrowsAsync<InvalidOperationException>(() => obs.ExecuteAsync("source", "Fantôme", default));
            Assert.Contains("Source introuvable", noSource.Message);
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task StateEventsUpdateValuesInRealTime()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            var changed = new System.Collections.Concurrent.ConcurrentBag<string>();
            obs.ValuesChanged += keys => { foreach (var k in keys) changed.Add(k); };

            await server.Emit("CurrentProgramSceneChanged", new JsonObject { ["sceneName"] = "Discussion" });
            await Until(() => obs.Values["scene"] == "Discussion");
            Assert.Equal("true", obs.Values["scene.Discussion"]);
            Assert.Equal("false", obs.Values["scene.Jeu"]);          // l'ancienne scène n'est plus active
            Assert.True(obs.Values.ContainsKey("scene.Pause"));      // la liste des scènes est conservée

            await server.Emit("InputMuteStateChanged", new JsonObject { ["inputName"] = "Micro", ["inputMuted"] = true });
            await Until(() => obs.Values["mute.Micro"] == "true");
            await server.Emit("StreamStateChanged", new JsonObject { ["outputActive"] = true, ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STARTED" });
            await Until(() => obs.Values["streaming"] == "true");
            await server.Emit("RecordStateChanged", new JsonObject { ["outputActive"] = true });
            await Until(() => obs.Values["recording"] == "true");
            await server.Emit("VirtualcamStateChanged", new JsonObject { ["outputActive"] = true });
            await Until(() => obs.Values["virtualcam"] == "true");
            Assert.Contains("streaming", changed);
            Assert.Contains("mute.Micro", changed);
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task StatsArePolledAndExposedForLabels()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            await Until(() => obs.Values.ContainsKey("fps"), 6000);
            Assert.Equal("60", obs.Values["fps"]);
            Assert.Equal("3", obs.Values["cpu"]);
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task ALostConnectionIsReportedAndValuesAreCleared()
    {
        var (obs, server) = await ConnectedAsync();
        try
        {
            server.DropConnections();
            await Until(() => obs.State == ConnectionState.Error);
            Assert.Empty(obs.Values);
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => obs.ExecuteAsync("stream", null, default));
            Assert.NotNull(ex.Message);
        }
        finally { await obs.DisconnectAsync(); server.Dispose(); }
    }

    [Fact]
    public async Task TheManagerReconnectsAfterObsRestarts()
    {
        var server = new FakeObsServer();
        var obs = new ObsIntegration();
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        doc.UpdateSettings(c => c.Integrations = new() { ["obs"] = new IntegrationConfig { Enabled = true, Values = new() { ["port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) } } });
        using var manager = new IntegrationManager(new IIntegration[] { obs }, doc, new MemorySecrets(), (_, ct) => Task.Delay(30, ct));
        manager.Start();
        try
        {
            await Until(() => obs.State == ConnectionState.Connected);
            server.DropConnections();
            await Until(() => obs.State != ConnectionState.Connected);
            await Until(() => obs.State == ConnectionState.Connected);   // reconnecté tout seul
            Assert.Equal("Jeu", manager.GetValue("obs.scene"));
        }
        finally { server.Dispose(); }
    }

    [Fact]
    public void CommandsAreCompleteAndDescribed()
    {
        var obs = new ObsIntegration();
        Assert.Equal(new[] { "scene", "mute", "stream", "record", "virtualcam", "source", "replay" }, obs.Commands.Select(c => c.Name));
        Assert.All(obs.Commands, c => { Assert.False(string.IsNullOrWhiteSpace(c.Description)); Assert.True(IconCatalog.TryResolve(c.Icon, out _), c.Icon); });
        Assert.All(obs.Commands.Where(c => c.ArgRequired), c => Assert.NotNull(c.ArgHint));
        Assert.Equal(new[] { "host", "port", "password" }, obs.Fields.Select(f => f.Key));
        Assert.Equal(SettingKind.Secret, obs.Fields.Single(f => f.Key == "password").Kind);
    }
}
