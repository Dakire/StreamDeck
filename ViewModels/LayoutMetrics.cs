using System;
using System.Windows;

namespace TouchDeck.ViewModels;

/// <summary>Dimensions de l'interface calculées à partir de la taille de la fenêtre (en DIP) ; recalculées à chaque redimensionnement.</summary>
public sealed class LayoutMetrics
{
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

    // grille de boutons
    public Thickness GridMargin { get; init; }
    public Thickness TilePad { get; init; }
    public CornerRadius TileRadius { get; init; }
    public double IconSize { get; init; }
    public double LabelSize { get; init; }
    public Thickness LabelMargin { get; init; }
    public double EditSize { get; init; }
    public Thickness EditMargin { get; init; }
    public double AddGlyphSize { get; init; }
    public double AddRadius { get; init; }

    // barre du bas
    public Thickness BarMargin { get; init; }
    public CornerRadius BarRadius { get; init; }
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

    // menu
    public double MenuRowH { get; init; }
    public double MenuFont { get; init; }
    public double MenuIconFont { get; init; }
    public double MenuIconW { get; init; }
    public double MenuWidth { get; init; }
    public Thickness MenuMargin { get; init; }

    public static readonly LayoutMetrics Initial = Compute(1100, 650, new DeckConfig());

    public static LayoutMetrics Compute(double W, double H, DeckConfig cfg)
    {
        double scale = Math.Clamp(H / 1080.0, 0.6, 1.6);
        double gap = Math.Max(6, cfg.Spacing * scale);
        double barH = Math.Clamp(H * 0.085, 52, 92);   // cibles tactiles >= 48 px
        double topH = Math.Clamp(H * 0.05, 36, 56);
        double availW = W - gap * 2, availH = H - topH - barH - gap * 1.5 - gap;

        // ≈ 20 cases en FHD, moins sur petit écran, plus sur grand
        int cols = cfg.Columns > 0 ? cfg.Columns : (int)Math.Max(2, Math.Round(availW / 360, MidpointRounding.AwayFromZero));
        int rows = cfg.Rows > 0 ? cfg.Rows : (int)Math.Max(2, Math.Round(availH / 230, MidpointRounding.AwayFromZero));

        double cellMin = Math.Min(availW / cols, availH / rows);
        double radius = Math.Clamp(cellMin * 0.12, 12, 30);
        double rowH = Math.Clamp(H * 0.075, 52, 76);
        double fs = Math.Max(15, rowH * 0.32);

        return new LayoutMetrics
        {
            Gap = gap, TopH = topH, BarH = barH, BarRowH = barH + gap * 1.5, Columns = cols, Rows = rows,

            TopMargin = new Thickness(gap, 0, gap, 0), LogoSize = topH * 0.6, TopFont = Math.Max(13, topH * 0.38),
            BrandTextMargin = new Thickness(topH * 0.25, 0, 0, 0),
            GripW = Math.Max(60, topH * 1.8), GripH = Math.Max(5, topH * 0.14),
            MinW = topH * 1.3, MinH = topH * 0.8, MinFont = topH * 0.4,

            GridMargin = new Thickness(gap, gap * 0.5, gap, 0), TilePad = new Thickness(gap / 2),
            TileRadius = new CornerRadius(radius), IconSize = cellMin * 0.36, LabelSize = Math.Max(13, cellMin * 0.095),
            LabelMargin = new Thickness(8, cellMin * 0.04, 8, 0), EditSize = Math.Max(11, cellMin * 0.065),
            EditMargin = new Thickness(0, cellMin * 0.03, 0, 0), AddGlyphSize = cellMin * 0.3, AddRadius = radius,

            BarMargin = new Thickness(gap, gap * 0.5, gap, gap), BarRadius = new CornerRadius(barH / 2),
            BarFont = Math.Max(15, barH * 0.33), SubFont = Math.Max(14, barH * 0.3), GearFont = barH * 0.48,
            TabMinW = barH * 2, TabMargin = new Thickness(0, 0, gap * 0.6, 0), TabPadding = new Thickness(barH * 0.5, 0, barH * 0.5, 0),
            ChipMargin = new Thickness(0, 0, barH * 0.15, 0), ChipPadding = new Thickness(barH * 0.4, 0, barH * 0.4, 0),
            StatusPadding = new Thickness(barH * 0.4, 0, barH * 0.4, 0), SubTextMargin = new Thickness(0, 0, barH * 0.2, 0),

            MenuRowH = rowH, MenuFont = fs, MenuIconFont = fs * 1.15, MenuIconW = rowH * 0.9,
            MenuWidth = Math.Min(W - gap * 2, Math.Max(380, rowH * 7)), MenuMargin = new Thickness(0, 0, gap, barH + gap * 2),
        };
    }
}
