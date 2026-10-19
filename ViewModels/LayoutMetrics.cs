using System;
using System.Windows;

namespace TouchDeck.ViewModels;

/// <summary>
/// Dimensions de l'interface calculées à partir de la taille de la fenêtre (en DIP) ; recalculées à chaque redimensionnement.
/// Jetons de mise en page : un espacement de base (Gap), une hauteur tactile (BarH ≥ 52), des rayons proportionnels aux touches.
/// </summary>
public sealed class LayoutMetrics
{
    /// <summary>Rapport largeur / hauteur maximal d'une touche (et inverse en portrait) : jamais de touche étirée en bandeau.</summary>
    public const double MaxAspect = 1.3;
    /// <summary>Marge intérieure du dock autour de ses commandes.</summary>
    public const double DockInset = 6;

    public double Gap { get; init; }
    public double TopH { get; init; }
    public double BarH { get; init; }
    public double BarRowH { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }

    // barre du haut
    public Thickness TopMargin { get; init; }
    public double LogoSize { get; init; }
    public double TopFont { get; init; }
    public Thickness BrandTextMargin { get; init; }
    public double GripW { get; init; }
    public double GripH { get; init; }
    public double MinW { get; init; }
    public double MinH { get; init; }
    public double MinFont { get; init; }
    public double PillH { get; init; }
    public CornerRadius PillRadius { get; init; }
    public double PillFont { get; init; }

    // grille de boutons : centrée, de la taille exacte des touches
    public Thickness GridMargin { get; init; }
    public double GridW { get; init; }
    public double GridH { get; init; }
    public Thickness TilePad { get; init; }
    public CornerRadius TileRadius { get; init; }
    public double IconSize { get; init; }
    public double LabelSize { get; init; }
    public Thickness LabelMargin { get; init; }
    public double EditSize { get; init; }
    public Thickness EditMargin { get; init; }
    public double AddGlyphSize { get; init; }
    public double AddRadius { get; init; }

    // dock du bas
    public Thickness BarMargin { get; init; }
    public CornerRadius BarRadius { get; init; }
    public CornerRadius DockRadius { get; init; }
    public double BarFont { get; init; }
    public double SubFont { get; init; }
    public double GearFont { get; init; }
    public double TabMinW { get; init; }
    public Thickness TabMargin { get; init; }
    public Thickness TabPadding { get; init; }
    public Thickness ChipMargin { get; init; }
    public Thickness ChipPadding { get; init; }
    public Thickness StatusPadding { get; init; }
    public Thickness SubTextMargin { get; init; }
    public double DotSize { get; init; }
    public double DotActiveW { get; init; }
    public Thickness ToastMargin { get; init; }

    // menu
    public double MenuRowH { get; init; }
    public double MenuFont { get; init; }
    public double MenuIconFont { get; init; }
    public double MenuIconW { get; init; }
    public double MenuWidth { get; init; }
    public Thickness MenuMargin { get; init; }

    public static readonly LayoutMetrics Initial = Compute(1100, 650, new DeckConfig());

    readonly record struct Frame(double Scale, double Gap, double BarH, double TopH, double AvailW, double AvailH);

    static Frame FrameOf(double W, double H, DeckConfig cfg)
    {
        double scale = Math.Clamp(Math.Min(H, W * 0.75) / 1080.0, 0.6, 1.6);
        double gap = Math.Max(6, cfg.Spacing * scale);
        double barH = Math.Clamp(H * 0.07, 52, 84);           // cibles tactiles >= 48 px
        double topH = Math.Clamp(H * 0.055, 40, 60);
        double dockRow = barH + DockInset * 2 + gap;            // dock + marge basse
        return new Frame(scale, gap, barH, topH, W - gap * 2, H - topH - dockRow - gap);
    }

    /// <summary>Taille de la grille (colonnes, lignes) pour une fenêtre donnée : format du profil, sinon réglage global, sinon automatique.</summary>
    public static (int Columns, int Rows) GridSize(double W, double H, DeckConfig cfg, int colsOverride = 0, int rowsOverride = 0)
    {
        var f = FrameOf(W, H, cfg);
        // Automatique : des lignes d'environ 230 DIP (≈ 4 en FHD), puis autant de colonnes que des touches presque carrées le permettent
        int rows = rowsOverride > 0 ? rowsOverride : cfg.Rows > 0 ? cfg.Rows
            : (int)Math.Clamp(Math.Round(f.AvailH / (230 * Math.Max(f.Scale, 0.8)), MidpointRounding.AwayFromZero), 2, 8);
        int cols = colsOverride > 0 ? colsOverride : cfg.Columns > 0 ? cfg.Columns
            : (int)Math.Clamp(Math.Round(f.AvailW / (f.AvailH / rows * 1.15), MidpointRounding.AwayFromZero), 2, 12);
        return (cols, rows);
    }

