using System.Collections.Generic;
using System.Linq;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Glisser-déposer du mode édition : une touche lâchée sur « Retour » sort du dossier, lâchée sur un onglet change de page ; l'ouverture d'un dossier prévient la fenêtre.</summary>
[Collection("ui")]
public class DragDropFlowTests
{
    readonly WpfHost _host;
    public DragDropFlowTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config()
    {
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Clear();
        folder.Children.Add(Btn("Dedans"));
        return new()
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages =
            {
                new DeckPage { Name = "A", Buttons = { Btn("Un"), folder } },
                new DeckPage { Name = "B", Buttons = { Btn("Autre") } },
            } } },
        };
    }

    MainViewModel NewDeck(out List<int> folderEvents)
    {
        _host.Reset(Config());
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        var events = new List<int>();
        vm.FolderNavigated += events.Add;
        folderEvents = events;
        return vm;
    }

    static void EnterEdit(MainViewModel vm) { vm.OpenMenuCommand.Execute(null); QuickEditTests.Pick(vm, "Modifier le deck"); }

    [Fact]
    public void OpeningAndLeavingAFolderTellsTheWindowToAnimateInTheRightDirection()
    {
        var vm = NewDeck(out var events);
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Click.Execute(null);
            Assert.Equal(new[] { 1 }, events);
            vm.ParentFolderCommand.Execute(null);
            Assert.Equal(new[] { 1, -1 }, events);
        });
    }

    [Fact]
    public void ATileDroppedOnTheBackTileLeavesTheFolderInOneUndoStep()
    {
        var vm = NewDeck(out _);
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Click.Execute(null);
            EnterEdit(vm);
            var inside = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dedans");
            var back = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Retour");

            vm.BeginDrag(inside);
            vm.UpdateDrag(10, 10, back);
            Assert.True(back.IsDropTarget);                                     // la case « Retour » s'allume
            Assert.Equal("Sortir du dossier", vm.DragHint);
            vm.EndDrag();

            vm.MoveTile(inside, back);
            Assert.Contains(Doc.Container(new Place("P", 0))!, b => b.Label == "Dedans");   // revenu à la racine de la page
            Doc.Undo();
            Assert.DoesNotContain(Doc.Container(new Place("P", 0))!, b => b.Label == "Dedans");
        });
    }

    [Fact]
    public void ATileDroppedOnAPageTabMovesToThatPageAndCanBeUndone()
    {
        var vm = NewDeck(out _);
        _host.Run(() =>
        {
            EnterEdit(vm);
            var tile = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un");

            vm.MoveTileToPage(tile, 0);                                          // la page affichée : rien
            Assert.Contains(Doc.Container(new Place("P", 0))!, b => b.Label == "Un");

            vm.MoveTileToPage(tile, 1);
            Assert.DoesNotContain(Doc.Container(new Place("P", 0))!, b => b.Label == "Un");
            Assert.Contains(Doc.Container(new Place("P", 1))!, b => b.Label == "Un");
            Doc.Undo();
            Assert.Contains(Doc.Container(new Place("P", 0))!, b => b.Label == "Un");
        });
    }

    [Fact]
    public void TheDragVisualsStayStaticWhenAnimationsAreOff()
    {
        var vm = NewDeck(out _);
        _host.Run(() =>
        {
            var tile = vm.Tiles.OfType<DeckTileViewModel>().First();
            Assert.Equal(Motion.Enabled, tile.Animate);                          // les gabarits n'animent que si les animations sont permises
        });
    }
}
