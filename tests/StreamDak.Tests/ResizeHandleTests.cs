using System.Linq;
using System.Windows;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Redimensionnement d'un widget en glissant sa poignée de coin sur la grille.</summary>
[Collection("ui")]
public class ResizeHandleTests
{
    readonly WpfHost _host;
    public ResizeHandleTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    // Grille 4 × 3 de 400 × 300 : cases de 100 × 100
    [Theory]
    [InlineData(50, 50, 0, 1, 1)]       // pointeur dans la case d'ancrage : une case
    [InlineData(250, 150, 0, 3, 2)]     // jusqu'à la troisième colonne et la deuxième ligne
    [InlineData(390, 290, 0, 4, 3)]     // tout en bas à droite
    [InlineData(999, 999, 0, 4, 3)]     // hors de la grille : borné à la page
    [InlineData(-40, -40, 5, 1, 1)]     // à gauche ou au-dessus de l'ancrage : une case, jamais moins
    [InlineData(390, 290, 5, 3, 2)]     // ancré en (1, 1) : il reste trois colonnes et deux lignes
    public void ThePointerChoosesTheSizeAndItIsBoundedByThePage(double x, double y, int cell, int cols, int rows) =>
        Assert.Equal((cols, rows), MainWindow.SizeAt(new Point(x, y), cell, 400, 300, 4, 3));

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    MainViewModel NewDeckInEditMode()
    {
        var clock = new DeckButton { Label = "Heure", Widget = WidgetTypes.Clock, ColSpan = 1, RowSpan = 1, Color = "#0F766E" };
        _host.Reset(new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 3, Pages = { new DeckPage { Name = "A", Buttons =
            {
                clock, new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true },
                new DeckButton { Blank = true }, new DeckButton { Blank = true }, Btn("Gêneur"), new DeckButton { Blank = true },
            } } } } },
        });
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() => { vm.OpenMenuCommand.Execute(null); QuickEditTests.Pick(vm, "Modifier le deck"); vm.Editor!.SelectIndex(0); });
        return vm;
    }

    [Fact]
    public void OnlyASelectedWidgetShowsItsHandle()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            var clock = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Heure");
            var plain = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Gêneur");
            Assert.True(clock.CanResize);
            Assert.False(plain.CanResize);                                       // un bouton ordinaire n'a pas de taille
            vm.Editor!.SelectIndex(6);
            Assert.False(vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Heure").CanResize);   // ni un widget qui n'est plus sélectionné
        });
    }

    [Fact]
    public void ResizingGrowsTheWidgetOverFreeCellsAndStopsBeforeAnOccupiedOne()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            vm.ResizeSelectedWidget(2, 1);
            Assert.Equal((2, 1), Doc.Get(Root, 0)!.Span());

            vm.ResizeSelectedWidget(2, 2);                                        // la case (1, 1) est libre : 2 × 2
            Assert.Equal((2, 2), Doc.Get(Root, 0)!.Span());

            vm.ResizeSelectedWidget(3, 3);                                        // le bouton « Gêneur » (case 6) est dans l'emprise : ramené à ce qui tient
            var span = Doc.Get(Root, 0)!.Span();
            Assert.True(span.Cols * span.Rows <= 6);
            Assert.Equal("Gêneur", Doc.Get(Root, 6)!.Label);

            vm.ResizeSelectedWidget(99, 99);                                      // jamais hors de la page
            span = Doc.Get(Root, 0)!.Span();
            Assert.InRange(span.Cols, 1, 4);
            Assert.InRange(span.Rows, 1, 3);
        });
    }

    [Fact]
    public void AWholeDragIsOneUndoStep()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            vm.ResizeSelectedWidget(2, 1);
            vm.ResizeSelectedWidget(1, 2);
            vm.ResizeSelectedWidget(2, 2);
            Assert.Equal((2, 2), Doc.Get(Root, 0)!.Span());

            Doc.Undo();
            Assert.Equal((1, 1), Doc.Get(Root, 0)!.Span());                       // un seul pas pour tout le glissement
        });
    }
}
