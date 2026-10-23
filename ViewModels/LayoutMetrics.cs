using System;
using System.Windows;

namespace StreamDak.ViewModels;

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

    /// <summary>Espacement de base entre les touches (réglage Spacing × échelle de l'écran, 6 au minimum).</summary>
    public double Gap { get; init; }
    /// <summary>Hauteur de la barre du haut.</summary>
    public double TopH { get; init; }
    /// <summary>Hauteur des commandes du dock (52 à 84 : cible tactile confortable).</summary>
    public double BarH { get; init; }
    /// <summary>Hauteur de la rangée du dock, marges comprises.</summary>
    public double BarRowH { get; init; }
    /// <summary>Colonnes de la grille affichée.</summary>
    public int Columns { get; init; }
    /// <summary>Lignes de la grille affichée.</summary>
    public int Rows { get; init; }

    // barre du haut
    /// <summary>Marges de la barre du haut.</summary>
    public Thickness TopMargin { get; init; }
    /// <summary>Taille du logo.</summary>
    public double LogoSize { get; init; }
    /// <summary>Taille du texte de la barre du haut.</summary>
    public double TopFont { get; init; }
    /// <summary>Marges du nom de l'application.</summary>
    public Thickness BrandTextMargin { get; init; }
    /// <summary>Largeur de la poignée de déplacement.</summary>
    public double GripW { get; init; }
    /// <summary>Épaisseur de la poignée de déplacement.</summary>
    public double GripH { get; init; }
    /// <summary>Largeur des boutons de fenêtre (réduire, plein écran, fermer).</summary>
    public double MinW { get; init; }
    /// <summary>Hauteur des boutons de fenêtre.</summary>
    public double MinH { get; init; }
    /// <summary>Taille des icônes des boutons de fenêtre.</summary>
    public double MinFont { get; init; }
    /// <summary>Hauteur des pastilles de la barre du haut (profil, modes).</summary>
    public double PillH { get; init; }
    /// <summary>Rayon des pastilles (forme de pilule).</summary>
    public CornerRadius PillRadius { get; init; }
    /// <summary>Taille du texte des pastilles.</summary>
    public double PillFont { get; init; }

    // grille de boutons : centrée, de la taille exacte des touches
    /// <summary>Marges de la grille.</summary>
    public Thickness GridMargin { get; init; }
    /// <summary>Largeur exacte de la grille (colonnes × largeur de touche) : le surplus est réparti autour.</summary>
    public double GridW { get; init; }
    /// <summary>Hauteur exacte de la grille.</summary>
    public double GridH { get; init; }
    /// <summary>Marge autour de chaque touche (moitié de l'espacement).</summary>
    public Thickness TilePad { get; init; }
    /// <summary>Rayon des touches (16 % de la touche, de 12 à 34).</summary>
    public CornerRadius TileRadius { get; init; }
    /// <summary>Taille de référence des icônes de touche.</summary>
    public double IconSize { get; init; }
    /// <summary>Taille des textes posés sur les touches (état d'échec).</summary>
    public double LabelSize { get; init; }
    /// <summary>Marges du titre sous l'icône.</summary>
    public Thickness LabelMargin { get; init; }
    /// <summary>Taille du badge « Modifier » du mode édition.</summary>
    public double EditSize { get; init; }
    /// <summary>Marges du badge « Modifier ».</summary>
    public Thickness EditMargin { get; init; }
    /// <summary>Taille du « + » des cases vides en mode édition.</summary>
    public double AddGlyphSize { get; init; }
    /// <summary>Rayon du cadre des cases vides en mode édition.</summary>
    public double AddRadius { get; init; }

    // dock du bas
    /// <summary>Marges du dock.</summary>
    public Thickness BarMargin { get; init; }
    /// <summary>Rayon des commandes du dock (pilule).</summary>
    public CornerRadius BarRadius { get; init; }
    /// <summary>Rayon du dock lui-même.</summary>
    public CornerRadius DockRadius { get; init; }
    /// <summary>Taille du texte du dock.</summary>
    public double BarFont { get; init; }
    /// <summary>Taille du texte de la sous-page (« 1 / 3 »).</summary>
    public double SubFont { get; init; }
    /// <summary>Taille de l'icône du menu.</summary>
    public double GearFont { get; init; }
    /// <summary>Largeur minimale d'un onglet de page.</summary>
    public double TabMinW { get; init; }
    /// <summary>Marges entre les onglets.</summary>
    public Thickness TabMargin { get; init; }
    /// <summary>Marges intérieures des onglets.</summary>
    public Thickness TabPadding { get; init; }
    /// <summary>Marges entre les pastilles du dock.</summary>
    public Thickness ChipMargin { get; init; }
    /// <summary>Marges intérieures des pastilles du dock.</summary>
    public Thickness ChipPadding { get; init; }
    /// <summary>Marges intérieures de la barre d'état.</summary>
    public Thickness StatusPadding { get; init; }
    /// <summary>Marges du texte de sous-page.</summary>
    public Thickness SubTextMargin { get; init; }
    /// <summary>Diamètre des points de sous-page.</summary>
    public double DotSize { get; init; }
    /// <summary>Largeur du point de la sous-page affichée.</summary>
    public double DotActiveW { get; init; }
    /// <summary>Position des messages, au-dessus du dock.</summary>
    public Thickness ToastMargin { get; init; }

    // menu
    /// <summary>Hauteur d'une entrée du menu (52 à 76).</summary>
    public double MenuRowH { get; init; }
    /// <summary>Taille du texte du menu.</summary>
    public double MenuFont { get; init; }
    /// <summary>Taille des icônes du menu.</summary>
    public double MenuIconFont { get; init; }
    /// <summary>Largeur de la colonne d'icônes du menu.</summary>
    public double MenuIconW { get; init; }
    /// <summary>Largeur du menu (bornée par la fenêtre).</summary>
    public double MenuWidth { get; init; }
    /// <summary>Position du menu, au-dessus du dock.</summary>
    public Thickness MenuMargin { get; init; }

    /// <summary>Dimensions d'une fenêtre de 1100 × 650 avant la première mise en page.</summary>
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
        double radius = Math.Clamp(cellMin * 0.17, 16, 30);   // MD3 : formes « large » à « extra large »
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