    /// <summary>colsOverride / rowsOverride : format imposé par le profil (0 = réglage global, puis automatique).</summary>
    public static LayoutMetrics Compute(double W, double H, DeckConfig cfg, int colsOverride = 0, int rowsOverride = 0)
    {
        var f = FrameOf(W, H, cfg);
        double gap = f.Gap, barH = f.BarH, topH = f.TopH;
        var (cols, rows) = GridSize(W, H, cfg, colsOverride, rowsOverride);

        // Cellules : on remplit l'espace, en bornant la déformation ; le surplus éventuel est réparti autour de la grille
        double cellW = f.AvailW / cols, cellH = f.AvailH / rows;
        if (cellW > cellH * MaxAspect) cellW = cellH * MaxAspect;
        if (cellH > cellW * MaxAspect) cellH = cellW * MaxAspect;
        double cellMin = Math.Min(cellW, cellH) - gap;
        double radius = Math.Clamp(cellMin * 0.16, 12, 34);
        double rowH = Math.Clamp(H * 0.075, 52, 76);
        double fs = Math.Max(15, rowH * 0.32);
        double pillH = Math.Max(34, topH * 0.74);

        return new LayoutMetrics
        {
            Gap = gap, TopH = topH, BarH = barH, BarRowH = barH + DockInset * 2 + gap, Columns = cols, Rows = rows,

            TopMargin = new Thickness(gap * 1.2, 0, gap * 0.6, 0), LogoSize = topH * 0.52, TopFont = Math.Max(14, topH * 0.34),
            BrandTextMargin = new Thickness(topH * 0.22, 0, topH * 0.3, 0),
            GripW = Math.Max(60, topH * 1.6), GripH = Math.Max(4, topH * 0.1),
            MinW = Math.Max(48, topH * 1.15), MinH = Math.Max(36, topH * 0.8), MinFont = Math.Max(14, topH * 0.34),
            PillH = pillH, PillRadius = new CornerRadius(pillH / 2), PillFont = Math.Max(14, pillH * 0.44),

            GridMargin = new Thickness(gap, 0, gap, 0), GridW = cellW * cols, GridH = cellH * rows,
            TilePad = new Thickness(gap / 2),
            TileRadius = new CornerRadius(radius), IconSize = cellMin * 0.3, LabelSize = Math.Max(13, cellMin * 0.09),
            LabelMargin = new Thickness(8, cellMin * 0.04, 8, 0), EditSize = Math.Max(11, cellMin * 0.065),
            EditMargin = new Thickness(0, cellMin * 0.03, 0, 0), AddGlyphSize = cellMin * 0.26, AddRadius = radius,

            BarMargin = new Thickness(gap, 0, gap, gap), BarRadius = new CornerRadius(barH / 2),
            DockRadius = new CornerRadius(barH / 2 + DockInset),
            BarFont = Math.Max(15, barH * 0.3), SubFont = Math.Max(14, barH * 0.27), GearFont = barH * 0.42,
            TabMinW = barH * 1.9, TabMargin = new Thickness(0, 0, 4, 0), TabPadding = new Thickness(barH * 0.45, 0, barH * 0.45, 0),
            ChipMargin = new Thickness(0, 0, 4, 0), ChipPadding = new Thickness(barH * 0.35, 0, barH * 0.35, 0),
            StatusPadding = new Thickness(barH * 0.35, barH * 0.18, barH * 0.4, barH * 0.18), SubTextMargin = new Thickness(0, 0, barH * 0.2, 0),
            DotSize = Math.Max(8, barH * 0.14), DotActiveW = Math.Max(22, barH * 0.4),
            ToastMargin = new Thickness(gap, 0, gap, barH + DockInset * 2 + gap * 2),

            MenuRowH = rowH, MenuFont = fs, MenuIconFont = fs * 1.15, MenuIconW = rowH * 0.9,
            MenuWidth = Math.Min(W - gap * 2, Math.Max(380, rowH * 7)), MenuMargin = new Thickness(0, 0, gap, barH + DockInset * 2 + gap * 2),
        };
    }
}
