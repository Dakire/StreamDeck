using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
}
