using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.ViewModels;
using Wpf.Ui.Controls;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Widgets de données : mise en forme, relevés avec de fausses sources, réseau local réel, cycle de vie des tuiles.</summary>
public class DataWidgetCoreTests
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public Func<Uri, (HttpStatusCode Status, string Body)> Reply { get; set; } = _ => (HttpStatusCode.NotFound, "");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            var (status, body) = Reply(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    static WidgetContext Context(IDictionary<string, string>? options = null, HttpMessageHandler? handler = null, Func<string, string?>? values = null, Func<DateTime>? now = null) => new()
    {
        Options = new Dictionary<string, string>(options ?? new Dictionary<string, string>()),
        Http = new HttpClient(handler ?? new StubHandler()),
        GetValue = values ?? (_ => null),
        Now = now ?? (() => new DateTime(2026, 10, 26, 10, 0, 0)),
        Culture = Fr,
    };

    // ---------- mise en forme ----------

    [Theory]
    [InlineData(0, "0 o")]
    [InlineData(512, "512 o")]
    [InlineData(1536, "1,5 Ko")]
    [InlineData(5L * 1024 * 1024, "5,0 Mo")]
    [InlineData(250L * 1024 * 1024 * 1024, "250 Go")]
    [InlineData(2L * 1024 * 1024 * 1024 * 1024, "2,0 To")]
    public void BytesUseTheRightUnitAndTheLocalDecimalSeparator(long bytes, string expected) => Assert.Equal(expected, WidgetFormat.Bytes(bytes, Fr));

    [Fact]
    public void ARateNeverGoesNegativeOrDividesByZeroWhenACounterRestarts()
    {
        Assert.Equal(1000, WidgetFormat.BytesPerSecond(1000, 3000, 2));
        Assert.Equal(0, WidgetFormat.BytesPerSecond(5000, 100, 2));      // interface relancée : le compteur repart de zéro
        Assert.Equal(0, WidgetFormat.BytesPerSecond(0, 100, 0));
        Assert.Equal("1,0 Ko/s", WidgetFormat.Rate(1024, Fr));
    }

    [Fact]
    public void ListsAcceptEveryUsualSeparatorAndDropDuplicates()
    {
        Assert.Equal(new[] { "a", "b", "c" }, WidgetFormat.SplitList("a; b,c\nA"));
        Assert.Empty(WidgetFormat.SplitList("  ;  "));
        Assert.Equal(3, WidgetFormat.SplitList("1 2 3 4 5", max: 3).Count);
    }

    [Theory]
    [InlineData("srv01", true)]
    [InlineData("nas.local", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("a b", false)]
    [InlineData("x;calc", false)]
    [InlineData("http://x", false)]
    [InlineData("", false)]
    public void OnlyPlausibleHostNamesAreAccepted(string host, bool expected) => Assert.Equal(expected, WidgetFormat.IsHostName(host));

    [Fact]
    public void ANumberIsFoundByPathInObjectsArraysAndNumericText()
    {
        var json = JsonNode.Parse("""{ "total": 12, "data": { "open": "7.5", "items": [ { "count": 3 }, { "count": 4 } ] }, "name": "x" }""");
        Assert.True(WidgetFormat.TryGetNumber(json, "total", out var a)); Assert.Equal(12, a);
        Assert.True(WidgetFormat.TryGetNumber(json, "data.open", out var b)); Assert.Equal(7.5, b);
        Assert.True(WidgetFormat.TryGetNumber(json, "data.items.1.count", out var c)); Assert.Equal(4, c);
        Assert.False(WidgetFormat.TryGetNumber(json, "name", out _));
        Assert.False(WidgetFormat.TryGetNumber(json, "data.items.9.count", out _));
        Assert.False(WidgetFormat.TryGetNumber(json, "data", out _));
        Assert.False(WidgetFormat.TryGetNumber(null, "total", out _));
    }

    // ---------- registre ----------

    [Fact]
    public void TheRegistryFindsWidgetsWithoutCaseAndRefusesDuplicateIds()
    {
        var registry = new WidgetRegistry(new IWidget[] { new DiskWidget(_ => null), new PingWidget() });
        Assert.Same(registry.Find("PING"), registry.Find("ping"));
        Assert.Null(registry.Find("clock"));
        Assert.Null(registry.Find(null));
        Assert.Throws<InvalidOperationException>(() => new WidgetRegistry(new IWidget[] { new PingWidget(), new PingWidget() }));
    }

    [Fact]
    public void TheRefreshIntervalIsTheWidgetsOwnUnlessTheUserChoosesAValidOneAndStaysBetweenOneSecondAndOneHour()
    {
        var info = PingWidget.Description;
        TimeSpan For(string? seconds) => WidgetRegistry.IntervalFor(info, seconds == null ? null : new Dictionary<string, string> { ["interval"] = seconds });
        Assert.Equal(TimeSpan.FromSeconds(5), For(null));
        Assert.Equal(TimeSpan.FromSeconds(20), For("20"));
        Assert.Equal(TimeSpan.FromSeconds(5), For("abc"));
        Assert.Equal(TimeSpan.FromSeconds(5), For("0"));
        Assert.Equal(TimeSpan.FromSeconds(5), For("-3"));
        Assert.Equal(TimeSpan.FromSeconds(3600), For("99999"));
    }

    // ---------- disque ----------

    [Fact]
    public async Task TheDiskShowsTheUsedPercentFreeSpaceAndRaisesTheAlert()
    {
        const long gb = 1024L * 1024 * 1024;
        var calm = await new DiskWidget(_ => (500 * gb, 200 * gb)).ReadAsync(Context(new Dictionary<string, string> { ["drive"] = "d" }), default);
        Assert.Equal("60 %", calm.Headline);
        Assert.Equal("D: · 200 Go libres sur 500 Go", calm.Detail);
        Assert.Equal(60, calm.Percent);
        Assert.Equal(WidgetLevel.Normal, calm.Level);

        Assert.Equal(WidgetLevel.Warning, (await new DiskWidget(_ => (100 * gb, 10 * gb)).ReadAsync(Context(), default)).Level);
        Assert.Equal(WidgetLevel.Danger, (await new DiskWidget(_ => (100 * gb, 2 * gb)).ReadAsync(Context(), default)).Level);
    }

    [Theory]
    [InlineData("c", "C:")]
    [InlineData("C:", "C:")]
    [InlineData("c:\\", "C:")]
    [InlineData("CC", null)]
    [InlineData("C:\\Windows", null)]
    [InlineData("\\\\srv\\share", null)]
    public void OnlyADriveLetterIsAccepted(string text, string? expected) => Assert.Equal(expected, DiskWidget.NormalizeDrive(text));

    [Fact]
    public async Task AMissingOrInvalidDriveExplainsItself()
    {
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => new DiskWidget(_ => null).ReadAsync(Context(new Dictionary<string, string> { ["drive"] = "z" }), default));
        Assert.Contains("Z:", missing.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DiskWidget(_ => (1, 1)).ReadAsync(Context(new Dictionary<string, string> { ["drive"] = "C:\\Windows" }), default));
    }

    [Fact]
    public async Task TheRealDiskReadsTheWindowsDrive()
    {
        var reading = await new DiskWidget().ReadAsync(Context(), default);
        Assert.InRange(reading.Percent!.Value, 0, 100);
        Assert.Contains("libres sur", reading.Detail);
    }

    // ---------- réseau ----------

    [Fact]
    public async Task TheNetworkNeedsTwoReadingsThenShowsTheRatesBetweenThem()
    {
        long received = 0, sent = 0, ticks = 0;
        var widget = new NetworkWidget(() => (received, sent), () => ticks);
        var context = Context();

        var first = await widget.ReadAsync(context, default);
        Assert.Equal("…", first.Headline);

        received = 4 * 1024 * 1024; sent = 512 * 1024; ticks = System.Diagnostics.Stopwatch.Frequency * 2;   // 2 s plus tard
        var second = await widget.ReadAsync(context, default);
        Assert.Equal("↓ 2,0 Mo/s", second.Headline);
        Assert.Equal("↑ 256 Ko/s", second.Detail);

        var other = Context();                       // une autre tuile a sa propre mémoire
        Assert.Equal("…", (await widget.ReadAsync(other, default)).Headline);
    }

    // ---------- ping ----------

    [Fact]
    public async Task OneHostShowsItsLatencyAndTheLevelFollowsIt()
    {
        var up = await new PingWidget((_, _) => Task.FromResult<long?>(23)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "srv01" }), default);
        Assert.Equal(("23 ms", "srv01", WidgetLevel.Good), (up.Headline, up.Detail, up.Level));
        var slow = await new PingWidget((_, _) => Task.FromResult<long?>(400)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "srv01" }), default);
        Assert.Equal(WidgetLevel.Warning, slow.Level);
        var down = await new PingWidget((_, _) => Task.FromResult<long?>(null)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "srv01" }), default);
        Assert.Equal(("Hors ligne", WidgetLevel.Danger), (down.Headline, down.Level));
    }

    [Fact]
    public async Task SeveralHostsShowHowManyAnswerAndListEachOne()
    {
        var widget = new PingWidget((host, _) => Task.FromResult<long?>(host == "b" ? null : 10));
        var mixed = await widget.ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "a; b; c" }), default);
        Assert.Equal("2/3", mixed.Headline);
        Assert.Equal("a · 10 ms\nb · hors ligne\nc · 10 ms", mixed.Detail);
        Assert.Equal(WidgetLevel.Warning, mixed.Level);

        var none = await new PingWidget((_, _) => Task.FromResult<long?>(null)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "a b" }), default);
        Assert.Equal(("0/2", WidgetLevel.Danger), (none.Headline, none.Level));
        Assert.Equal(WidgetLevel.Good, (await new PingWidget((_, _) => Task.FromResult<long?>(5)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "a b" }), default)).Level);
    }

    [Fact]
    public async Task PingTestsTheDefaultHostsWhenNothingIsTypedAndRefusesAnythingThatIsNotAHostName()
    {
        var asked = new List<string>();
        await new PingWidget((h, _) => { lock (asked) asked.Add(h); return Task.FromResult<long?>(1); }).ReadAsync(Context(), default);
        Assert.Equal(new[] { "1.1.1.1", "8.8.8.8" }, asked.OrderBy(h => h));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new PingWidget((_, _) => Task.FromResult<long?>(1)).ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "ok;x&calc" }), default));
        Assert.Contains("x&calc", ex.Message);
    }

    [Fact]
    public async Task TheRealPingReachesTheLoopbackAndReportsAnUnknownNameAsOffline()
    {
        var widget = new PingWidget();
        Assert.NotEqual("Hors ligne", (await widget.ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "127.0.0.1" }), default)).Headline);
        Assert.Equal("Hors ligne", (await widget.ReadAsync(Context(new Dictionary<string, string> { ["hosts"] = "hote-qui-nexiste-pas.invalid" }), default)).Headline);
    }

    // ---------- état des services ----------

    [Theory]
    [InlineData("https://intranet.local/sante", "intranet.local", true, 443)]
    [InlineData("NAS=nas.local:445", "NAS", false, 445)]
    [InlineData("srv01:3389", "srv01:3389", false, 3389)]
    [InlineData("Wiki=http://wiki:8080/x", "Wiki", true, 8080)]
    public void ATargetIsAWebAddressOrAHostAndPortWithAnOptionalName(string text, string name, bool web, int port)
    {
        var target = ServiceTarget.Parse(text);
        Assert.Equal((name, web, port), (target.Name, target.IsWeb, target.Port));
    }

    [Theory]
    [InlineData("srv01")]
    [InlineData("srv01:0")]
    [InlineData("srv01:70000")]
    [InlineData("ftp://x")]
    [InlineData("a b:80")]
    [InlineData("file:///c:/x")]
    public void ATargetWithoutAPortOrWithAnotherSchemeIsRefused(string text) => Assert.Throws<FormatException>(() => ServiceTarget.Parse(text));

    [Fact]
    public async Task TheStatusCountsTheOnlineTargetsAndNamesTheOfflineOnes()
    {
        var widget = new ServiceStatusWidget((t, _, _) => Task.FromResult(t.Name != "NAS"));
        var partial = await widget.ReadAsync(Context(new Dictionary<string, string> { ["targets"] = "https://a.local; NAS=nas:445; Wiki = http://wiki" }), default);
        Assert.Equal("2/3", partial.Headline);
        Assert.Equal("NAS · hors ligne", partial.Detail);
        Assert.Equal(WidgetLevel.Warning, partial.Level);

        var all = await widget.ReadAsync(Context(new Dictionary<string, string> { ["targets"] = "https://a.local" }), default);
        Assert.Equal(("1/1", "Tout est en ligne", WidgetLevel.Good), (all.Headline, all.Detail, all.Level));
        var none = await new ServiceStatusWidget((_, _, _) => Task.FromResult(false)).ReadAsync(Context(new Dictionary<string, string> { ["targets"] = "a:1; b:2" }), default);
        Assert.Equal(WidgetLevel.Danger, none.Level);
    }

    [Fact]
    public async Task TheStatusExplainsAnEmptyOrInvalidTargetList()
    {
        var widget = new ServiceStatusWidget((_, _, _) => Task.FromResult(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => widget.ReadAsync(Context(), default));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => widget.ReadAsync(Context(new Dictionary<string, string> { ["targets"] = "srv01" }), default));
        Assert.Contains("hôte:port", ex.Message);
    }

    [Fact]
    public async Task TheRealStatusChecksALocalWebServerAndALocalPort()
    {
        using var web = new FakeHttpServer { Handler = r => r.PathAndQuery == "/ok" ? (200, "ok") : r.PathAndQuery == "/auth" ? (401, "") : (503, "") };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int open = ((IPEndPoint)listener.LocalEndpoint).Port;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closed = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        try
        {
            var widget = new ServiceStatusWidget();
            string targets = $"Ok=http://localhost:{web.Port}/ok; Auth=http://localhost:{web.Port}/auth; Panne=http://localhost:{web.Port}/panne; Port=127.0.0.1:{open}; Ferme=127.0.0.1:{closed}";
            var reading = await widget.ReadAsync(Context(new Dictionary<string, string> { ["targets"] = targets }, new HttpClientHandler()), default);
            Assert.Equal("3/5", reading.Headline);                               // 401 = le service répond ; 503 et port fermé = hors ligne
            Assert.Contains("Panne · hors ligne", reading.Detail);
            Assert.Contains("Ferme · hors ligne", reading.Detail);
        }
        finally { listener.Stop(); }
    }

    // ---------- compteur ----------

    [Fact]
    public async Task TheCounterReadsANumberFromJsonAndRaisesTheAlertAtTheThreshold()
    {
        using var server = new FakeHttpServer { Handler = _ => (200, """{ "data": { "open": 12 } }""") };
        var options = new Dictionary<string, string> { ["url"] = $"http://localhost:{server.Port}/api", ["path"] = "data.open", ["label"] = "tickets ouverts", ["warn"] = "10" };
        var reading = await new CounterWidget().ReadAsync(Context(options, new HttpClientHandler()), default);
        Assert.Equal(("12", "tickets ouverts", WidgetLevel.Danger), (reading.Headline, reading.Detail, reading.Level));
        options["warn"] = "50";
        Assert.Equal(WidgetLevel.Normal, (await new CounterWidget().ReadAsync(Context(options, new HttpClientHandler()), default)).Level);
    }

    [Fact]
    public async Task TheCounterExplainsWhatIsWrong()
    {
        using var server = new FakeHttpServer { Handler = r => r.PathAndQuery == "/json" ? (200, """{ "x": "abc" }""") : r.PathAndQuery == "/text" ? (200, "pas du json") : (500, "") };
        async Task<string> Fail(string path, string? url = null)
        {
            var options = new Dictionary<string, string> { ["url"] = url ?? $"http://localhost:{server.Port}{path}", ["path"] = "x" };
            return (await Assert.ThrowsAsync<InvalidOperationException>(() => new CounterWidget().ReadAsync(Context(options, new HttpClientHandler()), default))).Message;
        }
        Assert.Contains("Aucun nombre", await Fail("/json"));
        Assert.Contains("pas du JSON", await Fail("/text"));
        Assert.Contains("500", await Fail("/boom"));
        Assert.Contains("adresse", await Fail("", "ftp://x"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ABigResponseIsRefusedInsteadOfBeingReadWhole()
    {
        using var server = new FakeHttpServer { Handler = _ => (200, "[" + new string('1', WidgetFormat.MaxResponseBytes + 10) + "]") };
        var options = new Dictionary<string, string> { ["url"] = $"http://localhost:{server.Port}/", ["path"] = "0" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new CounterWidget().ReadAsync(Context(options, new HttpClientHandler()), default));
        Assert.Contains("volumineuse", ex.Message);
    }

    // ---------- météo ----------

    const string Geocoding = """{ "results": [ { "name": "Lyon", "latitude": 45.75, "longitude": 4.85 } ] }""";
    const string Forecast = """{ "current": { "temperature_2m": 18.4, "weather_code": 3, "wind_speed_10m": 12.2 } }""";

    [Fact]
    public async Task TheWeatherFindsTheCityOnceThenReadsTheForecastOverHttpsOnly()
    {
        var handler = new StubHandler { Reply = u => (HttpStatusCode.OK, u.Host.StartsWith("geocoding", StringComparison.Ordinal) ? Geocoding : Forecast) };
        var context = Context(new Dictionary<string, string> { ["city"] = "Lyon & Co" }, handler);
        var widget = new WeatherWidget();

        var reading = await widget.ReadAsync(context, default);
        Assert.Equal("18 °C", reading.Headline);
        Assert.Equal("Lyon\nCouvert · vent 12 km/h", reading.Detail);
        Assert.All(handler.Urls, u => Assert.StartsWith("https://", u));
        Assert.Contains("name=Lyon%20%26%20Co", handler.Urls[0]);                 // le nom est encodé : aucune injection de paramètre
        Assert.Contains("latitude=45.75&longitude=4.85", handler.Urls[1]);

        await widget.ReadAsync(context, default);
        Assert.Equal(3, handler.Urls.Count);                                       // la position est gardée : un seul appel de plus
    }

    [Fact]
    public async Task TheWeatherExplainsAnUnknownCityAMissingCityAndABrokenAnswer()
    {
        var handler = new StubHandler { Reply = _ => (HttpStatusCode.OK, """{ "results": [] }""") };
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => new WeatherWidget().ReadAsync(Context(new Dictionary<string, string> { ["city"] = "Nullepart" }, handler), default));
        Assert.Contains("Nullepart", unknown.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WeatherWidget().ReadAsync(Context(), default));

        handler.Reply = u => (HttpStatusCode.OK, u.Host.StartsWith("geocoding", StringComparison.Ordinal) ? Geocoding : "{ }");
        var broken = await Assert.ThrowsAsync<InvalidOperationException>(() => new WeatherWidget().ReadAsync(Context(new Dictionary<string, string> { ["city"] = "Lyon" }, handler), default));
        Assert.Contains("incomplète", broken.Message);
    }

    [Theory]
    [InlineData(0, "Ciel dégagé")]
    [InlineData(3, "Couvert")]
    [InlineData(63, "Pluie")]
    [InlineData(95, "Orage")]
    [InlineData(1234, "Conditions inconnues")]
    public void WeatherCodesAreDescribedInFrench(int code, string text) => Assert.Equal(text, WeatherWidget.Describe(code));

    [Fact]
    public void AGeocodingAnswerWithImpossibleCoordinatesIsIgnored() =>
        Assert.Null(WeatherWidget.ParsePlace("x", JsonNode.Parse("""{ "results": [ { "name": "x", "latitude": 400, "longitude": 4 } ] }""")));

    // ---------- prochain événement ----------

    [Fact]
    public async Task TheNextEventShowsTheTitleAndWhen()
    {
        var now = new DateTime(2026, 10, 26, 10, 0, 0);
        var events = CalendarFeed.Serialize(new[]
        {
            new CalendarEvent(now.AddHours(-2), now.AddHours(-1), false, "Passé"),
            new CalendarEvent(now.AddHours(4), now.AddHours(5), false, "Point client"),
            new CalendarEvent(now.AddDays(1), now.AddDays(1).AddHours(1), false, "Demain"),
        });
        var widget = new NextEventWidget();
        var reading = await widget.ReadAsync(Context(values: k => k == "google.calendar.events" ? events : null, now: () => now), default);
        Assert.Equal(("Point client", "Aujourd'hui · 14:00"), (reading.Headline, reading.Detail));

        var running = CalendarFeed.Serialize(new[] { new CalendarEvent(now.AddMinutes(-10), now.AddMinutes(20), false, "Réunion") });
        var current = await widget.ReadAsync(Context(values: k => k == "google.calendar.events" ? running : null, now: () => now), default);
        Assert.Equal(("Réunion", "En cours", WidgetLevel.Good), (current.Headline, current.Detail, current.Level));

        var empty = await widget.ReadAsync(Context(values: k => k == "google.calendar.events" ? "[]" : null, now: () => now), default);
        Assert.Equal("Rien de prévu", empty.Headline);
    }

    [Fact]
    public async Task TheNextEventExplainsWhenGoogleIsNotConnectedOrFailing()
    {
        var notConnected = await Assert.ThrowsAsync<InvalidOperationException>(() => new NextEventWidget().ReadAsync(Context(), default));
        Assert.Contains("Google Workspace", notConnected.Message);
        var failing = await Assert.ThrowsAsync<InvalidOperationException>(() => new NextEventWidget().ReadAsync(Context(values: k => k == "google.calendar.error" ? "Accès refusé" : null), default));
        Assert.Equal("Accès refusé", failing.Message);
    }

    // ---------- cohérence du catalogue ----------

    [Fact]
    public void ADataWidgetSurvivesTheConfigurationRepairWithItsOptions()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages[0].Buttons.Clear();
        cfg.Profiles[0].Pages[0].Buttons.Add(new DeckButton { Label = "Ping", Widget = "ping", ColSpan = 2, RowSpan = 1, WidgetOptions = new() { ["hosts"] = "srv01", ["interval"] = "10" } });
        ConfigNormalizer.Normalize(cfg);
        var button = cfg.Profiles[0].Pages[0].Buttons[0];
        Assert.Equal("ping", button.Widget);
        Assert.Equal("srv01", button.Option("hosts"));
        Assert.Contains("\"hosts\"", ConfigStore.Serialize(cfg));
    }
}

