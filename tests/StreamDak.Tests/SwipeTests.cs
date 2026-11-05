using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Reconnaissance du balayage (logique pure, suites de points simulées, DIP et millisecondes).</summary>
public class SwipeGestureTests
{
    const double Width = 1000;

    /// <summary>Rejoue un geste : appui en (x0, y0) à t = 0, puis les points donnés ; retourne le sens obtenu au relâchement sur le dernier point.</summary>
    static (int Direction, SwipeGesture Gesture) Play(double x0, double y0, params (double X, double Y, long T)[] points)
    {
        var g = new SwipeGesture();
        g.Begin(x0, y0, 0, Width);
        foreach (var (x, y, t) in points[..^1]) g.Move(x, y, t);
        var (lx, ly, lt) = points[^1];
        return (g.End(lx, ly, lt), g);
    }

    static (double, double, long)[] Line(double x0, double y0, double x1, double y1, long duration, int steps = 10) =>
        Enumerable.Range(1, steps).Select(i => (x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps, duration * i / steps)).ToArray();

    [Fact]
    public void AQuickLeftSwipeGoesToTheNextPageAndARightOneToThePrevious()
    {
        Assert.Equal(1, Play(800, 300, Line(800, 300, 600, 310, 150)).Direction);
        Assert.Equal(-1, Play(200, 300, Line(200, 300, 420, 290, 150)).Direction);
    }

    [Fact]
    public void ADiagonalOrVerticalMoveIsNotASwipe()
    {
        var (dir, g) = Play(500, 300, Line(500, 300, 380, 200, 150));    // |dx| = 120 < 1,5 × |dy| = 150
        Assert.Equal(0, dir);
        Assert.Equal(SwipeState.Rejected, g.State);
        Assert.Equal(0, Play(500, 100, Line(500, 100, 510, 400, 150)).Direction);
    }

    [Fact]
    public void ASlowShortMoveSnapsBackButASlowLongOneStillTurnsThePage()
    {
        Assert.Equal(0, Play(800, 300, Line(800, 300, 650, 300, 2000, 40)).Direction);   // 150 DIP en 2 s : ni vif, ni assez long (350 DIP)
        Assert.Equal(1, Play(900, 300, Line(900, 300, 450, 300, 2000, 40)).Direction);   // 450 DIP : assez long même lentement
    }

    [Fact]
    public void AQuickButTooShortFlickDoesNothing()
    {
        Assert.Equal(0, Play(500, 300, Line(500, 300, 440, 300, 60)).Direction);   // 60 DIP < 80 DIP minimum
        Assert.Equal(SwipeGesture.MinDistanceDip, SwipeGesture.MinDistance(600));
        Assert.Equal(1600 * SwipeGesture.MinWidthFraction, SwipeGesture.MinDistance(1600));
    }

    [Fact]
    public void AFingerThatBarelyMovesIsStillATap()
    {
        var (dir, g) = Play(500, 300, (505, 303, 40), (508, 302, 90));
        Assert.Equal(0, dir);
        Assert.NotEqual(SwipeState.Swiping, g.State);
    }

    [Fact]
    public void AHeldFingerIsALongPressNotASwipe()
    {
        var (dir, g) = Play(500, 300, (502, 300, 300), (501, 301, SwipeGesture.HoldRejectMs + 10), (300, 300, 700), (200, 300, 760));
        Assert.Equal(0, dir);
        Assert.Equal(SwipeState.Rejected, g.State);
    }

    [Fact]
    public void ReturningToTheStartCancelsTheSwipe() =>
        Assert.Equal(0, Play(500, 300, (400, 300, 50), (300, 300, 100), (480, 300, 200), (495, 300, 220)).Direction);

    static int PlayBack(double x0, double y0, bool allowBack, params (double X, double Y, long T)[] points)
    {
        var g = new SwipeGesture();
        g.Begin(x0, y0, 0, Width, allowBack);
        foreach (var (x, y, t) in points[..^1]) g.Move(x, y, t);
        var (lx, ly, lt) = points[^1];
        return g.End(lx, ly, lt);
    }

