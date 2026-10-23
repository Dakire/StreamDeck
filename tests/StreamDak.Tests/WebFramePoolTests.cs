using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Règle « même site » des autorisations d'un cadre web (caméra, micro, notifications).</summary>
public class WebPermissionsTests
{
    [Theory]
    [InlineData("calendar.google.com", "google.com")]
    [InlineData("meet.google.com", "google.com")]
    [InlineData("a.b.example.org", "example.org")]
    [InlineData("example.org", "example.org")]
    [InlineData("www.bbc.co.uk", "bbc.co.uk")]
    [InlineData("localhost", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("Chat.Google.COM.", "google.com")]
    public void TheSiteIsTheRegistrableDomain(string host, string site) => Assert.Equal(site, WebPermissions.Site(host));

    [Theory]
    [InlineData("https://meet.google.com/abc", "https://calendar.google.com/", true)]
    [InlineData("https://calendar.google.com/", "https://calendar.google.com/", true)]
    [InlineData("http://localhost:8080/", "http://localhost:8080/", true)]           // la machine locale est un contexte sûr
    [InlineData("http://calendar.google.com/", "https://calendar.google.com/", false)] // http : jamais, hors machine locale
    [InlineData("https://evil.com/", "https://calendar.google.com/", false)]           // une page vers laquelle un lien a mené ailleurs
    [InlineData("https://google.com.evil.com/", "https://calendar.google.com/", false)]
    [InlineData("https://other.co.uk/", "https://bbc.co.uk/", false)]                  // deux sites distincts sous un même « co.uk »
    [InlineData("http://127.0.0.1:8080/", "http://localhost:8080/", false)]
    public void OnlyASecurePageOfTheConfiguredSiteMayUseDevices(string requester, string configured, bool allowed) =>
        Assert.Equal(allowed, WebPermissions.MayUseDevices(new Uri(requester), new Uri(configured)));
}

/// <summary>Un cadre web garde sa page quand sa tuile quitte l'écran, et n'accorde ses autorisations qu'au bon site (vrai WebView2).</summary>
[Collection("ui")]
public class WebFramePoolTests
{
    readonly WpfHost _host;
    public WebFramePoolTests(WpfHost host) => _host = host;

    /// <summary>Petit serveur local (localhost et 127.0.0.1, sur le même port) : renvoie la page demandée par son chemin et compte les requêtes de « / ».</summary>
    sealed class Site : IDisposable
    {
        readonly HttpListener _listener = new();
        readonly CancellationTokenSource _stop = new();
        public int Port { get; }
        public int RootHits;

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
                    var path = ctx.Request.Url!.AbsolutePath;
                    if (path == "/") Interlocked.Increment(ref RootHits);
                    var bytes = Encoding.UTF8.GetBytes(page(path));
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose() { _stop.Cancel(); _listener.Close(); }
    }

    static WebWidgetViewModel Tile(int port, string? key) =>
        new() { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = new Uri($"http://localhost:{port}/"), FrameKey = key };

    /// <summary>Le WebView2 d'une vue : le premier du cadre (posé par la vue au chargement).</summary>
    WebView2 WebOf(WebWidgetView view) { _host.Settle(); return _host.Run(() => WpfHost.Descendants<WebView2>(view).Single()); }

