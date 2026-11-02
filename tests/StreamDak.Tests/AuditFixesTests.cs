using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Actions;
using StreamDak.Integrations;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Correctifs de l'audit 2 (docs/AUDIT-2.md) : journal, intégrations, arrêt, diagnostic.</summary>
public class AuditFixesTests
{
    // ---------- LogRedactor ----------

    [Theory]
    [InlineData("Cookie: sid=abc123; theme=dark", "abc123")]
    [InlineData("Set-Cookie: session=SECRETVALUE; Path=/", "SECRETVALUE")]
    [InlineData("X-Api-Key: 0123456789abcdef", "0123456789abcdef")]
    [InlineData("GET https://x.test/v1?api-key=ZZZZ1234", "ZZZZ1234")]
    [InlineData("Authorization: pk_live_AAAAAAAAAAAA", "pk_live_AAAAAAAAAAAA")]
    [InlineData("Authorization: OAuth abcdefghijklmnop", "abcdefghijklmnop")]
    [InlineData("hue-application-key: HUEKEY1234567890", "HUEKEY1234567890")]
    [InlineData("POST https://discord.com/api/webhooks/123456789/TOKENabc-DEF_ghi", "TOKENabc-DEF_ghi")]
    [InlineData("POST https://hooks.slack.com/services/T0001/B0002/xyzXYZ123", "xyzXYZ123")]
    [InlineData("GET http://192.168.1.2/api/ABCDEFGHIJKLMNOPQRSTUVWX/lights", "ABCDEFGHIJKLMNOPQRSTUVWX")]
    [InlineData("callback code=4/0AbCdEfGhIjKl", "4/0AbCdEfGhIjKl")]
    [InlineData("password=\"mot de passe long\"", "passe long")]
    [InlineData("secret: a,b", ",b")]
    public void RedactorMasksTheSecretParts(string line, string secret)
    {
        var redacted = LogRedactor.Redact(line);
        Assert.DoesNotContain(secret, redacted);
        Assert.Contains(LogRedactor.Mask, redacted);
    }

    [Fact]
    public void RedactorNoLongerLeavesALiteralGroupReference()
    {
        Assert.DoesNotContain("$2", LogRedactor.Redact("password=hunter2"));
        Assert.DoesNotContain("$1", LogRedactor.Redact("token: abc"));
    }

    [Fact]
    public void RedactorKeepsOrdinaryLines()
    {
        const string line = "Hue : connecté (lampes 12, scène Jeu)";
        Assert.Equal(line, LogRedactor.Redact(line));
    }

    // ---------- adresse d'API remplaçable : machine locale seulement ----------

    [Theory]
    [InlineData("http://localhost:1234/api", true)]
    [InlineData("http://127.0.0.1:80", true)]
    [InlineData("https://evil.example/api", false)]
    [InlineData("http://user:pw@localhost/", false)]
    [InlineData("ftp://localhost/", false)]
    [InlineData("pas une adresse", false)]
    public void OnlyALocalAddressCanReplaceAServiceAddress(string value, bool accepted)
    {
        var settings = new IntegrationSettings { Values = new Dictionary<string, string> { ["baseurl"] = value } };
        Assert.Equal(accepted ? value : "https://api.service.test/", settings.GetLocalOverride("baseurl", "https://api.service.test/"));
    }

    [Fact]
    public void AMissingOverrideKeepsTheDefault() =>
        Assert.Equal("https://a/", new IntegrationSettings().GetLocalOverride("baseurl", "https://a/"));

    // ---------- relevé qui échoue autrement que par InvalidOperationException ----------

