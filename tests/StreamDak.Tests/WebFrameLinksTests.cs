using System;
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

/// <summary>Le cadre web n'ouvre jamais de fenêtre, mais un lien « nouvel onglet » doit quand même fonctionner (bouton « Sign in » de Google Agenda).</summary>
[Collection("ui")]
public class WebFrameLinksTests
{
    readonly WpfHost _host;
    public WebFrameLinksTests(WpfHost host) => _host = host;

    [Fact]
    public async Task FocusingATextFieldStartsTypingByItselfAndLeavingItGivesTheKeyboardBack()
    {
        using var listener = new HttpListener();
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                var bytes = Encoding.UTF8.GetBytes("<html><body><input id='t' type='text'><input id='c' type='checkbox'><button id='b'>OK</button></body></html>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        WidgetTypes.IsWebAddress($"http://localhost:{port}/", out var uri);
        var vm = new WebWidgetViewModel { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = uri, InputMode = WebInputModes.Interactive };   // comportement d'avant : la page reçoit tout
        var requests = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        vm.TypingRequested = on => { requests.Enqueue(on); vm.IsTyping = on; };   // le deck, simulé
        var window = _host.Run(() => new System.Windows.Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = vm }; window.Content = v; window.Show(); return v; });
            WebView2 web = _host.Run(() => WpfHost.Descendants<WebView2>(view).Single());
            var limit = DateTime.UtcNow.AddSeconds(20);
            while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith("http://localhost", StringComparison.Ordinal)) && DateTime.UtcNow < limit)
                await Task.Delay(100);
            async Task Script(string js) => await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.ExecuteScriptAsync(js)).Task.Unwrap();
            async Task<bool> Until(Func<bool> condition)
            {
                var end = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < end) { if (condition()) return true; await Task.Delay(100); }
                return condition();
            }

            await Script("document.getElementById('c').focus()");                       // une case à cocher n'accepte pas de texte
            await Task.Delay(1500);
            Assert.Empty(requests);

            await Script("document.getElementById('t').focus()");
            Assert.True(await Until(() => !requests.IsEmpty), "la saisie n'a pas démarré");
            Assert.True(requests.TryPeek(out var first) && first);
            Assert.True(vm.AutoTyping);

            await Script("document.getElementById('b').focus()");                       // on quitte le champ : le clavier est rendu
            Assert.True(await Until(() => !vm.IsTyping), "la saisie ne s'est pas arrêtée");
            Assert.False(vm.AutoTyping);
        }
        finally
        {
            await stop.CancelAsync();
            _host.Run(window.Close);
        }
    }

    [Fact]
    public async Task ANewTabLinkToAnotherSiteOpensInTheBrowserAndBackReturnsToTheStart()
    {
        using var listener = new HttpListener();
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var stop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                var html = ctx.Request.Url!.AbsolutePath == "/seconde" ? "<html><body>seconde</body></html>"
                    : "<html><body><a id='ext' target='_blank' href='https://docs.example.org/doc'>doc</a><a id='in' href='/seconde'>dedans</a></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        WidgetTypes.IsWebAddress($"http://localhost:{port}/", out var uri);
        var vm = new WebWidgetViewModel { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = uri, InputMode = WebInputModes.Interactive };   // comportement d'avant : la page reçoit tout
        var opened = new System.Collections.Concurrent.ConcurrentQueue<Uri>();
        vm.BrowserRequested = opened.Enqueue;                                            // le deck, simulé : rien n'est réellement lancé
        var window = _host.Run(() => new System.Windows.Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = vm }; window.Content = v; window.Show(); return v; });
            WebView2 web = _host.Run(() => WpfHost.Descendants<WebView2>(view).Single());
            async Task Until(Func<bool> condition)
            {
                var end = DateTime.UtcNow.AddSeconds(15);
                while (!_host.Run(condition) && DateTime.UtcNow < end) await Task.Delay(100);
            }
            string Source() => web.CoreWebView2?.Source ?? "";
            await Until(() => Source().StartsWith("http://localhost", StringComparison.Ordinal));
            await Task.Delay(500);
            async Task Click(string id) => await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.ExecuteScriptAsync($"document.getElementById('{id}').click()")).Task.Unwrap();

            await Click("ext");                                                          // autre site, nouvel onglet : le navigateur, pas le cadre
            await Task.Delay(1500);
            Assert.Equal(new[] { new Uri("https://docs.example.org/doc") }, opened.ToArray());
            Assert.Equal($"http://localhost:{port}/", _host.Run(Source));

            await Click("in");                                                           // lien ordinaire du même site : dans le cadre
            await Until(() => Source().EndsWith("/seconde", StringComparison.Ordinal));
            _host.Run(() => vm.GoBackCommand.Execute(null));                             // Retour : on n'est jamais bloqué
            await Until(() => Source() == $"http://localhost:{port}/");
            Assert.Equal($"http://localhost:{port}/", _host.Run(Source));
        }
        finally
        {
            await stop.CancelAsync();
            _host.Run(window.Close);
        }
    }

    [Fact]
    public async Task ALinkThatOpensANewTabLoadsInsideTheFrame()
    {
        using var listener = new HttpListener();
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var stop = new CancellationTokenSource();
        var second = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                bool isSecond = ctx.Request.Url!.AbsolutePath == "/seconde";
                if (isSecond) second.TrySetResult();
                var html = isSecond ? "<html><body>seconde</body></html>"
                    : "<html><body><a id='l' target='_blank' href='/seconde'>Sign in</a></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        var button = new DeckButton { Widget = WidgetTypes.Web };
        WidgetTypes.IsWebAddress($"http://localhost:{port}/", out var uri);
        var vm = new WebWidgetViewModel { Button = button, Source = uri, InputMode = WebInputModes.Interactive };
        var window = _host.Run(() => new System.Windows.Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false });
        try
        {
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = vm }; window.Content = v; window.Show(); return v; });
            WebView2 web = _host.Run(() => WpfHost.Descendants<WebView2>(view).Single());
            var limit = DateTime.UtcNow.AddSeconds(20);
            while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith("http://localhost", StringComparison.Ordinal)) && DateTime.UtcNow < limit)
                await Task.Delay(100);
            await Task.Delay(500);
            await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('l').click()")).Task.Unwrap();

            Assert.Same(second.Task, await Task.WhenAny(second.Task, Task.Delay(10000)));      // la page du lien a été demandée…
            await Task.Delay(500);
            Assert.EndsWith("/seconde", _host.Run(() => web.CoreWebView2.Source), StringComparison.Ordinal);   // …et affichée dans le cadre
        }
        finally
        {
            await stop.CancelAsync();
            _host.Run(window.Close);
        }
    }
}
