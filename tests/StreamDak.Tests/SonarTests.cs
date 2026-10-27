using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Sonar;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Connecteur Sonar contre un FAUX serveur local qui imite les routes que l'on croit être celles de GG. Ces tests prouvent la logique de StreamDak ;
/// ils ne prouvent pas que GG répond ainsi (API non vérifiée, voir docs/integrations/sonar.md).
/// </summary>
public sealed class SonarTests : IDisposable
{
    const string Levels = """
        {
          "masters": { "classic": { "volume": 0.8, "muted": false }, "streamer": { "streaming": { "volume": 0.5, "muted": false } } },
          "devices": {
            "game":        { "classic": { "volume": 0.6,  "muted": false } },
            "chatRender":  { "classic": { "volume": 0.35, "muted": true } },
            "media":       { "classic": { "volume": 1.0,  "muted": false } },
            "aux":         { "classic": { "volume": 0.0,  "muted": false } },
            "chatCapture": { "classic": { "volume": 0.9,  "muted": false } }
          }
        }
        """;

    readonly string _dir = Path.Combine(Path.GetTempPath(), "streamdak-sonar-" + Guid.NewGuid().ToString("N"));
    readonly FakeHttpServer _server = new();
    SonarIntegration? _sonar;

    public SonarTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _sonar?.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { /* nettoyage au mieux */ }
    }

    string WriteCoreProps(string json, string file = "coreProps.json")
    {
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, json);
        return path;
    }

    string CoreProps => WriteCoreProps($$"""{ "address": "localhost:{{_server.Port}}", "encryptedAddress": "localhost:1", "ggEncryptedAddress": "localhost:1" }""");

    /// <summary>Faux GG : liste des sous-applications, lecture des volumes, écriture (PUT) qui change l'état servi ensuite.</summary>
    void ServeFakeGg(string? sonarAddress = null, string levels = Levels)
    {
        var state = JsonNode.Parse(levels)!;
        _server.Handler = r =>
        {
            if (r.PathAndQuery == "/subApps")
                return (200, $$"""{ "subApps": { "sonar": { "isEnabled": true, "isReady": true, "isRunning": true, "metadata": { "webServerAddress": "{{sonarAddress ?? $"http://localhost:{_server.Port}"}}" } } } }""");
            if (r.PathAndQuery == "/volumeSettings/classic") return (200, state.ToJsonString());
            var parts = r.PathAndQuery.Trim('/').Split('/');
            if (r.Method == "PUT" && parts.Length == 5 && parts[0] == "volumeSettings" && parts[1] == "classic")
            {
                var node = parts[2] == "master" ? state["masters"]!["classic"]! : state["devices"]![parts[2]]!["classic"]!;
                if (parts[3] == "Volume") node["volume"] = double.Parse(parts[4], CultureInfo.InvariantCulture);
                else node["muted"] = bool.Parse(parts[4]);
                return (200, "");
            }
            return (404, "");
        };
    }

    SonarIntegration Create()
    {
        _sonar = new SonarIntegration { ReportDirectory = _dir };
        return _sonar;
    }

    IntegrationSettings Settings(params (string, string)[] more)
    {
        var values = new Dictionary<string, string> { ["corepropspath"] = CoreProps };
        foreach (var (k, v) in more) values[k] = v;
        return new IntegrationSettings { Values = values };
    }

    // ---------------------------------------------------------------- logique pure

    [Fact]
    public void LevelsAreReadFromTheMastersAndDevicesNodes()
    {
        var levels = SonarApi.ParseLevels(JsonNode.Parse(Levels), SonarApi.SplitChannels(SonarConfig.DefaultChannels), "classic");
        Assert.Equal(6, levels.Count);
        Assert.Equal(80, levels["master"].Percent);
        Assert.Equal(60, levels["game"].Percent);
        Assert.Equal(35, levels["chatRender"].Percent);
        Assert.True(levels["chatRender"].Muted);
        Assert.False(levels["game"].Muted);
        Assert.Equal(0, levels["aux"].Percent);
    }

    [Fact]
    public void StreamerModeReadsTheChosenSlider()
    {
        var levels = SonarApi.ParseLevels(JsonNode.Parse(Levels), new[] { "master" }, "streaming");
        Assert.Equal(50, levels["master"].Percent);
    }

    [Fact]
    public void OtherPlausibleShapesAreAccepted()
    {
        var flat = JsonNode.Parse("""{ "game": { "volume": "0.25", "isMuted": true }, "media": { "volume": 2.5 } }""");
        var levels = SonarApi.ParseLevels(flat, new[] { "game", "media", "aux" }, "classic");
        Assert.Equal(25, levels["game"].Percent);
        Assert.True(levels["game"].Muted);
        Assert.Equal(100, levels["media"].Percent);   // borné
        Assert.False(levels.ContainsKey("aux"));
        Assert.Empty(SonarApi.ParseLevels(JsonNode.Parse("[1,2]"), new[] { "game" }, "classic"));
        Assert.Empty(SonarApi.ParseLevels(JsonNode.Parse("""{ "game": 3 }"""), new[] { "game" }, "classic"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("https://localhost:5000/x", true)]
    [InlineData("http://[::1]:80/", true)]
    [InlineData("http://192.168.1.20:5000", false)]
    [InlineData("https://example.com", false)]
    [InlineData("http://127.0.0.1.evil.example/", false)]
    [InlineData("http://user:pass@localhost:80/", false)]
    [InlineData("ftp://localhost/", false)]
    [InlineData("file:///c:/x", false)]
    public void OnlyLocalWebAddressesAreAccepted(string address, bool local) =>
        Assert.Equal(local, Uri.TryCreate(address, UriKind.Absolute, out var uri) && SonarApi.IsLocalAddress(uri));

    [Fact]
    public void AddressesWithoutSchemeGetOneAndNonLocalOnesAreDropped()
    {
        Assert.Equal("http://127.0.0.1:123/", SonarApi.ParseLocalAddress("127.0.0.1:123", false)!.AbsoluteUri);
        Assert.Equal("https://127.0.0.1:123/", SonarApi.ParseLocalAddress("127.0.0.1:123", true)!.AbsoluteUri);
        Assert.Null(SonarApi.ParseLocalAddress("10.0.0.5:123", false));
        Assert.Null(SonarApi.ParseLocalAddress("", false));
        var addresses = SonarApi.GgAddresses(JsonNode.Parse("""{ "address": "10.1.1.1:80", "ggEncryptedAddress": "127.0.0.1:51250", "encryptedAddress": "127.0.0.1:51249" }"""));
        Assert.Equal(new[] { "https://127.0.0.1:51250/", "https://127.0.0.1:51249/" }, addresses.Select(a => a.AbsoluteUri));
    }

    [Theory]
    [InlineData("50", 10, 50)]
    [InlineData("+5", 10, 15)]
    [InlineData("-20", 10, 0)]
    [InlineData("250", 10, 100)]
    [InlineData("40 %", 10, 40)]
    public void RequestedLevelsAreAbsoluteOrRelativeAndBounded(string text, int current, int expected) => Assert.Equal(expected, SonarApi.ParseLevel(text, current));

    [Fact]
    public void ChannelsHaveFrenchNicknamesAndRoutesStayRelative()
    {
        Assert.Equal(new[] { "game", "chatCapture", "media" }, SonarApi.SplitChannels("jeu, micro;musique jeu"));
        var config = new SonarConfig();
        Assert.Equal("volumeSettings/classic/game/Volume/0.4", SonarApi.BuildRoute(config.EffectiveVolumeRoute, config, "game", SonarApi.FormatVolume(40)));
        Assert.Throws<InvalidOperationException>(() => SonarApi.BuildRoute("http://evil.example/{channel}", config, "game"));
        Assert.Throws<InvalidOperationException>(() => SonarApi.BuildRoute("a/../b", config));
        Assert.Equal("volumeSettings/classic/a%2Fb/Volume/1", SonarApi.BuildRoute(config.EffectiveVolumeRoute, config, "a/b", "1"));
    }

    // ---------------------------------------------------------------- contre le faux GG

    [Fact]
    public async Task ItDiscoversSonarThroughCorePropsAndPublishesEveryChannel()
    {
        ServeFakeGg();
        var sonar = Create();
        await sonar.ConnectAsync(Settings(), CancellationToken.None);
        Assert.Equal(ConnectionState.Connected, sonar.State);
        var v = sonar.Values;
        Assert.Equal("60", v["game.volume"]);
        Assert.Equal("false", v["game.muted"]);
        Assert.Equal("35", v["chatRender.volume"]);
        Assert.Equal("true", v["chatRender.muted"]);
        Assert.Equal("90", v["chatCapture.volume"]);
        Assert.Equal("80", v["master.volume"]);
        Assert.Contains(_server.Requests, r => r.PathAndQuery == "/subApps");
    }

    [Fact]
    public async Task VolumeCommandsWriteTheRoutesAndUpdateTheValues()
    {
        ServeFakeGg();
        var sonar = Create();
        await sonar.ConnectAsync(Settings(), CancellationToken.None);
        Assert.Equal("Jeu : 40 %", await sonar.ExecuteAsync("volume", "game=40", CancellationToken.None));
        Assert.Contains(_server.Requests, r => r.Method == "PUT" && r.PathAndQuery == "/volumeSettings/classic/game/Volume/0.4");
        Assert.Equal("40", sonar.Values["game.volume"]);

        await sonar.ExecuteAsync("volume", "jeu=+5", CancellationToken.None);   // surnom français, relatif
        Assert.Contains(_server.Requests, r => r.Method == "PUT" && r.PathAndQuery == "/volumeSettings/classic/game/Volume/0.45");

        Assert.Equal("Micro coupé", await sonar.ExecuteAsync("mute", "micro", CancellationToken.None));
        Assert.Contains(_server.Requests, r => r.Method == "PUT" && r.PathAndQuery == "/volumeSettings/classic/chatCapture/isMuted/true");
        Assert.Equal("true", sonar.Values["chatCapture.muted"]);
        Assert.Equal("Micro rétabli", await sonar.ExecuteAsync("togglemute", "chatCapture", CancellationToken.None));
        Assert.Equal("false", sonar.Values["chatCapture.muted"]);
        await sonar.ExecuteAsync("unmute", "chatRender", CancellationToken.None);
        Assert.Contains(_server.Requests, r => r.Method == "PUT" && r.PathAndQuery == "/volumeSettings/classic/chatRender/isMuted/false");
    }

    [Fact]
    public async Task ABadCommandIsRefusedWithAReadableMessage()
    {
        ServeFakeGg();
        var sonar = Create();
        await sonar.ConnectAsync(Settings(), CancellationToken.None);
        Assert.Contains("canal=niveau", Assert.Throws<ArgumentException>(() => sonar.ExecuteAsync("volume", "game", CancellationToken.None).GetAwaiter().GetResult()).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => sonar.ExecuteAsync("volume", "game=abc", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => sonar.ExecuteAsync("mute", "../x", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => sonar.ExecuteAsync("nope", "game", CancellationToken.None));
        Assert.DoesNotContain(_server.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public async Task GgClosedGivesACleanUnavailableStateNotACrash()
    {
        var sonar = Create();
        // coreProps.json absent : GG n'est pas installé ou pas lancé
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(("corepropspath", Path.Combine(_dir, "absent.json"))), CancellationToken.None));
        Assert.Contains("SteelSeries GG", ex.Message);
        Assert.Equal(ConnectionState.Error, sonar.State);
        Assert.Empty(sonar.Values);

        // coreProps.json présent mais personne n'écoute (GG fermé)
        _server.Dispose();
        var closed = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(), CancellationToken.None));
        Assert.Contains("SteelSeries GG", closed.Message);
        Assert.Equal(ConnectionState.Error, sonar.State);
    }

    [Fact]
    public async Task ARemoteAddressInCorePropsOrInSubAppsIsNeverContacted()
    {
        ServeFakeGg();
        var sonar = Create();
        var remoteCore = WriteCoreProps("""{ "address": "192.168.1.50:5000", "ggEncryptedAddress": "example.com:443" }""", "remote.json");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(("corepropspath", remoteCore)), CancellationToken.None));
        Assert.Contains("aucune adresse locale", ex.Message);
        Assert.Empty(_server.Requests);

        ServeFakeGg(sonarAddress: "http://192.168.1.77:9999");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(), CancellationToken.None));
        Assert.Contains("pas locale", refused.Message);

        var manual = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(("baseurl", "http://evil.example:80")), CancellationToken.None));
        Assert.Contains("locale", manual.Message);
    }

    [Fact]
    public async Task ARedirectIsNeverFollowed()
    {
        _server.Handler = r => (302, "");
        var sonar = Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(), CancellationToken.None));
        Assert.Equal(ConnectionState.Error, sonar.State);
    }

    [Fact]
    public async Task AnUnknownResponseShapePointsToTheDiagnostic()
    {
        ServeFakeGg(levels: """{ "something": "else" }""");
        var sonar = Create();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(), CancellationToken.None));
        Assert.Contains("Diagnostic", ex.Message);
        Assert.True(File.Exists(Path.Combine(_dir, SonarIntegration.ReportFileName)));   // le rapport automatique après un échec
    }

    [Fact]
    public async Task RoutesAndManualAddressCanBeChangedWithoutRecompiling()
    {
        var hits = new List<string>();
        _server.Handler = r =>
        {
            hits.Add(r.Method + " " + r.PathAndQuery);
            if (r.PathAndQuery == "/api/levels") return (200, """{ "game": { "volume": 0.5, "muted": false } }""");
            return r.Method == "PUT" ? (200, "") : (404, "");
        };
        var sonar = Create();
        await sonar.ConnectAsync(new IntegrationSettings
        {
            Values = new Dictionary<string, string>
            {
                ["baseurl"] = $"http://localhost:{_server.Port}", ["readroute"] = "api/levels", ["channels"] = "game",
                ["volumeroute"] = "api/vol/{channel}?v={value}",
            },
        }, CancellationToken.None);
        Assert.Equal("50", sonar.Values["game.volume"]);
        await sonar.ExecuteAsync("volume", "game=70", CancellationToken.None);
        Assert.Contains("PUT /api/vol/game?v=0.7", hits);
        Assert.DoesNotContain(hits, h => h.Contains("subApps", StringComparison.Ordinal));   // adresse donnée : pas de découverte
    }

    [Fact]
    public async Task TheDiagnosticIsReadOnlyListsTheChannelsAndWritesAReport()
    {
        ServeFakeGg();
        var sonar = Create();
        await sonar.ConnectAsync(Settings(), CancellationToken.None);
        var before = _server.Requests.Count;
        var message = await sonar.ExecuteAsync("diagnose", null, CancellationToken.None);
        Assert.Contains("game 60 %", message);
        Assert.Contains("chatRender 35 % (coupé)", message);
        Assert.DoesNotContain(_server.Requests.Skip(before), r => r.Method != "GET");   // lecture seule
        var report = File.ReadAllText(Path.Combine(_dir, SonarIntegration.ReportFileName));
        Assert.Contains("API Sonar non vérifiée", report);
        Assert.Contains("/volumeSettings/classic", report);
        Assert.Contains("Clés de premier niveau : masters, devices", report);
        Assert.Contains("Canaux reconnus", report);
    }

    [Fact]
    public async Task TheDiagnosticStillExplainsWhenGgIsClosed()
    {
        var sonar = Create();
        // non connecté : la commande est refusée par le moteur, mais l'appel direct reste propre et documente l'échec
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ExecuteAsync("diagnose", null, CancellationToken.None));
        Assert.Contains(SonarIntegration.ReportFileName, ex.Message);
    }

    sealed class Nav : StreamDak.Actions.INavigator
    {
        public void GoToPage(string name) { }
        public void SwitchProfile(string name) { }
    }

    [Fact]
    public async Task OnlyTheDiagnosticRunsThroughTheEngineWhileSonarIsNotConnected()
    {
        ServeFakeGg(levels: """{ "something": "else" }""");
        var sonar = Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sonar.ConnectAsync(Settings(), CancellationToken.None));   // réponse inconnue : en erreur
        Assert.Equal(ConnectionState.Error, sonar.State);

        ServeFakeGg();   // GG répond correctement à présent, mais le connecteur n'a pas encore repris sa connexion
        var engine = new StreamDak.Actions.ActionEngine(new StreamDak.Actions.ActionContext { Platform = new StreamDak.Actions.SimulatedPlatform().Services, Http = new System.Net.Http.HttpClient() },
            new StreamDak.Actions.IActionHandler[] { new IntegrationAction(sonar) });
        DeckButton Step(string command, string? args) => new() { Label = "S", Actions = { new DeckAction { Type = "sonar", Value = command, Args = args } } };

        var diagnostic = await engine.RunAsync(Step("diagnose", null), new Nav());
        Assert.True(diagnostic.Ok, diagnostic.Message);
        Assert.Contains("game 60 %", diagnostic.Message);
        Assert.False((await engine.RunAsync(Step("volume", "game=10"), new Nav())).Ok);   // une commande qui écrit exige la connexion
        Assert.DoesNotContain(_server.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public void TheIntegrationDescribesItsCommandsAndSettings()
    {
        var sonar = Create();
        Assert.Equal("sonar", sonar.Id);
        Assert.Equal(new[] { "volume", "mute", "unmute", "togglemute", "diagnose" }, sonar.Commands.Select(c => c.Name));
        Assert.All(sonar.Commands.Where(c => c.StateKey != null), c => Assert.Equal("{arg}.muted", c.StateKey));
        Assert.Contains(sonar.Fields, f => f.Key == "channels");
        Assert.DoesNotContain(sonar.Fields, f => f.Kind == SettingKind.Secret);   // aucun secret : GG n'en demande pas
    }
}
