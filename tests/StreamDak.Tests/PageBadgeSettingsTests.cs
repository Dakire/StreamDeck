using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Notifications;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Réglages de badge par page (ADR 0022) : relecture des anciens fichiers, écriture sans alourdir, historique.</summary>
public class PageBadgeSettingsTests
{
    static DeckConfig Read(string json) => JsonSerializer.Deserialize<DeckConfig>(json, ConfigStore.Options)!;
    static string Write(DeckConfig cfg) => JsonSerializer.Serialize(cfg, ConfigStore.Options);

    [Fact]
    public void AnOldFileWithoutTheKeysReadsWithTheDefaults()
    {
        var cfg = Read("""{ "Version": 2, "ActiveProfile": "P", "Profiles": [ { "Name": "P", "Pages": [ { "Name": "Chat", "Kind": "web", "Url": "https://chat.google.com/" } ] } ] }""");
        var page = cfg.Profiles[0].Pages[0];
        Assert.True(page.BadgeEnabled);
        Assert.False(page.BadgeExcludeFromTotal);
    }

    [Fact]
    public void TheDefaultsAreNeverWrittenSoTheFileIsNotHeavier()
    {
        var cfg = Samples.Doc("a").Config;
        Assert.DoesNotContain("Badge", Write(cfg));
        cfg.Profiles[0].Pages[0].BadgeEnabled = true;                    // vrai = défaut
        Assert.DoesNotContain("Badge", Write(cfg));
    }

    [Fact]
    public void OnlyValuesDifferentFromTheDefaultAreWrittenAndTheyRoundTrip()
    {
        var cfg = Samples.Doc("a").Config;
        cfg.Profiles[0].Pages[0].BadgeEnabled = false;
        cfg.Profiles[0].Pages[1].BadgeExcludeFromTotal = true;
        var json = Write(cfg);
        Assert.Contains("\"BadgeEnabled\": false", json);
        Assert.Contains("\"BadgeExcludeFromTotal\": true", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "BadgeEnabled"));
        var back = Read(json);
        Assert.False(back.Profiles[0].Pages[0].BadgeEnabled);
        Assert.True(back.Profiles[0].Pages[0].BadgeExcludeFromTotal == false);
        Assert.True(back.Profiles[0].Pages[1].BadgeEnabled);
        Assert.True(back.Profiles[0].Pages[1].BadgeExcludeFromTotal);
    }

    [Fact]
    public void AnExplicitTrueIsReadAndNormalizedAwayAndANullIsTheDefault()
    {
        var cfg = Read("""{ "Version": 2, "ActiveProfile": "P", "Profiles": [ { "Name": "P", "Pages": [ { "Name": "A", "BadgeEnabled": true }, { "Name": "B", "BadgeEnabled": null }, { "Name": "C", "BadgeEnabled": false } ] } ] }""");
        ConfigNormalizer.Normalize(cfg);
        Assert.Equal(new[] { true, true, false }, cfg.Profiles[0].Pages.Select(p => p.BadgeEnabled));
        Assert.DoesNotContain("\"BadgeEnabled\": true", Write(cfg));
    }

    [Fact]
    public void ACopyOfAPageKeepsItsBadgeSettings()
    {
        var d = Samples.Doc("a");
        Assert.True(d.SetPageBadgeEnabled("P", 0, false).Ok);
        Assert.True(d.SetPageBadgeExcluded("P", 0, true).Ok);
        Assert.True(d.DuplicatePage("P", 0).Ok);
        var copy = d.Config.Profiles[0].Pages[1];
        Assert.False(copy.BadgeEnabled);
        Assert.True(copy.BadgeExcludeFromTotal);
    }

    [Fact]
    public void TheSettingsAreUndoableAndNoOpsLeaveNoHistory()
    {
        var d = Samples.Doc("a");
        Assert.False(d.SetPageBadgeEnabled("P", 0, true).Ok);            // déjà le cas
        Assert.False(d.SetPageBadgeExcluded("P", 0, false).Ok);
        Assert.False(d.SetPageBadgeEnabled("P", 9, false).Ok);           // page inconnue
        Assert.False(d.CanUndo);

        Assert.True(d.SetPageBadgeEnabled("P", 0, false).Ok);
        Assert.False(d.Config.Profiles[0].Pages[0].BadgeEnabled);
        Assert.True(d.Undo());
        Assert.True(d.Config.Profiles[0].Pages[0].BadgeEnabled);
        Assert.True(d.Redo());
        Assert.False(d.Config.Profiles[0].Pages[0].BadgeEnabled);

        Assert.True(d.SetPageBadgeExcluded("P", 1, true).Ok);
        Assert.True(d.Undo());
        Assert.False(d.Config.Profiles[0].Pages[1].BadgeExcludeFromTotal);
    }
}

/// <summary>Le deck respecte <see cref="DeckPage.BadgeEnabled"/> et montre le point d'activité.</summary>
[Collection("ui")]
public class PageBadgeDeckTests
{
    readonly WpfHost _host;
    public PageBadgeDeckTests(WpfHost host) { _host = host; }

    sealed class RaisingWatcher : INotificationWatcher
    {
        public event Action<string, int?>? CountChanged;
        public event Action<string, bool>? ActivityChanged;
        public void Start(IReadOnlySet<string> only) { }
        public void Stop() { }
        public void Refresh() { }
        public void SetBackground(bool background) { }
        public void Raise(string id, int? count) => CountChanged?.Invoke(id, count);
        public void RaiseActivity(string id, bool on) => ActivityChanged?.Invoke(id, on);
    }

    async Task WaitFor(Func<bool> condition, int seconds = 8)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < limit) { _host.Settle(); await Task.Delay(50); }
        Assert.True(condition(), "condition non atteinte à temps");
    }

    [Fact]
    public async Task ADisabledPageTabHasNoBadgeAndActivityShowsADot()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Google Chat", Kind = "web", Url = "https://chat.google.com/" });
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Discord", Kind = "web", Url = "https://discord.com/channels/@me", BadgeEnabled = false });
        cfg.Integrations = new() { ["notify"] = new IntegrationConfig { Enabled = true } };
        _host.Reset(cfg);
        var watcher = new RaisingWatcher();
        var integration = new NotificationIntegration(watcher);
        var manager = new IntegrationManager(new IIntegration[] { integration }, _host.Document, new MemorySecrets());
        await integration.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows(), manager); v.Resize(1600, 900); return v; });
        _host.Settle();
        string? Badge(string name) => _host.Run(() => vm.Tabs.First(t => t.Text == name).Badge);

        watcher.Raise("gchat", 4);
        watcher.Raise("discord", 9);
        await WaitFor(() => Badge("Google Chat") == "4");
        Assert.Null(Badge("Discord"));                                           // badge masqué pour cette page

        watcher.Raise("gchat", 0);
        watcher.RaiseActivity("gchat", true);
        await WaitFor(() => Badge("Google Chat") == NotificationBadge.ActivityText);
        Assert.Equal("Page Google Chat, activité", _host.Run(() => vm.Tabs.First(t => t.Text == "Google Chat").AutomationName));

        var profile = _host.Document.Config.Profiles[0];
        Assert.True(_host.Run(() => _host.Document.SetPageBadgeEnabled(profile.Name, profile.Pages.FindIndex(p => p.Name == "Discord"), true).Ok));   // repris sans redémarrer
        await WaitFor(() => Badge("Discord") == "9", 8);
    }
}
