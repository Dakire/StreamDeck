using System;
using System.Linq;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Déplacer des boutons hors du mode édition (appui long) : états, règles de dépôt, corbeille, annulation. Le geste lui-même est dans MainWindow.Touch.cs.</summary>
[Collection("ui")]
public class LiftDragTests
{
    readonly WpfHost _host;
    public LiftDragTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config()
    {
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Clear();
        return new()
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages =
            {
                new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux"), folder } },
                new DeckPage { Name = "B", Buttons = { Btn("Autre") } },
            } } },
        };
    }

    MainViewModel NewDeck(bool enabled)
    {
        var cfg = Config();
        cfg.LiftDragDisabled = !enabled;
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    static DeckTileViewModel Tile(MainViewModel vm, string label) => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == label);

    [Fact]
    public void ADisabledGestureLiftsNothingAndChangesNothing()
    {
        var vm = NewDeck(enabled: false);
        _host.Run(() =>
        {
            Assert.False(vm.CanLift(Tile(vm, "Un")));
            Assert.False(vm.Lift(Tile(vm, "Un")));
            vm.BeginDrag(Tile(vm, "Un"));
            Assert.False(vm.IsDragging);                                              // sans appui long, aucun glissement hors édition
            vm.MoveTile(Tile(vm, "Un"), Tile(vm, "Deux"));
            Assert.Equal("Un", Doc.Container(new Place("P", 0))![0].Label);           // rien n'a bougé
        });
    }

    [Fact]
    public void ALongPressLiftsTheTileAndCancellingRestoresEverything()
    {
        var vm = NewDeck(enabled: true);
        _host.Run(() =>
        {
            var tile = Tile(vm, "Un");
            Assert.True(vm.Lift(tile));
            Assert.True(tile.IsLifted);
            Assert.True(vm.IsLifting);
            vm.BeginDrag(tile);
            Assert.True(vm.IsDragging);
            Assert.True(vm.ShowTrash);
            vm.CancelLift();                                                           // Échap, ou doigt relâché hors de la grille
            Assert.False(tile.IsLifted);
            Assert.False(vm.IsDragging);
            Assert.False(vm.ShowTrash);
            Assert.False(vm.IsLifting);
            Assert.Equal("Un", Doc.Container(new Place("P", 0))![0].Label);
            Assert.False(Doc.CanUndo);                                                 // rien d'écrit
        });
    }

    [Fact]
    public void ALiftedTileCanBeSwappedInOneUndoStep()
    {
        var vm = NewDeck(enabled: true);
        _host.Run(() =>
        {
            var one = Tile(vm, "Un");
            vm.Lift(one);
            vm.BeginDrag(one);
            vm.UpdateDrag(10, 10, Tile(vm, "Deux"));
            Assert.Equal("Échanger", vm.DragHint);
            vm.DropLifted(one, Tile(vm, "Deux"));
            var list = Doc.Container(new Place("P", 0))!;
            Assert.Equal("Deux", list[0].Label);
            Assert.Equal("Un", list[1].Label);
            Assert.False(vm.IsLifting);
            Doc.Undo();
            Assert.Equal("Un", Doc.Container(new Place("P", 0))![0].Label);
        });
    }

    [Fact]
    public void ALiftedTileDroppedOnAFolderIsStoredInsideIt()
    {
        var vm = NewDeck(enabled: true);
        _host.Run(() =>
        {
            var one = Tile(vm, "Un");
            vm.Lift(one);
            vm.BeginDrag(one);
            vm.DropLifted(one, Tile(vm, "Dossier"));
            var folder = Doc.Container(new Place("P", 0))!.First(b => b.IsFolder);
            Assert.Contains(folder.Children!, b => b.Label == "Un");
        });
    }

    [Fact]
    public void ALiftedTileDroppedOnAPageTabMovesToThatPage()
    {
        var vm = NewDeck(enabled: true);
        _host.Run(() =>
        {
            var one = Tile(vm, "Un");
            vm.Lift(one);
            vm.BeginDrag(one);
            vm.DropLiftedOnPage(one, 1);
            Assert.Contains(Doc.Container(new Place("P", 1))!, b => b?.Label == "Un");
            Assert.DoesNotContain(Doc.Container(new Place("P", 0))!, b => b?.Label == "Un");
        });
    }

    [Fact]
    public void TheTrashDeletesTheButtonAndIsUndoable()
    {
        var vm = NewDeck(enabled: true);
        _host.Run(() =>
        {
            var one = Tile(vm, "Un");
            vm.Lift(one);
            vm.BeginDrag(one);
            vm.IsOverTrash = true;
            Assert.Equal("Supprimer le bouton", vm.DragHint);
            vm.DropOnTrash(one);
            Assert.DoesNotContain(Doc.Container(new Place("P", 0))!, b => b?.Label == "Un");
            Assert.False(vm.IsOverTrash);
            Doc.Undo();
            Assert.Contains(Doc.Container(new Place("P", 0))!, b => b?.Label == "Un");
        });
    }

    [Fact]
    public void TheGestureIsOnByDefaultAndTheConfiguredDelayIsUsed()
    {
        var cfg = Config();
        cfg.LiftDelayMs = 800;
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        Assert.True(vm.LiftDragEnabled);
        Assert.Equal(800, vm.LiftDelayMs);
        Assert.True(_host.Run(() => vm.CanLift(Tile(vm, "Un"))));
    }

    [Theory]
    [InlineData(100, 300)] [InlineData(500, 500)] [InlineData(5000, 1500)]
    public void TheLongPressDelayIsKeptInItsRange(int asked, int expected)
    {
        var vm = NewDeck(enabled: true);
        vm.LiftDelayMs = asked;
        Assert.Equal(expected, vm.LiftDelayMs);
    }

    [Fact]
    public void TheTapRecognizerUsesTheConfiguredThresholdAndResetsItOnEachPress()
    {
        var tap = new TapRecognizer { LongPressThresholdMs = 800 };
        tap.Down(1, 0, 0, 0);
        Assert.Equal(TapRecognizer.LongPressMs, tap.LongPressThresholdMs);               // chaque appui repart du seuil par défaut
        tap.LongPressThresholdMs = 800;
        Assert.False(tap.TryLongPress(700));
        Assert.True(tap.TryLongPress(800));
        Assert.False(tap.Up(1, 0, 0));                                                    // un appui long consommé n'est pas un appui
    }
}

/// <summary>Le délai d'inactivité réglé dans la configuration arrive sur la tuile d'une page web.</summary>
[Collection("ui")]
public class WebIdleSettingTests
{
    readonly WpfHost _host;
    public WebIdleSettingTests(WpfHost host) { _host = host; }

    [Theory]
    [InlineData(0, 30)] [InlineData(90, 90)]
    public void TheConfiguredDelayReachesTheWebPageTile(int configured, int expectedSeconds)
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.WebIdleExitSeconds = configured;
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Web", Kind = PageKinds.Web, Url = "https://example.org/" });
        cfg.Profiles[0].Pages[^1].Buttons.Clear();
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() =>
        {
            ((StreamDak.Actions.INavigator)vm).GoToPage("Web");
        });
        _host.Settle();
        var web = _host.Run(() => vm.Tiles.OfType<WebWidgetViewModel>().FirstOrDefault());
        Assert.NotNull(web);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), web!.IdleExit);
    }
}