    sealed class FlakyPolled : PolledHttpIntegration
    {
        int _polls;
        public override string Id => "flaky";
        public override string Name => "Flaky";
        public override string Category => "Test";
        public override string Help => "";
        public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();
        public override IReadOnlyList<IntegrationCommand> Commands { get; } = Array.Empty<IntegrationCommand>();
        protected override TimeSpan PollInterval => TimeSpan.FromMilliseconds(10);
        protected override Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
        {
            // Le premier relevé (connexion) réussit, les suivants renvoient un JSON illisible
            if (Interlocked.Increment(ref _polls) > 1) throw new JsonException("réponse illisible");
            return Task.CompletedTask;
        }
        public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task APollThatThrowsAnythingEndsInALostConnectionNotAFrozenOne()
    {
        using var integration = new FlakyPolled();
        await integration.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        var deadline = Stopwatch.StartNew();
        while (integration.State == ConnectionState.Connected && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.NotEqual(ConnectionState.Connected, integration.State);
    }

    // ---------- arrêt en parallèle, sous une échéance globale ----------

    [Fact]
    public void ShutdownDisconnectsInParallelUnderOneGlobalDeadline()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        var list = Enumerable.Range(0, 6).Select(i => new FakeIntegration("slow" + i) { OnDisconnect = () => Task.Delay(TimeSpan.FromSeconds(20)) }).ToArray();
        foreach (var f in list) doc.UpdateSettings(c => (c.Integrations ??= new())[f.Id] = new IntegrationConfig { Enabled = true }, "test");
        var manager = new IntegrationManager(list, doc, new MemorySecrets());
        manager.Start();
        var wait = Stopwatch.StartNew();
        while (list.Any(f => f.State != ConnectionState.Connected) && wait.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);

        var clock = Stopwatch.StartNew();
        manager.Dispose();
        clock.Stop();

        // En série : 6 × 2 s au moins. En parallèle sous échéance : environ 2 s
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"arrêt trop long : {clock.Elapsed.TotalSeconds:0.0} s");
    }

    // ---------- diagnostic : pauses et tâches longues ----------

    [Fact]
    public void LongTasksAreCountedAndLoggedOnceInAWhile()
    {
        UiPauses.Reset();
        var lines = new List<string>();
        var previous = LongTask.Sink;
        LongTask.Sink = lines.Add;
        try
        {
            LongTask.Run("test-rapide", () => { });
            Assert.Empty(lines);
            LongTask.Report("test-lent", TimeSpan.FromMilliseconds(400));
            LongTask.Report("test-lent", TimeSpan.FromMilliseconds(400));   // répétée : une seule ligne
            Assert.Single(lines);
            Assert.Contains("test-lent", lines[0]);
            Assert.Equal(2, UiPauses.Snapshot().LongTasks);
        }
        finally { LongTask.Sink = previous; }
    }

    [Fact]
    public void RunningTaskNameIsVisibleWhileItRunsAndRestoredAfterwards()
    {
        string? inside = null;
        LongTask.Run("externe", () => LongTask.Run("interne", () => inside = LongTask.Current));
        Assert.Equal("interne", inside);
        Assert.Null(LongTask.Current);
    }

    [Fact]
    public void UiPausesKeepAFixedNumberOfRecentOnes()
    {
        UiPauses.Reset();
        for (int i = 0; i < UiPauses.MaxRecent + 5; i++) UiPauses.Record(250 + i, "t" + i);
        var s = UiPauses.Snapshot();
        Assert.Equal(UiPauses.MaxRecent + 5, s.Count);
        Assert.Equal(UiPauses.MaxRecent, s.Recent.Count);
        Assert.Equal(250 + UiPauses.MaxRecent + 4, s.MaxMilliseconds);
        UiPauses.Reset();
    }

    // ---------- programme au premier plan par événement ----------

    [Fact]
    public void SimulatedForegroundRaisesTheChangeEvent()
    {
        var sim = new SimulatedPlatform();
        var seen = new List<string?>();
        sim.ForegroundChanged += seen.Add;
        sim.Foreground = "notepad";
        sim.Foreground = null;
        Assert.Equal(new string?[] { "notepad", null }, seen);
    }

    // ---------- clés facultatives de configuration ----------

    [Fact]
    public void NewOptionalKeysAreAbsentFromTheFileWhileDefaultAndKeptWhenSet()
    {
        var cfg = ConfigStore.DefaultConfig();
        var json = ConfigStore.Serialize(cfg);
        foreach (var key in new[] { "EconomyMode", "CatalogWidth", "InspectorHeight", "CatalogCollapsed", "InspectorCollapsed", "SettingsWidth", "SettingsHeight" })
            Assert.DoesNotContain("\"" + key + "\"", json);

        cfg.EconomyMode = true; cfg.CatalogWidth = 380; cfg.InspectorHeight = 300; cfg.CatalogCollapsed = true; cfg.InspectorCollapsed = true; cfg.SettingsWidth = 900; cfg.SettingsHeight = 700;
        var back = ConfigStore.Parse(ConfigStore.Serialize(cfg));
        Assert.True(back.EconomyMode);
        Assert.Equal((380d, 300d, 900d, 700d), (back.CatalogWidth, back.InspectorHeight, back.SettingsWidth, back.SettingsHeight));
        Assert.True(back.CatalogCollapsed && back.InspectorCollapsed);
    }

