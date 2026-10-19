using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Actions;
using TouchDeck.Integrations;
using Xunit;

namespace TouchDeck.Tests;

sealed class MemorySecrets : ISecretStore
{
    readonly Dictionary<string, string> _values = new();
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string? value) { if (string.IsNullOrEmpty(value)) _values.Remove(key); else _values[key] = value; }
}

/// <summary>Intégration factice : on décide de ce que la connexion fait, et on observe ce qu'elle reçoit.</summary>
sealed class FakeIntegration : IntegrationBase
{
    readonly string _id;
    public FakeIntegration(string id = "fake") { _id = id; }
    public ConcurrentQueue<IntegrationSettings> Connects { get; } = new();
    public int Disconnects;
    public Func<int, Task>? OnConnect;
    int _attempts;

    public override string Id => _id;
    public override string Name => "Faux " + _id;
    public override string Category => "Faux";
    public override string Help => "";
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Hôte", SettingKind.Text, "localhost"), new SettingField("password", "Mot de passe", SettingKind.Secret),
    };
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("go", "Aller", "Va quelque part", "une destination", ArgRequired: true, StateKey: "on.{arg}"),
        new IntegrationCommand("ping", "Ping", "Répond"),
    };

    protected override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        Connects.Enqueue(settings);
        int attempt = Interlocked.Increment(ref _attempts);
        if (OnConnect != null) await OnConnect(attempt);
        SetValue("scene", "Jeu");
    }

    protected override Task DisconnectCoreAsync() { Interlocked.Increment(ref Disconnects); return Task.CompletedTask; }
    public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation) => Task.FromResult<string?>("fait " + command);
    public void Lose() => ConnectionLost("coupée");
    public void Publish(string key, string value) => SetValue(key, value);
}

public class IntegrationManagerTests
{
    sealed class Harness : IDisposable
    {
        public DeckDocument Doc = new(ConfigStore.DefaultConfig());
        public MemorySecrets Secrets = new();
        public ConcurrentQueue<TimeSpan> Waits = new();
        public SemaphoreSlim Gate = new(0);
        public IntegrationManager Manager;
        public Harness(params IIntegration[] integrations)
        {
            Manager = new IntegrationManager(integrations, Doc, Secrets, async (wait, ct) =>
            {
                Waits.Enqueue(wait);
                Gate.Release();
                await Task.Yield();
                await Task.Delay(5, ct);   // laisse la main sans attendre réellement 2 à 60 s
            });
        }
        public void Enable(string id, bool enabled = true, params (string Key, string Value)[] values) =>
            Doc.UpdateSettings(c =>
            {
                c.Integrations ??= new();
                c.Integrations[id] = new IntegrationConfig { Enabled = enabled, Values = values.Length == 0 ? null : values.ToDictionary(v => v.Key, v => v.Value) };
            });
        public void Dispose() => Manager.Dispose();
    }

