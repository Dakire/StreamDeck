using System;

namespace StreamDak;

/// <summary>Place du titre par rapport à l'icône d'une touche.</summary>
public enum TitlePlace
{
    /// <summary>Pas de titre : l'icône est centrée.</summary>
    None,
    /// <summary>Titre au-dessus de l'icône.</summary>
    Above,
    /// <summary>Titre sous l'icône.</summary>
    Below,
}

/// <summary>Rectangle en unités indépendantes de l'écran (DIP), coin haut gauche compris.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>Bord bas.</summary>
    public double Bottom => Y + Height;
    /// <summary>Bord droit.</summary>
    public double Right => X + Width;
    /// <summary>true si les deux rectangles se recouvrent (un bord commun ne compte pas).</summary>
    public bool Overlaps(LayoutRect other) => Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    /// <summary>true si l'autre rectangle est entièrement dedans (tolérance d'un centième de DIP pour les arrondis).</summary>
    public bool Contains(LayoutRect other) => other.X >= X - 0.01 && other.Y >= Y - 0.01 && other.Right <= Right + 0.01 && other.Bottom <= Bottom + 0.01;
}

/// <summary>
/// Zone d'une image d'icône (application, favicon, image « contenue ») dans une touche, et taille d'affichage de l'image (ADR 0036). Logique pure :
/// la zone ne recouvre jamais le titre, quelle que soit sa hauteur (une ou deux lignes), et une petite image n'est jamais étirée au point d'être floue.
/// </summary>
public static class IconLayout
{
    /// <summary>Part de la hauteur utile occupée par l'icône quand la touche a un titre.</summary>
    public const double ZoneWithTitle = 0.55;
    /// <summary>Part de la hauteur utile occupée par l'icône sans titre.</summary>
    public const double ZoneWithoutTitle = 0.62;
    /// <summary>Côté maximal de la zone (DIP) : sur une très grande touche, l'icône garde une taille d'icône.</summary>
    public const double MaxZoneDip = 128;
    /// <summary>Agrandissement lissé maximal d'une image plus petite que sa zone ; au-delà, multiple entier sans lissage.</summary>
    public const double MaxSmoothUpscale = 1.25;
    /// <summary>Multiple entier maximal d'une petite image (32 px → 96 px au plus).</summary>
    public const int MaxIntegerScale = 3;
    /// <summary>Côté minimal (pixels) d'une image pour remplir toute la touche (mode « Remplir ») ; plus petite, elle est montrée comme une icône.</summary>
    public const int MinFillPixels = 128;

    /// <summary>
    /// Zones de l'icône (carré) et du titre dans une touche de <paramref name="width"/> × <paramref name="height"/> DIP. L'ensemble icône + écart + titre est
    /// centré verticalement ; l'icône prend <see cref="ZoneWithTitle"/> de la hauteur utile au plus, et moins s'il le faut pour laisser toute sa place au titre.
    /// </summary>
    /// <param name="width">Largeur de la touche.</param>
    /// <param name="height">Hauteur de la touche.</param>
    /// <param name="padding">Marge intérieure.</param>
    /// <param name="titleHeight">Hauteur mesurée du titre (0 sans titre).</param>
    /// <param name="gap">Écart entre l'icône et le titre.</param>
    /// <param name="place">Place du titre.</param>
    public static (LayoutRect Icon, LayoutRect Title) Zones(double width, double height, double padding, double titleHeight, double gap, TitlePlace place)
    {
        double usefulW = Math.Max(0, width - 2 * padding), usefulH = Math.Max(0, height - 2 * padding);
        if (place == TitlePlace.None || titleHeight <= 0)
        {
            double alone = Math.Max(0, Math.Min(Math.Min(usefulH * ZoneWithoutTitle, usefulW), MaxZoneDip));
            return (new LayoutRect((width - alone) / 2, (height - alone) / 2, alone, alone), new LayoutRect(padding, height / 2, usefulW, 0));
        }
        titleHeight = Math.Min(titleHeight, usefulH);
        double side = Math.Max(0, Math.Min(Math.Min(Math.Min(usefulH * ZoneWithTitle, usefulW), MaxZoneDip), usefulH - titleHeight - gap));
        double spacing = side > 0 ? gap : 0;
        double top = padding + (usefulH - (side + spacing + titleHeight)) / 2;
        double iconY = place == TitlePlace.Above ? top + titleHeight + spacing : top;
        double titleY = place == TitlePlace.Above ? top : top + side + spacing;
        return (new LayoutRect((width - side) / 2, iconY, side, side), new LayoutRect(padding, titleY, usefulW, titleHeight));
    }

    /// <summary>
    /// Taille d'affichage (DIP) d'une image de <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> pixels dans un carré de <paramref name="side"/> DIP,
    /// à l'échelle d'écran <paramref name="dpiScale"/> (1 ; 1,5 ; 2). Une image plus grande est réduite à la zone (proportions gardées). Une image plus petite est
    /// agrandie de <see cref="MaxSmoothUpscale"/> au plus ; au-delà, elle est montrée à sa taille ou à un multiple entier (<see cref="MaxIntegerScale"/> au plus),
    /// sans lissage (<c>Pixelated</c>) : nette au lieu de floue.
    /// </summary>
    public static (double Width, double Height, bool Pixelated) ImageSize(int pixelWidth, int pixelHeight, double side, double dpiScale)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || side <= 0) return (0, 0, false);
        if (dpiScale <= 0) dpiScale = 1;
        double k = side * dpiScale / Math.Max(pixelWidth, pixelHeight);
        bool pixelated = false;
        if (k > MaxSmoothUpscale)
        {
            k = Math.Clamp(Math.Floor(k), 1, MaxIntegerScale);
            pixelated = k > 1;
        }
        return (pixelWidth * k / dpiScale, pixelHeight * k / dpiScale, pixelated);
    }
}
