using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.Audio;
using StreamDak.Controls;
using StreamDak.Integrations;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Logique de la roue et des curseurs : angle et niveau, rotation continue, cibles, envoi du dernier niveau.</summary>
public class LevelLogicTests
{
    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, 0, 90)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 0, -90)]
    public void TheFingerAngleIsMeasuredFromTheTopClockwise(double dx, double dy, double expected) =>
        Assert.Equal(expected, WheelGesture.AngleOf(dx, dy), 6);

    [Fact]
    public void TheNeedleSweeps270DegreesAroundTheTop()
    {
        Assert.Equal(-135, WheelGesture.AngleOfLevel(0), 6);
        Assert.Equal(0, WheelGesture.AngleOfLevel(50), 6);
        Assert.Equal(135, WheelGesture.AngleOfLevel(100), 6);
        Assert.Equal(135, WheelGesture.AngleOfLevel(250), 6);   // borné
    }

    [Theory]
    [InlineData(170, -170, 20)]      // franchir le bas : +20°, pas −340°
    [InlineData(-170, 170, -20)]
    [InlineData(10, 40, 30)]
    [InlineData(-30, -60, -30)]
    public void AnAngleDifferenceNeverJumpsAcrossTheBottom(double from, double to, double expected) =>
        Assert.Equal(expected, WheelGesture.Delta(from, to), 6);

    [Fact]
    public void PuttingTheFingerDownDoesNotMoveTheWheelAndTurningFollowsContinuously()
    {
        var wheel = new WheelGesture();
        wheel.Begin(fingerAngle: 80, level: 40);
        Assert.Equal(40, wheel.Level, 6);                 // aucun saut au contact
        Assert.Equal(40, wheel.Move(80), 6);
        Assert.Equal(50, wheel.Move(80 + 27), 6);         // 27° = 10 % de la course de 270°
        Assert.Equal(40, wheel.Move(80), 6);              // et retour
        Assert.Equal(1, wheel.Move(80 - 105.3), 1);       // −105,3° = −39 %
    }

    [Fact]
    public void TurningAcrossTheBottomOfTheScreenKeepsTheLevelContinuous()
    {
        var wheel = new WheelGesture();
        wheel.Begin(fingerAngle: 175, level: 50);
        double before = wheel.Level;
        double after = wheel.Move(-175);                  // le doigt passe sous le centre : +10°, pas −350°
        Assert.Equal(before + 10.0 / 270 * 100, after, 6);
    }

    [Fact]
    public void AtTheStopsTheWheelIsBoundedAndReversingReactsAtOnceWithoutADeadZone()
    {
        var wheel = new WheelGesture();
        wheel.Begin(0, 90);
        wheel.Move(60);
        wheel.Move(120);                                  // bien au-delà de la butée
        Assert.Equal(100, wheel.Level, 6);
        double back = wheel.Move(117);                    // le doigt revient de 3° : la roue repart tout de suite
        Assert.Equal(100 - 3.0 / 270 * 100, back, 6);
        wheel.Move(-33);                                  // −150° d'un coup (moins de 180° : sans ambiguïté)
        wheel.Move(-170);                                 // −137° de plus : bien au-delà de la butée basse
        Assert.Equal(0, wheel.Level, 6);
        Assert.Equal(3.0 / 270 * 100, wheel.Move(-167), 6);
    }

    [Fact]
    public void ASlowCircleGivesEveryIntegerLevelWithoutSkippingOne()
    {
        var wheel = new WheelGesture();
        wheel.Begin(0, 0);
        var seen = new HashSet<int>();
        for (double a = 0; a <= 270; a += 0.5) seen.Add((int)Math.Round(wheel.Move(a)));
        Assert.Equal(Enumerable.Range(0, 101), seen.OrderBy(v => v));
    }

    [Theory]
    [InlineData(10, 100, true, 100)]     // vertical : le haut est le maximum
    [InlineData(110, 100, true, 0)]
    [InlineData(60, 100, true, 50)]
    [InlineData(-50, 100, true, 100)]    // borné
    [InlineData(300, 100, true, 0)]
    public void AVerticalSliderPutsTheMaximumAtTheTop(double y, double length, bool reversed, double expected) =>
        Assert.Equal(expected, SliderMath.LevelAt(y, 10, length, reversed), 6);

    [Fact]
    public void SliderPositionAndLevelAreInverseOfEachOther()
    {
        foreach (var reversed in new[] { false, true })
            for (int level = 0; level <= 100; level += 7)
                Assert.Equal(level, SliderMath.LevelAt(SliderMath.PositionOf(level, 20, 240, reversed), 20, 240, reversed), 6);
        Assert.Equal(0, SliderMath.LevelAt(5, 0, 0, false));   // piste de longueur nulle : aucune division
    }

    [Theory]
    [InlineData("sonar:game", true, LevelBackend.Sonar, "game")]
    [InlineData("SONAR: chatCapture ", true, LevelBackend.Sonar, "chatCapture")]
    [InlineData("windows:Focusrite", true, LevelBackend.Windows, "Focusrite")]
    [InlineData("windows:capture:Focusrite", true, LevelBackend.Windows, "capture:Focusrite")]
    [InlineData("", false, LevelBackend.Sonar, "")]
    [InlineData(null, false, LevelBackend.Sonar, "")]
    [InlineData("sonar:", false, LevelBackend.Sonar, "")]
    [InlineData("game", false, LevelBackend.Sonar, "")]
    [InlineData("midi:x", false, LevelBackend.Sonar, "")]
    public void TargetsAreReadFromTheOptionText(string? text, bool ok, LevelBackend backend, string name)
    {
        Assert.Equal(ok, LevelTarget.TryParse(text, out var target));
        if (ok) { Assert.Equal(backend, target.Backend); Assert.Equal(name, target.Name); Assert.Equal(target, LevelTarget.TryParse(target.ToText(), out var again) ? again : null); }
    }

    [Fact]
    public void ShapesHaveStableKeysAndTheWheelIsTheDefaultThatIsNeverWritten()
    {
        Assert.Equal(LevelShape.Wheel, LevelShapes.Parse(null));
        Assert.Equal(LevelShape.Wheel, LevelShapes.Parse("n'importe quoi"));
        Assert.Equal(LevelShape.VerticalSlider, LevelShapes.Parse("vertical"));
        Assert.Equal(LevelShape.HorizontalSlider, LevelShapes.Parse(" Horizontal "));
        Assert.Null(LevelShapes.KeyOf(LevelShape.Wheel));
        Assert.Equal(new[] { "vertical", "horizontal" }, new[] { LevelShape.VerticalSlider, LevelShape.HorizontalSlider }.Select(LevelShapes.KeyOf));
    }

    [Fact]
    public async Task OnlyTheLastRequestedLevelIsSentWhileASendIsInProgress()
    {
        var sent = new ConcurrentQueue<int>();
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var sender = new LevelSender(async level => { sent.Enqueue(level); started.TrySetResult(); await gate.Task; }, _ => Task.CompletedTask);

        sender.Post(10);
        await started.Task;
        for (int i = 11; i <= 40; i++) sender.Post(i);    // pendant l'envoi : seul le dernier compte
        sender.Post(250);                                 // borné
        Assert.True(sender.IsBusy);
        gate.SetResult();
        await Until(() => !sender.IsBusy);
        Assert.Equal(new[] { 10, 100 }, sent.ToArray());
    }

    [Fact]
    public async Task ASendFailureIsReportedAndTheNextLevelStillGoesOut()
    {
        var errors = new ConcurrentQueue<string>();
        var sent = new ConcurrentQueue<int>();
        int calls = 0;
        var sender = new LevelSender(level => { sent.Enqueue(level); if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("GG fermé"); return Task.CompletedTask; },
            _ => Task.CompletedTask, ex => errors.Enqueue(ex.Message));
        sender.Post(20);
        await Until(() => !sender.IsBusy && calls == 1);
        sender.Post(30);
        await Until(() => !sender.IsBusy && calls == 2);
        Assert.Equal(new[] { "GG fermé" }, errors.ToArray());
        Assert.Equal(new[] { 20, 30 }, sent.ToArray());
    }

    internal static async Task Until(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte"); await Task.Delay(10); }
    }
}

