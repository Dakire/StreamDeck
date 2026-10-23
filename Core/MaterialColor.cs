using System;
using System.Globalization;

namespace StreamDak;

/// <summary>
/// Couleurs tonales à la manière de Material Design 3 : à partir d'une couleur source, des palettes tonales (teinte et chroma fixes,
/// clarté variable) dont on tire les rôles de l'interface. La clarté (« ton », 0 à 100) est la clarté CIELAB L*, comme dans HCT ; la teinte
/// et la chroma sont celles de CIELAB (HCT utilise CAM16 : écart de teinte de quelques degrés, sans effet sur les contrastes).
/// Un ton de 40 ou moins contre un ton de 90 ou plus donne toujours au moins 4,5 : c'est ce qui garantit la lisibilité des rôles.
/// </summary>
public static class MaterialColor
{
    // Blanc de référence D65
    const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    /// <summary>Teinte (0-360°) et chroma CIELAB d'une couleur #RRGGBB.</summary>
    public static (double Hue, double Chroma) HueChroma(string hex)
    {
        var (r, g, b) = Parse(hex);
        var (_, a, bb) = ToLab(r, g, b);
        double h = Math.Atan2(bb, a) * 180 / Math.PI;
        return (h < 0 ? h + 360 : h, Math.Sqrt(a * a + bb * bb));
    }

    /// <summary>
    /// Couleur #RRGGBB de teinte, chroma et ton donnés. Si la chroma demandée sort du gamut sRGB à ce ton, elle est réduite jusqu'à
    /// la couleur la plus saturée représentable (même teinte, même clarté).
    /// </summary>
    public static string Tone(double hue, double chroma, double tone)
    {
        if (tone <= 0) return "#000000";
        if (tone >= 100) return "#FFFFFF";
        double lo = 0, hi = Math.Max(0, chroma);
        if (!InGamut(hue, hi, tone))
        {
            for (int i = 0; i < 24; i++)
            {
                double mid = (lo + hi) / 2;
                if (InGamut(hue, mid, tone)) lo = mid; else hi = mid;
            }
            hi = lo;
        }
        var (r, g, b) = FromLab(tone, hi * Math.Cos(hue * Math.PI / 180), hi * Math.Sin(hue * Math.PI / 180));
        return Hex(r, g, b);
    }

    static bool InGamut(double hue, double chroma, double tone)
    {
        var (r, g, b) = FromLab(tone, chroma * Math.Cos(hue * Math.PI / 180), chroma * Math.Sin(hue * Math.PI / 180));
        const double e = 0.0005;
        return r >= -e && r <= 1 + e && g >= -e && g <= 1 + e && b >= -e && b <= 1 + e;
    }

    // ---------- conversions ----------

    static (double R, double G, double B) Parse(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8) s = s[2..];   // #AARRGGBB : l'alpha est ignoré
        return (int.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
                int.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
    }

    static string Hex(double r, double g, double b)
    {
        static int C(double v) => (int)Math.Round(Math.Clamp(Gamma(Math.Clamp(v, 0, 1)), 0, 1) * 255);
        return $"#{C(r):X2}{C(g):X2}{C(b):X2}";
    }

    static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    static double Gamma(double c) => c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
    static double FInv(double t) => t * t * t > 216.0 / 24389 ? t * t * t : (116 * t - 16) / (24389.0 / 27);

