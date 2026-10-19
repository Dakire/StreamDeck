using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Actions;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Moteur d'actions sur une plateforme simulée : rien n'est envoyé au système, aucune attente réelle sauf mention.</summary>
public class ActionEngineTests
{
    sealed class Nav : INavigator
    {
        public List<string> Calls { get; } = new();
        public void GoToPage(string name) => Calls.Add("page " + name);
        public void SwitchProfile(string name) => Calls.Add("profile " + name);
    }

    /// <summary>Serveur HTTP factice : enregistre les requêtes et répond avec le code demandé.</summary>
    sealed class FakeHttp : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<(HttpMethod Method, string Url, string? Body, string? Type)> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Requests.Add((r.Method, r.RequestUri!.ToString(), body, r.Content?.Headers.ContentType?.MediaType));
            return new HttpResponseMessage(Status);
        }
    }

    readonly SimulatedPlatform _sim = new();
    readonly Nav _nav = new();
    readonly FakeHttp _http = new();
    readonly List<TimeSpan> _delays = new();
    DateTime _now = new(2026, 10, 19, 9, 30, 0, DateTimeKind.Utc);

    ActionEngine Engine(IEnumerable<IActionHandler>? handlers = null, bool realDelay = false) => new(new ActionContext
    {
        Platform = _sim.Services,
        Http = new HttpClient(_http),
        Countdowns = new Countdowns(() => _now),
        Delay = realDelay ? Task.Delay : (t, ct) => { _delays.Add(t); return Task.CompletedTask; },
    }, handlers);

    static DeckButton Button(params (string Type, string Value, string? Args)[] steps) => new()
    {
        Label = "Test",
        Actions = steps.Select(s => new DeckAction { Type = s.Type, Value = s.Value, Args = s.Args }).ToList(),
    };

    [Fact]
    public async Task StepsRunInOrder()
    {
        var b = Button(("hotkey", "ctrl+c", null), ("delay", "250", null), ("text", "bonjour", null), ("launch", "notepad.exe", "a.txt"), ("media", "playpause", null));
        var r = await Engine().RunAsync(b, _nav);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(new[] { "hotkey ctrl+c", "text bonjour", "open notepad.exe a.txt", "hotkey playpause" }, _sim.Log);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250) }, _delays);
    }

    [Fact]
    public async Task FirstFailureStopsTheSequenceAndNamesTheStep()
    {
        var b = Button(("hotkey", "ctrl+c", null), ("hotkey", "", null), ("text", "jamais", null));
        var r = await Engine().RunAsync(b, _nav);
        Assert.Equal(ActionOutcome.Failed, r.Outcome);
        Assert.Equal(1, r.Step);
        Assert.Equal(new[] { "hotkey ctrl+c" }, _sim.Log);
    }

    [Fact]
    public async Task UnknownTypeAndBadMediaKeyFail()
    {
        var r = await Engine().RunAsync(Button(("teleport", "x", null)), _nav);
        Assert.Equal(ActionOutcome.Failed, r.Outcome);
        Assert.Contains("teleport", r.Message);
        Assert.False((await Engine().RunAsync(Button(("media", "eject", null)), _nav)).Ok);
        Assert.Empty(_sim.Log);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("data:text/html,<b>x</b>")]
    public async Task DangerousUrlSchemesAreNeverOpened(string url)
    {
        var r = await Engine().RunAsync(Button(("url", url, null)), _nav);
        Assert.Equal(ActionOutcome.Failed, r.Outcome);
        Assert.Empty(_sim.Log);
    }

    [Fact]
    public async Task UrlIsOpenedTrimmed()
    {
        Assert.True((await Engine().RunAsync(Button(("url", "  https://example.org  ", null)), _nav)).Ok);
        Assert.Equal(new[] { "open https://example.org" }, _sim.Log);
    }

    [Fact]
    public async Task NavigationGoesThroughTheNavigator()
    {
        var r = await Engine().RunAsync(Button(("page", "Outils", null), ("profile", "Jeux", null)), _nav);
        Assert.True(r.Ok);
        Assert.Equal(new[] { "page Outils", "profile Jeux" }, _nav.Calls);
    }

    [Fact]
    public async Task FocusLaunchesTheProgramWhenTheWindowIsMissing()
    {
        Assert.True((await Engine().RunAsync(Button(("focus", "chrome", "chrome.exe")), _nav)).Ok);
        Assert.Equal(new[] { "focus-missing chrome", "open chrome.exe", "focus-missing chrome" }, _sim.Log);

        _sim.Log.Clear();
        _sim.OpenWindows.Add("notepad");
        Assert.True((await Engine().RunAsync(Button(("focus", "notepad", null)), _nav)).Ok);
        Assert.Equal(new[] { "focus notepad" }, _sim.Log);

        var missing = await Engine().RunAsync(Button(("focus", "absent", null)), _nav);
        Assert.Equal(ActionOutcome.Failed, missing.Outcome);
    }

    sealed class SlowHandler : IActionHandler
    {
        public string Type => "slow";
        public TimeSpan? Timeout => TimeSpan.FromMilliseconds(50);
        public async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken ct)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return null;
        }
    }

    [Fact]
    public async Task StepsHaveATimeout()
    {
        var engine = Engine(BuiltInActions.All.Append(new SlowHandler()));
        var r = await engine.RunAsync(Button(("slow", "", null), ("text", "jamais", null)), _nav);
        Assert.Equal(ActionOutcome.TimedOut, r.Outcome);
        Assert.Equal(0, r.Step);
        Assert.Empty(_sim.Log);
    }

    [Fact]
    public async Task CancelAllStopsRunningSequencesButNotLaterOnes()
    {
        using var engine = Engine(realDelay: true);
        var running = engine.RunAsync(Button(("delay", "60000", null), ("text", "jamais", null)), _nav);
        await Task.Delay(50);
        engine.CancelAll();
        var r = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ActionOutcome.Cancelled, r.Outcome);
        Assert.Empty(_sim.Log);

        Assert.True((await engine.RunAsync(Button(("text", "ensuite", null)), _nav)).Ok);
        Assert.Equal(new[] { "text ensuite" }, _sim.Log);
    }

    [Fact]
    public async Task CallerCancellationIsReported()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await Engine().RunAsync(Button(("text", "x", null)), _nav, cts.Token);
        Assert.Equal(ActionOutcome.Cancelled, r.Outcome);
        Assert.Empty(_sim.Log);
    }

    [Fact]
    public async Task HttpSendsGetOrPostAndReportsServerErrors()
    {
        Assert.True((await Engine().RunAsync(Button(("http", "https://hook.example/ping", null)), _nav)).Ok);
        Assert.True((await Engine().RunAsync(Button(("http", "https://hook.example/scene", "{\"on\":true}")), _nav)).Ok);
        Assert.True((await Engine().RunAsync(Button(("http", "https://hook.example/note", "bonjour")), _nav)).Ok);
        Assert.Equal(HttpMethod.Get, _http.Requests[0].Method);
        Assert.Null(_http.Requests[0].Body);
        Assert.Equal((HttpMethod.Post, "{\"on\":true}", "application/json"), (_http.Requests[1].Method, _http.Requests[1].Body, _http.Requests[1].Type));
        Assert.Equal("text/plain", _http.Requests[2].Type);

        _http.Status = HttpStatusCode.InternalServerError;
        var failed = await Engine().RunAsync(Button(("http", "https://hook.example/ping", null)), _nav);
        Assert.Equal(ActionOutcome.Failed, failed.Outcome);
        Assert.Contains("500", failed.Message);

        var count = _http.Requests.Count;
        Assert.False((await Engine().RunAsync(Button(("http", "ftp://hook.example", null)), _nav)).Ok);
        Assert.Equal(count, _http.Requests.Count);
    }

    [Fact]
    public async Task AudioCommandsChangeTheDeviceAndReportItsState()
    {
        var engine = Engine();
        var r = await engine.RunAsync(Button(("audio", "mic-toggle", null)), _nav);
        Assert.Equal("Micro coupé", r.Message);
        Assert.True(_sim.IsMuted(AudioDevice.Microphone));
        Assert.Equal("Micro actif", (await engine.RunAsync(Button(("audio", "mic-toggle", null)), _nav)).Message);
        Assert.Equal("Volume 30 %", (await engine.RunAsync(Button(("audio", "volume", "30")), _nav)).Message);
        Assert.Equal(30, _sim.Volume);
        Assert.False((await engine.RunAsync(Button(("audio", "volume", "130")), _nav)).Ok);
        Assert.False((await engine.RunAsync(Button(("audio", "bass-boost", null)), _nav)).Ok);
    }

    [Fact]
    public async Task TimerStartsThenStopsAndFinishesOnTick()
    {
        var engine = Engine();
        var b = Button(("timer", "90", null));
        Assert.Equal("Minuteur lancé : 1:30", (await engine.RunAsync(b, _nav)).Message);
        var countdowns = engine.Context.Countdowns;
        Assert.Equal(TimeSpan.FromSeconds(90), countdowns.Remaining(b));

        _now = _now.AddSeconds(30.2);
        Assert.Equal(TimeSpan.FromSeconds(60), countdowns.Remaining(b));   // arrondi à la seconde supérieure

        Assert.Equal("Minuteur arrêté", (await engine.RunAsync(b, _nav)).Message);
        Assert.Null(countdowns.Remaining(b));

        await engine.RunAsync(b, _nav);
        var finished = new List<DeckButton>();
        countdowns.Finished += finished.Add;
        _now = _now.AddSeconds(89);
        countdowns.Tick();
        Assert.Empty(finished);
        _now = _now.AddSeconds(2);
        countdowns.Tick();
        Assert.Same(b, Assert.Single(finished));
        Assert.False(countdowns.Any);
        Assert.False((await engine.RunAsync(Button(("timer", "0", null)), _nav)).Ok);
    }

    [Fact]
    public void EveryKnownTypeHasAHandler()
    {
        var engine = Engine();
        Assert.Equal(ActionValidator.KnownTypes.OrderBy(t => t), engine.Types.OrderBy(t => t));
    }
}