/// <summary>Sources de niveau (Windows par nom, Sonar par le gestionnaire d'intégrations) et tuile qui les affiche.</summary>
public class LevelSourceTests
{
    sealed class StubSonar : IntegrationBase
    {
        public ConcurrentQueue<string> Commands2 { get; } = new();
        public override string Id => "sonar";
        public override string Name => "Faux Sonar";
        public override string Category => "Sonar";
        public override string Help => "";
        public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();
        public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[] { new IntegrationCommand("volume", "Volume", ""), new IntegrationCommand("mute", "Couper", ""), new IntegrationCommand("unmute", "Rétablir", "") };
        protected override Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation) { SetValue("game.volume", "60"); SetValue("game.muted", "false"); return Task.CompletedTask; }
        protected override Task DisconnectCoreAsync() => Task.CompletedTask;
        public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation) { Commands2.Enqueue($"{command} {argument}"); return Task.FromResult<string?>(null); }
        public void Publish(string key, string value) => SetValue(key, value);
    }

    static SimulatedPlatform Sim()
    {
        var sim = new SimulatedPlatform();
        sim.AudioEndpoints.Clear();
        sim.AudioEndpoints.Add(new AudioEndpoint("c1", "Microphone (Focusrite USB Audio)", AudioFlow.Capture, true));
        sim.AudioEndpoints.Add(new AudioEndpoint("r1", "Speakers (Arctis Pro Wireless Game)", AudioFlow.Render, false));
        return sim;
    }

    static (IntegrationManager Manager, StubSonar Sonar, DeckDocument Doc) SonarManager(bool enabled = true)
    {
        var sonar = new StubSonar();
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        var manager = new IntegrationManager(new IIntegration[] { sonar }, doc, new MemorySecrets());
        if (enabled) doc.UpdateSettings(c => (c.Integrations ??= new())["sonar"] = new IntegrationConfig { Enabled = true });
        manager.Start();
        return (manager, sonar, doc);
    }

    [Fact]
    public async Task AWindowsDeviceIsFoundByNameReadAndSetWithoutTouchingTheDefault()
    {
        var sim = Sim();
        var source = new WindowsLevelSource(sim, AudioDeviceQuery.Parse("capture:focusrite"));
        var reading = source.Read(out var problem);
        Assert.Equal("", problem);
        Assert.Equal(new LevelReading(50, false), reading);
        Assert.Equal("Focusrite USB Audio", source.Title);

        await source.SetLevelAsync(72);
        await source.SetMutedAsync(true);
        Assert.Equal(new LevelReading(72, true), source.Read(out _));
        Assert.Equal(new AudioEndpointState(72, true), sim.ReadEndpoint("c1"));
        Assert.Equal(50, sim.ReadEndpoint("r1").Percent);                       // l'autre périphérique ne bouge pas
        Assert.DoesNotContain(sim.Log, l => l.StartsWith("volume ", StringComparison.Ordinal) || l.StartsWith("mute ", StringComparison.Ordinal));   // le défaut de Windows non plus
    }

    [Fact]
    public async Task AMissingDeviceGivesAReadableProblemAndIsFoundAgainWhenItComesBack()
    {
        var sim = Sim();
        var source = new WindowsLevelSource(sim, AudioDeviceQuery.Parse("Arctis"));
        Assert.NotNull(source.Read(out _));
        var arctis = sim.AudioEndpoints[1];
        sim.AudioEndpoints.Remove(arctis);                                       // débranché
        Assert.Null(source.Read(out var problem));
        Assert.Contains("Aucun périphérique audio", problem);
        sim.AudioEndpoints.Add(arctis with { Id = "r9" });                       // rebranché : nouvel identifiant
        Assert.NotNull(source.Read(out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WindowsLevelSource(sim, AudioDeviceQuery.Parse("Realtek")).SetLevelAsync(10));
    }

    [Fact]
    public async Task ASonarChannelIsReadFromTheIntegrationValuesAndSetThroughItsCommands()
    {
        var (manager, sonar, _) = SonarManager();
        using var mgr = manager;
        await LevelLogicTests.Until(() => sonar.State == ConnectionState.Connected);
        using var source = new SonarLevelSource(manager, "game");
        Assert.Equal(new LevelReading(60, false), source.Read(out _));
        Assert.Equal("Sonar · Jeu", source.Title);

        int changes = 0;
        source.Changed += () => Interlocked.Increment(ref changes);
        sonar.Publish("game.volume", "33");
        Assert.True(changes >= 1);
        Assert.Equal(33, source.Read(out _)!.Percent);

        await source.SetLevelAsync(80);
        await source.SetMutedAsync(true);
        await source.SetMutedAsync(false);
        Assert.Equal(new[] { "volume game=80", "mute game", "unmute game" }, sonar.Commands2.ToArray());

        Assert.Null(new SonarLevelSource(manager, "aux").Read(out var missing));
        Assert.Contains("n'expose pas le canal", missing);
    }

    [Fact]
    public async Task SonarAbsentOrDisabledGivesAnExplanationNotAZero()
    {
        var (manager, _, _) = SonarManager(enabled: false);
        using var mgr = manager;
        using var source = new SonarLevelSource(manager, "game");
        Assert.Null(source.Read(out var problem));
        Assert.Contains("Paramètres › Intégrations", problem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.SetLevelAsync(10));
    }

    [Fact]
    public void TheFactoryBuildsTheRightSourceOrNothing()
    {
        var (manager, _, _) = SonarManager(enabled: false);
        using var mgr = manager;
        var sim = Sim();
        Assert.IsType<SonarLevelSource>(LevelSources.Create("sonar:game", manager, sim));
        Assert.IsType<WindowsLevelSource>(LevelSources.Create("windows:capture:Focusrite", manager, sim));
        Assert.Null(LevelSources.Create("", manager, sim));
        Assert.Null(LevelSources.Create("windows:capture:", manager, sim));
        Assert.Null(LevelSources.Create("n'importe quoi", manager, sim));
    }

    // ---------------------------------------------------------------- tuile

    sealed class FakeSource : ILevelSource
    {
        public int Percent = 40;
        public bool Muted;
        public string? Problem;
        public ConcurrentQueue<int> Sent { get; } = new();
        public Exception? Failure;
        Action? _changed;
        public string Title => "Faux";
        public TimeSpan? PollInterval => null;
        public event Action? Changed { add => _changed += value; remove => _changed -= value; }
        public void Raise() => _changed?.Invoke();
        public LevelReading? Read(out string problem) { problem = Problem ?? ""; return Problem != null ? null : new LevelReading(Percent, Muted); }
        public Task SetLevelAsync(int percent) { if (Failure != null) throw Failure; Sent.Enqueue(percent); Percent = percent; return Task.CompletedTask; }
        public Task SetMutedAsync(bool muted) { if (Failure != null) throw Failure; Muted = muted; return Task.CompletedTask; }
    }

    static LevelWidgetViewModel Tile(FakeSource? source, Func<long>? clock = null) =>
        new(source, LevelShape.Wheel, a => a(), clock, _ => Task.CompletedTask) { Button = new DeckButton { Widget = WidgetTypes.Level }, AccessibleName = "Niveau audio" };

    [Fact]
    public void TheTileShowsTheRealLevelAndFollowsTheSourceWhenItChanges()
    {
        var source = new FakeSource();
        using var tile = Tile(source);
        tile.Start();
        Assert.Equal((40, false, true), (tile.Level, tile.IsMuted, tile.HasReading));
        source.Percent = 65; source.Muted = true; source.Raise();
        Assert.Equal((65, true), (tile.Level, tile.IsMuted));
        source.Problem = "Connexion à Sonar…"; source.Raise();
        Assert.Equal("Connexion à Sonar…", tile.Status);
        Assert.True(tile.HasStatus);
        Assert.False(tile.HasReading);
        source.Problem = null; source.Raise();
        Assert.True(tile.HasReading);
        Assert.Equal("Faux", tile.Title);
    }

    [Fact]
    public async Task ChangingTheLevelShowsItAtOnceSendsItAndIgnoresStaleReadingsForAMoment()
    {
        long now = 1000;
        var source = new FakeSource();
        using var tile = Tile(source, () => now);
        tile.Start();
        tile.ChangeCommand.Execute(70);
        Assert.Equal(70, tile.Level);                                           // immédiat
        await LevelLogicTests.Until(() => source.Sent.Contains(70));

        source.Percent = 40; source.Raise();                                    // relevé en retard : ignoré
        Assert.Equal(70, tile.Level);
        now += (long)LevelWidgetViewModel.HoldAfterChange.TotalMilliseconds + 1;
        source.Raise();                                                         // le système a le dernier mot
        Assert.Equal(40, tile.Level);

        tile.ChangeCommand.Execute(500);                                        // borné
        Assert.Equal(100, tile.Level);
    }

    [Fact]
    public async Task MutingTogglesAndAFailureRestoresTheDisplayAndIsReported()
    {
        var source = new FakeSource();
        using var tile = Tile(source);
        var errors = new List<string>();
        tile.ErrorReported = errors.Add;
        tile.Start();
        await tile.ToggleMuteCommand.ExecuteAsync(null);
        Assert.True(tile.IsMuted);
        Assert.True(source.Muted);
        await tile.ToggleMuteCommand.ExecuteAsync(null);
        Assert.False(tile.IsMuted);

        source.Failure = new InvalidOperationException("Sonar ne répond plus");
        await tile.ToggleMuteCommand.ExecuteAsync(null);
        Assert.False(tile.IsMuted);                                             // l'affichage revient à la réalité
        Assert.Equal(new[] { "Sonar ne répond plus" }, errors);

        tile.ChangeCommand.Execute(10);
        await LevelLogicTests.Until(() => errors.Count == 2);
        Assert.Equal("Sonar ne répond plus", errors[1]);
    }

    [Fact]
    public void ADisposedOrPausedTileReadsNothingAndWithoutATargetItSaysSo()
    {
        var source = new FakeSource();
        var tile = Tile(source);
        tile.Start();
        tile.SetPaused(true);
        source.Percent = 99; source.Raise();
        Assert.Equal(40, tile.Level);
        tile.SetPaused(false);                                                  // reprise : relit tout de suite
        Assert.Equal(99, tile.Level);
        tile.Dispose();
        source.Percent = 1; source.Raise();
        Assert.Equal(99, tile.Level);
        Assert.True(tile.IsStopped);

        using var empty = Tile(null);
        empty.Start();
        Assert.Contains("Choisis un canal Sonar", empty.Status);
        empty.ChangeCommand.Execute(10);                                        // sans cible : sans effet
    }
}