    static async Task Until(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task EnabledIntegrationsConnectAtStartAndDisabledOnesDoNot()
    {
        var on = new FakeIntegration("on"); var off = new FakeIntegration("off");
        using var h = new Harness(on, off);
        h.Enable("on");
        h.Manager.Start();
        await Until(() => on.State == ConnectionState.Connected);
        Assert.Equal(ConnectionState.Disconnected, off.State);
        Assert.True(off.Connects.IsEmpty);
        Assert.True(h.Manager.IsEnabled("on"));
        Assert.False(h.Manager.IsEnabled("off"));
    }

    [Fact]
    public async Task FailedConnectionsAreRetriedWithGrowingWaitsAndTheCounterResetsOnSuccess()
    {
        var fake = new FakeIntegration { OnConnect = attempt => attempt <= 3 ? throw new InvalidOperationException("logiciel fermé") : Task.CompletedTask };
        using var h = new Harness(fake);
        h.Enable("fake");
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        Assert.Equal(new[] { 2.0, 4.0, 8.0 }, h.Waits.Take(3).Select(w => w.TotalSeconds));

        fake.Lose();   // la connexion se perd : nouvelle tentative, et l'attente repart de 2 s
        await Until(() => fake.Connects.Count >= 5);
        await Until(() => h.Waits.Count >= 4);
        Assert.Equal(2.0, h.Waits.ElementAt(3).TotalSeconds);
    }

    [Fact]
    public async Task TheErrorMessageIsKeptAndAFailureNeverEscapesTheManager()
    {
        var fake = new FakeIntegration { OnConnect = _ => throw new InvalidOperationException("mot de passe refusé") };
        using var h = new Harness(fake);
        h.Enable("fake");
        h.Manager.Start();
        await Until(() => !h.Waits.IsEmpty);
        Assert.Equal("mot de passe refusé", fake.Error);
        Assert.Empty(fake.Values);
    }

    [Fact]
    public async Task OneFailingIntegrationNeverBlocksTheOthers()
    {
        var broken = new FakeIntegration("broken") { OnConnect = _ => throw new InvalidOperationException("non") };
        var fine = new FakeIntegration("fine");
        using var h = new Harness(broken, fine);
        h.Enable("broken"); h.Enable("fine");
        h.Manager.Start();
        await Until(() => fine.State == ConnectionState.Connected);
        Assert.NotEqual(ConnectionState.Connected, broken.State);
    }

    [Fact]
    public async Task ChangingTheSettingsReconnectsWithTheNewOnesAndDisablingDisconnects()
    {
        var fake = new FakeIntegration();
        using var h = new Harness(fake);
        h.Enable("fake", true, ("host", "alpha"));
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        Assert.Equal("alpha", fake.Connects.Last().Get("host"));

        h.Enable("fake", true, ("host", "beta"));
        await Until(() => fake.Connects.Count == 2 && fake.State == ConnectionState.Connected);
        Assert.Equal("beta", fake.Connects.Last().Get("host"));

        h.Enable("fake", false);
        await Until(() => fake.State != ConnectionState.Connected);
        Assert.False(h.Manager.IsEnabled("fake"));
    }

    [Fact]
    public async Task SecretsComeFromTheSecretStoreNeverFromTheConfiguration()
    {
        var fake = new FakeIntegration();
        using var h = new Harness(fake);
        h.Secrets.Set(IntegrationManager.SecretKey("fake", "password"), "s3cret");
        h.Enable("fake", true, ("host", "h"));
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        Assert.Equal("s3cret", fake.Connects.Last().Secret("password"));
        Assert.DoesNotContain("s3cret", ConfigStore.Serialize(h.Doc.Config));

        h.Secrets.Set(IntegrationManager.SecretKey("fake", "password"), "autre");
        h.Doc.UpdateSettings(c => c.Spacing = 15);   // un changement de réglages relit aussi les secrets
        await Until(() => fake.Connects.Count == 2);
        Assert.Equal("autre", fake.Connects.Last().Secret("password"));
    }

    [Fact]
    public async Task ValuesAreQualifiedAndHiddenWhileDisconnected()
    {
        var fake = new FakeIntegration();
        using var h = new Harness(fake);
        var changed = new ConcurrentBag<string>();
        h.Manager.ValuesChanged += keys => { foreach (var k in keys) changed.Add(k); };
        Assert.Null(h.Manager.GetValue("fake.scene"));
        h.Enable("fake");
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        Assert.Equal("Jeu", h.Manager.GetValue("fake.scene"));
        Assert.Contains("fake.scene", changed);
        Assert.Null(h.Manager.GetValue("fake.inconnue"));
        Assert.Null(h.Manager.GetValue("scene"));
        fake.Lose();
        Assert.Null(h.Manager.GetValue("fake.scene"));
    }

    [Fact]
    public async Task StateOfFollowsTheCommandStateKeyWithItsArgument()
    {
        var fake = new FakeIntegration();
        using var h = new Harness(fake);
        h.Enable("fake");
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        var go = new DeckAction { Type = "fake", Value = "go", Args = "salon" };
        Assert.Null(h.Manager.StateOf(go));                 // valeur encore inconnue
        fake.Publish("on.salon", "true");
        Assert.True(h.Manager.StateOf(go));
        fake.Publish("on.salon", "false");
        Assert.False(h.Manager.StateOf(go));
        Assert.Null(h.Manager.StateOf(new DeckAction { Type = "fake", Value = "ping" }));       // pas d'état pour cette commande
        Assert.Null(h.Manager.StateOf(new DeckAction { Type = "inconnu", Value = "go" }));
    }

    [Fact]
    public async Task TestReportsSuccessOrTheReasonAndLeavesADisabledIntegrationDisconnected()
    {
        var fake = new FakeIntegration();
        using var h = new Harness(fake);
        Assert.Contains("réussie", await h.Manager.TestAsync("fake"));
        Assert.Equal(1, fake.Disconnects);
        Assert.NotEqual(ConnectionState.Connected, fake.State);

        fake.OnConnect = _ => throw new InvalidOperationException("injoignable");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Manager.TestAsync("fake"));
        Assert.Equal("injoignable", ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Manager.TestAsync("nope"));
    }

