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
    // Direction « nuit indigo » : trois niveaux d'élévation (application #0B0E17, panneaux #121624, cartes #1A1F30), accent #6C5CE7.
    // Accent : fond des boutons principaux (texte blanc, contraste ≥ 4,5) ; AccentText : accent utilisé comme texte ou contour sur fond de panneau.
    /// <summary>Palette sombre (thème par défaut des nouvelles configurations).</summary>
    public static readonly Palette DarkPalette = new(
        Window: "#0B0E17", Bar: "#121624", TopBar: "#00000000", Panel: "#121624", Surface: "#1A1F30", SurfaceHi: "#252B40",
        Line: "#2B3249", Grip: "#3A4260", Dim: "#A3ABBD", Text: "#F4F6FB", TextSoft: "#CDD3E0", Placeholder: "#737C90",
        Accent: "#6C5CE7", AccentText: "#B0A8FF", Danger: "#FF8A97", Scrim: "#A6050710",
        Input: "#0F1320", EditorBg: "#0E121D", Focus: "#FFFFFF", Success: "#5CD6A0",
        StatusBg: "#3A1F26", StatusLine: "#7A3340", StatusText: "#FFB4BE");

    /// <summary>Palette AMOLED : fond noir pur (pixels éteints sur un écran OLED), cartes à peine plus claires.</summary>
    public static readonly Palette AmoledPalette = DarkPalette with
    {
        Window = "#000000", Bar = "#07090F", Panel = "#07090F", Surface = "#0E111A", SurfaceHi = "#181C28",
        Line = "#222839", Grip = "#323A52", Input = "#05070B", EditorBg = "#000000", Scrim = "#B3000000",
    };

    /// <summary>Palette claire.</summary>
    public static readonly Palette LightPalette = new(
        Window: "#F3F4F8", Bar: "#FFFFFF", TopBar: "#00FFFFFF", Panel: "#FFFFFF", Surface: "#FFFFFF", SurfaceHi: "#ECEEF4",
        Line: "#D5D9E3", Grip: "#C3C8D4", Dim: "#565C6E", Text: "#171A21", TextSoft: "#363B48", Placeholder: "#7A8092",
        Accent: "#6C5CE7", AccentText: "#4334D6", Danger: "#B3202F", Scrim: "#660B0D14",
        Input: "#FFFFFF", EditorBg: "#F3F4F8", Focus: "#171A21", Success: "#0F7A52",
        StatusBg: "#FBE3E6", StatusLine: "#E0A0A8", StatusText: "#8C1D2B");

    /// <summary>Fond des touches avant teinte (haut, bas) : un cran au-dessus des surfaces pour détacher la grille du fond.</summary>
    static (string Top, string Bottom) TileBase => Amoled ? ("#141824", "#0E111A") : Dark ? ("#202639", "#1A1F30") : ("#FFFFFF", "#F5F6FA");
    /// <summary>Part de la couleur du bouton dans le corps de la touche (haut, bas) : la couleur vive reste sur la pastille d'icône.</summary>
    public const double TintTop = 0.26, TintBottom = 0.16;

    /// <summary>true si le thème sombre est appliqué.</summary>
    public static bool Dark { get; private set; } = true;
    /// <summary>Palette du thème appliqué.</summary>
    public static Palette Current => Amoled ? AmoledPalette : Dark ? DarkPalette : LightPalette;
    /// <summary>true pour le thème AMOLED (sombre à fond noir).</summary>
    public static bool Amoled { get; private set; }

    // Raccourcis conservés pour le code existant
    /// <summary>Surface du thème appliqué (raccourci).</summary>
    public static string Surface => Current.Surface;
    /// <summary>Surface surélevée du thème appliqué (raccourci).</summary>
    public static string SurfaceHi => Current.SurfaceHi;
    /// <summary>Accent du thème appliqué (raccourci).</summary>
    public static string Accent => Current.Accent;

    /// <summary>Teinte de fond Mica de repli : la couleur de config par défaut laisse Mica visible.</summary>
    public const string DefaultBackground = ConfigNormalizer.DefaultBackground;

    /// <summary>Le thème ou le style des icônes a changé : les vues qui ne passent pas par DynamicResource se redessinent.</summary>
    public static event Action? Changed;

    /// <summary>Signale un changement d'apparence qui n'est pas un changement de thème (style des icônes).</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>mode : "system" | "dark" | "light" | "amoled".</summary>
    public static void Apply(string mode)
    {
        bool dark = mode.ToLowerInvariant() switch
        {
            "dark" or "amoled" => true,
            "light" => false,
            _ => SystemUsesDark(),
        };
        Dark = dark;
        Amoled = dark && mode.Equals("amoled", StringComparison.OrdinalIgnoreCase);
        var accent = (Color)ColorConverter.ConvertFromString(Current.Accent);
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        // Même accent en clair et en sombre (WPF-UI l'éclaircit en sombre par défaut)
        ApplicationAccentColorManager.Apply(accent, accent,
            (Color)ColorConverter.ConvertFromString("#5A4AD1"), (Color)ColorConverter.ConvertFromString("#4C3DB8"));
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
        r["AccentFillColorSecondaryBrush"] = B("#E66C5CE7");
        r["AccentFillColorTertiaryBrush"] = B("#CC6C5CE7");
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
        r["DeckAccentSoft"] = B(Dark ? "#336C5CE7" : "#226C5CE7");
        r["DeckDock"] = B(Amoled ? "#E607090F" : Dark ? "#E6121624" : "#F2FFFFFF");
        r["DeckBackdrop"] = Backdrop();

        // Repères posés sur la face des touches (voiles d'état, badges, liseré, reflet) : mêmes valeurs dans les deux thèmes,
        // car ils s'affichent sur la couleur de la touche ou sur un voile sombre, jamais directement sur le fond de l'application
        r["DeckTileRunningVeil"] = B(OnTile.RunningVeil);
        r["DeckTileDoneVeil"] = B(OnTile.DoneVeil);
        r["DeckTileErrorVeil"] = B(OnTile.ErrorVeil);
        r["DeckTileSuccessMark"] = B(OnTile.SuccessMark);
        r["DeckTileErrorLine"] = B(OnTile.ErrorLine);
        r["DeckTileWarningMark"] = B(OnTile.WarningMark);
        r["DeckTileBadge"] = B(OnTile.Badge);
        r["DeckTileTrack"] = B(OnTile.Track);
        r["DeckTileLine"] = B(OnTile.Line);
        r["DeckTileSheen"] = Vertical(OnTile.SheenTop, OnTile.SheenBottom, 0.35);
        r["DeckImageScrim"] = Vertical("#00000000", OnTile.ImageScrim, 1);
        // Pastille « Simulation » de la barre du haut, éditeur (case de dépôt) et nuancier
        r["DeckSimulation"] = B(OnTile.WarningMark);
        r["DeckDropFill"] = B("#2600FF99");
        r["DeckSwatchRing"] = B("#80808080");
    }

    /// <summary>Couleurs des repères posés sur les touches (voir <see cref="PublishResources"/>).</summary>
    public static class OnTile
    {
        /// <summary>Fond de la pastille « en cours » (anneau de progression).</summary>
        public const string RunningVeil = "#B3000000";
        /// <summary>Voile de la touche après une action réussie.</summary>
        public const string DoneVeil = "#99000000";
        /// <summary>Voile de la touche après un échec.</summary>
        public const string ErrorVeil = "#FA0B0D12";
        /// <summary>Coche de réussite.</summary>
        public const string SuccessMark = "#FF5CD6A0";
        /// <summary>Contour de la touche en échec.</summary>
        public const string ErrorLine = "#FFFF8A97";
        /// <summary>Icône d'alerte (échec, mode simulation).</summary>
        public const string WarningMark = "#FFFCD34D";
        /// <summary>Fond des badges (dossier, nombre d'étapes).</summary>
        public const string Badge = "#66000000";
        /// <summary>Rail de la barre de progression du minuteur.</summary>
        public const string Track = "#33FFFFFF";
        /// <summary>Liseré par défaut d'une touche (remplacé par la couleur du bouton).</summary>
        public const string Line = "#33FFFFFF";
        /// <summary>Reflet du haut de la touche, du haut vers le bas.</summary>
        public const string SheenTop = "#14FFFFFF", SheenBottom = "#00FFFFFF";
        /// <summary>Bas du dégradé qui rend le titre lisible sur une image.</summary>
        public const string ImageScrim = "#C0000000";
    }

    /// <summary>Dégradé vertical figé : <paramref name="top"/> en haut, <paramref name="bottom"/> à la hauteur relative <paramref name="end"/>.</summary>
    static LinearGradientBrush Vertical(string top, string bottom, double end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1),
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString(top), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(bottom), end),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Fond de la fenêtre du deck : lueur douce en haut, plus profond en bas. Opaque : lisible quel que soit le fond d'écran.</summary>
    static RadialGradientBrush Backdrop()
    {
        var (glow, mid, deep) = Amoled ? ("#000000", "#000000", "#000000") : Dark ? ("#161B2D", "#0E121D", "#0B0E17") : ("#FFFFFF", "#F3F4F8", "#E8EBF2");
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
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Warn("Thème de Windows illisible, thème sombre utilisé : " + ex.Message);
            return true;
        }
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

    /// <summary>Brush figé pour une couleur saisie ; false si elle est vide ou mal formée (validation, sans exception).</summary>
    public static bool TryBrush(string? hex, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { brush = B(hex.Trim()); return true; }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return false;   // validation de saisie : une couleur mal formée n'est pas une erreur à consigner
        }
    }

    // ---------- contraste (WCAG) ----------

    /// <summary>Rapport de contraste WCAG entre deux couleurs opaques (de 1 à 21).</summary>
    public static double Contrast(string fg, string bg)
    {
        double a = Luminance(fg), b = Luminance(bg);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Luminance relative WCAG d'une couleur (0 : noir, 1 : blanc).</summary>
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
