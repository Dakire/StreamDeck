using System;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Modes d'interaction d'un cadre web : modèle, configuration et document (sans WebView2).</summary>
public class WebInputModeCoreTests
{
    [Theory]
    [InlineData(null, "touch")] [InlineData("", "touch")] [InlineData("PASSIVE", "passive")] [InlineData("interactive", "interactive")] [InlineData("n'importe", "touch")]
    public void AnUnknownOrMissingModeIsTouchToInteract(string? stored, string expected) => Assert.Equal(expected, WebInputModes.Normalize(stored));

    [Fact]
    public void TheDefaultModeWritesNothingAndAnOldConfigLoadsWithIt()
    {
        Assert.DoesNotContain("WebInput", JsonSerializer.Serialize(new DeckPage { Name = "A", Kind = PageKinds.Web, Url = "https://example.org/" }, ConfigStore.Options));
        var back = JsonSerializer.Deserialize<DeckPage>(JsonSerializer.Serialize(new DeckPage { Name = "A", WebInput = "passive" }, ConfigStore.Options), ConfigStore.Options)!;
        Assert.Equal("passive", back.WebInput);
        Assert.Null(WebInputModes.ForStorage("touch"));
        Assert.Equal("interactive", WebInputModes.ForStorage("Interactive"));
    }

    [Fact]
    public void ChangingAPagesModeIsOneUndoableStep()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Chat", Kind = PageKinds.Web, Url = "https://chat.google.com/" });
        var doc = new DeckDocument(cfg);
        int page = cfg.Profiles[0].Pages.Count - 1;
        Assert.True(doc.SetPageWebInput(cfg.Profiles[0].Name, page, WebInputModes.Passive).Ok);
        Assert.Equal("passive", doc.Config.Profiles[0].Pages[page].WebInput);
        Assert.False(doc.SetPageWebInput(cfg.Profiles[0].Name, page, WebInputModes.Passive).Ok);   // inchangé : pas de pas d'annulation vide
        doc.Undo();
        Assert.Null(doc.Config.Profiles[0].Pages[page].WebInput);
    }

    static WebWidgetViewModel Tile(string mode) => new() { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = new Uri("https://example.org/"), InputMode = mode };

    [Fact]
    public void ATouchModePageWaitsForATapThenEndsByItself()
    {
        var vm = Tile(WebInputModes.Touch);
        Assert.False(vm.IsInteracting);
        Assert.True(vm.ShowTouchShield);
        Assert.Equal("Toucher la page pour interagir", vm.InteractHint);
        vm.InteractCommand.Execute(null);
        Assert.True(vm.IsInteracting);
        Assert.False(vm.ShowTouchShield);
        vm.EndInteraction();
        Assert.False(vm.IsInteracting);
    }

    [Fact]
    public void EndingTheInteractionGivesTheKeyboardBack()
    {
        var vm = Tile(WebInputModes.Passive);
        bool? typing = null;
        vm.TypingRequested = on => { typing = on; vm.IsTyping = on; };
        vm.ToggleTypingCommand.Execute(null);   // le bouton clavier ouvre aussi l'interaction
        Assert.True(vm.IsInteracting);
        Assert.True(typing);
        vm.EndInteraction();
        Assert.False(typing);
    }

    [Fact]
    public void AnInteractivePageNeverStopsAndAMenuStillHidesIt()
    {
        var vm = Tile(WebInputModes.Interactive);
        Assert.True(vm.IsInteracting);
        Assert.False(vm.CanToggleInteraction);
        vm.EndInteraction();
        Assert.True(vm.IsInteracting);
        vm.Suspended = true;
        Assert.False(vm.ShowPage);
        Assert.False(vm.ShowTouchShield);
    }

    [Theory]
    [InlineData("[true,1234]", true, 1234L)]
    [InlineData("[false,12.7]", false, 12L)]
    [InlineData("null", false, null)]
    [InlineData("pas du json", false, null)]
    public void ThePollReplyIsReadDefensively(string json, bool field, long? idle) => Assert.Equal((field, idle), WebWidgetView.ParsePoll(json));
}

/// <summary>
/// Le vrai cadre WebView2, dans une fenêtre placée hors de tout écran (jamais visible ni activée) : en mode passif, sa fenêtre Win32 est désactivée et Windows
/// donne le point sous le pointeur à la fenêtre du deck, pas à la page.
/// </summary>
[Collection("ui")]
public class WebInputModeFrameTests
{
    readonly WpfHost _host;
    public WebInputModeFrameTests(WpfHost host) { _host = host; }

    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    static (HttpListener Listener, Uri Uri) Serve()
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
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
                var bytes = Encoding.UTF8.GetBytes("<html><body style='margin:0;height:100vh'><input id='t' type='text' autofocus></body></html>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        WidgetTypes.IsWebAddress($"http://localhost:{port}/", out var uri);
        return (listener, uri);
    }

    [Fact]
    public async Task APassivePageIsADisabledWindowAndThePointerGoesToTheDeck()
    {
        var (listener, uri) = Serve();
        using var _ = listener;
        var vm = new WebWidgetViewModel { Button = new DeckButton { Widget = WidgetTypes.Web }, Source = uri, InputMode = WebInputModes.Passive };
        int typingRequests = 0;
        vm.TypingRequested = on => { typingRequests++; vm.IsTyping = on; };
        var window = _host.Run(() => new Window { Left = -30000, Top = -30000, Width = 800, Height = 600, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None });
        try
        {
            var view = _host.Run(() => { var v = new WebWidgetView { DataContext = vm }; window.Content = v; window.Show(); return v; });
            WebView2 web = _host.Run(() => WpfHost.Descendants<WebView2>(view).Single());
            var limit = DateTime.UtcNow.AddSeconds(20);
            while (_host.Run(() => web.CoreWebView2 == null || !web.CoreWebView2.Source.StartsWith("http://localhost", StringComparison.Ordinal)) && DateTime.UtcNow < limit)
                await Task.Delay(100);
            Assert.True(_host.Run(() => web.CoreWebView2 != null), "WebView2 indisponible sur cette machine");

            IntPtr frame = _host.Run(() => web.Handle), deck = _host.Run(() => new WindowInteropHelper(window).Handle);
            POINT center = _host.Run(() =>
            {
                var p = web.PointToScreen(new Point(web.ActualWidth / 2, web.ActualHeight / 2));
                return new POINT { X = (int)p.X, Y = (int)p.Y };
            });

            Assert.False(IsWindowEnabled(frame));                           // le cadre ne reçoit plus rien…
            Assert.Equal(deck, WindowFromPoint(center));                    // …le pointeur au-dessus de la page appartient au deck
            await Task.Delay(1500);
            Assert.Equal(0, typingRequests);                                // le champ « autofocus » de la page ne vole pas le clavier

            _host.Run(() => vm.InteractCommand.Execute(null));
            Assert.True(IsWindowEnabled(frame));
            var hit = WindowFromPoint(center);
            Assert.True(hit == frame || IsChild(frame, hit), "en interaction, la page reçoit le pointeur");

            _host.Run(() => vm.EndInteraction());
            Assert.False(IsWindowEnabled(frame));
            Assert.Equal(deck, GetAncestor(WindowFromPoint(center), 2 /* GA_ROOT */));
        }
        finally { _host.Run(() => window.Close()); }
    }
}
