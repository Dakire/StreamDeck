using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.Notifications;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Badges de notifications : lecture des compteurs dans les titres, services configurables, texte du badge.</summary>
public class NotificationCoreTests
{
    static readonly NotificationProviders Providers = new();

    static int? Count(string providerId, string page, string title) => Providers.Find(providerId)!.CountFromTitle(new Uri(page), title);

    [Theory]
    [InlineData("Boîte de réception (12) - moi@gmail.com - Gmail", 12)]
    [InlineData("Inbox (3) - moi@gmail.com - Gmail", 3)]
    [InlineData("Inbox - moi@gmail.com - Gmail", 0)]
    [InlineData("Gmail", 0)]
    [InlineData("Inbox (2000000) - Gmail", 0)]            // plus de 6 chiffres : pas un compteur
    public void GmailReadsTheUnreadCountInTheTitle(string title, int expected) =>
        Assert.Equal(expected, Count("gmail", "https://mail.google.com/mail/u/0/#inbox", title));

    [Theory]
    [InlineData("gchat", "https://chat.google.com/u/0/", "(4) Chat", 4)]
    [InlineData("gchat", "https://chat.google.com/", "Chat", 0)]
    [InlineData("discord", "https://discord.com/channels/@me", "(3) Discord | #general", 3)]
    [InlineData("discord", "https://discord.com/channels/1/2", "Discord | #general", 0)]
    [InlineData("teams", "https://teams.microsoft.com/v2/", "(7) Chat | Microsoft Teams", 7)]
    [InlineData("teams", "https://teams.live.com/", "Microsoft Teams", 0)]
    [InlineData("slack", "https://app.slack.com/client/T0/C0", "(5) général - Équipe - Slack", 5)]
    [InlineData("slack", "https://app.slack.com/client/T0/C0", "* général - Équipe - Slack", 1)]    // non lu sans compteur : au moins un
    [InlineData("slack", "https://app.slack.com/client/T0/C0", "! général - Équipe - Slack", 1)]
    [InlineData("slack", "https://app.slack.com/client/T0/C0", "général - Équipe - Slack", 0)]
    public void EachServiceReadsItsOwnTitleFormat(string id, string page, string title, int expected) => Assert.Equal(expected, Count(id, page, title));

    [Theory]
    [InlineData("https://accounts.google.com/signin", "(3) Connexion")]            // page de connexion : pas du service
    [InlineData("https://example.org/", "(3) Gmail")]
    [InlineData("https://mail.google.com.attacker.net/", "(3) Gmail")]              // un hôte qui ressemble au vrai
    [InlineData("https://notmail.google.com.evil/", "(3) Gmail")]
    public void AnotherPageGivesNoCountSoNoBadgeIsShown(string page, string title) => Assert.Null(Count("gmail", page, title));

    [Theory]
    [InlineData("https://mail.google.com/mail/u/0/#inbox", "gmail")]
    [InlineData("https://chat.google.com/", "gchat")]
    [InlineData("https://discord.com/channels/@me", "discord")]
    [InlineData("https://discordapp.com/", "discord")]
    [InlineData("https://teams.microsoft.com/", "teams")]
    [InlineData("https://app.slack.com/client", "slack")]
    [InlineData("https://monequipe.slack.com/", "slack")]
    public void AButtonAddressSelectsItsService(string url, string expected) => Assert.Equal(expected, Providers.ForUrl(url)?.Id);

