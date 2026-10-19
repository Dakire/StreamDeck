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
    // Accent : fond des boutons principaux (texte blanc, contraste ≥ 4,5) ; AccentText : accent utilisé comme texte ou contour sur fond de panneau.
    public static readonly Palette DarkPalette = new(
        Window: "#1C1C1F", Bar: "#141417", TopBar: "#33000000", Panel: "#252529", Surface: "#2C2C31", SurfaceHi: "#37373D",
        Line: "#46464E", Grip: "#4A4A53", Dim: "#A8A8B3", Text: "#F5F5F7", TextSoft: "#D2D2DA", Placeholder: "#7C7C88",
        Accent: "#4361EE", AccentText: "#9FB1FF", Danger: "#FF8A97", Scrim: "#96000000",
        Input: "#18181B", EditorBg: "#1C1C1F", Focus: "#FFFFFF", Success: "#5CD6A0",
        StatusBg: "#3A1F26", StatusLine: "#7A3340", StatusText: "#FFB4BE");

    public static readonly Palette LightPalette = new(
        Window: "#F3F3F6", Bar: "#E9EAEE", TopBar: "#33FFFFFF", Panel: "#FFFFFF", Surface: "#FFFFFF", SurfaceHi: "#EDEDF2",
        Line: "#C9C9D2", Grip: "#B8BDCB", Dim: "#565662", Text: "#1B1B20", TextSoft: "#3A3A44", Placeholder: "#7A7F90",
        Accent: "#4361EE", AccentText: "#2F46C8", Danger: "#B3202F", Scrim: "#66000000",
        Input: "#FFFFFF", EditorBg: "#F3F3F6", Focus: "#1B1B20", Success: "#0F7A52",
        StatusBg: "#FBE3E6", StatusLine: "#E0A0A8", StatusText: "#8C1D2B");

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
            (Color)ColorConverter.ConvertFromString("#3A55D8"), (Color)ColorConverter.ConvertFromString("#3049BE"));
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
        r["AccentFillColorSecondaryBrush"] = B("#E64361EE");
        r["AccentFillColorTertiaryBrush"] = B("#CC4361EE");
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