    [Fact]
    public void AbsurdSavedSizesBecomeAbsentInsteadOfAnUnusableWindow()
    {
        const string json = "{\"Version\":2,\"CatalogWidth\":5,\"InspectorHeight\":99999,\"SettingsWidth\":10,\"SettingsHeight\":100000,\"Profiles\":[]}";
        var cfg = ConfigStore.Parse(json);
        Assert.Null(cfg.CatalogWidth);
        Assert.Null(cfg.InspectorHeight);
        Assert.Null(cfg.SettingsWidth);
        Assert.Null(cfg.SettingsHeight);
    }

    [Fact]
    public void AConfigWithoutTheNewKeysStillLoads()
    {
        var cfg = ConfigStore.Parse("{\"Version\":2,\"Profiles\":[{\"Name\":\"A\",\"Pages\":[{\"Name\":\"P\",\"Buttons\":[]}]}]}");
        Assert.False(cfg.EconomyMode);
        Assert.Null(cfg.CatalogWidth);
        Assert.Single(cfg.Profiles);
    }

    // ---------- existence d'un chemin sans bloquer ----------

    [Fact]
    public void PathProbeAnswersQuicklyAndCachesTheAnswer()
    {
        var temp = System.IO.Path.GetTempFileName();
        try
        {
            Assert.True(PathProbe.Exists(temp));
            Assert.False(PathProbe.Exists(temp + ".absent"));
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) Assert.True(PathProbe.Exists(temp));   // en cache
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally { System.IO.File.Delete(temp); }
    }

    // ---------- niveau audio : une annulation est un échec, pas la fin silencieuse de la boucle ----------

    [Fact]
    public async Task ACancelledSendIsReportedAndDoesNotLeaveTheSenderBusyForever()
    {
        var failures = new List<Exception>();
        var sender = new StreamDak.Audio.LevelSender(_ => throw new OperationCanceledException(), _ => Task.CompletedTask, failures.Add);
        sender.Post(40);
        var wait = Stopwatch.StartNew();
        while (sender.IsBusy && wait.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.False(sender.IsBusy);
        Assert.Single(failures);
        Assert.IsType<TimeoutException>(failures[0]);
    }

    // ---------- touche d'intégration indisponible ----------

    [Fact]
    public void AnEnabledButDisconnectedIntegrationMakesItsKeyUnavailableUnlessTheCommandWorksOffline()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        var fake = new FakeIntegration("dispo");
        var manager = new IntegrationManager(new[] { fake }, doc, new MemorySecrets());
        var go = new DeckAction { Type = "dispo", Value = "go", Args = "x" };

        Assert.False(manager.IsUnavailable(go));                                    // désactivée : pas « indisponible », simplement non utilisée
        doc.UpdateSettings(c => (c.Integrations ??= new())["dispo"] = new IntegrationConfig { Enabled = true }, "test");
        Assert.True(manager.IsUnavailable(go));                                     // activée mais pas connectée
        Assert.False(manager.IsUnavailable(new DeckAction { Type = "hotkey", Value = "ctrl+c" }));
    }

    // ---------- diagnostic ----------

