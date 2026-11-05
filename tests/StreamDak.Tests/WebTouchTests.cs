using System;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Toucher d'une page web (ADR 0036) : le cadre est rendu par composition, sans fenêtre Win32 sous le doigt, et le deck lui transmet le doigt comme un vrai toucher.
/// Vrai WebView2, fenêtre hors écran (jamais affichée sur le bureau ni activée) ; les contacts sont simulés comme le deck les lit dans WM_POINTER.
/// Non vérifiable ici : un vrai écran tactile (défilement inertiel réel, pincement de deux vrais doigts, rejet de paume).
/// </summary>
[Collection("ui")]
public class WebTouchTests
{
    readonly WpfHost _host;
    public WebTouchTests(WpfHost host) => _host = host;

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    const int Down = 0x0246, Update = 0x0245, Up = 0x0247;
    const uint New = 0x1, InRange = 0x2, InContact = 0x4, Primary = 0x2000, First = 0x20, FlagDown = 0x10000, FlagUpdate = 0x20000, FlagUp = 0x40000;

    const string Page = "<!doctype html><html><head><meta name='viewport' content='width=device-width'></head>" +
        "<body style='margin:0;background:#2060ff;height:5000px'><div id='top' style='height:120px;background:#ff4000'>haut</div>" +
        "<script>window.L=[];['pointerdown','pointerup','pointercancel','touchstart','touchend','mousedown','mousemove','mouseup','click','contextmenu'].forEach(function(n){" +
        "addEventListener(n,function(e){L.push(n+(e.pointerType?':'+e.pointerType:''))},{capture:true,passive:true})});</script></body></html>";

    static (HttpListener Listener, Uri Uri) Serve()
    {
        int port;
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                var bytes = Encoding.UTF8.GetBytes(Page);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        WidgetTypes.IsWebAddress($"http://localhost:{port}/", out var uri);
        return (listener, uri);
    }

    async Task<string> Eval(DeckWebView web, string js) =>
        await await _host.Dispatcher.InvokeAsync(async () => await web.CoreWebView2.ExecuteScriptAsync(js)).Task;