    [Fact]
    public void ASwipeDownFromTheMiddleOfTheGridGoesBackOnlyWhereItIsAllowed()
    {
        Assert.Equal(SwipeGesture.Back, PlayBack(500, 100, true, Line(500, 100, 510, 300, 150)));    // vif
        Assert.Equal(SwipeGesture.Back, PlayBack(500, 100, true, Line(500, 100, 500, 300, 2000, 40)));   // lent mais long (200 DIP)
        Assert.Equal(0, PlayBack(500, 100, false, Line(500, 100, 510, 300, 150)));                    // hors dossier : rien
        Assert.Equal(0, PlayBack(500, 300, true, Line(500, 300, 500, 100, 150)));                     // vers le haut : rien
        Assert.Equal(0, PlayBack(500, 100, true, Line(500, 100, 540, 150, 80)));                      // trop court
        Assert.Equal(0, PlayBack(500, 100, true, Line(500, 100, 600, 250, 150)));                     // diagonale
    }

    [Fact]
    public void AHorizontalSwipeStillTurnsThePageWhenBackIsAllowed() =>
        Assert.Equal(1, PlayBack(800, 300, true, Line(800, 300, 600, 305, 150)));

    [Fact]
    public void AtTheEndOfTheListTheGridResistsAndStaysClose()
    {
        Assert.Equal(-300, SwipeGesture.Follow(-300, Width, hasPageThatWay: true));
        double edge = SwipeGesture.Follow(-300, Width, hasPageThatWay: false);
        Assert.InRange(edge, -Width * SwipeGesture.EdgeMaxFraction, -1);
        Assert.Equal(-Width * SwipeGesture.EdgeMaxFraction, SwipeGesture.Follow(-5000, Width, false));
    }
}

/// <summary>Balayage dans le deck : règles du ViewModel et geste rejoué sur une vraie fenêtre hors écran (aucune action exécutée).</summary>
[Collection("ui")]
public class DeckSwipeTests
{
    readonly WpfHost _host;
    public DeckSwipeTests(WpfHost host) => _host = host;

