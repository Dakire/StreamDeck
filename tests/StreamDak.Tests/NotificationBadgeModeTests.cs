using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Notifications;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Badges (ADR 0021) : titre pur, plafond, total, modes, anciens fichiers, message web, activité.</summary>
public class NotificationBadgeModeTests
{
    [Theory]
    [InlineData("(3) Boîte", 3)]
    [InlineData("[3] Boîte", 3)]
    [InlineData("  (12) Chat", 12)]
    [InlineData("(0) Chat", 0)]
    [InlineData("[999999] x", 999_999)]
    [InlineData("(1000000) x", null)]            // plus de 6 chiffres
    [InlineData("(-3) x", null)]
    [InlineData("(3.5) x", null)]
    [InlineData("(3] x", null)]                   // parenthèses mal assorties
    [InlineData("Boîte (3)", null)]                // pas en tête
    [InlineData("()", null)]
    [InlineData("(abc) x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheTitleParserReadsALeadingBoundedCounter(string? title, int? expected) => Assert.Equal(expected, NotificationTitle.Parse(title));

    [Fact]
    public void AVeryLongTitleIsNotScannedBeyondItsLimit() =>
        Assert.Null(NotificationTitle.Parse(new string(' ', NotificationTitle.MaxLength) + "(3) x"));

    [Theory]
    [InlineData("discord", "https://discord.com/channels/@me", "[4] Discord", 4)]
    [InlineData("slack", "https://app.slack.com/client/T0/C0", "[5] général - Slack", 5)]
    [InlineData("gchat", "https://chat.google.com/", "[2] Chat", 2)]
    public void TheBuiltInServicesAlsoAcceptSquareBrackets(string id, string page, string title, int expected) =>
        Assert.Equal(expected, new NotificationProviders().Find(id)!.CountFromTitle(new Uri(page), title));

    [Theory]
    [InlineData(100, true, "99+")]
    [InlineData(0, true, "•")]
    [InlineData(null, true, "•")]
    [InlineData(5, true, "5")]
    [InlineData(0, false, null)]
    public void TheBadgeTextCapsAt99AndShowsADotForPureActivity(int? count, bool activity, string? expected) =>
        Assert.Equal(expected, NotificationBadge.Text(count, activity));

    [Fact]
    public void TheTotalAddsTheCountsAndIgnoresExcludedUnknownAndNegativeOnes()
    {
        Assert.Equal(0, NotificationBadge.Total(Array.Empty<(int?, bool)>()));
        Assert.Equal(12, NotificationBadge.Total(new (int?, bool)[] { (5, false), (7, false), (30, true), (null, false), (-4, false) }));
        Assert.Equal(int.MaxValue, NotificationBadge.Total(Enumerable.Repeat<(int?, bool)>((NotificationTitle.MaxCount, false), 5000)));      // pas de dépassement
    }

    [Theory]
    [InlineData(BadgeMode.Auto, 3, 8, false, 3, false)]            // le titre d'abord
    [InlineData(BadgeMode.Auto, 0, 8, false, 8, false)]            // puis le sélecteur
    [InlineData(BadgeMode.Auto, 0, null, true, 0, true)]           // puis l'activité, sans nombre
    [InlineData(BadgeMode.Auto, 4, null, true, 4, false)]          // un nombre masque l'activité
    [InlineData(BadgeMode.Title, 0, 8, true, 0, false)]            // le sélecteur et le favicon sont ignorés
    [InlineData(BadgeMode.Selector, 9, 6, true, 6, false)]         // le titre est ignoré
    [InlineData(BadgeMode.Selector, 9, null, false, 0, false)]
    public void EachModeReadsItsOwnSources(BadgeMode mode, int title, int? selector, bool activity, int count, bool expectedActivity) =>
        Assert.Equal(new BadgeReading(count, expectedActivity), NotificationBadge.Resolve(mode, title, selector, activity));

    [Theory]
    [InlineData(BadgeMode.Auto)]
    [InlineData(BadgeMode.Title)]
    [InlineData(BadgeMode.Selector)]
    [InlineData(BadgeMode.Off)]
    public void AnUnknownPageIsUnknownInEveryModeAndOffNeverShowsAnything(BadgeMode mode)
    {
        Assert.Equal(new BadgeReading(null, false), NotificationBadge.Resolve(mode, null, 5, true));
        if (mode == BadgeMode.Off) Assert.Equal(new BadgeReading(null, false), NotificationBadge.Resolve(mode, 3, 5, true));
    }

    [Fact]
    public void FaviconActivityIsAChangeFromTheFirstIconSeen()
    {
        var favicon = new FaviconActivity();
        Assert.False(favicon.Observe(null));
        Assert.False(favicon.Observe("https://x/a.ico"));
        Assert.True(favicon.Observe("https://x/dot.ico"));
        Assert.False(favicon.Observe("https://x/a.ico"));
        favicon.Reset();
        Assert.False(favicon.Observe("https://x/dot.ico"));
    }

    // ---------- notifications.json ----------

    static IReadOnlyList<INotificationProvider> Parse(string json, List<string>? warnings = null) => NotificationProviders.Parse(json, w => warnings?.Add(w));

    [Fact]
    public void AnOldFileWithoutBadgeModeStillLoadsAsAuto()
    {
        const string old = """{ "providers": [ { "id": "intranet", "url": "https://intranet.local/", "titleRegex": "\\((\\d+)\\)", "selector": ".b" } ] }""";
        var provider = Parse(old).Single();
        Assert.Equal(BadgeMode.Auto, provider.Mode);
        Assert.NotNull(provider.ObserverScript);
        Assert.Empty(NotificationProviders.ParseOverrides(old, _ => { }));
        Assert.Equal(BadgeMode.Auto, new NotificationProviders().All[0].Mode);
    }

    [Theory]
    [InlineData("auto", BadgeMode.Auto, true)]
    [InlineData("Title", BadgeMode.Title, false)]
    [InlineData("SELECTOR", BadgeMode.Selector, true)]
    [InlineData("off", BadgeMode.Off, false)]
    public void TheModeIsReadWithoutCaseAndTheObserverOnlyExistsWhenTheModeAsksForIt(string text, BadgeMode mode, bool observer)
    {
        var provider = Parse($$"""{ "providers": [ { "id": "s", "url": "https://s.local/", "titleRegex": "x", "selector": ".b", "badgeMode": "{{text}}" } ] }""").Single();
        Assert.Equal(mode, provider.Mode);
        Assert.Equal(observer, provider.ObserverScript != null);
        Assert.Equal(observer, provider.CountScript != null);
    }

    [Fact]
    public void AnInvalidModeOrASelectorModeWithoutSelectorIgnoresTheEntry()
    {
        var warnings = new List<string>();
        var providers = Parse("""
            { "providers": [
              { "id": "a", "url": "https://a.local/", "titleRegex": "x", "badgeMode": "bizarre" },
              { "id": "b", "url": "https://b.local/", "titleRegex": "x", "badgeMode": 3 },
              { "id": "c", "url": "https://c.local/", "titleRegex": "x", "badgeMode": "selector" },
              { "id": "d", "url": "https://d.local/", "badgeMode": "off" }
            ] }
            """, warnings);
        Assert.Equal(new[] { "d" }, providers.Select(p => p.Id));
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void PerServiceSettingsApplyToBuiltInServicesToo()
    {
        const string json = """{ "badges": { "gmail": { "badgeMode": "off" }, "slack": { "badgeMode": "selector", "selector": "span.c", "attribute": "data-n" }, "x y": {}, "teams": { "badgeMode": "nope" } } }""";
        var warnings = new List<string>();
        var overrides = NotificationProviders.ParseOverrides(json, warnings.Add);
        Assert.Equal(2, warnings.Count);
        var providers = new NotificationProviders(null, overrides);
        Assert.Equal(BadgeMode.Off, providers.Find("gmail")!.Mode);
        var slack = providers.Find("slack")!;
        Assert.Equal(BadgeMode.Selector, slack.Mode);
        Assert.NotNull(slack.ObserverScript);
        Assert.Equal(BadgeMode.Auto, providers.Find("discord")!.Mode);
        Assert.Equal(BadgeMode.Auto, providers.Find("teams")!.Mode);
        Assert.Equal(5, slack.CountFromTitle(new Uri("https://app.slack.com/client"), "(5) x"));      // adresses et motif de titre conservés
    }

    [Fact]
    public void TheObserverScriptKeepsTheSelectorAsAStringAndOnlyPostsTheBadgeMessage()
    {
        var script = NotificationProvider.BuildObserverScript("a\"); alert(1); (\"", null);
        Assert.Contains("MutationObserver", script);
        Assert.Contains("500", script);
        Assert.Contains("chrome.webview.postMessage", script);
        Assert.DoesNotContain("alert(1); (\"", script);
        Assert.DoesNotContain("window.Notification", script);
        Assert.DoesNotContain("fetch", script);
    }

    // ---------- message web ----------

    static NotificationProvider Auto => new("css", "Site", "https://site.local/", new[] { "site.local" }, null, ".b");

    [Theory]
    [InlineData("""{"type":"streamdak.badge","count":7}""", true, 7)]
    [InlineData("""{"count":0,"type":"streamdak.badge"}""", true, 0)]
    [InlineData("""{"type":"streamdak.badge","count":999999}""", true, 999_999)]
    [InlineData("""{"type":"streamdak.badge","count":-1}""", false, 0)]
    [InlineData("""{"type":"streamdak.badge","count":1000000}""", false, 0)]
    [InlineData("""{"type":"streamdak.badge","count":2.5}""", false, 0)]
    [InlineData("""{"type":"streamdak.badge","count":"7"}""", false, 0)]
    [InlineData("""{"type":"autre","count":7}""", false, 0)]
    [InlineData("""{"count":7}""", false, 0)]
    [InlineData("""{"type":"streamdak.badge","count":7,"extra":1}""", false, 0)]
    [InlineData("""[7]""", false, 0)]
    [InlineData("7", false, 0)]
    [InlineData("\"streamdak.badge\"", false, 0)]
    [InlineData("{ pas du json", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void OnlyTheExactBadgeMessageIsAccepted(string? json, bool ok, int expected)
    {
        Assert.Equal(ok, NotificationMessage.TryParse(json, out var count));
        Assert.Equal(expected, count);
    }

    [Fact]
    public void AMessageFromAnotherHostOrWithoutTheObserverModeIsIgnored()
    {
        const string good = """{"type":"streamdak.badge","count":4}""";
        Assert.True(NotificationMessage.TryRead(Auto, "https://site.local/boite", good, out var n));
        Assert.Equal(4, n);
        Assert.True(NotificationMessage.TryRead(Auto, "https://sub.site.local/", good, out _));
        Assert.False(NotificationMessage.TryRead(Auto, "https://site.local.evil.net/", good, out _));      // hôte qui ressemble
        Assert.False(NotificationMessage.TryRead(Auto, "https://evil.net/", good, out _));
        Assert.False(NotificationMessage.TryRead(Auto, "pas une adresse", good, out _));
        Assert.False(NotificationMessage.TryRead(Auto, null, good, out _));
        var title = Auto.With(BadgeMode.Title, ".b", null);
        var off = Auto.With(BadgeMode.Off, ".b", null);
        Assert.False(NotificationMessage.TryRead(title, "https://site.local/", good, out _));
        Assert.False(NotificationMessage.TryRead(off, "https://site.local/", good, out _));
        Assert.Null(title.ObserverScript);
        Assert.Null(off.ObserverScript);
    }

    [Fact]
    public async Task ActivityIsPublishedAsAValueAndAnOldWatcherWithoutItStillWorks()
    {
        var watcher = new ActivityWatcher();
        var integration = new NotificationIntegration(watcher);
        await integration.ConnectAsync(new IntegrationSettings { Values = new Dictionary<string, string> { ["services"] = "" } }, CancellationToken.None);
        watcher.RaiseActivity("gmail", true);
        Assert.Equal("1", integration.Values[NotificationIntegration.ActivityKey("gmail")]);
        watcher.RaiseActivity("gmail", false);
        Assert.Equal("", integration.Values["gmail.activity"]);
        await integration.DisconnectAsync();
        Assert.Equal(0, watcher.ActivitySubscribers);

        INotificationWatcher legacy = new LegacyWatcher();                 // aucune implémentation d'ActivityChanged : membre par défaut sans effet
        legacy.ActivityChanged += (_, _) => { };
    }

    sealed class LegacyWatcher : INotificationWatcher
    {
        public event Action<string, int?>? CountChanged { add { } remove { } }
        public void Start(IReadOnlySet<string> only) { }
        public void Stop() { }
        public void Refresh() { }
        public void SetBackground(bool background) { }
    }

    sealed class ActivityWatcher : INotificationWatcher
    {
        Action<string, bool>? _activity;
        public int ActivitySubscribers => _activity?.GetInvocationList().Length ?? 0;
        public event Action<string, int?>? CountChanged { add { } remove { } }
        public event Action<string, bool>? ActivityChanged { add => _activity += value; remove => _activity -= value; }
        public void Start(IReadOnlySet<string> only) { }
        public void Stop() { }
        public void Refresh() { }
        public void SetBackground(bool background) { }
        public void RaiseActivity(string id, bool on) => _activity?.Invoke(id, on);
    }
}
