using System;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Integrations;
using TouchDeck.Integrations.WindowsSystem;
using Xunit;

namespace TouchDeck.Tests;

public class WindowsSystemTests
{
    sealed class FakeProbe : ISystemProbe
    {
        public double Cpu = 12.4, Ram = 63.6;
        public MediaInfo? Media = new("Titre", "Artiste", "Spotify", true);
        public string? Sent;
        public bool Disposed;

        public double CpuPercent() => Cpu;
        public double MemoryPercent() => Ram;
        public Task<MediaInfo?> GetMediaAsync() => Task.FromResult(Media);
        public Task<bool> SendMediaAsync(string command)
        {
            Sent = command;
            if (Media != null) Media = Media with { Playing = command != "playpause" || !Media.Playing };
            return Task.FromResult(Media != null);
        }
        public void Dispose() => Disposed = true;
    }

    static readonly IntegrationSettings None = new() { Values = new System.Collections.Generic.Dictionary<string, string>() };

    [Fact]
    public async Task PublishesLoadMemoryAndMediaRoundedToWholePercents()
    {
        var probe = new FakeProbe();
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(None, CancellationToken.None);

        Assert.Equal(ConnectionState.Connected, sys.State);
        Assert.Equal("12", sys.Values["cpu"]);
        Assert.Equal("64", sys.Values["ram"]);
        Assert.Equal("Titre", sys.Values["media.title"]);
        Assert.Equal("Artiste", sys.Values["media.artist"]);
        Assert.Equal("Spotify", sys.Values["media.app"]);
        Assert.Equal("true", sys.Values["media.playing"]);
    }

    [Fact]
    public async Task ValuesFollowTheProbeAndClearWhenNothingPlays()
    {
        var probe = new FakeProbe();
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(None, CancellationToken.None);
        probe.Cpu = 250;   // aberrant : borné
        probe.Media = null;

        var limit = DateTime.UtcNow.AddSeconds(5);
        while (sys.Values["media.title"].Length > 0 && DateTime.UtcNow < limit) await Task.Delay(20);

        Assert.Equal("", sys.Values["media.title"]);
        Assert.Equal("false", sys.Values["media.playing"]);
        Assert.Equal("100", sys.Values["cpu"]);
    }

    [Fact]
    public async Task MediaCommandsReachTheSessionAndRefreshTheState()
    {
        var probe = new FakeProbe();
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(None, CancellationToken.None);

        Assert.Equal("Lecture / pause", await sys.ExecuteAsync("playpause", null, default));
        Assert.Equal("playpause", probe.Sent);
        Assert.Equal("false", sys.Values["media.playing"]);   // l'état suit sans attendre le relevé suivant
        Assert.Equal("Piste suivante", await sys.ExecuteAsync("next", null, default));
        Assert.Equal("next", probe.Sent);
        await sys.ExecuteAsync("previous", null, default);
        Assert.Equal("previous", probe.Sent);
    }

    [Fact]
    public async Task CommandsExplainWhenNothingPlaysOrTheCommandIsUnknown()
    {
        var probe = new FakeProbe { Media = null };
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(None, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sys.ExecuteAsync("playpause", null, default));
        Assert.Contains("Aucune application", ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => sys.ExecuteAsync("explode", null, default));
    }

    [Fact]
    public async Task DisconnectingReleasesTheProbeAndClearsValues()
    {
        var probe = new FakeProbe();
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(None, CancellationToken.None);
        await sys.DisconnectAsync();

        Assert.True(probe.Disposed);
        Assert.Empty(sys.Values);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sys.ExecuteAsync("next", null, default));
    }

    [Fact]
    public async Task TheRealProbeReadsCpuAndMemoryOfThisMachine()
    {
        // Lecture seule : compteur de performance et mémoire du système, aucune action sur le bureau
        using var sys = new WindowsSystemIntegration();
        await sys.ConnectAsync(None, CancellationToken.None);

        Assert.InRange(int.Parse(sys.Values["cpu"], System.Globalization.CultureInfo.InvariantCulture), 0, 100);
        var ram = int.Parse(sys.Values["ram"], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(ram, 1, 100);
        Assert.True(sys.Values.ContainsKey("media.playing"));
    }
}
