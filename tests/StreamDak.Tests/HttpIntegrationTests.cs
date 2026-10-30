using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.ElgatoLight;
using StreamDak.Integrations.Hue;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Lampes Elgato et Philips Hue contre de faux serveurs HTTP : états, commandes, erreurs lisibles.</summary>
public class HttpIntegrationTests
{
    static IntegrationSettings For(FakeHttpServer server, string? appKey = null, params (string, string)[] more)
    {
        var values = new Dictionary<string, string> { ["host"] = "localhost", ["port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), ["scheme"] = "http" };
        foreach (var (k, v) in more) values[k] = v;
        return new IntegrationSettings
        {
            Values = values,
            Secrets = appKey == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["appkey"] = appKey },
        };
    }

    static async Task Until(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte"); await Task.Delay(10); }
    }

    // ---------------------------------------------------------------- lampes Elgato

    sealed class FakeLight
    {
        public int On = 1, Brightness = 40, Temperature = 213;
        public (int Status, string Body) Handle(SeenRequest r)
        {
            if (r.Method == "PUT")
            {
                var l = JsonNode.Parse(r.Body)!["lights"]![0]!;
                if (l["on"] != null) On = l["on"]!.GetValue<int>();
                if (l["brightness"] != null) Brightness = l["brightness"]!.GetValue<int>();
                if (l["temperature"] != null) Temperature = l["temperature"]!.GetValue<int>();
            }
            return (200, $$$"""{"numberOfLights":1,"lights":[{"on":{{{On}}},"brightness":{{{Brightness}}},"temperature":{{{Temperature}}}}]}""");
        }
    }

    [Fact]
    public void ElgatoConvertsBetweenKelvinAndMired()
    {
        Assert.Equal(143, ElgatoLightIntegration.Mired(7000));
        Assert.Equal(344, ElgatoLightIntegration.Mired(2900));
        Assert.Equal(143, ElgatoLightIntegration.Mired(9000));   // borné aux capacités de l'appareil
        Assert.Equal(344, ElgatoLightIntegration.Mired(1000));
        Assert.Equal(7000, ElgatoLightIntegration.Kelvin(143));
        Assert.Equal(2900, ElgatoLightIntegration.Kelvin(344));
        Assert.Equal(4700, ElgatoLightIntegration.Kelvin(213));
        Assert.Equal(40, ElgatoLightIntegration.Brightness("40", 10));
        Assert.Equal(20, ElgatoLightIntegration.Brightness("+10", 10));
        Assert.Equal(0, ElgatoLightIntegration.Brightness("-50", 10));
        Assert.Equal(100, ElgatoLightIntegration.Brightness("+500", 10));
        Assert.Throws<ArgumentException>(() => ElgatoLightIntegration.Brightness("fort", 10));
    }

    [Fact]
    public async Task ElgatoReadsAndDrivesTheLight()
    {
        using var server = new FakeHttpServer();
        var light = new FakeLight();
        server.Handler = light.Handle;
        using var elgato = new ElgatoLightIntegration();
        await elgato.ConnectAsync(For(server), CancellationToken.None);
        Assert.Equal("true", elgato.Values["on"]);
        Assert.Equal("40", elgato.Values["brightness"]);
        Assert.Equal("4700", elgato.Values["temperature"]);

        Assert.Equal("Lampe éteinte", await elgato.ExecuteAsync("toggle", null, default));
        Assert.Equal(0, light.On);
        Assert.Equal("false", elgato.Values["on"]);                 // voyant mis à jour sans attendre le relevé
        Assert.Equal("Lampe allumée", await elgato.ExecuteAsync("toggle", null, default));
        Assert.Equal("Luminosité 55 %", await elgato.ExecuteAsync("brightness", "+15", default));
        Assert.Equal(55, light.Brightness);
        Assert.Equal("Luminosité 80 %", await elgato.ExecuteAsync("brightness", "80", default));
        Assert.Equal("Température 6500 K", await elgato.ExecuteAsync("temperature", "6500", default));
        Assert.Equal(154, light.Temperature);
        await elgato.ExecuteAsync("off", null, default);
        Assert.Equal(0, light.On);
        await elgato.ExecuteAsync("on", null, default);
        Assert.Equal(1, light.On);
        Assert.All(server.Requests.Where(r => r.Method == "PUT"), r => Assert.Equal("/elgato/lights", r.PathAndQuery));
    }

    [Fact]
    public async Task ElgatoValidatesArgumentsAndExplainsFailures()
    {
        using var server = new FakeHttpServer();
        server.Handler = new FakeLight().Handle;
        using var elgato = new ElgatoLightIntegration();
        await elgato.ConnectAsync(For(server), CancellationToken.None);
        Assert.Contains("entre 2900 et 7000", (await Assert.ThrowsAsync<ArgumentException>(() => elgato.ExecuteAsync("temperature", "9000", default))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => elgato.ExecuteAsync("temperature", "chaud", default));
        await Assert.ThrowsAsync<ArgumentException>(() => elgato.ExecuteAsync("brightness", null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => elgato.ExecuteAsync("danser", null, default));

        using var empty = new ElgatoLightIntegration();
        Assert.Contains("adresse IP", (await Assert.ThrowsAsync<InvalidOperationException>(() => empty.ConnectAsync(new IntegrationSettings(), CancellationToken.None))).Message);

        server.Handler = _ => (200, """{"autre":1}""");
        using var wrong = new ElgatoLightIntegration();
        Assert.Contains("n'est pas une lampe Elgato", (await Assert.ThrowsAsync<InvalidOperationException>(() => wrong.ConnectAsync(For(server), CancellationToken.None))).Message);
    }

    [Fact]
    public async Task ElgatoDrivesSeveralLightsTogether()
    {
        using var a = new FakeHttpServer(); using var b = new FakeHttpServer();
        var la = new FakeLight(); var lb = new FakeLight { On = 0 };
        a.Handler = la.Handle; b.Handler = lb.Handle;
        using var elgato = new ElgatoLightIntegration();
        // Deux adresses sur des ports différents : on utilise l'hôte « localhost:port » via le champ host (le port commun reste celui de la première)
        var settings = For(a, null, ("host", $"localhost, localhost"));
        await elgato.ConnectAsync(settings, CancellationToken.None);
        Assert.Equal("true", elgato.Values["on"]);                  // au moins une lampe allumée
        await elgato.ExecuteAsync("off", null, default);
        Assert.Equal(0, la.On);
        Assert.Equal(2, a.Requests.Count(r => r.Method == "PUT"));  // chaque adresse reçoit la commande
        Assert.Empty(b.Requests);
    }

    // ---------------------------------------------------------------- Philips Hue

    sealed class FakeBridge
    {
        public bool LightOn = true; public bool RoomOn = true; public int Brightness = 50; public string? Scene;
        public string Key = "cle-secrete";
        public bool ButtonPressed;
        public ConcurrentBag<string> Calls = new();

        public (int Status, string Body) Handle(SeenRequest r)
        {
            if (r.Method == "POST" && r.PathAndQuery == "/api")
                return (200, ButtonPressed ? $$$"""[{"success":{"username":"{{{Key}}}","clientkey":"ABCD"}}]"""
                                           : """[{"error":{"type":101,"address":"","description":"link button not pressed"}}]""");
            if (r.Headers["hue-application-key"] != Key) return (403, """{"errors":[{"description":"unauthorized user"}]}""");
            Calls.Add(r.Method + " " + r.PathAndQuery + " " + r.Body);
            if (r.Method == "PUT")
            {
                var b = JsonNode.Parse(r.Body)!;
                if (r.PathAndQuery.EndsWith("/light/l1", StringComparison.Ordinal))
                {
                    if (b["on"] != null) LightOn = b["on"]!["on"]!.GetValue<bool>();
                    if (b["dimming"] != null) Brightness = (int)b["dimming"]!["brightness"]!.GetValue<double>();
                }
                if (r.PathAndQuery.EndsWith("/grouped_light/g1", StringComparison.Ordinal) && b["on"] != null) RoomOn = b["on"]!["on"]!.GetValue<bool>();
                if (r.PathAndQuery.Contains("/scene/", StringComparison.Ordinal)) Scene = r.PathAndQuery.Split('/').Last();
                if (r.PathAndQuery.Contains("/light/refuse", StringComparison.Ordinal)) return (207, """{"errors":[{"description":"device unreachable"}],"data":[]}""");
                return (200, """{"errors":[],"data":[{"rid":"x","rtype":"light"}]}""");
            }
            var data = r.PathAndQuery switch
            {
                "/clip/v2/resource/light" => $$$"""{"data":[{"id":"l1","metadata":{"name":"Bureau"},"on":{"on":{{{LightOn.ToString().ToLowerInvariant()}}}},"dimming":{"brightness":{{{Brightness}}}.0}}]}""",
                "/clip/v2/resource/room" => """{"data":[{"id":"r1","metadata":{"name":"Salon"},"services":[{"rid":"g1","rtype":"grouped_light"}]}]}""",
                "/clip/v2/resource/grouped_light" => $$$"""{"data":[{"id":"g1","on":{"on":{{{RoomOn.ToString().ToLowerInvariant()}}}},"dimming":{"brightness":70.0}}]}""",
                "/clip/v2/resource/scene" => """{"data":[{"id":"s1","metadata":{"name":"Détente"}},{"id":"s2","metadata":{"name":"Lecture"}}]}""",
                _ => "{\"data\":[]}",
            };
            return (200, data);
        }
    }

    [Fact]
    public async Task HuePairingReturnsTheKeyOnceTheBridgeButtonIsPressed()
    {
        using var server = new FakeHttpServer();
        var bridge = new FakeBridge();
        server.Handler = bridge.Handle;
        var hue = new HueIntegration();
        var early = await Assert.ThrowsAsync<InvalidOperationException>(() => hue.PairAsync(For(server), default));
        Assert.Contains("bouton du pont", early.Message);
        bridge.ButtonPressed = true;
        Assert.Equal("cle-secrete", await hue.PairAsync(For(server), default));
        Assert.Contains("\"generateclientkey\":true", server.Requests.Last().Body);
        Assert.Equal("appkey", hue.PairedSecretField);
        await Assert.ThrowsAsync<InvalidOperationException>(() => hue.PairAsync(new IntegrationSettings(), default));
    }

    [Fact]
    public async Task HueReadsLightsRoomsAndScenes()
    {
        using var server = new FakeHttpServer();
        var bridge = new FakeBridge();
        server.Handler = bridge.Handle;
        using var hue = new HueIntegration();
        await hue.ConnectAsync(For(server, bridge.Key), CancellationToken.None);
        var v = hue.Values;
        Assert.Equal("true", v["on.Bureau"]);
        Assert.Equal("50", v["brightness.Bureau"]);
        Assert.Equal("true", v["on.Salon"]);
        Assert.Equal("70", v["brightness.Salon"]);
        Assert.Equal("true", v["anyon"]);
    }

    [Fact]
    public async Task HueCommandsTargetLightsRoomsAndScenesByName()
    {
        using var server = new FakeHttpServer();
        var bridge = new FakeBridge();
        server.Handler = bridge.Handle;
        using var hue = new HueIntegration();
        await hue.ConnectAsync(For(server, bridge.Key), CancellationToken.None);

        Assert.Equal("Bureau éteint", await hue.ExecuteAsync("toggle", "bureau", default));   // insensible à la casse
        Assert.False(bridge.LightOn);
        Assert.Equal("false", hue.Values["on.Bureau"]);
        Assert.Equal("Salon éteint", await hue.ExecuteAsync("off", "Salon", default));
        Assert.False(bridge.RoomOn);
        Assert.Equal("Bureau allumé", await hue.ExecuteAsync("on", "Bureau", default));
        Assert.Equal("Bureau : 80 %", await hue.ExecuteAsync("brightness", "Bureau=80", default));
        Assert.Equal(80, bridge.Brightness);
        Assert.Equal("Bureau : 90 %", await hue.ExecuteAsync("brightness", "Bureau=+10", default));
        Assert.Equal("Scène : Lecture", await hue.ExecuteAsync("scene", "lecture", default));
        Assert.Equal("s2", bridge.Scene);
        Assert.Contains(bridge.Calls, c => c.StartsWith("PUT /clip/v2/resource/grouped_light/g1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HueErrorsNameWhatIsAvailableAndWhatToDo()
    {
        using var server = new FakeHttpServer();
        var bridge = new FakeBridge();
        server.Handler = bridge.Handle;
        using var hue = new HueIntegration();
        await hue.ConnectAsync(For(server, bridge.Key), CancellationToken.None);

        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => hue.ExecuteAsync("toggle", "Cave", default));
        Assert.Contains("Disponibles : Bureau, Salon", unknown.Message);
        Assert.Contains("Disponibles : Détente, Lecture", (await Assert.ThrowsAsync<InvalidOperationException>(() => hue.ExecuteAsync("scene", "Fête", default))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => hue.ExecuteAsync("brightness", "Bureau", default));
        await Assert.ThrowsAsync<ArgumentException>(() => hue.ExecuteAsync("brightness", "Bureau=beaucoup", default));
        await Assert.ThrowsAsync<ArgumentException>(() => hue.ExecuteAsync("toggle", " ", default));
        await Assert.ThrowsAsync<ArgumentException>(() => hue.ExecuteAsync("danser", "x", default));
    }

    [Fact]
    public async Task HueRefusesAWrongKeyAndAMissingKeyWithTheRightAdvice()
    {
        using var server = new FakeHttpServer();
        server.Handler = new FakeBridge().Handle;
        using var wrong = new HueIntegration();
        Assert.Contains("refais l'association", (await Assert.ThrowsAsync<InvalidOperationException>(() => wrong.ConnectAsync(For(server, "mauvaise"), CancellationToken.None))).Message);
        using var missing = new HueIntegration();
        Assert.Contains("Associer", (await Assert.ThrowsAsync<InvalidOperationException>(() => missing.ConnectAsync(For(server), CancellationToken.None))).Message);
        using var noHost = new HueIntegration();
        Assert.Contains("adresse IP", (await Assert.ThrowsAsync<InvalidOperationException>(() => noHost.ConnectAsync(new IntegrationSettings(), CancellationToken.None))).Message);
    }

    [Fact]
    public async Task AllHuePutsFailLoudlyWhenTheBridgeReportsErrorsEvenWithAnOkStatus()
    {
        using var server = new FakeHttpServer();
        var bridge = new FakeBridge();
        server.Handler = r => r.Method == "PUT" ? (207, """{"errors":[{"description":"device unreachable"}],"data":[]}""") : bridge.Handle(r);
        using var hue = new HueIntegration();
        await hue.ConnectAsync(For(server, bridge.Key), CancellationToken.None);
        Assert.Contains("device unreachable", (await Assert.ThrowsAsync<InvalidOperationException>(() => hue.ExecuteAsync("on", "Bureau", default))).Message);
    }

    // ---------------------------------------------------------------- commun

    [Fact]
    public void EveryConnectorDescribesItsCommandsAndStateKeys()
    {
        var all = new IIntegration[] { new ElgatoLightIntegration(), new HueIntegration() };
        Assert.Equal(new[] { "elgato", "hue" }, all.Select(i => i.Id));
        foreach (var integration in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(integration.Help), integration.Id);
            Assert.Equal(integration.Commands.Count, integration.Commands.Select(c => c.Name).Distinct().Count());
            Assert.All(integration.Commands, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Description), c.Name);
                Assert.True(IconCatalog.TryResolve(c.Icon, out _), $"{integration.Id}.{c.Name} : icône {c.Icon}");
                if (c.ArgRequired) Assert.NotNull(c.ArgHint);
            });
            Assert.True(IconCatalog.TryResolve(integration.Icon, out _), integration.Icon);
        }
        Assert.Equal(SettingKind.Secret, new HueIntegration().Fields.Single(f => f.Key == "appkey").Kind);
        Assert.IsAssignableFrom<IPairableIntegration>(new HueIntegration());
    }
}
