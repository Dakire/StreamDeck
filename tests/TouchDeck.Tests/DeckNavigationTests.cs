using System;
using System.Linq;
using System.Threading.Tasks;
using TouchDeck.Actions;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Fenêtre factice : le deck est piloté sans placement Win32.</summary>
sealed class FakeDeckShell : IShell
{
    public bool IsFullscreen { get; set; }
    public bool IsOnTop { get; set; }
    public int Placements { get; private set; }
    public void NextScreen() { }
    public void ToggleFullscreen() => IsFullscreen = !IsFullscreen;
    public void SetOnTop(bool onTop) => IsOnTop = onTop;
    public void Minimize() { }
    public void ReapplyPlacement() => Placements++;
}

/// <summary>
/// Tests de caractérisation de la navigation du deck (pages, dossiers, sous-pages, profils, profil par application) :
/// ils figent le comportement actuel avant tout remaniement de <see cref="MainViewModel"/>.
/// </summary>
[Collection("ui")]
public class DeckNavigationTests
{
    readonly WpfHost _host;
    public DeckNavigationTests(WpfHost host) => _host = host;

    static DeckButton Btn(string label, string type = "hotkey", string value = "ctrl+c") =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = type, Value = value } } };

    /// <summary>Profil « P » en 3×2 (6 cases) : page A visible, page B visible, page C masquée.</summary>
    static DeckConfig Config()
    {
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Add(Btn("Dedans"));
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
            Profiles =
            {
                new DeckProfile
                {
                    Name = "P", Columns = 3, Rows = 2,
                    Pages =
                    {
                        new DeckPage { Name = "A", Buttons = { Btn("Copier"), folder, Btn("Vers C", "page", "C"), Btn("Retour", "page", "back"), Btn("Profil Q", "profile", "Q") } },
                        new DeckPage { Name = "B", Buttons = { Btn("Accueil", "page", "home"), Btn("Inconnue", "page", "Nulle part") } },
                        new DeckPage { Name = "C", Hidden = true, Buttons = { Btn("Retour", "page", "back") } },
                    },
                },
                new DeckProfile { Name = "Q", Columns = 3, Rows = 2, Pages = { new DeckPage { Name = "Q1", Buttons = { Btn("Q") } } } },
            },
        };
        return cfg;
    }

    MainViewModel NewDeck(DeckConfig cfg)
    {
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = new MainViewModel(new FakeDeckShell()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    DeckTileViewModel Tile(MainViewModel vm, string label) =>
        _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == label));

    string[] Labels(MainViewModel vm) => _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().Select(t => t.Label).ToArray());
    string ActiveTab(MainViewModel vm) => _host.Run(() => vm.Tabs.Single(t => t.IsActive).Text);

    async Task Press(MainViewModel vm, string label)
    {
        var tile = Tile(vm, label);
        await _host.Run(() => vm.RunTileAsync(tile)).WaitAsync(TimeSpan.FromSeconds(10));
        _host.Settle();
    }

    [Fact]
    public void TheHarnessNeverUsesTheRealPlatformNorShowsWindows()
    {
        Assert.True(_host.Run(() => AppServices.Simulated));
        Assert.IsType<SimulatedPlatform>(_host.Run(() => AppServices.Engine.Context.Platform.Keyboard));
        Assert.DoesNotContain(_host.Run(() => System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().ToList()), w => w.IsVisible);
    }

    [Fact]
    public void HiddenPagesAreNotTabsAndTabsSwitchPages()
    {
        var vm = NewDeck(Config());
        Assert.Equal(new[] { "A", "B" }, _host.Run(() => vm.Tabs.Select(t => t.Text).ToArray()));
        Assert.Equal("A", ActiveTab(vm));
        _host.Run(() => vm.Tabs[1].Command.Execute(null));
        Assert.Equal("B", ActiveTab(vm));
        Assert.Equal(new[] { "Accueil", "Inconnue" }, Labels(vm));
    }

    [Fact]
    public async Task PageActionsGoToHiddenPagesBackAndHome()
    {
        var vm = NewDeck(Config());
        await Press(vm, "Vers C");                                   // page masquée : accessible par une action
        Assert.Equal(new[] { "Retour" }, Labels(vm));
        Assert.DoesNotContain(_host.Run(() => vm.Tabs.ToList()), t => t.IsActive);
        await Press(vm, "Retour");                                   // « back » : page précédente
        Assert.Equal("A", ActiveTab(vm));

        _host.Run(() => vm.Tabs[1].Command.Execute(null));
        await Press(vm, "Accueil");                                  // « home » : première page
        Assert.Equal("A", ActiveTab(vm));
    }

    [Fact]
    public async Task UnknownPageKeepsThePageAndShowsAnError()
    {
        var vm = NewDeck(Config());
        _host.Run(() => vm.Tabs[1].Command.Execute(null));
        await Press(vm, "Inconnue");
        Assert.Equal("B", ActiveTab(vm));
        Assert.Contains("Page introuvable", _host.Run(() => vm.Status) ?? "");
    }

    [Fact]
    public void FoldersShowABackTileAndAPath()
    {
        var vm = NewDeck(Config());
        _host.Run(() => Tile(vm, "Dossier").Click.Execute(null));
        Assert.True(_host.Run(() => vm.InFolder));
        Assert.Equal("Dossier", _host.Run(() => vm.PathText));
        Assert.Equal(new[] { "Retour", "Dedans" }, Labels(vm));
        Assert.Null(_host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First().Button));   // case « retour »

        _host.Run(() => vm.ParentFolderCommand.Execute(null));
        Assert.False(_host.Run(() => vm.InFolder));
        Assert.Equal("", _host.Run(() => vm.PathText));
        Assert.Contains("Copier", Labels(vm));
    }

    [Fact]
    public void PagesLargerThanTheGridGetSubPagesThatWrapAround()
    {
        var cfg = Config();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Clear();
        for (int i = 1; i <= 8; i++) page.Buttons.Add(Btn("B" + i));
        var vm = NewDeck(cfg);

        Assert.Equal(6, _host.Run(() => vm.Tiles.Count));
        Assert.True(_host.Run(() => vm.HasSubs));
        Assert.Equal("1 / 2", _host.Run(() => vm.SubText));
        _host.Run(() => vm.NextSubCommand.Execute(null));
        Assert.Equal("2 / 2", _host.Run(() => vm.SubText));
        Assert.Equal(new[] { "B7", "B8" }, Labels(vm));
        Assert.Equal(4, _host.Run(() => vm.Tiles.OfType<SpacerTileViewModel>().Count()));   // cases libres dessinées éteintes
        _host.Run(() => vm.NextSubCommand.Execute(null));
        Assert.Equal("1 / 2", _host.Run(() => vm.SubText));
        _host.Run(() => vm.PrevSubCommand.Execute(null));
        Assert.Equal("2 / 2", _host.Run(() => vm.SubText));
    }

    [Fact]
    public async Task ProfileActionChangesTheActiveProfileAndStartsOnItsFirstPage()
    {
        var vm = NewDeck(Config());
        await Press(vm, "Profil Q");
        Assert.Equal("Q", _host.Run(() => AppServices.Document.Config.ActiveProfile));
        Assert.Equal("Q", _host.Run(() => vm.ProfileName));
        Assert.Equal(new[] { "Q" }, Labels(vm));
        Assert.False(_host.Run(() => vm.IsAutoProfile));
    }

    [Fact]
    public async Task ButtonsRunTheirActionsOnThePlatform()
    {
        var vm = NewDeck(Config());
        var sim = (SimulatedPlatform)_host.Run(() => AppServices.Engine.Context.Platform.Keyboard);
        int before = sim.Log.Count;
        var tile = Tile(vm, "Copier");
        var run = _host.Run(() => vm.RunTileAsync(tile));
        await WaitUntil(() => _host.Run(() => tile.RunState) == TileRunState.Success);   // coche pendant 600 ms
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TileRunState.Idle, _host.Run(() => tile.RunState));
        Assert.Contains("hotkey ctrl+c", sim.Log.Skip(before));
    }

    [Fact]
    public async Task ForegroundApplicationShowsItsProfileUntilItLosesFocus()
    {
        var cfg = Config();
        cfg.Profiles[1].AppMatch = "notepad";
        var vm = NewDeck(cfg);
        var sim = (SimulatedPlatform)_host.Run(() => AppServices.Engine.Context.Platform.Windows);
        try
        {
            _host.Run(() => sim.Foreground = "Notepad");
            await WaitUntil(() => _host.Run(() => vm.IsAutoProfile));
            Assert.Equal("Q", _host.Run(() => vm.ProfileName));
            Assert.Equal("P", _host.Run(() => AppServices.Document.Config.ActiveProfile));   // jamais enregistré

            _host.Run(() => sim.Foreground = "explorer");
            await WaitUntil(() => !_host.Run(() => vm.IsAutoProfile));
            Assert.Equal("P", _host.Run(() => vm.ProfileName));
        }
        finally { _host.Run(() => sim.Foreground = null); }
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("Condition non atteinte en 5 s");
            await Task.Delay(50);
        }
    }
}
