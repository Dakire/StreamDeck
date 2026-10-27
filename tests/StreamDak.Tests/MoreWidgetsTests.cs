using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using StreamDak.Actions;
using StreamDak.Platform.Windows;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Widgets ajoutés (batterie, durée de fonctionnement, périphériques audio, lecture en cours, flux), chronomètre, verrouillage et scripts observés.</summary>
public class MoreWidgetsTests
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    static WidgetContext Context(Dictionary<string, string>? options = null, Func<string, string?>? values = null, HttpClient? http = null) => new()
    {
        Options = options ?? new Dictionary<string, string>(), Culture = Fr, GetValue = values ?? (_ => null), Http = http ?? new HttpClient(),
        Now = () => new DateTime(2026, 10, 27, 9, 0, 0),
    };

    [Theory]
    [InlineData(45, "45 min")] [InlineData(200, "3 h 20")] [InlineData(3000, "2 j 2 h")]
    public void DurationsAreShort(int minutes, string expected) => Assert.Equal(expected, WidgetFormat.Duration(TimeSpan.FromMinutes(minutes), Fr));

    [Fact]
    public async Task TheBatteryWidgetSaysWhenThereIsNoBatteryAndWarnsWhenItIsLow()
    {
        Assert.Equal("Secteur", (await new BatteryWidget(() => new BatteryState(false, null, true, null)).ReadAsync(Context(), default)).Headline);
        var low = await new BatteryWidget(() => new BatteryState(true, 8, false, TimeSpan.FromMinutes(25))).ReadAsync(Context(), default);
        Assert.Equal(("8 %", WidgetLevel.Danger, 8d), (low.Headline, low.Level, low.Percent!.Value));
        Assert.Contains("25 min restantes", low.Detail);
        Assert.Equal(WidgetLevel.Good, (await new BatteryWidget(() => new BatteryState(true, 60, true, null)).ReadAsync(Context(), default)).Level);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BatteryWidget(() => null).ReadAsync(Context(), default));
        Assert.Null(Record.Exception(() => WindowsPlatform.ReadBattery()));   // la vraie lecture ne lève jamais (bureau : pas de batterie)
    }

    [Fact]
    public async Task UptimeShowsTheDurationAndTheBootDateAndWarnsAfterTwoWeeks()
    {
        var r = await new UptimeWidget(() => TimeSpan.FromHours(27)).ReadAsync(Context(), default);
        Assert.Equal("1 j 3 h", r.Headline);
        Assert.Contains("26 oct.", r.Detail);
        Assert.Equal(WidgetLevel.Warning, (await new UptimeWidget(() => TimeSpan.FromDays(20)).ReadAsync(Context(), default)).Level);
    }

    [Fact]
    public async Task TheAudioDevicesWidgetShowsTheDefaultOutputAndMicrophone()
    {
        var sim = new SimulatedPlatform();
        sim.SetEndpointMute("capture-default", true);
        var r = await new AudioDevicesWidget(sim).ReadAsync(Context(), default);
        Assert.Equal("Simulation", r.Headline);
        Assert.Contains("Sortie : Haut-parleurs (Simulation)", r.Detail);
        Assert.Contains("Micro : Microphone (Simulation) · coupé", r.Detail);
        Assert.Equal(WidgetLevel.Warning, r.Level);   // micro coupé : visible d'un coup d'œil
    }

    [Fact]
    public async Task NowPlayingReadsTheSystemIntegrationAndExplainsWhenItIsOff()
    {
        var widget = new NowPlayingWidget();
        Assert.Contains("Système Windows", (await Assert.ThrowsAsync<InvalidOperationException>(() => widget.ReadAsync(Context(), default))).Message);
        var values = new Dictionary<string, string> { ["sys.media.title"] = "Clair de lune", ["sys.media.artist"] = "Debussy", ["sys.media.app"] = "Spotify", ["sys.media.playing"] = "false" };
        var r = await widget.ReadAsync(Context(values: k => values.GetValueOrDefault(k)), default);
        Assert.Equal("Clair de lune", r.Headline);
        Assert.Equal("Debussy · en pause · Spotify", r.Detail);
    }

    const string Rss = "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>Le Journal</title><item><title>Premier</title><media:title xmlns:media=\"m\">x</media:title></item>"
                     + "<item><title><![CDATA[Deuxième  titre]]></title></item><item><title>Troisième</title></item></channel></rss>";
    const string Atom = "<feed xmlns=\"http://www.w3.org/2005/Atom\"><title>État du service</title><entry><title>Incident résolu</title></entry><entry><title>Maintenance</title></entry></feed>";

    [Fact]
    public void FeedsAreReadAsRssOrAtomAndADtdIsRefused()
    {
        var (title, items) = FeedWidget.Parse(Encoding.UTF8.GetBytes(Rss), 2);
        Assert.Equal("Le Journal", title);
        Assert.Equal(new[] { "Premier", "Deuxième titre" }, items);
        Assert.Equal(new[] { "Incident résolu", "Maintenance" }, FeedWidget.Parse(Encoding.UTF8.GetBytes(Atom), 5).Items);
        const string xxe = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><rss><channel><item><title>&x;</title></item></channel></rss>";
        Assert.Throws<InvalidOperationException>(() => FeedWidget.Parse(Encoding.UTF8.GetBytes(xxe), 3));
        Assert.Throws<InvalidOperationException>(() => FeedWidget.Parse(Encoding.UTF8.GetBytes("pas du xml"), 3));
    }

    [Fact]
    public async Task TheFeedWidgetFetchesTheFeedAndReportsAServerError()
    {
        using var server = new FakeHttpServer { Handler = r => r.PathAndQuery == "/rss" ? (200, Rss) : (503, "") };
        using var http = new HttpClient();
        var ok = await new FeedWidget().ReadAsync(Context(new() { ["url"] = $"http://localhost:{server.Port}/rss", ["count"] = "3" }, http: http), default);
        Assert.Equal("Premier", ok.Headline);
        Assert.Equal("Deuxième titre\nTroisième\n— Le Journal", ok.Detail);
        var down = await Assert.ThrowsAsync<InvalidOperationException>(() => new FeedWidget().ReadAsync(Context(new() { ["url"] = $"http://localhost:{server.Port}/hs" }, http: http), default));
        Assert.Contains("503", down.Message);
    }

    [Fact]
    public void ATimerOfZeroSecondsIsAStopwatchThatCountsUp()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var countdowns = new Countdowns(() => now);
        var button = new DeckButton();
        Assert.True(countdowns.ToggleStopwatch(button, out _));
        Assert.True(countdowns.Any);
        now = now.AddSeconds(83.7);
        Assert.Equal(TimeSpan.FromSeconds(83), countdowns.Remaining(button));
        Assert.Null(countdowns.Progress(button));
        countdowns.Tick();                                           // un chronomètre ne « finit » jamais
        Assert.False(countdowns.ToggleStopwatch(button, out var elapsed));
        Assert.Equal("1:23", Countdowns.Format(TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds))));
        Assert.False(countdowns.Any);
        Assert.Empty(ActionValidator.Validate(new DeckAction { Type = ActionTypes.Timer, Value = "0" }, ConfigStore.DefaultConfig(), null));
    }

    [Fact]
    public void LockingUsesLockWorkStationBecauseASimulatedWinLDoesNothing()
    {
        var step = ActionCatalog.Find("sys.lock")!.Actions.Single();
        Assert.Equal((ActionTypes.Launch, "rundll32.exe", "user32.dll,LockWorkStation"), (step.Type, step.Value, step.Args));
        Assert.All(new[] { "sys.sound", "sys.services", "dev.terminal", "dev.ssh", "dev.vscode", "auto.stopwatch" }, id => Assert.NotNull(ActionCatalog.Find(id)));
    }

    [Theory]
    [InlineData(true, 0, "ligne 1\nligne 2", "", "Script terminé : ligne 1")]
    [InlineData(false, 0, "", "", "Script lancé : il continue en arrière-plan")]
    public void AScriptResultBecomesTheTileMessage(bool finished, int code, string output, string error, string expected) =>
        Assert.Equal(expected, ScriptAction.Describe(new ScriptResult(finished, code, output, error)));

    [Fact]
    public void AFailedScriptShowsItsExitCodeAndLastErrorLine() =>
        Assert.Equal("Le script a échoué (code 2) : Accès refusé", Assert.Throws<InvalidOperationException>(() =>
            ScriptAction.Describe(new ScriptResult(true, 2, "début", "Avertissement\nAccès refusé"))).Message);

    [Fact]
    public void ARealPowerShellCommandIsObservedWithItsExitCode()
    {
        var platform = WindowsPlatform.Create().Launcher;
        var failed = platform.RunScriptObserved("[Console]::Error.WriteLine('panne disque'); exit 3", null, TimeSpan.FromSeconds(20));
        Assert.True(failed.Finished);
        Assert.Equal(3, failed.ExitCode);
        Assert.Contains("panne disque", failed.Error);
        var slow = platform.RunScriptObserved("Start-Sleep -Seconds 5", null, TimeSpan.FromMilliseconds(300));
        Assert.False(slow.Finished);                                   // il continue seul, l'action ne bloque pas
    }
}