    async Task WaitLoaded(WebView2 web, string startsWith = "http://")
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith(startsWith, StringComparison.Ordinal)) && DateTime.UtcNow < limit)
            await Task.Delay(100);
        await Task.Delay(700);
    }

    async Task<string> Eval(WebView2 web, string js) =>
        await await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.ExecuteScriptAsync(js)).Task;

    [Fact]
    public async Task SwitchingAwayAndBackKeepsThePageLoadedWithItsState()
    {
        WebFramePool.Clear();
        using var site = new Site(_ => "<html><body>page</body></html>");
        var window = _host.Run(() => new System.Windows.Window { Content = new System.Windows.Controls.Grid(), Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            _host.Run(window.Show);
            var first = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, "page|P|Agenda") }; window.Content = v; return v; });
            var web = WebOf(first);
            await WaitLoaded(web);
            await Eval(web, "window.marker = 42; document.cookie = 'k=1'");
            Assert.Equal(1, site.RootHits);

            _host.Run(() => window.Content = null); _host.Settle();                                  // l'utilisateur change de page : la tuile quitte l'écran
            Assert.Equal(1, _host.Run(() => WebFramePool.Count));

            var second = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, "page|P|Agenda") }; window.Content = v; return v; });
            var again = WebOf(second);
            Assert.Same(web, again);                                                 // le même cadre, pas un nouveau
            Assert.Equal(0, _host.Run(() => WebFramePool.Count));
            await Task.Delay(500);
            Assert.Equal("42", (await Eval(again, "String(window.marker)")).Trim((char)34));          // l'état de la page est intact…
            Assert.Equal(1, site.RootHits);                                          // …et elle n'a pas été rechargée
        }
        finally { _host.Run(() => { window.Content = null; window.Close(); WebFramePool.Clear(); }); }
    }

    [Fact]
    public async Task AnotherKeyOrNoKeyLoadsAFreshFrameAndNoKeyNeverKeepsOne()
    {
        WebFramePool.Clear();
        using var site = new Site(_ => "<html><body>page</body></html>");
        var window = _host.Run(() => new System.Windows.Window { Content = new System.Windows.Controls.Grid(), Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            _host.Run(window.Show);
            var a = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, "A") }; window.Content = v; return v; });
            await WaitLoaded(WebOf(a));
            _host.Run(() => window.Content = null); _host.Settle();
            Assert.Equal(1, _host.Run(() => WebFramePool.Count));

            var b = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, null) }; window.Content = v; return v; });
            await WaitLoaded(WebOf(b));
            Assert.Equal(2, site.RootHits);                                          // une autre clé : nouveau chargement
            _host.Run(() => window.Content = null); _host.Settle();
            Assert.Equal(1, _host.Run(() => WebFramePool.Count));                    // sans clé : libéré, pas rangé
        }
        finally { _host.Run(() => { window.Content = null; window.Close(); WebFramePool.Clear(); }); }
    }

    [Fact]
    public async Task TheFrameFollowsTheZoomOfItsTileInBothDirections()
    {
        WebFramePool.Clear();
        using var site = new Site(_ => "<html><body>page</body></html>");
        var window = _host.Run(() => new System.Windows.Window { Content = new System.Windows.Controls.Grid(), Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            _host.Run(window.Show);
            var vm = Tile(site.Port, null);
            vm.ZoomPercent = 150;                                                       // zoom enregistré : la page s'ouvre ainsi
            var saved = new ConcurrentQueue<int>();
            vm.ZoomChanged = saved.Enqueue;
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = vm }; window.Content = v; return v; });
            var web = WebOf(view);
            await WaitLoaded(web);
            Assert.Equal(1.5, _host.Run(() => web.ZoomFactor), 2);

            _host.Run(() => vm.ZoomOutCommand.Execute(null));                           // bouton −
            await Task.Delay(300);
            Assert.Equal(1.25, _host.Run(() => web.ZoomFactor), 2);

            await Task.Delay(1500);                                                     // le cadre est prêt depuis un moment (le zoom enregistré est appliqué)
            _host.Run(() => web.ZoomFactor = 2.0);                                      // pincement dans la page
            await Task.Delay(1000);
            Assert.Equal(200, _host.Run(() => vm.ZoomPercent));
            Assert.Equal(new[] { 125, 200 }, saved.ToArray());                          // chaque changement a été enregistré, pas le zoom initial
        }
        finally { _host.Run(() => { window.Content = null; window.Close(); WebFramePool.Clear(); }); }
    }

    [Fact]
    public void ThePoolNeverKeepsMoreThanItsLimit()
    {
        WebFramePool.Clear();
        _host.Run(() =>
        {
            for (int i = 0; i < WebFramePool.MaxParked + 3; i++)
                WebFramePool.Park("k" + i, new WebFrame { Web = new WebView2(), Source = new Uri("https://example.org/") });
            Assert.Equal(WebFramePool.MaxParked, WebFramePool.Count);
            Assert.Null(WebFramePool.Take("k0"));                                    // le plus ancien est parti
            Assert.NotNull(WebFramePool.Take("k" + (WebFramePool.MaxParked + 2)));
            WebFramePool.Clear();
            Assert.Equal(0, WebFramePool.Count);
        });
    }

    [Fact]
    public async Task NotificationsAreGrantedToTheConfiguredSiteAndRefusedToAnyOther()
    {
        WebFramePool.Clear();
        // « / » : page du site configuré ; « /autre » : le même serveur, mais appelé par une autre adresse (127.0.0.1 : autre site)
        using var site = new Site(path => "<html><body>page</body></html>");
        var window = _host.Run(() => new System.Windows.Window { Content = new System.Windows.Controls.Grid(), Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            _host.Run(window.Show);
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, null) }; window.Content = v; return v; });
            var web = WebOf(view);
            await WaitLoaded(web);

            const string ask = "window.answer = 'attente'; Notification.requestPermission().then(r => window.answer = r)";
            await Eval(web, ask);
            Assert.Equal("granted", await Until(web, "String(window.answer)", "attente"));    // même site, contexte sûr : accordé

            _host.Run(() => web.CoreWebView2.Navigate($"http://127.0.0.1:{site.Port}/"));     // une autre adresse, donc un autre site
            await WaitLoaded(web, $"http://127.0.0.1:{site.Port}");
            await Eval(web, ask);
            Assert.Equal("denied", await Until(web, "String(window.answer)", "attente"));
        }
        finally { _host.Run(() => { window.Content = null; window.Close(); WebFramePool.Clear(); }); }
    }

    [Fact]
    public async Task AnOldStoredRefusalIsResetSoThePageAsksAgainAndANewRefusalIsNeverStored()
    {
        WebFramePool.Clear();
        using var site = new Site(_ => "<html><body>page</body></html>");
        var window = _host.Run(() => new System.Windows.Window { Content = new System.Windows.Controls.Grid(), Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        string origin = $"http://localhost:{site.Port}";
        try
        {
            _host.Run(window.Show);
            var first = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, null) }; window.Content = v; return v; });
            var web = WebOf(first);
            await WaitLoaded(web);
            // Ce que les versions précédentes ont laissé dans le profil : un refus mémorisé des notifications pour ce site
            await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.Profile.SetPermissionStateAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Notifications, origin, Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny)).Task.Unwrap();
            _host.Run(() => window.Content = new System.Windows.Controls.Grid()); _host.Settle();
            _host.Run(WebFramePool.Clear);

            var second = _host.Run(() => { var v = new WebWidgetView { DataContext = Tile(site.Port, null) }; window.Content = v; return v; });
            var fresh = WebOf(second);
            await WaitLoaded(fresh);
            Assert.Equal("default", (await Eval(fresh, "Notification.permission")).Trim((char)34));     // la page n'est plus « bloquée » : elle peut redemander
        }
        finally { _host.Run(() => { window.Content = null; window.Close(); WebFramePool.Clear(); }); }
    }

    async Task<string> Until(WebView2 web, string js, string whileValue)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        string value = whileValue;
        while (DateTime.UtcNow < end)
        {
            value = (await Eval(web, js)).Trim('"');
            if (value != whileValue) return value;
            await Task.Delay(150);
        }
        return value;
    }
}