    [Theory]
    [InlineData("https://example.org/")]
    [InlineData("https://notslack.com/")]
    [InlineData("https://slack.com.evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/x")]
    [InlineData("pas une adresse")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherAddressesNeverGetABadge(string? url) => Assert.Null(Providers.ForUrl(url));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-4, null)]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(123456, "99+")]
    public void TheBadgeShowsTheCountAndCapsAt99(int? count, string? expected) => Assert.Equal(expected, NotificationBadge.Text(count));

    [Fact]
    public void TheWatchedAddressesAreTheUrlStepsWidgetsAndWebPagesOfEveryProfile()
    {
        var cfg = ConfigStore.DefaultConfig();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Clear();
        page.Buttons.Add(new DeckButton { Label = "Mail", Actions = { new DeckAction { Type = "url", Value = "https://mail.google.com/" } } });
        page.Buttons.Add(new DeckButton { Label = "Touche", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } });
        page.Buttons.Add(new DeckButton { Label = "Chat", Widget = "web", WidgetOptions = new() { ["url"] = "https://chat.google.com/" } });
        page.Buttons.Add(new DeckButton { Label = "Dossier", IsFolder = true, Children = new() { new DeckButton { Actions = { new DeckAction { Type = "url", Value = "https://discord.com/" } } } } });
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Web", Kind = "web", Url = "https://teams.microsoft.com/" });
        var hosts = NotificationBadge.Urls(cfg).Select(u => u.Host).ToList();
        Assert.All(new[] { "chat.google.com", "discord.com", "mail.google.com", "teams.microsoft.com" }, h => Assert.Contains(h, hosts));
        Assert.DoesNotContain("copy", string.Join(" ", hosts));            // l'étape « touche » n'est pas une adresse
    }

    // ---------- services définis par l'utilisateur ----------

    static IReadOnlyList<INotificationProvider> Parse(string json, List<string>? warnings = null) => NotificationProviders.Parse(json, w => warnings?.Add(w));

    [Fact]
    public void AGenericServiceReadsATitleRegexWithItsFirstGroup()
    {
        var provider = Parse("""{ "providers": [ { "id": "intranet", "name": "Intranet", "url": "https://intranet.local/", "titleRegex": "Messages \\((\\d+)\\)" } ] }""").Single();
        Assert.Equal("Intranet", provider.Name);
        Assert.True(provider.Matches(new Uri("https://intranet.local/boite")));
        Assert.False(provider.Matches(new Uri("https://autre.local/")));
        Assert.Equal(9, provider.CountFromTitle(new Uri("https://intranet.local/x"), "Messages (9) | Intranet"));
        Assert.Equal(0, provider.CountFromTitle(new Uri("https://intranet.local/x"), "Accueil"));
        Assert.Null(provider.CountScript);
    }

    [Fact]
    public void AGenericSelectorBuildsAReadOnlyScriptWhereTheSelectorIsJustAString()
    {
        var provider = Parse("""{ "providers": [ { "id": "tickets", "url": "https://help.local/", "selector": "span.badge[data-x=\"1\"]", "attribute": "data-count" } ] }""").Single();
        Assert.Equal(TimeSpan.FromSeconds(60), provider.ScriptInterval);
        Assert.Contains("document.querySelectorAll(", provider.CountScript);
        Assert.DoesNotContain("data-x=\"1\"", provider.CountScript);                          // les guillemets du sélecteur sont échappés : jamais du code
        Assert.Contains("\\u0022", provider.CountScript);
        Assert.DoesNotContain("fetch", provider.CountScript);
        Assert.DoesNotContain("XMLHttpRequest", provider.CountScript);
    }

    [Fact]
    public void InvalidDefinitionsAreIgnoredWithAWarningAndTheValidOnesKept()
    {
        var warnings = new List<string>();
        var providers = Parse("""
            { "providers": [
              { "id": "Bon Id", "url": "https://a.local/", "titleRegex": "x" },
              { "id": "gmail", "url": "https://a.local/", "titleRegex": "x" },
              { "id": "ftp", "url": "ftp://a.local/", "titleRegex": "x" },
              { "id": "regex", "url": "https://a.local/", "titleRegex": "(" },
              { "id": "vide", "url": "https://a.local/" },
              { "id": "attr", "url": "https://a.local/", "selector": "a", "attribute": "on click" },
              { "id": "host", "url": "https://a.local/", "titleRegex": "x", "hosts": ["bad host"] },
              { "id": "long", "url": "https://a.local/", "selector": "%LONG%" },
              { "id": "ok", "url": "https://ok.local/", "titleRegex": "\\((\\d+)\\)" },
              { "id": "ok", "url": "https://dup.local/", "titleRegex": "x" }
            ] }
            """.Replace("%LONG%", new string('a', 201)), warnings);
        Assert.Equal(new[] { "ok" }, providers.Select(p => p.Id));
        Assert.Equal(9, warnings.Count);
        Assert.All(warnings, w => Assert.Contains("notifications.json", w));
    }

    [Fact]
    public void NoMoreThanTenGenericServicesAreRead()
    {
        var items = string.Join(",", Enumerable.Range(0, 15).Select(i => $$"""{ "id": "s{{i}}", "url": "https://s{{i}}.local/", "titleRegex": "x" }"""));
        Assert.Equal(NotificationProviders.MaxCustom, Parse("{ \"providers\": [" + items + "] }").Count);
    }

    [Fact]
    public void AnEvilRegexTimesOutInsteadOfFreezingAndGivesNoCount()
    {
        var provider = Parse("""{ "providers": [ { "id": "evil", "url": "https://a.local/", "titleRegex": "^(a+)+$" } ] }""").Single();
        var title = new string('a', 60) + "!";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(provider.CountFromTitle(new Uri("https://a.local/"), title));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TheFileIsOptionalBoundedAndNeverThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sd-notify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(NotificationProviders.BuiltIn.Count, NotificationProviders.Load(dir).All.Count);                  // absent
            var path = Path.Combine(dir, NotificationProviders.FileName);
            File.WriteAllText(path, "{ pas du json");
            Assert.Equal(NotificationProviders.BuiltIn.Count, NotificationProviders.Load(dir).All.Count);                  // illisible
            File.WriteAllText(path, new string(' ', NotificationProviders.MaxFileBytes + 1));
            Assert.Equal(NotificationProviders.BuiltIn.Count, NotificationProviders.Load(dir).All.Count);                  // trop gros
            File.WriteAllText(path, """{ "providers": [ { "id": "mien", "url": "https://x.local/", "titleRegex": "\\((\\d+)\\)" } ] }""");
            var loaded = NotificationProviders.Load(dir);
            Assert.Equal("mien", loaded.All[^1].Id);
            Assert.Equal("mien", loaded.ForUrl("https://x.local/page")?.Id);
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Le connecteur « Notifications » : il pilote le suivi et publie les compteurs comme valeurs d'état.</summary>
public class NotificationIntegrationTests
{
    sealed class FakeWatcher : INotificationWatcher
    {
        public event Action<string, int?>? CountChanged;
        public IReadOnlySet<string>? Started;
        public int Stops, Refreshes;
        public bool? Background;
        public void Start(IReadOnlySet<string> only) => Started = only;
        public void Stop() => Stops++;
        public void Refresh() => Refreshes++;
        public void SetBackground(bool background) => Background = background;
        public void Raise(string id, int? count) => CountChanged?.Invoke(id, count);
    }

    static IntegrationSettings Settings(string services = "") => new() { Values = new Dictionary<string, string> { ["services"] = services } };

    [Fact]
    public async Task ConnectingStartsTheWatcherWithTheChosenServicesAndPublishesTheCounts()
    {
        var watcher = new FakeWatcher();
        var integration = new NotificationIntegration(watcher);
        await integration.ConnectAsync(Settings("Gmail, discord;teams"), CancellationToken.None);
        Assert.Equal(new[] { "discord", "gmail", "teams" }, watcher.Started!.OrderBy(s => s));
        Assert.Equal(ConnectionState.Connected, integration.State);

        watcher.Raise("gmail", 12);
        Assert.Equal("12", integration.Values["gmail"]);
        watcher.Raise("gmail", null);
        Assert.Equal("", integration.Values["gmail"]);           // inconnu : valeur vide, donc aucun badge

        await integration.DisconnectAsync();
        Assert.Equal(1, watcher.Stops);
        watcher.Raise("gmail", 5);                                 // plus abonné
        Assert.Empty(integration.Values);
    }

    [Fact]
    public async Task AnEmptyServiceListMeansEveryServiceWithAButton()
    {
        var watcher = new FakeWatcher();
        await new NotificationIntegration(watcher).ConnectAsync(Settings(), CancellationToken.None);
        Assert.Empty(watcher.Started!);
    }

    [Fact]
    public async Task TheRefreshCommandAndTheBackgroundStateReachTheWatcher()
    {
        var watcher = new FakeWatcher();
        var integration = new NotificationIntegration(watcher);
        await integration.ConnectAsync(Settings(), CancellationToken.None);
        Assert.Equal("Badges actualisés", await integration.ExecuteAsync("refresh", null, default));
        Assert.Equal(1, watcher.Refreshes);
        integration.SetBackground(true);
        Assert.True(watcher.Background);
        await Assert.ThrowsAsync<ArgumentException>(() => integration.ExecuteAsync("autre", null, default));
    }

    [Fact]
    public async Task WithoutAWatcherTheConnectorRefusesToConnectAndSaysWhy()
    {
        var integration = new NotificationIntegration();
        await Assert.ThrowsAsync<InvalidOperationException>(() => integration.ConnectAsync(Settings(), CancellationToken.None));
        Assert.Equal(ConnectionState.Error, integration.State);
        Assert.Equal("notify", integration.Id);
    }
}

/// <summary>Le deck pose le badge rouge sur les touches « site web » et le met à jour tout seul.</summary>
[Collection("ui")]
public class DeckNotificationBadgeTests
{
    readonly WpfHost _host;
    public DeckNotificationBadgeTests(WpfHost host) { _host = host; }

    sealed class RaisingWatcher : INotificationWatcher
    {
        public event Action<string, int?>? CountChanged;
        public void Start(IReadOnlySet<string> only) { }
        public void Stop() { }
        public void Refresh() { }
        public void SetBackground(bool background) { }
        public void Raise(string id, int? count) => CountChanged?.Invoke(id, count);
    }

    static DeckConfig Config()
    {
        var cfg = ConfigStore.DefaultConfig();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Clear();
        page.Buttons.Add(new DeckButton { Label = "Gmail", Icon = "Mail24", Actions = { new DeckAction { Type = "url", Value = "https://mail.google.com/" } } });
        page.Buttons.Add(new DeckButton { Label = "Autre", Icon = "Globe24", Actions = { new DeckAction { Type = "url", Value = "https://example.org/" } } });
        page.Buttons.Add(new DeckButton { Label = "Copier", Icon = "Copy24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } });
        cfg.Integrations = new() { ["notify"] = new IntegrationConfig { Enabled = true } };
        return cfg;
    }

    [Fact]
    public async Task OnlyAServiceButtonShowsTheBadgeAndItFollowsTheCount()
    {
        _host.Reset(Config());
        var watcher = new RaisingWatcher();
        var integration = new NotificationIntegration(watcher);
        var manager = new IntegrationManager(new IIntegration[] { integration }, _host.Document, new MemorySecrets());
        await integration.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows(), manager); v.Resize(1600, 900); return v; });
        _host.Settle();
        DeckTileViewModel Tile(string label) => _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Button?.Label == label));

        Assert.Null(_host.Run(() => Tile("Gmail").Face.Badge));                       // rien de non lu : pas de badge
        watcher.Raise("gmail", 5);
        await WaitFor(() => _host.Run(() => Tile("Gmail").Face.Badge) == "5");
        Assert.Contains("5 notification", _host.Run(() => Tile("Gmail").AccessibleName));
        Assert.Null(_host.Run(() => Tile("Autre").Face.Badge));                       // autre site : jamais
        Assert.Null(_host.Run(() => Tile("Copier").Face.Badge));

        watcher.Raise("gmail", 250);
        await WaitFor(() => _host.Run(() => Tile("Gmail").Face.Badge) == "99+");
        watcher.Raise("gmail", null);                                                  // session fermée : le badge disparaît
        await WaitFor(() => _host.Run(() => Tile("Gmail").Face.Badge) == null);
    }

    [Fact]
    public void TheBadgeIsDrawnInRedWithWhiteTextInBothThemesAndNeverCoversTheStepBadge()
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.Reset(Config());
            _host.SetTheme(theme);
            var win = _host.Run(() =>
            {
                var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(20) };
                foreach (var (label, badge, steps) in new[] { ("Gmail", "3", 1), ("Discord", "99+", 1), ("Teams", "12", 3), ("Chat", (string?)null, 1) })
                {
                    var button = new DeckButton { Label = label, Icon = "Mail24", Color = "#1A73E8" };
                    for (int i = 0; i < steps; i++) button.Actions.Add(new DeckAction { Type = "url", Value = "https://example.org/" });
                    row.Children.Add(new Controls.TileFace { Width = 200, Height = 180, Margin = new System.Windows.Thickness(8), Face = TileFaceData.From(button, badge: badge) });
                }
                return new System.Windows.Window { Content = row };
            });
            _host.Render(win, $"deck-badge-notifications-{theme}", 960, 240);
            _host.Run(() =>
            {
                var visible = WpfHost.Descendants<System.Windows.Controls.Border>((System.Windows.DependencyObject)win.Content)
                    .Where(b => b.Name == "NotifyBadge" && b.Visibility == System.Windows.Visibility.Visible).ToList();
                Assert.Equal(new[] { "3", "99+", "12" }, visible.Select(b => ((System.Windows.Controls.TextBlock)b.Child).Text));
                Assert.All(visible, b => Assert.Equal(System.Windows.Media.Color.FromRgb(0xD9, 0x2D, 0x20), ((System.Windows.Media.SolidColorBrush)b.Background).Color));
                Assert.True(Theme.Contrast("#FFFFFF", "#D92D20") >= 4.5);
                Assert.True(visible[1].ActualWidth > visible[0].ActualWidth, "« 99+ » est plus large que « 3 »");
                // La touche à plusieurs étapes montre les deux badges côte à côte, sans recouvrement
                var faces = WpfHost.Descendants<Controls.TileFace>((System.Windows.DependencyObject)win.Content).ToList();
                var steps = WpfHost.Descendants<System.Windows.Controls.Border>(faces[2]).Single(b => b.Name == "StepBadge");
                var notify = WpfHost.Descendants<System.Windows.Controls.Border>(faces[2]).Single(b => b.Name == "NotifyBadge");
                var stepBox = steps.TransformToAncestor(faces[2]).TransformBounds(new System.Windows.Rect(steps.RenderSize));
                var notifyBox = notify.TransformToAncestor(faces[2]).TransformBounds(new System.Windows.Rect(notify.RenderSize));
                Assert.False(stepBox.IntersectsWith(notifyBox), $"{stepBox} recouvre {notifyBox}");
            });
            _host.Run(win.Close);
        }
    }

    [Fact]
    public async Task AWebPageTabShowsTheBadgeAndNavigationToItNeverAnimatesTheFrame()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Google Chat", Kind = "web", Url = "https://chat.google.com/" });
        cfg.Integrations = new() { ["notify"] = new IntegrationConfig { Enabled = true } };
        _host.Reset(cfg);
        var watcher = new RaisingWatcher();
        var integration = new NotificationIntegration(watcher);
        var manager = new IntegrationManager(new IIntegration[] { integration }, _host.Document, new MemorySecrets());
        await integration.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows(), manager); v.Resize(1600, 900); return v; });
        _host.Settle();
        string? Badge(string name) => _host.Run(() => vm.Tabs.First(t => t.Text == name).Badge);

        Assert.Null(Badge("Google Chat"));
        watcher.Raise("gchat", 7);
        await WaitFor(() => Badge("Google Chat") == "7");
        Assert.Null(Badge(vm.Tabs[0].Text));                                        // les autres onglets n'ont rien
        Assert.Contains("7 notification", _host.Run(() => vm.Tabs.First(t => t.Text == "Google Chat").AutomationName));

        Assert.False(_host.Run(() => vm.ShowsWebPage));                             // page de boutons : le glissement est permis
        _host.Run(() => ((StreamDak.Actions.INavigator)vm).GoToPage("Google Chat"));
        Assert.True(_host.Run(() => vm.ShowsWebPage));                              // page web : jamais d'animation du cadre
    }

    async Task WaitFor(Func<bool> condition, int seconds = 8)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < limit) { _host.Settle(); await Task.Delay(50); }
        Assert.True(condition(), "condition non atteinte à temps");
    }
}

