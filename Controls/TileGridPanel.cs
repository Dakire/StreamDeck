using System;
using System.Windows;
using System.Windows.Controls;

namespace StreamDak.Controls;

/// <summary>
/// Grille du deck : <see cref="Columns"/> × <see cref="Rows"/> cases de même taille, chaque tuile posée à son numéro de case
/// (<see cref="CellProperty"/>, ligne par ligne) et sur autant de cases que son emprise (<see cref="ColSpanProperty"/>,
/// <see cref="RowSpanProperty"/>). Remplace <c>UniformGrid</c> : un widget peut occuper plusieurs cases, et les cases qu'il recouvre n'ont pas de tuile.
/// </summary>
public sealed class TileGridPanel : Panel
{
    /// <summary>Nombre de colonnes.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Nombre de lignes.</summary>
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Numéro de la case d'ancrage d'une tuile (0 = en haut à gauche, ligne par ligne).</summary>
    public static readonly DependencyProperty CellProperty = DependencyProperty.RegisterAttached("Cell", typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Largeur d'une tuile, en cases.</summary>
    public static readonly DependencyProperty ColSpanProperty = DependencyProperty.RegisterAttached("ColSpan", typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Hauteur d'une tuile, en cases.</summary>
    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached("RowSpan", typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Nombre de colonnes.</summary>
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    /// <summary>Nombre de lignes.</summary>
    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    /// <summary>Lit le numéro de case d'une tuile.</summary>
    public static int GetCell(DependencyObject element) => (int)element.GetValue(CellProperty);
    /// <summary>Pose le numéro de case d'une tuile.</summary>
    public static void SetCell(DependencyObject element, int value) => element.SetValue(CellProperty, value);
    /// <summary>Lit la largeur d'une tuile en cases.</summary>
    public static int GetColSpan(DependencyObject element) => (int)element.GetValue(ColSpanProperty);
    /// <summary>Pose la largeur d'une tuile en cases.</summary>
    public static void SetColSpan(DependencyObject element, int value) => element.SetValue(ColSpanProperty, value);
    /// <summary>Lit la hauteur d'une tuile en cases.</summary>
    public static int GetRowSpan(DependencyObject element) => (int)element.GetValue(RowSpanProperty);
    /// <summary>Pose la hauteur d'une tuile en cases.</summary>
    public static void SetRowSpan(DependencyObject element, int value) => element.SetValue(RowSpanProperty, value);

    Rect RectOf(UIElement child, Size size)
    {
        int cols = Math.Max(1, Columns), rows = Math.Max(1, Rows);
        double cw = size.Width / cols, ch = size.Height / rows;
        int cell = Math.Max(0, GetCell(child));
        int col = Math.Min(cell % cols, cols - 1), row = cell / cols;
        int cs = Math.Clamp(GetColSpan(child), 1, cols - col), rs = Math.Clamp(GetRowSpan(child), 1, Math.Max(1, rows - row));
        return new Rect(col * cw, row * ch, cw * cs, ch * rs);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        foreach (UIElement child in InternalChildren) child.Measure(RectOf(child, size).Size);
        return size;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren) child.Arrange(RectOf(child, finalSize));
        return finalSize;
    }
}