    [Fact]
    public void TheDiagnosticCarriesFiguresAndNeverASecret()
    {
        Log.Warn("Appel https://hooks.slack.com/services/T0001/B0002/SECRETTOKEN refusé, token=abcdef123456");
        var text = DiagnosticReport.Build(new DiagnosticInput
        {
            Version = "StreamDak 1.0.0",
            WorkingSetBytes = 120L * 1048576,
            WebView = new ProcessUsage(5, 640L * 1048576),
            Uptime = TimeSpan.FromMinutes(75),
            Pauses = new UiPauseSummary(3, 900, 450, 2, new[] { new UiPause(DateTimeOffset.Now, 450, "tâche : Reconstruction de la grille") }),
            Integrations = new[] { new IntegrationStatus("OBS", true, "Error", "mot de passe=secret123 refusé"), new IntegrationStatus("Hue", false, "Disabled", null) },
            RecentLog = Log.Recent(),
            Content = (1, 2, 12),
        });
        Assert.Contains("120 Mo", text);
        Assert.Contains("5 processus, 640 Mo", text);
        Assert.Contains("760 Mo", text);
        Assert.Contains("450 ms", text);
        Assert.Contains("OBS : Error", text);
        Assert.DoesNotContain("Hue :", text);              // désactivée : omise
        Assert.DoesNotContain("SECRETTOKEN", text);
        Assert.DoesNotContain("abcdef123456", text);
        Assert.DoesNotContain("secret123", text);
    }

    // ---------- réordonner les profils ----------

    [Fact]
    public void ProfilesCanBeMovedUpAndDownAndTheMoveIsUndoable()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        doc.AddProfile("B");
        doc.AddProfile("C");
        var names = () => doc.Config.Profiles.Select(p => p.Name).ToList();
        var start = names();

        Assert.True(doc.MoveProfile("C", -1).Ok);
        Assert.Equal(new[] { start[0], "C", "B" }, names());
        Assert.False(doc.MoveProfile(start[0], -1).Ok);                 // déjà en tête
        Assert.False(doc.MoveProfile("B", 1).Ok);                       // déjà en queue
        Assert.False(doc.MoveProfile("absent", 1).Ok);

