using System;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace TouchDeck;

/// <summary>Jeu de couleurs (hexadécimal) d'un thème. Séparé des brushes pour pouvoir vérifier les contrastes dans les tests.</summary>
public sealed record Palette(
    string Window, string Bar, string TopBar, string Panel, string Surface, string SurfaceHi, string Line, string Grip,
    string Dim, string Text, string TextSoft, string Placeholder, string Accent, string AccentText, string Danger, string Scrim,
    string Input, string EditorBg, string Focus, string Success, string StatusBg, string StatusLine, string StatusText);

/// <summary>Palette claire / sombre de l'application ; sombre par défaut pour les nouvelles configurations, « système » suit Windows.</summary>
public static class Theme
{
    // Direction « graphite » : fond bleuté nuancé (jamais noir pur), trois profondeurs de surface, accent indigo propre à TouchDeck.
    // Accent : fond des boutons principaux (texte blanc, contraste ≥ 4,5) ; AccentText : accent utilisé comme texte ou contour sur fond de panneau.
    public static readonly Palette DarkPalette = new(
        Window: "#0F1218", Bar: "#171B24", TopBar: "#00000000", Panel: "#181C25", Surface: "#1F2430", SurfaceHi: "#2A303D",
        Line: "#313849", Grip: "#3C4456", Dim: "#A3ABBD", Text: "#F4F6FB", TextSoft: "#CDD3E0", Placeholder: "#737C90",
        Accent: "#5B4CF0", AccentText: "#B0A8FF", Danger: "#FF8A97", Scrim: "#A6060810",
        Input: "#12151C", EditorBg: "#12151B", Focus: "#FFFFFF", Success: "#5CD6A0",
        StatusBg: "#3A1F26", StatusLine: "#7A3340", StatusText: "#FFB4BE");

    public static readonly Palette LightPalette = new(
        Window: "#F3F4F8", Bar: "#FFFFFF", TopBar: "#00FFFFFF", Panel: "#FFFFFF", Surface: "#FFFFFF", SurfaceHi: "#ECEEF4",
        Line: "#D5D9E3", Grip: "#C3C8D4", Dim: "#565C6E", Text: "#171A21", TextSoft: "#363B48", Placeholder: "#7A8092",
        Accent: "#5B4CF0", AccentText: "#4334D6", Danger: "#B3202F", Scrim: "#660B0D14",
        Input: "#FFFFFF", EditorBg: "#F3F4F8", Focus: "#171A21", Success: "#0F7A52",
        StatusBg: "#FBE3E6", StatusLine: "#E0A0A8", StatusText: "#8C1D2B");

    /// <summary>Fond des touches avant teinte (haut, bas) : un cran au-dessus des surfaces pour détacher la grille du fond.</summary>
    static (string Top, string Bottom) TileBase => Dark ? ("#1D222D", "#161A23") : ("#FFFFFF", "#F5F6FA");
    /// <summary>Part de la couleur du bouton dans le corps de la touche (haut, bas) : la couleur vive reste sur la pastille d'icône.</summary>
    public const double TintTop = 0.26, TintBottom = 0.16;

    public static bool Dark { get; private set; } = true;
    public static Palette Current => Dark ? DarkPalette : LightPalette;

    // Raccourcis conservés pour le code existant
    public static string Surface => Current.Surface;
    public static string SurfaceHi => Current.SurfaceHi;
    public static string Accent => Current.Accent;

    /// <summary>Teinte de fond Mica de repli : la couleur de config par défaut laisse Mica visible.</summary>
    public const string DefaultBackground = "#0E1015";

    public static event Action? Changed;

