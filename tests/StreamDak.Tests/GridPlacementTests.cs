using System.Linq;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Placement des curseurs 1 × 2 (et des widgets plus larges) dans la grille : jamais hors page, jamais sur une case occupée, jamais l'un sur l'autre.</summary>
public class GridPlacementTests
{
    const int Cols = 5, Rows = 3;

    static (int Cols, int Rows) Place(int cell, int wantCols, int wantRows, params int[] occupied) =>
        TileSpans.Effective(cell, wantCols, wantRows, Cols, Rows, occupied.Contains);

    [Fact]
    public void AVerticalSliderKeepsItsTwoRowsWhenTheCellBelowIsFree() => Assert.Equal((1, 2), Place(2, 1, 2));

    [Fact]
    public void AVerticalSliderOnTheLastRowShrinksToOneCellInsteadOfLeavingThePage() => Assert.Equal((1, 1), Place(Cols * (Rows - 1) + 3, 1, 2));

    [Fact]
    public void AVerticalSliderOverAnOccupiedCellBelowShrinksToOneCell() => Assert.Equal((1, 1), Place(1, 1, 2, 1 + Cols));

    [Fact]
    public void ASliderNextToAnOccupiedCellOnItsSideStillKeepsItsHeight() => Assert.Equal((1, 2), Place(1, 1, 2, 2, 2 + Cols));

    [Fact]
    public void ASliderOnTheLastColumnIsClampedToOneColumn() => Assert.Equal((1, 2), Place(Cols - 1, 3, 2));

    [Fact]
    public void TwoSideBySideSlidersCoverDistinctCells()
    {
        var first = Place(0, 1, 2);
        var second = Place(1, 1, 2);
        var coveredByFirst = TileSpans.Covered(0, first.Cols, first.Rows, Cols).Append(0).ToHashSet();
        var coveredBySecond = TileSpans.Covered(1, second.Cols, second.Rows, Cols).Append(1).ToHashSet();
        Assert.Equal((1, 2), first);
        Assert.Equal((1, 2), second);
        Assert.Empty(coveredByFirst.Intersect(coveredBySecond));
    }

    [Fact]
    public void CoveredCellsOfASliderAreTheOnesBelowItsAnchor() => Assert.Equal(new[] { 3 + Cols }, TileSpans.Covered(3, 1, 2, Cols));

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 2, 1, 2)]
    [InlineData(-3, 99, 1, WidgetTypes.MaxSpan)]
    public void AnOldButtonWithNoSpanIsOneCellAndSpansAreBounded(int cols, int rows, int expectedCols, int expectedRows)
    {
        var button = new DeckButton { ColSpan = cols, RowSpan = rows };
        var (c, r) = button.Span();
        Assert.Equal(expectedCols, c);
        Assert.Equal(expectedRows, r);
    }
}
