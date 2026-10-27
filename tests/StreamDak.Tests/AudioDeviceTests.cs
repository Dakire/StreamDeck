using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using StreamDak.Actions;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Audio par périphérique choisi par nom : correspondance, commandes, validation, sur la plateforme simulée (le défaut de Windows n'est jamais touché).</summary>
public class AudioDeviceTests
{
    readonly SimulatedPlatform _sim = new();

    public AudioDeviceTests()
    {
        _sim.AudioEndpoints.Clear();
        _sim.AudioEndpoints.AddRange(new[]
        {
            new AudioEndpoint("r1", "Haut-parleurs (Focusrite USB Audio)", AudioFlow.Render, true),
            new AudioEndpoint("r2", "Headset Earphone (SteelSeries Sonar - Gaming)", AudioFlow.Render, false),
            new AudioEndpoint("r3", "Arctis Chat (SteelSeries Sonar - Chat)", AudioFlow.Render, false),
            new AudioEndpoint("c1", "Microphone (Focusrite USB Audio)", AudioFlow.Capture, true),
            new AudioEndpoint("c2", "Arctis Chat (SteelSeries Sonar Virtual Audio Device)", AudioFlow.Capture, false),
        });
    }

    ActionEngine Engine() => new(new ActionContext { Platform = _sim.Services, Http = new HttpClient(), Delay = (_, _) => Task.CompletedTask });

    static DeckButton Step(string value, string? args) =>
        new() { Label = "T", Actions = new List<DeckAction> { new() { Type = ActionTypes.AudioDevice, Value = value, Args = args } } };

    [Theory]
    [InlineData("capture:focusrite", "c1")]
    [InlineData("CAPTURE:FOCUSRITE", "c1")]
    [InlineData("entrée:Focusrite", "c1")]
    [InlineData("render:focusrite", "r1")]
    [InlineData("gaming", "r2")]                          // sous-chaîne, casse ignorée, un seul périphérique de ce nom
    [InlineData("render:Arctis", "r3")]
    [InlineData("capture:arctis chat", "c2")]
    [InlineData("c1", "c1")]                              // identifiant du système
    public void NamesMatchBySubstringIgnoringCaseAndFlow(string query, string expected) =>
        Assert.Equal(expected, AudioDeviceQuery.Parse(query).Resolve(_sim.Endpoints()).Id);

    [Fact]
    public void AmbiguousOrUnknownNamesAreExplainedNotGuessed()
    {
        var ambiguous = Assert.Throws<InvalidOperationException>(() => AudioDeviceQuery.Parse("Focusrite").Resolve(_sim.Endpoints()));
        Assert.Contains("Plusieurs périphériques", ambiguous.Message);
        Assert.Contains("Haut-parleurs (Focusrite USB Audio)", ambiguous.Message);
        Assert.Contains("Plusieurs", Assert.Throws<InvalidOperationException>(() => AudioDeviceQuery.Parse("Arctis Chat").Resolve(_sim.Endpoints())).Message);
        Assert.Contains("Aucun périphérique", Assert.Throws<InvalidOperationException>(() => AudioDeviceQuery.Parse("Realtek").Resolve(_sim.Endpoints())).Message);
        Assert.Throws<ArgumentException>(() => AudioDeviceQuery.Parse("  "));
        Assert.Throws<ArgumentException>(() => AudioDeviceQuery.Parse("capture:"));
    }

    [Fact]
    public void AnExactNameWinsOverASubstring()
    {
        var list = new[] { new AudioEndpoint("a", "Micro", AudioFlow.Capture, false), new AudioEndpoint("b", "Micro USB", AudioFlow.Capture, false) };
        Assert.Equal("a", AudioDeviceQuery.Parse("micro").Resolve(list).Id);
        Assert.Equal("b", AudioDeviceQuery.Parse("micro usb").Resolve(list).Id);
    }

    [Fact]
    public async Task VolumeMuteToggleAndListWorkOnTheChosenDeviceOnly()
    {
        var engine = Engine();
        var nav = new Nav();

        var r = await engine.RunAsync(Step("volume", "capture:Focusrite=35"), nav);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("Focusrite USB Audio : 35 %", r.Message);
        Assert.Equal(35, _sim.ReadEndpoint("c1").Percent);
        Assert.Equal(50, _sim.ReadEndpoint("r1").Percent);   // l'autre périphérique Focusrite n'est pas touché

        Assert.Equal("SteelSeries Sonar - Chat : 55 %", (await engine.RunAsync(Step("volume", "render:Arctis=+5"), nav)).Message);   // relatif au niveau actuel (50)
        Assert.Equal(0, (await RunVolume(engine, nav, "render:Arctis=-100")));

        Assert.Equal("Focusrite USB Audio coupé", (await engine.RunAsync(Step("toggle", "capture:Focusrite"), nav)).Message);
        Assert.True(_sim.ReadEndpoint("c1").Muted);
        Assert.Equal("Focusrite USB Audio rétabli", (await engine.RunAsync(Step("toggle", "capture:Focusrite"), nav)).Message);
        Assert.False(_sim.ReadEndpoint("c1").Muted);
        await engine.RunAsync(Step("mute", "gaming"), nav);
        Assert.True(_sim.ReadEndpoint("r2").Muted);
        await engine.RunAsync(Step("unmute", "gaming"), nav);
        Assert.False(_sim.ReadEndpoint("r2").Muted);

        var list = await engine.RunAsync(Step("list", null), nav);
        Assert.Contains("Entrée : Microphone (Focusrite USB Audio)", list.Message);
        Assert.Contains("Sortie : Headset Earphone", list.Message);

        // Le défaut de Windows (micro / sortie par défaut) n'a jamais été changé
        Assert.DoesNotContain(_sim.Log, l => l.StartsWith("mute Output", StringComparison.Ordinal) || l.StartsWith("mute Microphone", StringComparison.Ordinal) || l.StartsWith("volume ", StringComparison.Ordinal));
    }

    static async Task<int> RunVolume(ActionEngine engine, INavigator nav, string args)
    {
        var r = await engine.RunAsync(Step("volume", args), nav);
        return r.Ok ? int.Parse(r.Message!.Split(':')[^1].Trim().TrimEnd('%').Trim(), System.Globalization.CultureInfo.InvariantCulture) : -1;
    }

    [Fact]
    public async Task ErrorsAreReportedAsAFailedStepNotAnException()
    {
        var engine = Engine();
        var nav = new Nav();
        Assert.False((await engine.RunAsync(Step("volume", "Realtek=50"), nav)).Ok);
        Assert.False((await engine.RunAsync(Step("volume", "Focusrite"), nav)).Ok);
        Assert.False((await engine.RunAsync(Step("volume", "gaming=abc"), nav)).Ok);
        Assert.False((await engine.RunAsync(Step("toggle", ""), nav)).Ok);
        Assert.False((await engine.RunAsync(Step("sing", "gaming"), nav)).Ok);
        Assert.Empty(_sim.Log);
    }

    [Theory]
    [InlineData("toggle", "capture:Focusrite", 0)]
    [InlineData("toggle", "", 1)]
    [InlineData("toggle", null, 1)]
    [InlineData("volume", "Focusrite=50", 0)]
    [InlineData("volume", "Focusrite", 1)]
    [InlineData("volume", "Focusrite=beaucoup", 1)]
    [InlineData("volume", "Focusrite=+5", 0)]
    [InlineData("list", null, 0)]
    [InlineData("shout", "x", 1)]
    public void TheValidatorChecksTheCommandAndTheArguments(string value, string? args, int errors)
    {
        var step = new DeckAction { Type = ActionTypes.AudioDevice, Value = value, Args = args };
        var issues = ActionValidator.Validate(step, new DeckConfig(), null);
        Assert.Equal(errors, issues.Count(i => i.Severity == Severity.Error));
    }

    [Fact]
    public void TheNewTypeIsRegisteredAndOldTypesAreUnchanged()
    {
        Assert.Contains(BuiltInActions.All, h => h.Type == ActionTypes.AudioDevice);
        Assert.Equal("audiodevice", ActionTypes.AudioDevice);
        Assert.Contains(ActionTypes.Audio, ActionTypes.All);
        Assert.Contains(ActionCatalog.All, c => c.Actions.Any(a => a.Type == ActionTypes.AudioDevice));
    }

    [Fact]
    public void LevelSyntaxIsAbsoluteOrRelativeAndBounded()
    {
        Assert.True(AudioDeviceSyntax.TryParseLevel("40 %", 10, out var a)); Assert.Equal(40, a);
        Assert.True(AudioDeviceSyntax.TryParseLevel("+5", 98, out var b)); Assert.Equal(100, b);
        Assert.True(AudioDeviceSyntax.TryParseLevel("-5", 2, out var c)); Assert.Equal(0, c);
        Assert.False(AudioDeviceSyntax.TryParseLevel("x", 2, out _));
        var (device, level) = AudioDeviceSyntax.SplitLevel("capture:Focusrite = +5");
        Assert.Equal((AudioFlow.Capture, "Focusrite", "+5"), (device.Flow, device.Text, level));
    }

    sealed class Nav : INavigator
    {
        public void GoToPage(string name) { }
        public void SwitchProfile(string name) { }
    }

    [Fact]
    public void TheRealWindowsServiceEnumeratesAndReadsDevicesWithoutChangingAnything()
    {
        // Lecture seule sur le vrai Core Audio : vérifie les déclarations COM (énumération, nom convivial, niveau). Sans périphérique actif, rien à lire.
        var audio = StreamDak.Platform.Windows.WindowsPlatform.Create().Audio;
        var endpoints = audio.Endpoints();
        Assert.All(endpoints, e => { Assert.False(string.IsNullOrWhiteSpace(e.Id)); Assert.False(string.IsNullOrWhiteSpace(e.Name)); Assert.NotEqual(e.Id, e.Name); });   // le nom convivial, pas l'identifiant
        foreach (var e in endpoints.Take(3))
        {
            var state = audio.ReadEndpoint(e.Id);
            Assert.InRange(state.Percent, 0, 100);
        }
        Assert.Throws<InvalidOperationException>(() => audio.ReadEndpoint("{0.0.0.00000000}.{inexistant}"));
    }
}
