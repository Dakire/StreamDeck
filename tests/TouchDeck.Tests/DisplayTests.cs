using System;
using System.Collections.Generic;
using System.Linq;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Écrans simulés : branchement, débranchement et renumérotation sans écran réel.</summary>
sealed class FakeDisplays : IDisplayService
{
    public List<DisplayInfo> Screens { get; } = new();
    public event Action? Changed;
    public IReadOnlyList<DisplayInfo> All() => Screens.Select((d, i) => d with { Index = i }).ToList();
    public void Raise() => Changed?.Invoke();

    public static DisplayInfo Screen(string device, int x, int w, int h, bool primary = false) =>
        new(0, device, new PixelRect(x, 0, w, h), new PixelRect(x, 0, w, h - 48), primary);
}

[Collection("ui")]
public class DisplayTests
{
    static readonly DisplayInfo Main = FakeDisplays.Screen(@"\\.\DISPLAY1", 0, 2560, 1440, primary: true);
    static readonly DisplayInfo Touch = FakeDisplays.Screen(@"\\.\DISPLAY2", 2560, 1920, 1080);
    static readonly DisplayInfo Portrait = FakeDisplays.Screen(@"\\.\DISPLAY3", 4480, 1080, 1920);

    static List<DisplayInfo> List(params DisplayInfo[] d) => d.Select((x, i) => x with { Index = i }).ToList();

    [Fact]
    public void RememberedDeviceWinsOverAChangedIndex()
    {
        // L'écran tactile était n° 2 ; après rebranchement Windows le place en 3e position
        var after = List(Main, Portrait, Touch);
        Assert.Equal(Touch.Device, DisplayResolver.Resolve(after, 1, Touch.Device)!.Device);
        Assert.Equal(Portrait.Device, DisplayResolver.Resolve(after, 1, null)!.Device);   // ancien réglage : index seul
    }

    [Fact]
    public void FallsBackToIndexThenAutomaticChoice()
    {
        var two = List(Main, Touch);
        Assert.Equal(Touch.Device, DisplayResolver.Resolve(two, 1, @"\\.\DISPLAY9")!.Device);
        Assert.Null(DisplayResolver.Resolve(two, 5, null));
        Assert.Equal(Touch.Device, DisplayResolver.Resolve(two, -1, null)!.Device);
        Assert.Null(DisplayResolver.Resolve(List(Main), -1, null));
        Assert.Null(DisplayResolver.Resolve(List(Main), 1, Touch.Device));          // débranché
    }

    [Fact]
    public void DescriptionsTellWhatHappens()
    {
        Assert.Contains("automatique", DisplayResolver.Describe(List(Main, Touch), -1, null).Text);
        var (text, ok) = DisplayResolver.Describe(List(Main), 1, Touch.Device);
        Assert.False(ok);
        Assert.Contains("absent", text);
        Assert.Contains("remplace", DisplayResolver.Describe(List(Main, Portrait), 1, Touch.Device).Text);
        Assert.Contains("portrait", Portrait.Resolution);
    }

    [Fact]
    public void ScreenChoicesFollowTheDeviceAcrossReconnection()
    {
        var fake = new FakeDisplays();
        fake.Screens.AddRange(new[] { Main, Touch });
        using var displays = new DisplayService(fake);   // la source est injectée : aucun écran réel
        int changes = 0;
        displays.Changed += () => changes++;

        var cfg = new DeckConfig();
        var (choices, selected) = ScreenChoice.Build(cfg, displays.All(), "Auto");
        Assert.Equal(3, choices.Count);
        Assert.Equal(-1, selected.Monitor);

        choices[2].ApplyTo(cfg);
        Assert.Equal((1, Touch.Device), (cfg.Monitor, cfg.MonitorDevice));

        // Renumérotation : le même écran est retrouvé et resélectionné
        fake.Screens.Insert(1, Portrait);
        fake.Raise();
        Assert.Equal(1, changes);
        (_, selected) = ScreenChoice.Build(cfg, displays.All(), "Auto");
        Assert.Equal(Touch.Device, selected.Device);
        Assert.Equal(Touch.Device, displays.ResolveTarget(cfg)!.Device);

        // Débranché : choix « absent » affiché, deck sans cible
        fake.Screens.RemoveAll(d => d.Device == Touch.Device);
        fake.Screens.Remove(Portrait);
        (choices, selected) = ScreenChoice.Build(cfg, displays.All(), "Auto");
        Assert.Contains("absent", selected.Text);
        Assert.Null(displays.ResolveTarget(cfg));
    }

    [Fact]
    public void ADisposedDisplayServiceStopsFollowingItsSource()
    {
        var fake = new FakeDisplays();
        var displays = new DisplayService(fake);
        int changes = 0;
        displays.Changed += () => changes++;
        fake.Raise();
        displays.Dispose();
        fake.Raise();
        Assert.Equal(1, changes);
    }
    [Fact]
    public void MonitorDeviceIsOptionalInConfigJson()
    {
        var cfg = ConfigStore.DefaultConfig();
        Assert.DoesNotContain("MonitorDevice", ConfigStore.Serialize(cfg));
        cfg.MonitorDevice = Touch.Device;
        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.Equal(Touch.Device, back.MonitorDevice);
        Assert.Null(ConfigStore.Parse("""{ "Version": 2, "MonitorDevice": "  ", "Profiles": [] }""").MonitorDevice);
    }
}