        doc.Undo();
        Assert.Equal(start, names());
    }

    // ---------- requêtes web : méthode et en-têtes personnalisés ----------

    [Fact]
    public void HeadersAreParsedLineByLineAndDangerousOnesRefused()
    {
        Assert.True(HttpHeaderSpec.TryParse("Authorization: Bearer abc\r\nX-Token:  zz \n\nAccept: application/json", out var ok, out _));
        Assert.Equal(new[] { ("Authorization", "Bearer abc"), ("X-Token", "zz"), ("Accept", "application/json") }, ok);
        Assert.True(HttpHeaderSpec.TryParse(null, out var none, out _));
        Assert.Empty(none);

        foreach (var bad in new[] { "pas d'en-tête", "Host: evil.example", "Content-Length: 5", "Mauvais nom: x", ": vide", "X-A: ab" })
        {
            Assert.False(HttpHeaderSpec.TryParse(bad, out _, out var why), bad);
            Assert.NotEmpty(why);
        }
        Assert.False(HttpHeaderSpec.TryParse(string.Join("\n", Enumerable.Range(0, 21).Select(i => "X-" + i + ": v")), out _, out _));
        Assert.False(HttpHeaderSpec.TryParse(new string('a', HttpHeaderSpec.MaxLength + 1), out _, out _));
    }

    sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string? Type;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            Type = request.Content?.Headers.ContentType?.MediaType;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task AWebStepSendsItsCustomHeadersAndMethod()
    {
        var recorder = new Recorder();
        var engine = new ActionEngine(new ActionContext
        {
            Platform = new SimulatedPlatform().Services,
            Http = new HttpClient(recorder),
            Countdowns = new Countdowns(() => DateTime.UtcNow),
            Delay = (_, _) => Task.CompletedTask,
        });
        var button = new DeckButton
        {
            Label = "Appel",
            Actions = new List<DeckAction> { new() { Type = ActionTypes.Http, Value = "https://hook.example/hook", Args = "{\"a\":1}", Method = "put", Headers = "Authorization: Bearer jeton\nX-Test: 42\nContent-Type: application/vnd.test+json" } },
        };

        var result = await engine.RunAsync(button, null!);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(HttpMethod.Put, recorder.Seen!.Method);
        Assert.Equal("Bearer jeton", recorder.Seen.Headers.GetValues("Authorization").Single());
        Assert.Equal("42", recorder.Seen.Headers.GetValues("X-Test").Single());
        Assert.Equal("application/vnd.test+json", recorder.Type);
    }

    [Fact]
    public void TheValidatorFlagsABadMethodOrHeader()
    {
        var cfg = ConfigStore.DefaultConfig();
        var step = new DeckAction { Type = ActionTypes.Http, Value = "https://exemple.test/x", Method = "TRACE", Headers = "Host: x" };
        var issues = ActionValidator.Validate(step, cfg, cfg.Profiles[0]);
        Assert.Equal(2, issues.Count(i => i.Severity == Severity.Error));
        step.Method = "put"; step.Headers = "X-Ok: 1";
        Assert.DoesNotContain(ActionValidator.Validate(step, cfg, cfg.Profiles[0]), i => i.Severity == Severity.Error);
    }

    [Fact]
    public void OptionalWebStepKeysStayOutOfTheFileWhenUnused()
    {
        var json = JsonSerializer.Serialize(new DeckAction { Type = "http", Value = "https://a.b/" }, ConfigStore.Options);
        Assert.DoesNotContain("Headers", json);
        Assert.DoesNotContain("Method", json);
    }

    // ---------- raccourcis globaux ----------

    [Theory]
    [InlineData("ctrl+alt+f1", true)]
    [InlineData("Alt + Ctrl + F1", true)]
    [InlineData("f13", true)]
    [InlineData("win+d", true)]
    [InlineData("f1", false)]                // sans modificateur : volerait la touche à toutes les applications
    [InlineData("a", false)]
    [InlineData("ctrl+shift", false)]
    [InlineData("ctrl+a+b", false)]          // une seule touche
    [InlineData("win+l", false)]             // réservé à Windows
    [InlineData("ctrl+truc", false)]
    [InlineData("", false)]
    public void GlobalHotkeyValidationRequiresAModifierExceptForFreeFunctionKeys(string combo, bool valid) =>
        Assert.Equal(valid, StreamDak.Hotkeys.GlobalHotkeyPlan.TryValidate(combo, out _));

    [Fact]
    public void GlobalHotkeysAreNormalizedAndDuplicatesKeepTheFirstButton()
    {
        Assert.Equal("ctrl+alt+shift+f5", StreamDak.Hotkeys.GlobalHotkeyPlan.Normalize("Shift + Alt + Control + F5"));
        var cfg = ConfigStore.DefaultConfig();
        var buttons = cfg.Profiles[0].Pages[0].Buttons;
        buttons.Clear();
        buttons.Add(new DeckButton { Label = "A", GlobalHotkey = "Ctrl+Alt+F1" });
        buttons.Add(new DeckButton { Label = "B", GlobalHotkey = "alt+ctrl+f1" });   // même combinaison
        buttons.Add(new DeckButton { Label = "C", GlobalHotkey = "f1" });             // invalide
        buttons.Add(new DeckButton { Label = "D", GlobalHotkey = "f14" });
        buttons.Add(new DeckButton { Label = "E" });

        var (planned, rejected) = StreamDak.Hotkeys.GlobalHotkeyPlan.Build(cfg);

        Assert.Equal(new[] { "A", "D" }, planned.Select(p => p.Button.Label));
        Assert.Equal(new[] { "B", "C" }, rejected.Select(r => r.Label));
        Assert.Contains("Déjà utilisé", rejected[0].Reason);
    }

    [Fact]
    public void ASimulatedHotkeyServiceRefusesWhatAnotherApplicationOwnsAndReportsPresses()
    {
        using var service = new StreamDak.Hotkeys.SimulatedHotkeyService();
        service.Taken.Add("ctrl+alt+f9");
        Assert.False(service.Register(1, "ctrl+alt+f9").Ok);
        Assert.True(service.Register(2, "ctrl+alt+f1").Ok);
        var pressed = new List<int>();
        service.Pressed += pressed.Add;
        service.Press(2);
        service.Press(1);                       // jamais enregistré
        service.Unregister(2);
        service.Press(2);
        Assert.Equal(new[] { 2 }, pressed);
    }

    [Fact]
    public void AGlobalHotkeyKeyIsOptionalInTheFile()
    {
        var json = JsonSerializer.Serialize(new DeckButton { Label = "x" }, ConfigStore.Options);
        Assert.DoesNotContain("GlobalHotkey", json);
        var back = JsonSerializer.Deserialize<DeckButton>(JsonSerializer.Serialize(new DeckButton { Label = "x", GlobalHotkey = "ctrl+alt+f2" }, ConfigStore.Options), ConfigStore.Options)!;
        Assert.Equal("ctrl+alt+f2", back.GlobalHotkey);
    }
}