/// <summary>Cycle de vie d'une tuile de widget de données : relevés, pause, reprise, libération.</summary>
public class DataWidgetTileTests
{
    sealed class CountingWidget : IWidget
    {
        int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public Exception? Fail { get; set; }
        public WidgetInfo Info { get; } = new("count", "Compteur test", "", "Apps24", "#000000", 1, 1, null, null, 1);
        public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            if (Fail != null) throw Fail;
            return Task.FromResult(new WidgetReading("42", "détail", 42, WidgetLevel.Warning));
        }
    }

    static DataWidgetViewModel Tile(CountingWidget widget, TimeSpan interval) =>
        new(widget, new WidgetContext(), interval, a => a()) { Button = new DeckButton { Label = "t" } };

    static async Task WaitFor(Func<bool> condition, int milliseconds = 3000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(10);
        Assert.True(condition(), "condition non atteinte à temps");
    }

    [Fact]
    public async Task AReadingFillsTheTileAndAnErrorReplacesItWithTheMessage()
    {
        var widget = new CountingWidget();
        var tile = Tile(widget, TimeSpan.FromHours(1));
        Assert.True(tile.HasStatus);                       // « Mesure en cours… » avant le premier relevé

        await tile.ReadOnceAsync(default);
        Assert.Equal(("42", "détail", true, true, true), (tile.Headline, tile.Detail, tile.HasReading, tile.HasPercent, tile.IsWarning));

        widget.Fail = new InvalidOperationException("Serveur injoignable");
        await tile.ReadOnceAsync(default);
        Assert.Equal(("Serveur injoignable", "", false), (tile.Status, tile.Headline, tile.HasReading));

        widget.Fail = new FormatException("boum");
        await tile.ReadOnceAsync(default);
        Assert.Equal("Mesure impossible : boum", tile.Status);   // une erreur imprévue ne fait jamais tomber l'application
    }

    [Fact]
    public async Task TheTileReadsAtItsIntervalAndStopsForGoodWhenReleased()
    {
        var widget = new CountingWidget();
        var tile = Tile(widget, TimeSpan.FromMilliseconds(20));
        tile.Start();
        await WaitFor(() => widget.Reads >= 3);
        Assert.Equal("42", tile.Headline);

        tile.Dispose();
        Assert.True(tile.IsStopped);
        await Task.Delay(100);                              // laisse finir un relevé éventuel en cours
        int after = widget.Reads;
        await Task.Delay(200);
        Assert.Equal(after, widget.Reads);
    }

    [Fact]
    public async Task APausedTileStopsReadingAndResumesImmediatelyEvenWithALongInterval()
    {
        var widget = new CountingWidget();
        var tile = Tile(widget, TimeSpan.FromMinutes(30));
        tile.Start();
        await WaitFor(() => widget.Reads == 1);

        tile.SetPaused(true);
        Assert.True(tile.IsPaused);
        await Task.Delay(150);
        Assert.Equal(1, widget.Reads);

        tile.SetPaused(false);                              // le deck revient : relevé tout de suite, pas dans 30 minutes
        await WaitFor(() => widget.Reads == 2);
        tile.Dispose();
    }

    [Fact]
    public async Task ATileCreatedWhilePausedDoesNotReadUntilResumed()
    {
        var widget = new CountingWidget();
        var tile = Tile(widget, TimeSpan.FromMilliseconds(10));
        tile.SetPaused(true);
        tile.Start();
        await Task.Delay(150);
        Assert.Equal(0, widget.Reads);
        tile.SetPaused(false);
        await WaitFor(() => widget.Reads >= 1);
        tile.Dispose();
    }

    [Fact]
    public void StartingTwiceOrAfterReleaseDoesNothing()
    {
        var widget = new CountingWidget();
        var tile = Tile(widget, TimeSpan.FromHours(1));
        tile.Dispose();
        tile.Start();
        tile.Dispose();
        Assert.True(tile.IsStopped);
        Assert.Equal(0, widget.Reads);
    }
}