    /// <summary>Signale un changement d'apparence qui n'est pas un changement de thème (style des icônes).</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>mode : "system" | "dark" | "light".</summary>
    public static void Apply(string mode)
    {
        bool dark = mode.ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => SystemUsesDark(),
        };
        Dark = dark;
        var accent = (Color)ColorConverter.ConvertFromString(Current.Accent);
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        // Même accent en clair et en sombre (WPF-UI l'éclaircit en sombre par défaut)
        ApplicationAccentColorManager.Apply(accent, accent,
            (Color)ColorConverter.ConvertFromString("#4E40DA"), (Color)ColorConverter.ConvertFromString("#4436C2"));
        Fills.Clear();
        PublishResources();
        Changed?.Invoke();
    }

    /// <summary>Expose la palette au XAML (DynamicResource) : les vues suivent le thème sans reconstruction.</summary>
    static void PublishResources()
    {
        var p = Current;
        var r = System.Windows.Application.Current.Resources;
        // Les boutons « Primary » de WPF-UI suivent notre accent (en sombre, WPF-UI l'éclaircit par défaut)
        r["AccentFillColorDefaultBrush"] = B(p.Accent);
        r["AccentFillColorSecondaryBrush"] = B("#E65B4CF0");
        r["AccentFillColorTertiaryBrush"] = B("#CC5B4CF0");
        r["TextOnAccentFillColorPrimaryBrush"] = B("#FFFFFF");
        r["TextOnAccentFillColorSecondaryBrush"] = B("#CCFFFFFF");
        // En thème sombre WPF-UI écrit en noir sur l'accent ; notre accent est assez sombre pour du texte blanc (contraste ≥ 4,5)
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
            r[key] = B("#FFFFFF");
        r["DeckWindow"] = B(p.Window);
        r["DeckBar"] = B(p.Bar);
        r["DeckTopBar"] = B(p.TopBar);
        r["DeckPanel"] = B(p.Panel);
        r["DeckSurface"] = B(p.Surface);
        r["DeckSurfaceHi"] = B(p.SurfaceHi);
        r["DeckLine"] = B(p.Line);
        r["DeckGrip"] = B(p.Grip);
        r["DeckDim"] = B(p.Dim);
        r["DeckText"] = B(p.Text);
        r["DeckTextSoft"] = B(p.TextSoft);
        r["DeckPlaceholder"] = B(p.Placeholder);
        r["DeckAccent"] = B(p.Accent);
        r["DeckAccentText"] = B(p.AccentText);
        r["DeckDanger"] = B(p.Danger);
        r["DeckScrim"] = B(p.Scrim);
        r["DeckInput"] = B(p.Input);
        r["DeckFocus"] = B(p.Focus);
        r["DeckSuccess"] = B(p.Success);
        r["DeckStatusBg"] = B(p.StatusBg);
        r["DeckStatusLine"] = B(p.StatusLine);
        r["DeckStatusText"] = B(p.StatusText);
        r["DeckAccentSoft"] = B(Dark ? "#335B4CF0" : "#225B4CF0");
        r["DeckDock"] = B(Dark ? "#E6171B24" : "#F2FFFFFF");
        r["DeckBackdrop"] = Backdrop();
    }

    /// <summary>Fond de la fenêtre du deck : lueur douce en haut, plus profond en bas. Opaque : lisible quel que soit le fond d'écran.</summary>
    static Brush Backdrop()
    {
        var (glow, mid, deep) = Dark ? ("#1C2233", "#12151D", "#0C0E13") : ("#FFFFFF", "#F3F4F8", "#E8EBF2");
        var brush = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, -0.15), GradientOrigin = new System.Windows.Point(0.5, -0.15),
            RadiusX = 1.0, RadiusY = 1.25,
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString(glow), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(mid), 0.55),
                new GradientStop((Color)ColorConverter.ConvertFromString(deep), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    // ---------- touches ----------

    /// <summary>Mélange linéaire de deux couleurs opaques (part de b : 0 à 1), en hexadécimal #RRGGBB.</summary>
    public static string Mix(string a, string b, double amount)
    {
        var x = (Color)ColorConverter.ConvertFromString(a);
        var y = (Color)ColorConverter.ConvertFromString(b);
        byte M(byte p, byte q) => (byte)Math.Round(p + (q - p) * amount);
        return $"#{M(x.R, y.R):X2}{M(x.G, y.G):X2}{M(x.B, y.B):X2}";
    }

    /// <summary>Corps d'une touche (haut, bas) pour une couleur de bouton dans le thème courant. Logique pure : contraste testé.</summary>
    public static (string Top, string Bottom) TileFillColors(string hex, double tint = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex.Trim());
        var opaque = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var (top, bottom) = TileBase;
        return (Mix(top, opaque, TintTop * tint), Mix(bottom, opaque, TintBottom * tint));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, double), Brush> Fills = new();

    /// <summary>Corps teinté d'une touche (dégradé vertical discret) ; tint = 0,5 pour une touche de navigation, plus neutre.</summary>
    public static Brush TileFill(string hex, double tint = 1) => Fills.GetOrAdd((hex.Trim().ToUpperInvariant(), tint), key =>
    {
        var (top, bottom) = TileFillColors(key.Item1, key.Item2);
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
        brush.Freeze();
        return brush;
    });

    /// <summary>Liseré de la touche : la couleur du bouton, très atténuée.</summary>
    public static Brush TileBorder(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex.Trim());
        var b = new SolidColorBrush(Color.FromArgb(Dark ? (byte)0x66 : (byte)0x73, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    static bool SystemUsesDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v == 0;
        }
        catch { return true; }
    }

    static readonly BrushConverter Converter = new();

    /// <summary>Brush figé à partir d'un hexadécimal (#RGB, #RRGGBB ou #AARRGGBB).</summary>
    public static Brush B(string hex)
    {
        var b = (Brush)Converter.ConvertFromString(hex)!;
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Brush> Gradients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fond d'un bouton : la couleur choisie en haut, légèrement assombrie en bas. Le haut garde exactement la couleur de l'utilisateur
    /// (le contraste du texte blanc n'est donc jamais dégradé), le bas donne du volume sans reflet ni dégradé voyant.
    /// </summary>
    public static Brush TileGradient(string hex)
    {
        return Gradients.GetOrAdd(hex.Trim(), h =>
        {
            var c = (Color)ColorConverter.ConvertFromString(h);
            byte Dim(byte v) => (byte)(v * 0.78);
            var brush = new LinearGradientBrush(c, Color.FromArgb(c.A, Dim(c.R), Dim(c.G), Dim(c.B)), 90);
            brush.Freeze();
            return brush;
        });
    }

    public static bool TryBrush(string? hex, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { brush = B(hex.Trim()); return true; }
        catch { return false; }
    }

    // ---------- contraste (WCAG) ----------

    /// <summary>Rapport de contraste WCAG entre deux couleurs opaques (de 1 à 21).</summary>
    public static double Contrast(string fg, string bg)
    {
        double a = Luminance(fg), b = Luminance(bg);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static double Luminance(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}