public class LabelTemplateTests
{
    static readonly DateTime Monday = new(2026, 10, 19, 7, 5, 9);

    [Theory]
    [InlineData("{time}", "07:05")]
    [InlineData("{time:HH:mm:ss}", "07:05:09")]
    [InlineData("{date:dddd d MMMM}", "lundi 19 octobre")]
    [InlineData("Il est {time}", "Il est 07:05")]
    [InlineData("{TIME}", "07:05")]
    [InlineData("Sans modèle", "Sans modèle")]
    [InlineData("{time:%}", "{time:%}")]
    public void RendersTokens(string label, string expected) => Assert.Equal(expected, LabelTemplate.Render(label, Monday));

    [Fact]
    public void TimerTokenAndSecondsDetection()
    {
        Assert.Equal("Pâtes 4:59", LabelTemplate.Render("Pâtes {timer}", Monday, "4:59"));
        Assert.True(LabelTemplate.IsDynamic("{date}"));
        Assert.False(LabelTemplate.IsDynamic("{autre}"));
        Assert.True(LabelTemplate.NeedsSeconds("{time:HH:mm:ss}"));
        Assert.True(LabelTemplate.NeedsSeconds("{timer}"));
        Assert.False(LabelTemplate.NeedsSeconds("{time}"));
        Assert.Equal("1:02:03", Countdowns.Format(new TimeSpan(1, 2, 3)));
        Assert.Equal("0:07", Countdowns.Format(TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void ValidatorKnowsTheNewTypes()
    {
        var cfg = ConfigStore.DefaultConfig();
        Severity? Worst(string type, string value, string? args = null) =>
            ActionValidator.Worst(ActionValidator.Validate(new DeckAction { Type = type, Value = value, Args = args }, cfg, null));

        Assert.Null(Worst("http", "https://hook.example/x"));
        Assert.Equal(Severity.Warning, Worst("http", "http://hook.example/x"));
        Assert.Null(Worst("http", "http://localhost:8123/api"));
        Assert.Equal(Severity.Error, Worst("http", "ftp://x"));
        Assert.Null(Worst("audio", "mic-toggle"));
        Assert.Equal(Severity.Error, Worst("audio", "volume"));
        Assert.Null(Worst("audio", "volume", "40"));
        Assert.Null(Worst("timer", "300"));
        Assert.Equal(Severity.Error, Worst("timer", "-1"));

        var clock = new DeckButton { Label = "{time}" };
        Assert.Empty(ActionValidator.Validate(clock, cfg, null));
    }
}