/// <summary>Le deck ne relève que les widgets de la page affichée, et suspend tout quand il est masqué.</summary>
[Collection("ui")]
public class DeckDataWidgetTests
{
    readonly WpfHost _host;
    public DeckDataWidgetTests(WpfHost host) { _host = host; }

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles =
        {
            new DeckProfile
            {
                Name = "P", Columns = 4, Rows = 2, Pages =
                {
                    new DeckPage { Name = "Réseau", Buttons =
                    {
                        new DeckButton { Label = "Ping", Widget = "ping", ColSpan = 2, RowSpan = 1, WidgetOptions = new() { ["hosts"] = "127.0.0.1", ["interval"] = "30" } }, new DeckButton { Blank = true },
                        new DeckButton { Label = "Disque", Widget = "disk" },
                    } },
                    new DeckPage { Name = "Autre", Buttons = { new DeckButton { Label = "Heure", Widget = WidgetTypes.Clock, ColSpan = 2, RowSpan = 1 } } },
                },
            },
        },
    };

    MainViewModel NewDeck()
    {
        _host.Reset(Config());
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    [Fact]
    public void DataWidgetsAreTilesOfTheirOwnKindWithTheirIntervalAndStopWhenThePageChanges()
    {
        var vm = NewDeck();
        var (ping, disk) = _host.Run(() => { var data = vm.Tiles.OfType<DataWidgetViewModel>().ToList(); return (data[0], data[1]); });
        Assert.Equal(("Ping", TimeSpan.FromSeconds(30)), (ping.Title, ping.Interval));        // réglage « interval » du bouton
        Assert.Equal(TimeSpan.FromSeconds(30), disk.Interval);                               // intervalle par défaut du disque
        Assert.False(ping.IsStopped);

        _host.Run(() => ((INavigator)vm).GoToPage("Autre"));
        _host.Settle();
        Assert.True(_host.Run(() => ping.IsStopped && disk.IsStopped), "les relevés de la page quittée doivent s'arrêter");
        Assert.Empty(_host.Run(() => vm.Tiles.OfType<DataWidgetViewModel>().ToList()));

        _host.Run(() => ((INavigator)vm).GoToPage("Réseau"));
        _host.Settle();
        var again = _host.Run(() => vm.Tiles.OfType<DataWidgetViewModel>().ToList());
        Assert.Equal(2, again.Count);
        Assert.All(again, t => Assert.False(t.IsStopped));                                    // de nouvelles tuiles, de nouveaux relevés
    }

    [Fact]
    public void HidingTheDeckPausesEveryDataWidgetAndShowingItResumesThem()
    {
        var vm = NewDeck();
        var tiles = _host.Run(() => vm.Tiles.OfType<DataWidgetViewModel>().ToList());
        Assert.All(tiles, t => Assert.False(t.IsPaused));

        _host.Run(() => vm.SetActive(false));
        Assert.All(tiles, t => Assert.True(t.IsPaused));

        _host.Run(() => ((INavigator)vm).GoToPage("Autre"));
        _host.Run(() => ((INavigator)vm).GoToPage("Réseau"));
        _host.Settle();
        var recreated = _host.Run(() => vm.Tiles.OfType<DataWidgetViewModel>().ToList());
        Assert.All(recreated, t => Assert.True(t.IsPaused));                                 // une tuile créée pendant l'absence reste en pause

        _host.Run(() => vm.SetActive(true));
        Assert.All(recreated, t => Assert.False(t.IsPaused));
    }

    [Fact]
    public void EveryDataWidgetIsDescribedConsistentlyAndRegisteredInTheContainer()
    {
        var registered = _host.Services.GetServices<IWidget>().ToList();
        var data = WidgetTypes.All.Where(w => w.IsData).ToList();

        Assert.Equal(data.Select(w => w.Id).OrderBy(i => i), registered.Select(w => w.Info.Id).OrderBy(i => i));   // ni description sans classe, ni classe sans description
        foreach (var widget in registered)
        {
            var info = widget.Info;
            Assert.Same(info, WidgetTypes.Find(info.Id));
            Assert.NotNull(info.Sizes);
            Assert.Contains(new WidgetSize(info.Cols, info.Rows), info.Sizes!);                                   // la taille par défaut est permise
            Assert.All(info.Sizes!, s => Assert.Contains(s, new[] { WidgetSize.One, WidgetSize.Wide, WidgetSize.Large }));   // 1×1, 2×1 ou 2×2
            Assert.True(Enum.TryParse<SymbolRegular>(info.Icon, out _), $"icône inconnue : {info.Icon}");
            Assert.InRange(info.RefreshSeconds, 1, 3600);
            Assert.Equal((info.Options ?? Array.Empty<WidgetOption>()).Count, (info.Options ?? Array.Empty<WidgetOption>()).Select(o => o.Key).Distinct().Count());
            Assert.NotNull(ActionCatalog.Find("widget." + info.Id));                                              // proposé dans le catalogue
        }
    }

    static readonly Place Root = new("P", 0);

    [Fact]
    public void ThePropertiesOfADataWidgetOfferItsOwnSizesAndFieldsAndOnlyWriteValidValues()
    {
        var vm = NewDeck();
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            Assert.True(props.IsDataWidget);
            Assert.Equal(new[] { "1 × 1", "2 × 1", "2 × 2" }, props.SizeChoices.Select(c => c.Text));        // 1×1, 2×1, 2×2 seulement
            Assert.Equal("2 × 1", props.SizeChoices.Single(c => c.IsSelected).Text);
            Assert.Equal(new[] { "hosts", "interval" }, props.WidgetFields.Select(f => f.Key));
            Assert.Equal(new[] { "127.0.0.1", "30" }, props.WidgetFields.Select(f => f.Text));              // valeurs du bouton

            props.WidgetFields[0].Text = "srv01; srv02";
            props.Flush();
            Assert.Equal("srv01; srv02", _host.Document.Get(Root, 0)!.Option("hosts"));

            var interval = props.WidgetFields[1];
            interval.Text = "abc";
            Assert.True(interval.HasError);
            props.Flush();
            Assert.Equal("30", _host.Document.Get(Root, 0)!.Option("interval"));                              // refusé : rien n'est écrit
            interval.Text = "10";
            Assert.False(interval.HasError);
            props.Flush();
            Assert.Equal("10", _host.Document.Get(Root, 0)!.Option("interval"));
            interval.Text = "";
            props.Flush();
            Assert.Equal("", _host.Document.Get(Root, 0)!.Option("interval"));                                // vide : retour à l'intervalle du widget
        });
    }

    [Fact]
    public void ASizeThatTheWidgetDoesNotOfferIsNotListedAndAClockKeepsEveryCommonSize()
    {
        var cfg = Config();
        cfg.Profiles[0].Pages[1].Buttons.Add(new DeckButton { Label = "Heure", Widget = WidgetTypes.Clock, ColSpan = 2, RowSpan = 1 });
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            Assert.Equal(3, vm.Editor!.Properties.SizeChoices.Count);
            vm.StartEditing(new Place("P", 1), 0);
            var clock = vm.Editor!.Properties;
            Assert.False(clock.IsDataWidget);
            Assert.Empty(clock.WidgetFields);
            Assert.Equal(10, clock.SizeChoices.Count);
        });
    }

    [Fact]
    public void ThePanelOfADataWidgetRendersWithTouchSizedFields()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(Root, 0));
            _host.Settle();
            _host.Render(win, "deck-widget-donnees-edition", 1600, 900);
            _host.Run(() =>
            {
                var boxes = WpfHost.Descendants<Wpf.Ui.Controls.TextBox>(win.DeckProps).Where(t => t.ActualHeight > 0 && Equals(System.Windows.Automation.AutomationProperties.GetName(t), "Hôtes")
                    || Equals(System.Windows.Automation.AutomationProperties.GetName(t), "Rafraîchissement (secondes)")).ToList();
                Assert.Equal(2, boxes.Count);
                Assert.All(boxes, b => Assert.True(b.ActualHeight >= 36, $"{System.Windows.Automation.AutomationProperties.GetName(b)} : {b.ActualHeight} px"));
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void ClosingTheDeckWindowStopsEveryReading()
    {
        var vm = NewDeck();
        var tiles = _host.Run(() => vm.Tiles.OfType<DataWidgetViewModel>().ToList());
        _host.Run(vm.StopWidgets);
        Assert.All(tiles, t => Assert.True(t.IsStopped));
    }

    [Fact]
    public void ADataWidgetTileRendersItsValueAndGaugeInDarkAndLight()
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.Reset(Config());
            _host.SetTheme(theme);
            var win = _host.Run(() => new MainWindow(_host.Services));
            try
            {
                _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
                _host.Settle();
                _host.Run(() =>
                {
                    var tiles = ((MainViewModel)win.DataContext).Tiles.OfType<DataWidgetViewModel>().ToList();
                    tiles[0].Show(new WidgetReading("2/3", "srv01 · 12 ms\nnas · hors ligne\nbox · 3 ms", null, WidgetLevel.Warning));
                    tiles[1].Show(new WidgetReading("92 %", "C: · 40 Go libres sur 500 Go", 92, WidgetLevel.Danger));
                });
                _host.Render(win, $"deck-widgets-donnees-{theme}", 1600, 900);
                _host.Run(() =>
                {
                    var bars = WpfHost.Descendants<System.Windows.Controls.ProgressBar>((System.Windows.DependencyObject)win.Content).ToList();
                    var shown = bars.Where(b => b.Visibility == System.Windows.Visibility.Visible).ToList();
                    Assert.Single(shown);                                                        // seule la tuile avec un pourcentage montre une jauge
                    Assert.True(shown[0].ActualWidth > 100, $"jauge de {shown[0].ActualWidth} px");
                });
            }
            finally { _host.Run(win.Close); }
        }
    }
}