    const int Down = 0x0246, Update = 0x0245, Up = 0x0247;

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config(bool swipeDisabled = false)
    {
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Clear();
        folder.Children.Add(Btn("Dedans"));
        return new()
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false, SwipePagesDisabled = swipeDisabled,
            Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages =
            {
                new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux"), folder } },
                new DeckPage { Name = "Cachée", Hidden = true, Buttons = { Btn("Secret") } },
                new DeckPage { Name = "B", Buttons = { Btn("Autre") } },
            } } },
        };
    }

    static int ActivePage(MainViewModel vm) => vm.Tabs.First(t => t.IsActive).Index;

    MainViewModel NewDeck(DeckConfig cfg)
    {
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    [Fact]
    public void TheSwipeUsesThePageTabsSkipsHiddenPagesAndNeverWraps()
    {
        var vm = NewDeck(Config());
        _host.Run(() =>
        {
            Assert.False(vm.HasSwipeTarget(-1));
            Assert.False(vm.SwipePage(-1));                   // première page : pas de bouclage
            Assert.True(vm.SwipePage(1));
            Assert.Equal(2, ActivePage(vm));                  // la page masquée est sautée, comme dans les onglets
            Assert.False(vm.SwipePage(1));                    // dernière page
            Assert.True(vm.SwipePage(-1));
            Assert.Equal(0, ActivePage(vm));
        });
    }

    [Fact]
    public void FromInsideAFolderTheSwipeGoesToTheNextPageOfTheProfile()
    {
        var vm = NewDeck(Config());
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Click.Execute(null);
        });
        _host.Settle();
        _host.Run(() =>
        {
            Assert.True(vm.InFolder);
            Assert.True(vm.SwipePage(1));
            Assert.False(vm.InFolder);
            Assert.Equal(2, ActivePage(vm));
        });
    }

    [Fact]
    public void ASwipeDownInsideAFolderReturnsToTheParentAndDoesNothingOutsideOne()
    {
        var vm = NewDeck(Config());
        _host.Run(() =>
        {
            Assert.False(vm.CanSwipeBack);
            Assert.False(vm.SwipeBack());
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Click.Execute(null);
        });
        _host.Settle();
        _host.Run(() =>
        {
            Assert.True(vm.InFolder);
            Assert.True(vm.CanSwipeBack);
            Assert.True(vm.SwipeBack());
            Assert.False(vm.InFolder);
            Assert.Equal(0, ActivePage(vm));
        });
    }

    [Fact]
    public void NoSwipeInEditModeOrWhenTheSettingIsOff()
    {
        var vm = NewDeck(Config());
        _host.Run(() =>
        {
            vm.StartEditing(new Place("P", 0), -1);
            Assert.False(vm.CanSwipe);
            Assert.False(vm.SwipePage(1));
            Assert.Equal(0, ActivePage(vm));
        });
        var off = NewDeck(Config(swipeDisabled: true));
        _host.Run(() =>
        {
            Assert.False(off.CanSwipe);
            Assert.False(off.SwipePage(1));
        });
    }

    [Fact]
    public void TheSettingIsAbsentFromTheFileByDefaultAndRoundTrips()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ConfigStore.DefaultConfig(), ConfigStore.Options);
        Assert.DoesNotContain("SwipePagesDisabled", json);
        var back = ConfigStore.Parse("""{ "Version": 2, "Profiles": [], "SwipePagesDisabled": true }""");
        Assert.True(back.SwipePagesDisabled);
        Assert.False(ConfigStore.Parse("""{ "Version": 2, "Profiles": [] }""").SwipePagesDisabled);
    }

    /// <summary>Vraie fenêtre du deck, affichée hors écran (jamais sur le bureau, sans activation) : le geste est rejoué par <see cref="MainWindow.HandleTouch"/>.</summary>
    (MainWindow Window, MainViewModel Vm) ShowDeck(DeckConfig cfg)
    {
        _host.Reset(cfg);
        var win = _host.Run(() =>
        {
            var w = new MainWindow(_host.Services) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 1100, Height = 650, ShowInTaskbar = false };
            w.PointerInContact = _ => true;
            w.Show();
            return w;
        });
        _host.Settle();
        return (win, _host.Run(() => (MainViewModel)win.DataContext));
    }

    /// <summary>Centre (pixels écran) de la touche portant ce titre.</summary>
    static Point CenterOf(MainWindow win, string label)
    {
        var tile = WpfHost.Descendants<FrameworkElement>(win).First(e => e.DataContext is DeckTileViewModel t && t.Label == label && e.ActualWidth > 20);
        return tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
    }

    static int Hotkeys(WpfHost host) => ((SimulatedPlatform)host.Services.GetRequiredService<PlatformServices>().Keyboard).Log.Count;

    [Fact]
    public void ASwipeStartedOnAKeyTurnsThePageWithoutRunningTheKey()
    {
        var (win, vm) = ShowDeck(Config());
        try
        {
            int before = Hotkeys(_host);
            _host.Run(() =>
            {
                var start = CenterOf(win, "Un");
                double dpi = VisualTreeHelper.GetDpi(win).DpiScaleX;
                int x = (int)start.X, y = (int)start.Y;
                win.HandleTouch(Down, 7, x, y, true, 0);
                for (int i = 1; i <= 8; i++) win.HandleTouch(Update, 7, x - (int)(i * 40 * dpi), y + i, true, i * 15);
                win.HandleTouch(Up, 7, x - (int)(320 * dpi), y + 8, false, 130);
            });
            _host.Settle();
            _host.Run(() =>
            {
                Assert.Equal(2, ActivePage(vm));                  // page suivante (la masquée est sautée)
                Assert.False(win.IsTouchTracking);
            });
            Assert.Equal(before, Hotkeys(_host));                 // la touche d'origine n'a rien envoyé
            Assert.True(_host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().All(t => t.RunState == TileRunState.Idle)));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void ATapStillRunsTheKeyAndASwipeInEditModeChangesNothing()
    {
        var (win, vm) = ShowDeck(Config());
        try
        {
            int before = Hotkeys(_host);
            _host.Run(() =>
            {
                var c = CenterOf(win, "Un");
                win.HandleTouch(Down, 8, (int)c.X, (int)c.Y, true, 0);
                win.HandleTouch(Up, 8, (int)c.X + 2, (int)c.Y, false, 60);
            });
            _host.Settle();
            var limit = DateTime.UtcNow.AddSeconds(5);
            while (Hotkeys(_host) == before && DateTime.UtcNow < limit) _host.Settle();
            Assert.Equal(before + 1, Hotkeys(_host));             // l'appui bref garde son comportement
            Assert.Equal(0, _host.Run(() => ActivePage(vm)));

            _host.Run(() => vm.StartEditing(new Place("P", 0), -1));
            _host.Settle();
            _host.Run(() =>
            {
                var c = CenterOf(win, "Deux");
                win.HandleTouch(Down, 9, (int)c.X, (int)c.Y, true, 0);
                for (int i = 1; i <= 8; i++) win.HandleTouch(Update, 9, (int)c.X - i * 40, (int)c.Y, true, i * 15);
                win.HandleTouch(Up, 9, (int)c.X - 320, (int)c.Y, false, 130);
            });
            _host.Settle();
            Assert.Equal(0, _host.Run(() => ActivePage(vm)));     // le glisser-déposer de l'édition a la priorité
            _host.Run(() => vm.EndEditCommand.Execute(null));
        }
        finally { _host.Run(win.Close); }
    }
}