/// <summary>Le suivi réel : une page WebView2 cachée par service, compteur lu sur événement, mise en veille, fermeture (vrai WebView2, serveur local).</summary>
[Collection("ui")]
public class WebNotificationWatcherTests
{
    readonly WpfHost _host;
    public WebNotificationWatcherTests(WpfHost host) { _host = host; }

    sealed class Site : IDisposable
    {
        readonly HttpListener _listener = new();
        readonly CancellationTokenSource _stop = new();
        public int Port { get; }

        public Site(Func<string, string> page)
        {
            using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                    var bytes = Encoding.UTF8.GetBytes(page(ctx.Request.Url!.AbsolutePath));
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose() { _stop.Cancel(); _listener.Close(); }
    }

    static DeckConfig ConfigFor(int port, string path = "/")
    {
        var cfg = ConfigStore.DefaultConfig();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Clear();
        page.Buttons.Add(new DeckButton { Label = "Site", Actions = { new DeckAction { Type = "url", Value = $"http://localhost:{port}{path}" } } });
        return cfg;
    }

    static NotificationProvider TitleProvider(int port) =>
        new NotificationProvider("local", "Site local", $"http://localhost:{port}/", new[] { "localhost" }, @"\((\d+)\)");

    WebNotificationWatcher NewWatcher(INotificationProvider provider, List<(string Id, int? Count)> seen)
    {
        var watcher = _host.Run(() => new WebNotificationWatcher(_host.Document, new NotificationProviders(new[] { provider })));
        watcher.CountChanged += (id, count) => { lock (seen) seen.Add((id, count)); };
        return watcher;
    }

