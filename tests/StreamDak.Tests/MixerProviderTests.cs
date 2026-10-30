using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Audio;
using StreamDak.Integrations;
using StreamDak.Integrations.Sonar;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Fournisseur de mélangeur simulé : prouve que les curseurs ne dépendent que de <see cref="IAudioMixerProvider"/> (Wave Link, Voicemeeter…).</summary>
sealed class FakeMixerProvider : IAudioMixerProvider
{
    readonly Dictionary<string, (int Percent, bool Muted)> _levels = new(StringComparer.OrdinalIgnoreCase);
    Action? _changed;

    public FakeMixerProvider(params string[] channels)
    {
        foreach (var c in channels) _levels[c] = (50, false);
        Channels = channels.Select(c => new MixerChannel(c, c.ToUpperInvariant(), "Speaker224", "Mélangeur simulé")).ToList();
    }

    public string Id => "fake";
    public string Name => "Faux";
    public IReadOnlyList<MixerChannel> Channels { get; }
    public bool Available { get; set; } = true;
    public List<int> SentLevels { get; } = new();
    public List<bool> SentMutes { get; } = new();
    public event Action? Changed { add => _changed += value; remove => _changed -= value; }
    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
    public void Raise() => _changed?.Invoke();

    public MixerChannel Describe(string channel) => Channels.FirstOrDefault(c => c.Id.Equals(channel, StringComparison.OrdinalIgnoreCase)) ?? new(channel, channel, "Speaker224", "");

    public LevelReading? Read(string channel, out string problem)
    {
        problem = "";
        if (!Available) { problem = "Mélangeur éteint"; return null; }
        if (!_levels.TryGetValue(channel, out var level)) { problem = "Canal inconnu"; return null; }
        return new LevelReading(level.Percent, level.Muted);
    }

    public Task SetLevelAsync(string channel, int percent, CancellationToken cancellation = default)
    {
        SentLevels.Add(percent);
        _levels[channel] = (percent, _levels[channel].Muted);
        return Task.CompletedTask;
    }

    public Task SetMutedAsync(string channel, bool muted, CancellationToken cancellation = default)
    {
        SentMutes.Add(muted);
        _levels[channel] = (_levels[channel].Percent, muted);
        return Task.CompletedTask;
    }
}

public class MixerProviderTests
{
    [Fact]
    public void ASourceReadsAndDescribesItsChannelThroughTheProvider()
    {
        var provider = new FakeMixerProvider("game", "aux");
        using var source = new MixerLevelSource(provider, "aux");

        Assert.Equal("Faux · AUX", source.Title);
        Assert.Equal("Mélangeur simulé", source.Controls);
        Assert.Equal("Speaker224", source.Icon);
        Assert.Null(source.PollInterval);   // le fournisseur prévient : pas de relevé
        var reading = source.Read(out _);
        Assert.Equal(new LevelReading(50, false), reading);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(37, 37)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public async Task TheLevelSentToTheProviderIsAlwaysAPercentageFromZeroToAHundred(int asked, int sent)
    {
        var provider = new FakeMixerProvider("game");
        using var source = new MixerLevelSource(provider, "game");

        await source.SetLevelAsync(asked);

        Assert.Equal(new[] { sent }, provider.SentLevels);
        Assert.Equal(sent, source.Read(out _)!.Percent);
    }

    [Fact]
    public async Task MutingGoesThroughTheProviderAndIsReadBack()
    {
        var provider = new FakeMixerProvider("chatCapture");
        using var source = new MixerLevelSource(provider, "chatCapture");

        await source.SetMutedAsync(true);
        Assert.True(source.Read(out _)!.Muted);
        await source.SetMutedAsync(false);
        Assert.False(source.Read(out _)!.Muted);
        Assert.Equal(new[] { true, false }, provider.SentMutes);
    }

    [Fact]
    public void AnUnavailableMixerGivesNoLevelAndSaysWhy()
    {
        var provider = new FakeMixerProvider("game") { Available = false };
        using var source = new MixerLevelSource(provider, "game");

        Assert.Null(source.Read(out var problem));
        Assert.Equal("Mélangeur éteint", problem);
        provider.Available = true;
        Assert.NotNull(source.Read(out _));
        Assert.Null(new MixerLevelSource(provider, "absent").Read(out var unknown));
        Assert.Equal("Canal inconnu", unknown);
    }

    [Fact]
    public void ProviderChangesReachTheSourceUntilItIsDisposed()
    {
        var provider = new FakeMixerProvider("game");
        var source = new MixerLevelSource(provider, "game");
        int changes = 0;
        source.Changed += () => changes++;

        provider.Raise();
        Assert.Equal(1, changes);

        source.Dispose();
        provider.Raise();
        Assert.Equal(1, changes);
        Assert.Equal(0, provider.Subscribers);
    }

    [Fact]
    public void TheSonarProviderListsTheSixChannelsWithTheirIconsAndWarnsAboutTheMicGain()
    {
        using var manager = new IntegrationManager(Array.Empty<IIntegration>(), new DeckDocument(ConfigStore.DefaultConfig()), new MemorySecrets());
        using var sonar = new SonarMixerProvider(manager);

        Assert.Equal(new[] { "master", "game", "chatRender", "media", "aux", "chatCapture" }, sonar.Channels.Select(c => c.Id));
        Assert.Equal("Micro", sonar.Describe("chatCapture").Label);
        Assert.Contains("pas le gain de la carte", sonar.Describe("chatCapture").Controls);
        Assert.Equal("xyz", sonar.Describe("xyz").Label);   // un canal inconnu reste affichable
        Assert.Null(sonar.Read("game", out var problem));   // connecteur absent : pas de faux zéro, un message
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void EveryPercentageSurvivesTheRoundTripThroughTheSonarApiScale()
    {
        for (int percent = 0; percent <= 100; percent++)
        {
            var written = SonarApi.FormatVolume(percent);
            Assert.True(double.TryParse(written, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var volume), written);
            Assert.InRange(volume, 0, 1);
            Assert.Equal(percent, new SonarLevel(volume, false).Percent);
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.01")]
    [InlineData(40, "0.4")]
    [InlineData(45, "0.45")]
    [InlineData(100, "1")]
    [InlineData(-5, "0")]
    [InlineData(180, "1")]
    public void TheVolumeSentToSonarIsAFractionBetweenZeroAndOneWithAPoint(int percent, string expected) =>
        Assert.Equal(expected, SonarApi.FormatVolume(percent));

    [Theory]
    [InlineData(-0.2, 0)]
    [InlineData(0.006, 1)]
    [InlineData(0.404, 40)]
    [InlineData(1.7, 100)]
    public void AVolumeReadFromSonarIsRoundedAndBoundedToAPercentage(double volume, int percent) =>
        Assert.Equal(percent, new SonarLevel(volume, null).Percent);

    [Theory]
    [InlineData("50", 10, 50)]
    [InlineData("+5", 98, 100)]
    [InlineData("-5", 3, 0)]
    [InlineData("75 %", 10, 75)]
    public void ARequestedLevelIsAbsoluteOrRelativeAndBounded(string text, int current, int expected) =>
        Assert.Equal(expected, SonarApi.ParseLevel(text, current));
}