    static (double L, double A, double B) ToLab(double r, double g, double b)
    {
        double lr = Linear(r), lg = Linear(g), lb = Linear(b);
        double x = 0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb;
        double y = 0.2126729 * lr + 0.7151522 * lg + 0.0721750 * lb;
        double z = 0.0193339 * lr + 0.1191920 * lg + 0.9503041 * lb;
        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>Composantes sRGB linéaires converties en valeurs sRGB (0 à 1, hors gamut possibles) : la gamma est appliquée dans <see cref="Hex"/>.</summary>
    static (double R, double G, double B) FromLab(double l, double a, double b)
    {
        double fy = (l + 16) / 116, fx = fy + a / 500, fz = fy - b / 200;
        double x = Xn * FInv(fx), y = Yn * FInv(fy), z = Zn * FInv(fz);
        double lr = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double lg = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double lb = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        return (lr, lg, lb);
    }
}

/// <summary>
/// Rôles de couleur d'un thème Material Design 3 (clair, sombre ou sombre à fond noir) tirés d'une couleur source : primaire, secondaire,
/// tertiaire, erreur, surfaces tonales et contours. Les surfaces sont plus claires quand le conteneur est plus élevé (sombre) ou plus
/// foncées (clair), comme dans la spécification : l'élévation se lit par le ton, pas par une ombre.
/// </summary>
public sealed record MaterialScheme(
    string Primary, string OnPrimary, string PrimaryContainer, string OnPrimaryContainer,
    string Secondary, string OnSecondary, string SecondaryContainer, string OnSecondaryContainer,
    string Tertiary, string TertiaryContainer, string OnTertiaryContainer,
    string Error, string OnError, string ErrorContainer, string OnErrorContainer,
    string Surface, string SurfaceContainerLowest, string SurfaceContainerLow, string SurfaceContainer, string SurfaceContainerHigh, string SurfaceContainerHighest,
    string OnSurface, string OnSurfaceVariant, string Outline, string OutlineVariant)
{
    /// <summary>Teinte de l'erreur dans Material Design 3.</summary>
    const double ErrorHue = 25;

    /// <summary>Crée le thème à partir de la couleur source. <paramref name="amoled"/> (avec <paramref name="dark"/>) : surfaces au noir pur.</summary>
    public static MaterialScheme Create(string seed, bool dark, bool amoled = false)
    {
        var (hue, seedChroma) = MaterialColor.HueChroma(seed);
        double primaryChroma = Math.Max(48, seedChroma);        // MD3 : la palette primaire est au moins aussi vive que 48
        string P(double t) => MaterialColor.Tone(hue, primaryChroma, t);
        string S(double t) => MaterialColor.Tone(hue, 16, t);   // secondaire : même teinte, chroma faible
        string T(double t) => MaterialColor.Tone(hue + 60, 24, t);   // tertiaire : teinte voisine
        string E(double t) => MaterialColor.Tone(ErrorHue, 84, t);
        string N(double t) => MaterialColor.Tone(hue, 6, t);          // neutres : presque gris, légèrement teintés
        string NV(double t) => MaterialColor.Tone(hue, 10, t);         // neutres variantes (contours, textes secondaires)

        if (dark)
        {
            // Au noir pur (AMOLED), les surfaces gardent leur ordre mais démarrent à 0 : pixels éteints sur écran OLED
            double s = amoled ? 0 : 6;
            double[] steps = amoled ? new double[] { 0, 3, 7, 9, 13 } : new double[] { 4, 10, 12, 17, 22 };
            return new MaterialScheme(
                Primary: P(80), OnPrimary: P(20), PrimaryContainer: P(30), OnPrimaryContainer: P(90),
                Secondary: S(80), OnSecondary: S(20), SecondaryContainer: S(30), OnSecondaryContainer: S(90),
                Tertiary: T(80), TertiaryContainer: T(30), OnTertiaryContainer: T(90),
                Error: E(80), OnError: E(20), ErrorContainer: E(30), OnErrorContainer: E(90),
                Surface: N(s), SurfaceContainerLowest: N(steps[0]), SurfaceContainerLow: N(steps[1]), SurfaceContainer: N(steps[2]),
                SurfaceContainerHigh: N(steps[3]), SurfaceContainerHighest: N(steps[4]),
                OnSurface: N(90), OnSurfaceVariant: NV(80), Outline: NV(60), OutlineVariant: NV(30));
        }
        return new MaterialScheme(
            Primary: P(40), OnPrimary: P(100), PrimaryContainer: P(90), OnPrimaryContainer: P(10),
            Secondary: S(40), OnSecondary: S(100), SecondaryContainer: S(90), OnSecondaryContainer: S(10),
            Tertiary: T(40), TertiaryContainer: T(90), OnTertiaryContainer: T(10),
            Error: E(40), OnError: E(100), ErrorContainer: E(90), OnErrorContainer: E(10),
            Surface: N(98), SurfaceContainerLowest: N(100), SurfaceContainerLow: N(96), SurfaceContainer: N(94),
            SurfaceContainerHigh: N(92), SurfaceContainerHighest: N(90),
            OnSurface: N(10), OnSurfaceVariant: NV(30), Outline: NV(50), OutlineVariant: NV(80));
    }
}