    static async Task WaitFor(Func<bool> condition, int seconds = 25)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(50);
        Assert.True(condition(), "condition non atteinte à temps");
    }

    static bool Has(List<(string Id, int? Count)> seen, int? count) { lock (seen) return seen.Any(s => s.Count == count); }

    [Fact]
    public async Task TheCountIsReadFromTheTitleAndFollowsItWithoutPolling()
    {
        using var site = new Site(_ => "<html><head><title>(3) Boîte</title></head><body><script>setTimeout(function(){document.title='(7) Boîte'},1200)</script></body></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 3));
            await WaitFor(() => Has(seen, 7));                                       // le titre change : l'événement suffit
            Assert.Equal(new[] { "local" }, watcher.Watched);
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task ACssSelectorServiceSumsTheNumbersOfTheBadgesOnThePage()
    {
        using var site = new Site(_ => "<html><head><title>Messagerie</title></head><body><span class='b'>2</span><span class='b' data-n='x'>5</span><i class='b'></i></body></html>");
        _host.Reset(ConfigFor(site.Port));
        var provider = new NotificationProvider("css", "Site CSS", $"http://localhost:{site.Port}/", new[] { "localhost" }, null, ".b");
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(provider, seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 8));                                       // 2 + 5 + 1 (un badge sans nombre compte pour 1)
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task TheObserverFollowsThePageWithoutPollingAndTheTitleModeInjectsNothing()
    {
        using var site = new Site(_ => "<html><head><title>Messagerie</title></head><body><script>setTimeout(function(){var s=document.createElement('span');s.className='b';s.textContent='6';document.body.appendChild(s)},1500)</script></body></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(new NotificationProvider("obs", "Observé", $"http://localhost:{site.Port}/", new[] { "localhost" }, null, ".b"), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 6));                                       // le DOM change : l'observateur envoie le nombre (60 s d'intervalle sinon)
        }
        finally { await watcher.StopAsync(); }

        var quiet = new List<(string, int?)>();
        var titleOnly = NewWatcher(new NotificationProvider("titre", "Titre", $"http://localhost:{site.Port}/", new[] { "localhost" }, null, ".b", null, BadgeMode.Title), quiet);
        try
        {
            titleOnly.Start(new HashSet<string>());
            await WaitFor(() => quiet.Count > 0);
            await Task.Delay(2500);
            lock (quiet) Assert.DoesNotContain(quiet, s => s.Item2 == 6);           // mode titre : le sélecteur est ignoré
        }
        finally { await titleOnly.StopAsync(); }
    }

    const string LabelPage = "<html><head><title>Chat</title></head><body>" +
        "<div aria-label='3 messages non lus'><span aria-label='3 messages non lus'></span></div>" +       // parent et enfant : compté une fois
        "<button title='2 unread'></button>" +
        "<span aria-label='0 unread'></span><span aria-label='-4 non lus'></span><span aria-label='1234567 unread'></span>" +   // valeurs invalides
        "<span aria-label='Messages non lus'></span><span aria-label='Salut, 9 non lus ? voici un très long texte qui ressemble à un message de conversation et qui dépasse largement la limite de cent vingt caractères permise'></span>" +
        "</body></html>";

    static NotificationProvider LabelProvider(int port, bool favicon = false) =>
        new("chatlocal", "Chat local", $"http://localhost:{port}/", new[] { "localhost" }, @"^\s*\((\d+)\)", unreadLabels: true, showFavicon: favicon);

    [Fact]
    public async Task UnreadLabelsAreSummedWithoutDoubleCountingAndInvalidOnesAreIgnored()
    {
        using var site = new Site(_ => LabelPage);
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(LabelProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 5));                                        // 3 (parent/enfant) + 2 ; le reste est ignoré
            lock (seen) Assert.DoesNotContain(seen, s => s.Item2 is > 5);
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task AFaviconChangeWithATitleWithoutNumberShowsNoBadgeByDefault()
    {
        const string page = "<html><head><title>Chat</title><link rel='icon' href='/a.ico'></head><body>" +
            "<script>setTimeout(function(){document.querySelector('link').href='/b.ico?v=2'},1500)</script></body></html>";
        using var site = new Site(_ => page);
        _host.Reset(ConfigFor(site.Port));

        var counts = new List<(string, int?)>();
        var quiet = NewWatcher(LabelProvider(site.Port), counts);
        var activity = new List<bool>();
        quiet.ActivityChanged += (_, on) => { lock (activity) activity.Add(on); };
        try
        {
            quiet.Start(new HashSet<string>());
            await WaitFor(() => Has(counts, 0));                                      // la page est lue : « Chat » sans nombre = 0
            await Task.Delay(4000);                                                   // le favicon a changé entre-temps
            lock (activity) Assert.DoesNotContain(true, activity);                    // aucun point par défaut
            lock (counts) Assert.DoesNotContain(counts, s => s.Item2 is > 0);
        }
        finally { await quiet.StopAsync(); }
    }

    [Fact]
    public async Task ModeOffNeverOpensAPage()
    {
        using var site = new Site(_ => "<html><head><title>(2) x</title></head></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(new NotificationProvider("eteint", "Éteint", $"http://localhost:{site.Port}/", new[] { "localhost" }, @"\((\d+)\)", null, null, BadgeMode.Off), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await Task.Delay(1500);
            Assert.Empty(watcher.Watched);
            lock (seen) Assert.Empty(seen);
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task AnInjectionAttemptInTheSelectorStaysAString()
    {
        using var site = new Site(_ => "<html><head><title>x</title></head><body><script>window.hacked = false</script></body></html>");
        _host.Reset(ConfigFor(site.Port));
        var provider = new NotificationProvider("evil", "Mal", $"http://localhost:{site.Port}/", new[] { "localhost" }, null, "a\"); document.title='(99) pwn'; (\"");
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(provider, seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => seen.Count > 0);
            await Task.Delay(1000);
            lock (seen) Assert.DoesNotContain(seen, s => s.Item2 == 99);          // le sélecteur invalide ne s'exécute pas : au pire aucun compteur
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task LeavingTheServicesDomainGivesNoCountAndClosingThePageReportsUnknown()
    {
        var html = new Site(path => path == "/login" ? "<html><head><title>(5) Connexion</title></head></html>"
            : "<html><head><title>(4) Boîte</title></head><body><script>setTimeout(function(){location.href='http://127.0.0.1:' + location.port + '/login'},1500)</script></body></html>");
        _host.Reset(ConfigFor(html.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(html.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 4));
            await WaitFor(() => { lock (seen) return seen.Count > 0 && seen[^1].Item2 == null; });   // redirigé vers 127.0.0.1 : plus le domaine du service
            lock (seen) Assert.DoesNotContain(seen, s => s.Item2 == 5);                                // le titre de l'autre domaine n'est jamais lu comme un compteur
        }
        finally { await watcher.StopAsync(); html.Dispose(); }
    }

    [Fact]
    public async Task RemovingTheButtonStopsTheWatchAndReportsUnknown()
    {
        using var site = new Site(_ => "<html><head><title>(2) x</title></head></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 2));
            _host.Run(() => _host.Document.ClearButton(new Place(_host.Document.Config.Profiles[0].Name, 0), 0));
            await WaitFor(() => watcher.Watched.Count == 0);
            await WaitFor(() => { lock (seen) return seen[^1].Item2 == null; });
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task AServiceWithoutAnyButtonIsNeverOpened()
    {
        using var site = new Site(_ => "<html><head><title>(2) x</title></head></html>");
        var cfg = ConfigStore.DefaultConfig();
        _host.Reset(cfg);
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await Task.Delay(1500);
            Assert.Empty(watcher.Watched);
            lock (seen) Assert.Empty(seen);
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task TheChosenServicesLimitTheWatch()
    {
        using var site = new Site(_ => "<html><head><title>(2) x</title></head></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string> { "gmail" });                           // « local » n'est pas dans la liste
            await Task.Delay(1500);
            Assert.Empty(watcher.Watched);
        }
        finally { await watcher.StopAsync(); }
    }

    [Fact]
    public async Task ASleepingDeckFreezesTheWatchedPageAndWakingItCatchesUp()
    {
        using var site = new Site(_ => "<html><head><title>(1) x</title></head><body><script>var n=1;setInterval(function(){n++;document.title='('+n+') x'},250)</script></body></html>");
        _host.Reset(ConfigFor(site.Port));
        var seen = new List<(string, int?)>();
        var watcher = NewWatcher(TitleProvider(site.Port), seen);
        try
        {
            watcher.Start(new HashSet<string>());
            await WaitFor(() => Has(seen, 3));
            watcher.SetBackground(true);
            await Task.Delay(2500);                                                    // la mise en veille s'installe
            int frozen;
            lock (seen) frozen = seen.Count;
            await Task.Delay(2000);
            lock (seen) Assert.True(seen.Count - frozen <= 1, $"{seen.Count - frozen} lectures pendant le sommeil");
            watcher.SetBackground(false);
            await WaitFor(() => { lock (seen) return seen.Count > frozen + 2; });      // la page repart et le compteur suit
        }
        finally { await watcher.StopAsync(); }
    }

    [Theory]
    [InlineData("7", true, 7)]
    [InlineData("7.6", true, 8)]
    [InlineData("-3", true, 0)]
    [InlineData("null", false, 0)]
    [InlineData("\"abc\"", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("1e400", false, 0)]
    public void AScriptResultIsAWholeNonNegativeNumberOrNothing(string json, bool ok, int expected)
    {
        Assert.Equal(ok, WebNotificationWatcher.TryNumber(json, out var value));
        if (ok) Assert.Equal(expected, value);
    }
}