    /// <summary>Un widget web en interaction dans une fenêtre hors écran, page chargée.</summary>
    async Task<(Window Window, WebWidgetViewModel Vm, DeckWebView Web, HttpListener Listener)> Open()
    {
        var (listener, uri) = Serve();
        var vm = new WebWidgetViewModel { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = uri, InputMode = WebInputModes.Interactive };
        var window = _host.Run(() => new Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None });
        _host.Run(() => { window.Content = new WebWidgetView { DataContext = vm }; window.Show(); });
        var web = _host.Run(() => WpfHost.Descendants<DeckWebView>((DependencyObject)window.Content).Single());
        var limit = DateTime.UtcNow.AddSeconds(25);
        while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith("http://localhost", StringComparison.Ordinal)) && DateTime.UtcNow < limit)
            await Task.Delay(100);
        Assert.True(_host.Run(() => web.CoreWebView2 != null), "WebView2 indisponible sur cette machine");
        limit = DateTime.UtcNow.AddSeconds(10);
        while (await Eval(web, "typeof window.L") != "\"object\"" && DateTime.UtcNow < limit) await Task.Delay(100);
        // Le compositeur de la page doit avoir rendu au moins deux images avant le premier doigt : sinon le geste est lu avant que le défilement existe
        await Eval(web, "window.__frames=0;(function f(){window.__frames++;requestAnimationFrame(f)})()");
        limit = DateTime.UtcNow.AddSeconds(10);
        while (int.Parse(await Eval(web, "window.__frames")) < 3 && DateTime.UtcNow < limit) await Task.Delay(50);
        return (window, vm, web, listener);
    }

    /// <summary>
    /// Contact simulé au point (DIP du cadre) donné, converti en pixels écran comme le deck les lit. L'horodatage est celui de Windows (<c>dwTime</c> : horloge
    /// système en ms) : Chromium en tire la vitesse du geste ; <paramref name="frame"/> numérote les trames de contacts simultanés.
    /// </summary>
    WebTouchPoint Touch(DeckWebView web, uint id, double x, double y, uint flags, uint frame)
    {
        var screen = _host.Run(() => web.PointToScreen(new Point(x, y)));
        uint now = (uint)Environment.TickCount;
        return new WebTouchPoint(id, frame, flags, screen, (int)(x * 26), (int)(y * 26), now);
    }

    async Task Send(DeckWebView web, int message, WebTouchPoint p)
    {
        Assert.True(_host.Run(() => web.Forward(message, p)));
        await Task.Delay(16);
    }

    [Fact]
    public async Task TheFrameHasNoChildWindowUnderTheFinger()
    {
        var (window, _, web, listener) = await Open();
        try
        {
            var (center, top) = _host.Run(() =>
            {
                var c = web.PointToScreen(new Point(web.ActualWidth / 2, web.ActualHeight / 2));
                return (new POINT { X = (int)c.X, Y = (int)c.Y }, new WindowInteropHelper(window).Handle);
            });
            // Le point sous le doigt appartient à la fenêtre du deck : Windows lui envoie le WM_POINTER, que le deck retire du traitement par défaut (aucune souris synthétique)
            Assert.Equal(top, WindowFromPoint(center));
        }
        finally { _host.Run(window.Close); listener.Close(); }
    }

    [Fact]
    public async Task ASwipeScrollsTheTapClicksAndOnlyTouchEventsReachThePageWithoutMovingTheCursor()
    {
        var (window, _, web, listener) = await Open();
        try
        {
            uint t = 1;
            await Send(web, Down, Touch(web, 11, 400, 500, Primary | First | New | InRange | InContact | FlagDown, t++));
            for (int y = 480; y >= 180; y -= 20) await Send(web, Update, Touch(web, 11, 400, y, Primary | First | InRange | InContact | FlagUpdate, t++));
            await Send(web, Up, Touch(web, 11, 400, 180, Primary | First | FlagUp, t++));
            await Task.Delay(700);
            int scrolled = int.Parse(await Eval(web, "Math.round(scrollY)"));
            string seen = await Eval(web, "JSON.stringify({l:L.slice(0,12),n:L.length,h:document.documentElement.scrollHeight,w:innerWidth,ih:innerHeight,z:devicePixelRatio,vs:visualViewport.scale,vis:document.visibilityState})");
            Assert.True(scrolled > 100, $"défilement {scrolled} px ; page : {seen} ; cadre {_host.Run(() => (web.ActualWidth, web.ActualHeight, web.IsVisible, web.IsEnabled, web.ForwardedTouches))}");

            await Eval(web, "scrollTo(0,0); L.length=0");
            await Task.Delay(200);
            await Send(web, Down, Touch(web, 12, 200, 60, Primary | First | New | InRange | InContact | FlagDown, t++));
            await Task.Delay(50);
            await Send(web, Up, Touch(web, 12, 200, 60, Primary | First | FlagUp, t++));
            await Task.Delay(500);
            var events = JsonSerializer.Deserialize<string[]>(JsonSerializer.Deserialize<string>(await Eval(web, "JSON.stringify(L)"))!)!;
            Assert.Contains("pointerdown:touch", events);
            Assert.Contains("click:touch", events);                                       // un appui est un vrai toucher pour la page
            // Jamais de pointeur souris. Les événements souris « de compatibilité » qui suivent un appui (mousemove, mousedown, mouseup) viennent de la page
            // elle-même, comme dans un onglet Chrome au doigt : aucune souris système n'est en jeu
            Assert.DoesNotContain(events, e => e.EndsWith(":mouse", StringComparison.Ordinal));
            Assert.True(Array.IndexOf(events, "pointerup:touch") < Array.IndexOf(events, "mousedown"), string.Join(", ", events));

            Assert.True(_host.Run(() => web.ForwardedTouches) >= 20);
            // Le curseur système n'est volontairement pas comparé ici : il bouge tout seul sur une machine utilisée (souris, session distante), ce qui rendrait le test
            // aléatoire. Rien dans ce chemin ne peut le déplacer : aucun SetCursorPos ni SendInput souris (ArchitectureTests), et la page ne reçoit aucun pointeur souris (ci-dessus).
        }
        finally { _host.Run(window.Close); listener.Close(); }
    }

    [Fact]
    public async Task ALongPressGivesThePageItsContextMenuEvent()
    {
        var (window, _, web, listener) = await Open();
        try
        {
            await Eval(web, "L.length=0");
            await Send(web, Down, Touch(web, 21, 300, 300, Primary | First | New | InRange | InContact | FlagDown, 1));
            for (uint i = 0; i < 10; i++) { await Task.Delay(100); await Send(web, Update, Touch(web, 21, 300, 300, Primary | First | InRange | InContact | FlagUpdate, 2 + i)); }
            await Send(web, Up, Touch(web, 21, 300, 300, Primary | First | FlagUp, 20));
            await Task.Delay(300);
            Assert.Contains("contextmenu", await Eval(web, "JSON.stringify(L)"));
        }
        finally { _host.Run(window.Close); listener.Close(); }
    }

    [Fact]
    public async Task TwoFingersSpreadingApartPinchZoomThePage()
    {
        var (window, _, web, listener) = await Open();
        try
        {
            uint t = 1;
            await Send(web, Down, Touch(web, 31, 350, 300, Primary | First | New | InRange | InContact | FlagDown, t));
            await Send(web, Down, Touch(web, 32, 450, 300, New | InRange | InContact | FlagDown, t++));
            for (int d = 10; d <= 150; d += 10)
            {
                await Send(web, Update, Touch(web, 31, 350 - d, 300, Primary | First | InRange | InContact | FlagUpdate, t));
                await Send(web, Update, Touch(web, 32, 450 + d, 300, InRange | InContact | FlagUpdate, t++));
            }
            await Send(web, Up, Touch(web, 32, 600, 300, FlagUp, t));
            await Send(web, Up, Touch(web, 31, 200, 300, Primary | First | FlagUp, t++));
            await Task.Delay(500);
            double scale = double.Parse(await Eval(web, "visualViewport.scale"), System.Globalization.CultureInfo.InvariantCulture);
            double zoom = _host.Run(() => web.ZoomFactor);
            Assert.True(scale > 1.05 || zoom > 1.05, $"échelle {scale}, zoom {zoom}");
        }
        finally { _host.Run(window.Close); listener.Close(); }
    }

    [Fact]
    public async Task TheDeckForwardsAFingerOnAnActivePageAndNeverSwipesOverIt()
    {
        var (listener, uri) = Serve();
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 2, Pages =
            {
                new DeckPage { Name = "Site", Kind = PageKinds.Web, Url = uri.AbsoluteUri },
                new DeckPage { Name = "Boutons", Buttons = { new DeckButton { Label = "Un", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } } } },
            } } },
        };
        _host.Reset(cfg);
        var win = _host.Run(() =>
        {
            var w = new MainWindow(_host.Services) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 1100, Height = 650, ShowInTaskbar = false };
            w.PointerInContact = _ => true;
            w.Show();
            return w;
        });
        try
        {
            _host.Settle();
            var vm = _host.Run(() => (MainViewModel)win.DataContext);
            var web = _host.Run(() => WpfHost.Descendants<DeckWebView>(win).Single());
            var limit = DateTime.UtcNow.AddSeconds(25);
            while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith("http://localhost", StringComparison.Ordinal)) && DateTime.UtcNow < limit)
                await Task.Delay(100);
            Assert.True(_host.Run(() => web.CoreWebView2 != null), "WebView2 indisponible sur cette machine");
            var center = _host.Run(() => web.PointToScreen(new Point(web.ActualWidth / 2, web.ActualHeight / 2)));
            WebTouchPoint At(uint id, double dx, uint flags) => new(id, 1, flags, new Point(center.X + dx, center.Y), 0, 0, (uint)Environment.TickCount);

            // Au repos (mode « toucher pour interagir ») : le bouclier reçoit le doigt, rien n'est transmis à la page
            Assert.False(_host.Run(() => win.RouteToWebFrame(Down, At(41, 0, Primary | First | New | InRange | InContact | FlagDown))));
            _host.Run(() => vm.Tiles.OfType<WebWidgetViewModel>().Single().InteractCommand.Execute(null));
            _host.Settle();

            // En interaction : chaque contact va à la page jusqu'au relâchement, même s'il glisse loin (balayage de 400 px)
            Assert.True(_host.Run(() => win.RouteToWebFrame(Down, At(42, 0, Primary | First | New | InRange | InContact | FlagDown))));
            for (int i = 1; i <= 8; i++) Assert.True(_host.Run(() => win.RouteToWebFrame(Update, At(42, -50 * i, Primary | First | InRange | InContact | FlagUpdate))));
            Assert.True(_host.Run(() => win.RouteToWebFrame(Up, At(42, -400, Primary | First | FlagUp))));
            Assert.Equal(10, _host.Run(() => web.ForwardedTouches));
            Assert.Equal(0, _host.Run(() => vm.Tabs.First(t => t.IsActive).Index));   // la page web garde son défilement : pas de changement de page

            // Le même geste rejoué par le traitement du deck (sans transmission) ne change pas de page non plus : pas de balayage sur un cadre web
            _host.Run(() =>
            {
                win.HandleTouch(Down, 43, (int)center.X, (int)center.Y, true, 0);
                for (int i = 1; i <= 8; i++) win.HandleTouch(Update, 43, (int)center.X - 50 * i, (int)center.Y, true, i * 15);
                win.HandleTouch(Up, 43, (int)center.X - 400, (int)center.Y, false, 130);
            });
            _host.Settle();
            Assert.Equal(0, _host.Run(() => vm.Tabs.First(t => t.IsActive).Index));
        }
        finally { _host.Run(win.Close); listener.Close(); }
    }

    [Fact]
    public async Task AtRestThePageIsDisabledAndTheDeckRoutesNothingToIt()
    {
        var (listener, uri) = Serve();
        var vm = new WebWidgetViewModel { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = uri, InputMode = WebInputModes.Touch };
        var window = _host.Run(() => new Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None });
        try
        {
            _host.Run(() => { window.Content = new WebWidgetView { DataContext = vm }; window.Show(); });
            var web = _host.Run(() => WpfHost.Descendants<DeckWebView>((DependencyObject)window.Content).Single());
            _host.Settle();
            Assert.False(_host.Run(() => web.IsEnabled));          // au repos : le bouclier reçoit le premier toucher, la page rien
            _host.Run(() => vm.InteractCommand.Execute(null));
            Assert.True(_host.Run(() => web.IsEnabled));
            _host.Run(() => vm.EndInteraction());
            Assert.False(_host.Run(() => web.IsEnabled));
        }
        finally { _host.Run(window.Close); listener.Close(); }
    }
}
