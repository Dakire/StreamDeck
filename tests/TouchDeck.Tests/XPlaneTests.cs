using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Integrations;
using TouchDeck.Integrations.XPlane;
using Xunit;

namespace TouchDeck.Tests;

public class XPlaneTests
{
    const string Gear = "sim/cockpit2/controls/gear_handle_down";
    const string Alt = "sim/cockpit2/gauges/indicators/altitude_ft_pilot";
    const string Landing = "sim/lights/landing_lights_toggle";

    sealed class FakeSim
    {
        public double GearValue = 1;
        public double AltValue = 3500.456;
        public double? Written;
        public readonly List<string> Calls = new();

        public (int, string) Handle(SeenRequest r)
        {
            Calls.Add($"{r.Method} {r.PathAndQuery}");
            var path = Uri.UnescapeDataString(r.PathAndQuery);
            if (path.StartsWith("/api/capabilities", StringComparison.Ordinal)) return (200, """{"api":{"versions":["v1","v2"]}}""");
            if (path.StartsWith("/api/v1/datarefs?filter[name]=" + Gear, StringComparison.Ordinal)) return (200, $$"""{"data":[{"id":101,"name":"{{Gear}}","value_type":"int"}]}""");
            if (path.StartsWith("/api/v1/datarefs?filter[name]=" + Alt, StringComparison.Ordinal)) return (200, $$"""{"data":[{"id":102,"name":"{{Alt}}","value_type":"float"}]}""");
            if (path.StartsWith("/api/v1/datarefs?", StringComparison.Ordinal)) return (200, """{"data":[]}""");
            if (path == "/api/v1/datarefs/101/value" && r.Method == "GET") return (200, $$"""{"data":{{GearValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
            if (path == "/api/v1/datarefs/102/value" && r.Method == "GET") return (200, $$"""{"data":{{AltValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
            if (path == "/api/v1/datarefs/101/value" && r.Method == "PATCH")
            {
                Written = JsonNode.Parse(r.Body)!["data"]!.GetValue<double>();
                GearValue = Written.Value;
                return (200, "{}");
            }
            if (path.StartsWith("/api/v2/commands?filter[name]=" + Landing, StringComparison.Ordinal)) return (200, $$"""{"data":[{"id":900,"name":"{{Landing}}","description":"x"}]}""");
            if (path == "/api/v2/command/900/activate" && r.Method == "POST") return (200, "{}");
            if (path.StartsWith("/api/v2/commands?", StringComparison.Ordinal)) return (200, """{"data":[]}""");
            return (404, "");
        }
    }

    static IntegrationSettings For(FakeHttpServer server, string watch = "") => new()
    {
        Values = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["watch"] = watch,
        },
    };

    [Fact]
    public void WatchListParsesNamedDatarefsAndRejectsMalformedEntries()
    {
        var list = XPlaneIntegration.ParseWatch($"gear={Gear}, alt = {Alt}");
        Assert.Equal(new[] { "gear", "alt" }, list.Select(p => p.Key));
        Assert.Equal(Alt, list[1].Value);
        Assert.Empty(XPlaneIntegration.ParseWatch(""));
        Assert.Throws<InvalidOperationException>(() => XPlaneIntegration.ParseWatch("gear"));
        Assert.Throws<InvalidOperationException>(() => XPlaneIntegration.ParseWatch("=x"));
    }

    [Fact]
    public void ValuesAreFormattedWithoutNoise()
    {
        Assert.Equal("1", XPlaneIntegration.Format(JsonNode.Parse("1")));
        Assert.Equal("3500.46", XPlaneIntegration.Format(JsonNode.Parse("3500.456")));
        Assert.Equal("true", XPlaneIntegration.Format(JsonNode.Parse("true")));
        Assert.Equal("[1,2]", XPlaneIntegration.Format(JsonNode.Parse("[1,2]")));
    }

    [Fact]
    public async Task ReadsTheWatchedDatarefsUnderTheirShortNames()
    {
        using var server = new FakeHttpServer();
        var sim = new FakeSim();
        server.Handler = sim.Handle;
        using var xp = new XPlaneIntegration();
        await xp.ConnectAsync(For(server, $"gear={Gear}, alt={Alt}"), CancellationToken.None);

        Assert.Equal("1", xp.Values["gear"]);
        Assert.Equal("3500.46", xp.Values["alt"]);
        // L'identifiant d'un dataref n'est demandé qu'une fois
        Assert.Single(sim.Calls, c => c.StartsWith("GET /api/v1/datarefs?filter", StringComparison.Ordinal) && c.Contains("gear_handle_down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WritesADatarefAndRefreshesTheStateAtOnce()
    {
        using var server = new FakeHttpServer();
        var sim = new FakeSim { GearValue = 1 };
        server.Handler = sim.Handle;
        using var xp = new XPlaneIntegration();
        await xp.ConnectAsync(For(server, $"gear={Gear}"), CancellationToken.None);

        Assert.Equal($"{Gear} = 0", await xp.ExecuteAsync("set", $"{Gear}=0", default));
        Assert.Equal(0, sim.Written);
        Assert.Equal("0", xp.Values["gear"]);
    }

    [Fact]
    public async Task ActivatesACommandByName()
    {
        using var server = new FakeHttpServer();
        var sim = new FakeSim();
        server.Handler = sim.Handle;
        using var xp = new XPlaneIntegration();
        await xp.ConnectAsync(For(server), CancellationToken.None);   // sans datarefs : simple vérification du simulateur

        Assert.Equal($"Commande {Landing}", await xp.ExecuteAsync("command", Landing, default));
        Assert.Contains("POST /api/v2/command/900/activate", sim.Calls);
    }

    [Fact]
    public async Task ErrorsTellWhatToCheck()
    {
        using var server = new FakeHttpServer();
        var sim = new FakeSim();
        server.Handler = sim.Handle;
        using var xp = new XPlaneIntegration();
        await xp.ConnectAsync(For(server), CancellationToken.None);

        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => xp.ExecuteAsync("command", "sim/nope", default));
        Assert.Contains("introuvable", unknown.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => xp.ExecuteAsync("set", "sim/x", default));
        await Assert.ThrowsAsync<ArgumentException>(() => xp.ExecuteAsync("set", "sim/x=abc", default));
        await Assert.ThrowsAsync<ArgumentException>(() => xp.ExecuteAsync("fly", "x", default));

        server.Dispose();
        using var down = new XPlaneIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => down.ConnectAsync(For(server), CancellationToken.None));
        Assert.Contains("X-Plane ne répond pas", ex.Message);
    }

    [Fact]
    public async Task ABadWatchEntryFailsTheConnectionWithAClearMessage()
    {
        using var server = new FakeHttpServer();
        server.Handler = new FakeSim().Handle;
        using var xp = new XPlaneIntegration();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => xp.ConnectAsync(For(server, "gear"), CancellationToken.None));
        Assert.Contains("nom=dataref", ex.Message);
        Assert.Equal(ConnectionState.Error, xp.State);
    }
}