    [Fact]
    public async Task ActionsFailClearlyWhenNotConnectedAndValidateTheirArguments()
    {
        var fake = new FakeIntegration();
        var handler = new IntegrationAction(fake);
        var button = new DeckButton();
        var context = new StepContext(button, null!, new ActionContext { Platform = new SimulatedPlatform().Services, Http = new System.Net.Http.HttpClient() });
        var step = new DeckAction { Type = "fake", Value = "ping" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(step, context, default));
        Assert.Contains("n'est pas connecté", ex.Message);

        using var h = new Harness(fake);
        h.Enable("fake");
        h.Manager.Start();
        await Until(() => fake.State == ConnectionState.Connected);
        Assert.Equal("fait ping", await handler.ExecuteAsync(step, context, default));

        Assert.Empty(IntegrationAction.Validate(fake, new DeckAction { Type = "fake", Value = "go", Args = "x" }));
        Assert.Contains(IntegrationAction.Validate(fake, new DeckAction { Type = "fake", Value = "go" }), i => i.Message.Contains("une destination"));
        Assert.Contains(IntegrationAction.Validate(fake, new DeckAction { Type = "fake", Value = "?" }), i => i.Severity == Severity.Error);
    }

    [Fact]
    public void IntegrationSettingsAreOptionalAndNormalized()
    {
        var cfg = ConfigStore.DefaultConfig();
        Assert.DoesNotContain("Integrations", ConfigStore.Serialize(cfg));

        var parsed = ConfigStore.Parse("""
            { "Version": 2, "Profiles": [], "Integrations": { " OBS ": { "Enabled": true, "Values": { "host": " h ", "port": "4455" } }, "vide": null } }
            """);
        Assert.Equal(new[] { "obs" }, parsed.Integrations!.Keys);
        Assert.True(parsed.Integrations["obs"].Enabled);
        Assert.Equal("h", parsed.Integrations["obs"].Values!["host"]);

        Assert.Null(ConfigStore.Parse("""{ "Version": 2, "Profiles": [], "Integrations": {} }""").Integrations);
        var back = ConfigStore.Parse(ConfigStore.Serialize(parsed));
        Assert.Equal("4455", back.Integrations!["obs"].Values!["port"]);
    }

    [Fact]
    public void ExternalActionTypesAreAcceptedByTheValidatorOnceRegistered()
    {
        var cfg = ConfigStore.DefaultConfig();
        Assert.Contains(ActionValidator.Validate(new DeckAction { Type = "zzz", Value = "x" }, cfg, null), i => i.Severity == Severity.Error);
        ActionValidator.RegisterExternalType("zzz", a => a.Value == "x" ? Array.Empty<Issue>() : new[] { new Issue(Severity.Error, "non") });
        Assert.Empty(ActionValidator.Validate(new DeckAction { Type = "zzz", Value = "x" }, cfg, null));
        Assert.Contains("zzz", ActionValidator.KnownTypes);
        Assert.NotEmpty(ActionValidator.Validate(new DeckAction { Type = "zzz", Value = "y" }, cfg, null));
    }

    [Fact]
    public void CatalogEntriesOfIntegrationsAreSearchableAndIdempotent()
    {
        var entry = new CatalogAction("zzz.test", "Zzz", "Commande zzz", "Une commande", "Star24", "#334155", new[] { new DeckAction { Type = "zzz", Value = "x" } });
        ActionCatalog.Register("Zzz", "Plug24", new[] { entry });
        ActionCatalog.Register("Zzz", "Plug24", new[] { entry });
        Assert.Single(ActionCatalog.Categories, c => c == "Zzz");
        Assert.Same(entry, ActionCatalog.Find("zzz.test"));
        Assert.Single(ActionCatalog.Search("commande zzz"));
        Assert.Equal(ActionCatalog.All.Count, ActionCatalog.All.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void ValueTokensInLabelsShowTheValueOrTheirFallback()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 5);
        string? Lookup(string k) => k == "obs.scene" ? "Jeu" : null;
        Assert.Equal("Scène : Jeu", LabelTemplate.Render("Scène : {obs.scene}", now, null, Lookup));
        Assert.Equal("hors ligne", LabelTemplate.Render("{obs.live:hors ligne}", now, null, Lookup));
        Assert.Equal("—", LabelTemplate.Render("{obs.live}", now, null, null));
        Assert.Equal("03:04 Jeu", LabelTemplate.Render("{time} {obs.scene}", now, null, Lookup));
        Assert.Equal(new[] { "obs.scene", "sys.cpu" }, LabelTemplate.ValueKeys("{OBS.Scene} · {sys.cpu:0} · {time}"));
        Assert.True(LabelTemplate.IsDynamic("{sys.cpu}"));
        Assert.False(LabelTemplate.NeedsSeconds("{obs.scene:secondes}"));   // le repli n'est pas un format d'heure
        Assert.False(LabelTemplate.IsDynamic("{pas un modèle}"));
        Assert.False(LabelTemplate.IsDynamic("{a.}"));
    }
}