/// <summary>Le widget niveau audio dans le deck : rendu hors écran en thèmes sombre et clair, gestes à la roue et aux curseurs, éditeur.</summary>
[Collection("ui")]
public class DeckLevelWidgetTests
{
    readonly WpfHost _host;
    public DeckLevelWidgetTests(WpfHost host) => _host = host;

    static DeckButton Level(string source, string? shape, int cols, int rows) => new()
    {
        Label = "Niveau", Widget = WidgetTypes.Level, ColSpan = cols, RowSpan = rows, Color = "#C2410C",
        WidgetOptions = shape == null ? new() { ["source"] = source } : new() { ["source"] = source, ["shape"] = shape },
    };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 5, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons =
        {
            Level("windows:capture:Microphone", null, 2, 2), new DeckButton { Blank = true }, Level("windows:render:Haut-parleurs", "vertical", 1, 2), Level("windows:capture:Microphone", "horizontal", 2, 1), new DeckButton { Blank = true },
            new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true }, Level("sonar:game", "horizontal", 2, 1), new DeckButton { Blank = true },
        } } } } },
    };

    /// <summary>Aucune erreur de liaison, hors celles des onglets du dock (liaisons vers la fenêtre, absentes d'une fenêtre jamais affichée : sans rapport avec ce widget).</summary>
    void NoOwnBindingError()
    {
        lock (_host.Bindings.Messages)
        {
            var own = _host.Bindings.Messages.Where(m => !m.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal)).ToList();
            Assert.True(own.Count == 0, string.Join(" | ", own));
        }
    }

    /// <summary>Laisse finir les envois du scénario précédent (la roue envoie en tâche de fond) avant de fixer les niveaux de départ.</summary>
    async Task PrepareAsync(int mic, int speakers)
    {
        await Task.Delay(400);
        Sim.SetEndpointVolume("capture-default", mic);
        Sim.SetEndpointMute("capture-default", false);
        Sim.SetEndpointVolume("render-default", speakers);
        Sim.Log.Clear();
    }

    SimulatedPlatform Sim => (SimulatedPlatform)_host.Services.GetRequiredService<PlatformServices>().Audio;

    static async Task<bool> WaitAsync(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < limit) { if (condition()) return true; await Task.Delay(20); }
        return condition();
    }

    [Fact]
    public async Task TheWheelTurnsWithoutJumpingSendsTheLevelAndTheCenterMutes()
    {
        _host.Reset(Config());
        await PrepareAsync(35, 50);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
            _host.Settle();
            _host.Render(win, "deck-niveaux-audio-zero", 1600, 900);
            var vm = _host.Run(() => (MainViewModel)win.DataContext);
            var wheelTile = _host.Run(() => vm.Tiles.OfType<LevelWidgetViewModel>().First(t => t.IsWheel));
            Assert.True(await WaitAsync(() => _host.Run(() => wheelTile.Level == 35 && wheelTile.HasReading)), "niveau réel non affiché");

            var wheel = _host.Run(() => WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).First(c => c.Shape == LevelShape.Wheel));
            var (w, h) = _host.Run(() => (wheel.ActualWidth, wheel.ActualHeight));
            Assert.True(w >= 48 && h >= 48, $"roue de {w} × {h}");
            double cx = w / 2, cy = h / 2, ring = (Math.Min(w, h) / 2 - 6) * 0.9;
            Point At(double deg) => new(cx + ring * Math.Sin(deg * Math.PI / 180), cy - ring * Math.Cos(deg * Math.PI / 180));

            _host.Run(() =>
            {
                wheel.PointerDown(At(-60));                                       // le doigt se pose n'importe où sur l'anneau : pas de saut
                Assert.Equal(35, wheel.Value);
                wheel.PointerMove(At(-60 + 27));                                  // +27° = +10 %
                Assert.Equal(45, wheel.Value);
                wheel.PointerMove(At(-60 + 54));
                wheel.PointerUp(At(-60 + 54));
                Assert.Equal(55, wheel.Value);
                Assert.Equal(55, wheelTile.Level);
            });
            Assert.True(await WaitAsync(() => Sim.ReadEndpoint("capture-default").Percent == 55), "niveau non envoyé");

            _host.Run(() => { wheel.PointerDown(new Point(cx, cy)); wheel.PointerUp(new Point(cx, cy)); });   // toucher le centre
            Assert.True(await WaitAsync(() => Sim.ReadEndpoint("capture-default").Muted), "coupure non envoyée");
            Assert.True(_host.Run(() => wheelTile.IsMuted));
            _host.Render(win, "deck-niveaux-audio-coupe", 1600, 900);

            // Un geste qui bouge n'est pas un appui : le centre ne coupe pas
            _host.Run(() => { wheel.PointerDown(new Point(cx, cy)); wheel.PointerMove(new Point(cx + 30, cy)); wheel.PointerUp(new Point(cx + 30, cy)); });
            await Task.Delay(150);
            Assert.True(Sim.ReadEndpoint("capture-default").Muted);

            NoOwnBindingError();
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public async Task TheSlidersGrabTheHandleWithoutJumpingAndGoWhereTheFingerLandsElsewhere()
    {
        _host.Reset(Config());
        await PrepareAsync(40, 50);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
            _host.Settle();
            _host.Render(win, "deck-niveaux-audio-curseurs", 1600, 900);
            var vm = _host.Run(() => (MainViewModel)win.DataContext);
            Assert.True(await WaitAsync(() => _host.Run(() => vm.Tiles.OfType<LevelWidgetViewModel>().Count(t => t.HasReading) >= 3)), "lectures manquantes");

            var vertical = _host.Run(() => WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).First(c => c.Shape == LevelShape.VerticalSlider));
            var (w, h) = _host.Run(() => (vertical.ActualWidth, vertical.ActualHeight));
            Assert.True(w >= 48 && h >= 48, $"curseur de {w} × {h}");
            _host.Run(() =>
            {
                // Piste verticale : le haut est le maximum. On se pose bas (loin de la poignée) : la poignée va sous le doigt.
                vertical.PointerDown(new Point(w / 2, h - 20));
                Assert.True(vertical.Value < 10, $"{vertical.Value}");
                vertical.PointerMove(new Point(w / 2, 20));
                Assert.True(vertical.Value > 90, $"{vertical.Value}");
                vertical.PointerUp(new Point(w / 2, 20));
            });
            Assert.True(await WaitAsync(() => Sim.ReadEndpoint("render-default").Percent == (int)_host.Run(() => vertical.Value)), "niveau du curseur non envoyé");

            var horizontal = _host.Run(() => WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).First(c => c.Shape == LevelShape.HorizontalSlider && c.Value > 0));
            double before = _host.Run(() => horizontal.Value);
            var hw = _host.Run(() => horizontal.ActualWidth);
            _host.Run(() =>
            {
                // Poignée saisie là où elle est : aucun saut, puis un petit déplacement
                const double margin = 18;
                double left = Math.Min(hw * 0.35, 64) + margin, right = hw - margin;
                double handle = left + before / 100 * (right - left);
                horizontal.PointerDown(new Point(handle + 6, horizontal.ActualHeight / 2));
                Assert.InRange(horizontal.Value, before - 0.5, before + 0.5);
                horizontal.PointerUp(new Point(handle + 6, horizontal.ActualHeight / 2));
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public async Task EveryShapeRendersInBothThemesWithoutBindingErrorsAndKeyboardAndAutomationWork()
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.Reset(Config());
            _host.SetTheme(theme);
            await PrepareAsync(62, 50);
            var win = _host.Run(() => new MainWindow(_host.Services));
            try
            {
                _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
                _host.Settle();
                var vm = _host.Run(() => (MainViewModel)win.DataContext);
                Assert.True(await WaitAsync(() => _host.Run(() => vm.Tiles.OfType<LevelWidgetViewModel>().Count(t => t.HasReading) >= 3)), "lectures manquantes");
                var sonar = _host.Run(() => vm.Tiles.OfType<LevelWidgetViewModel>().Single(t => t.Title == "Sonar · Jeu"));
                Assert.True(_host.Run(() => sonar.HasStatus));                  // Sonar éteint : un message, pas un faux zéro
                _host.Render(win, $"deck-niveaux-audio-{theme}", 1600, 900);

                _host.Run(() =>
                {
                    var controls = WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).ToList();
                    Assert.Equal(4, controls.Count);
                    Assert.All(controls.Where(c => c.Visibility == Visibility.Visible), c => Assert.True(c.ActualWidth >= 48 && c.ActualHeight >= 48, $"{c.Shape} {c.ActualWidth} × {c.ActualHeight}"));
                    Assert.All(controls, c => Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(c))));

                    var wheel = controls.First(c => c.Shape == LevelShape.Wheel);
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(wheel);
                    var range = (System.Windows.Automation.Provider.IRangeValueProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)!;
                    Assert.True(range.Value == 62, $"valeur {range.Value} ; tuiles : " + string.Join(" / ", vm.Tiles.OfType<LevelWidgetViewModel>().Select(t => $"{t.Title} {t.Level} '{t.Status}'")) + " ; contrôles : " + string.Join(" / ", controls.Select(c => $"{c.Shape} {c.Value} {c.Visibility} {c.ActualWidth}")));
                    Assert.Equal((0, 100), (range.Minimum, range.Maximum));
                    range.SetValue(40);
                    Assert.Equal(40, wheel.Value);
                });
                // Le clavier agit comme les flèches d'un curseur
                _host.Run(() =>
                {
                    var wheel = WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).First(c => c.Shape == LevelShape.Wheel);
                    Assert.Equal(40, wheel.Value);
                    Assert.True(wheel.HandleKey(System.Windows.Input.Key.Right));
                    Assert.Equal(41, wheel.Value);
                    Assert.True(wheel.HandleKey(System.Windows.Input.Key.PageDown));
                    Assert.Equal(31, wheel.Value);
                    Assert.True(wheel.HandleKey(System.Windows.Input.Key.End));
                    Assert.Equal(100, wheel.Value);
                    Assert.True(wheel.HandleKey(System.Windows.Input.Key.Home));
                    Assert.False(wheel.HandleKey(System.Windows.Input.Key.A));
                    Assert.Equal(0, wheel.Value);
                });
                NoOwnBindingError();
            }
            finally { _host.Run(win.Close); }
        }
    }

    static readonly Place Root = new("P", 0);

    [Fact]
    public void TheEditorPicksTheTargetAndTheShapeAndWritesOnlyWhatIsNotTheDefault()
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
            Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons =
            {
                new DeckButton { Label = "Niveau", Widget = WidgetTypes.Level, ColSpan = 2, RowSpan = 2, Color = "#C2410C" }, new DeckButton { Blank = true },
            } } } } },
        };
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            Assert.True(props.IsWidget);
            Assert.True(props.IsLevelWidget);
            Assert.Equal(new[] { "1 × 1", "2 × 2", "3 × 3", "1 × 2", "1 × 3", "2 × 1", "3 × 1" }, props.SizeChoices.Select(c => c.Text));

            props.LevelTargetChoices.First(c => c.Text == "Sonar : Jeu").Command.Execute(null);
            Assert.Equal("sonar:game", _host.Document.Get(Root, 0)!.Option(LevelOptions.Source));
            Assert.True(props.LevelTargetChoices.Single(c => c.IsSelected).Text == "Sonar : Jeu");
            Assert.False(props.ShowLevelDevice);

            props.LevelShapeChoices.First(c => c.Text == "Curseur vertical").Command.Execute(null);
            Assert.Equal("vertical", _host.Document.Get(Root, 0)!.Option(LevelOptions.Shape));
            props.LevelShapeChoices.First(c => c.Text == "Roue").Command.Execute(null);
            Assert.Equal("", _host.Document.Get(Root, 0)!.Option(LevelOptions.Shape));                // la roue est le défaut : rien d'écrit

            props.LevelTargetChoices.Last().Command.Execute(null);                                    // périphérique Windows
            Assert.True(props.ShowLevelDevice);
            Assert.NotEmpty(props.LevelDeviceChoices);                                                // les périphériques détectés
            props.LevelDevice = "capture:Microphone";
            props.Flush();
            Assert.Equal("windows:capture:Microphone", _host.Document.Get(Root, 0)!.Option(LevelOptions.Source));
            Assert.True(props.ShowLevelDevice);
            Assert.True(props.LevelTargetChoices.Single(c => c.IsSelected).Text == "Périphérique Windows");

            props.LevelDeviceChoices.First().Command.Execute(null);                                   // une pastille de périphérique remplit le nom
            Assert.StartsWith("windows:", _host.Document.Get(Root, 0)!.Option(LevelOptions.Source), StringComparison.Ordinal);
            Assert.True(_host.Document.Undo());
        });
    }

    [Fact]
    public void OldFilesStayReadableAndALevelWidgetRoundTripsWithoutNewTopLevelKeys()
    {
        // Un fichier sans widget niveau se relit tel quel
        var old = ConfigStore.Parse("""{"Version":2,"Profiles":[{"Name":"P","Columns":3,"Rows":2,"Pages":[{"Name":"A","Buttons":[{"Label":"Un","Actions":[{"Type":"audio","Value":"mic-toggle"}]}]}]}]}""");
        Assert.Equal("audio", old.Profiles[0].Pages[0].Buttons[0].Actions[0].Type);

        // Un widget niveau : seuls Widget et WidgetOptions (déjà dans le format) portent ses réglages
        var json = """{"Version":2,"Profiles":[{"Name":"P","Columns":3,"Rows":2,"Pages":[{"Name":"A","Buttons":[{"Label":"Jeu","Widget":"level","ColSpan":2,"RowSpan":2,"WidgetOptions":{"source":"sonar:game","shape":"vertical"}},{"Blank":true},{"Blank":true},{"Blank":true},{"Blank":true},{"Blank":true}]}]}]}""";
        var cfg = ConfigStore.Parse(json);
        var button = cfg.Profiles[0].Pages[0].Buttons[0];
        Assert.Equal(WidgetTypes.Level, button.Widget);
        Assert.Equal("sonar:game", button.Option(LevelOptions.Source));
        Assert.Equal(LevelShape.VerticalSlider, LevelShapes.Parse(button.Option(LevelOptions.Shape)));
        var again = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.Equal(button.WidgetOptions, again.Profiles[0].Pages[0].Buttons[0].WidgetOptions);

        // Une forme inconnue retombe sur la roue, une cible illisible est ignorée : jamais d'exception
        var odd = ConfigStore.Parse(json.Replace("vertical", "triangle", StringComparison.Ordinal).Replace("sonar:game", "???", StringComparison.Ordinal)).Profiles[0].Pages[0].Buttons[0];
        Assert.Equal(LevelShape.Wheel, LevelShapes.Parse(odd.Option(LevelOptions.Shape)));
        Assert.False(LevelTarget.TryParse(odd.Option(LevelOptions.Source), out _));
    }

    [Fact]
    public void TheLevelWidgetIsInTheCatalogWithAValidIconAndNoAction()
    {
        var info = WidgetTypes.Find("LEVEL")!;
        Assert.Equal(WidgetTypes.Level, info.Id);
        Assert.False(info.IsData);
        Assert.True(Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(info.Icon, out _));
        Assert.Empty(ActionCatalog.Find("widget." + WidgetTypes.Level)!.CreateButton().Actions);
    }
}
