using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using StreamDak.Behaviors;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Écartement des touches voisines de la case visée (logique pure, grille de 4 colonnes).</summary>
public class DragSpreadCoreTests
{
    static readonly int[] Cells = Enumerable.Range(0, 12).ToArray();   // 4 colonnes × 3 lignes

    [Fact]
    public void TheEightNeighboursMoveAwayFromTheTargetAndEverythingElseStays()
    {
        var o = DragSpread.Offsets(4, Cells, sourceCell: 0, targetCell: 5);          // cible : ligne 1, colonne 1
        Assert.Equal((0.0, 0.0), o[5]);                                             // la cible ne bouge pas
        Assert.Equal((0.0, 0.0), o[0]);                                             // ni la source (voisine diagonale, mais tenue par le doigt)
        Assert.Equal((DragSpread.Amount, 0.0), o[6]);                               // à droite : vers la droite
        Assert.Equal((-DragSpread.Amount, 0.0), o[4]);                              // à gauche : vers la gauche
        Assert.Equal((0.0, DragSpread.Amount), o[9]);                               // dessous : vers le bas
        Assert.Equal((0.0, -DragSpread.Amount), o[1]);                              // dessus : vers le haut
        Assert.Equal((DragSpread.Amount * 0.7, DragSpread.Amount * 0.7), o[10]);    // diagonale : 70 %
        Assert.Equal((0.0, 0.0), o[3]);                                             // éloignée
        Assert.Equal((0.0, 0.0), o[11]);
    }

    [Fact]
    public void NoTargetOrTheSourceAsTargetMovesNothing()
    {
        Assert.All(DragSpread.Offsets(4, Cells, 2, -1), v => Assert.Equal((0.0, 0.0), v));
        Assert.All(DragSpread.Offsets(4, Cells, 5, 5), v => Assert.Equal((0.0, 0.0), v));
        Assert.All(DragSpread.Offsets(0, Cells, 1, 2), v => Assert.Equal((0.0, 0.0), v));
    }

    [Fact]
    public void ACornerTargetOnlyHasItsRealNeighbours()
    {
        var o = DragSpread.Offsets(4, Cells, -1, 0);                                 // coin haut gauche, source inconnue (page changée)
        Assert.Equal(3, o.Count(v => v != (0.0, 0.0)));                              // droite, dessous, diagonale
    }
}

/// <summary>Les voisines s'écartent dans le modèle du deck pendant un glissé, et reviennent en place à la fin.</summary>
[Collection("ui")]
public class DragSpreadDeckTests
{
    readonly WpfHost _host;
    public DragSpreadDeckTests(WpfHost host) { _host = host; }

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    [Fact]
    public void NeighboursOfTheHoveredTileMoveAndComeBack()
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 3, Pages = { new DeckPage { Name = "A", Buttons = { Btn("0"), Btn("1"), Btn("2"), Btn("3"), Btn("4"), Btn("5"), Btn("6"), Btn("7"), Btn("8") } } } } },
        };
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() =>
        {
            DeckTileViewModel T(string l) => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == l);
            Assert.True(vm.Lift(T("0")));
            vm.BeginDrag(T("0"));
            vm.UpdateDrag(10, 10, T("5"));                                           // survole le bouton « 5 » (ligne 1, colonne 1 si 4 colonnes)
            Assert.Equal(4, vm.L.Columns);
            Assert.Equal((0.0, 0.0), (T("5").SpreadX, T("5").SpreadY));              // la cible ne bouge pas
            Assert.Equal(DragSpread.Amount, T("6").SpreadX);                         // sa voisine de droite s'écarte
            Assert.Equal(-DragSpread.Amount, T("4").SpreadX);
            Assert.Equal(0.0, T("0").SpreadX);                                       // la touche tenue non plus
            vm.UpdateDrag(10, 10, null);                                             // le doigt quitte la grille
            Assert.All(vm.Tiles.OfType<DeckTileViewModel>(), t => Assert.Equal((0.0, 0.0), (t.SpreadX, t.SpreadY)));
            vm.UpdateDrag(10, 10, T("5"));
            vm.CancelLift();                                                         // annulation : tout revient en place
            Assert.All(vm.Tiles.OfType<DeckTileViewModel>(), t => Assert.Equal((0.0, 0.0), (t.SpreadX, t.SpreadY)));
        });
    }
}

/// <summary>Comportements animés : valeurs finales exactes sans animations, nettoyage au repos.</summary>
[Collection("ui")]
public class TileMotionTests
{
    readonly WpfHost _host;
    public TileMotionTests(WpfHost host) { _host = host; }

    [Fact]
    public void WithoutAnimationsTheValuesAreSetImmediatelyAndClearedAtRest()
    {
        _host.Reset();
        _host.Run(() =>
        {
            Motion.Apply(reduceMotion: true);
            try
            {
                var grid = new Grid();
                TileMotion.SetLifted(grid, true);
                var scale = Assert.IsType<ScaleTransform>(grid.RenderTransform);
                Assert.Equal(TileMotion.LiftScale, scale.ScaleX);
                Assert.Equal(0.55, Assert.IsType<DropShadowEffect>(grid.Effect).Opacity);
                TileMotion.SetLifted(grid, false);
                Assert.Same(Transform.Identity, grid.RenderTransform);                // posée : plus de transformation ni d'ombre
                Assert.Null(grid.Effect);

                var button = new Button();
                TileMotion.SetSpreadX(button, 10);
                TileMotion.SetSpreadY(button, -7);
                var move = Assert.IsType<TranslateTransform>(button.RenderTransform);
                Assert.Equal((10.0, -7.0), (move.X, move.Y));
                TileMotion.SetSpreadX(button, 0);
                TileMotion.SetSpreadY(button, 0);
                Assert.Equal((0.0, 0.0), (move.X, move.Y));
            }
            finally { Motion.Apply(reduceMotion: false); }
        });
    }

    [Fact]
    public void ASpreadOfZeroNeverCreatesATransform()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var button = new Button();
            TileMotion.SetSpreadX(button, 0);
            Assert.Same(Transform.Identity, button.RenderTransform);
        });
    }
}
