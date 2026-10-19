using System;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace TouchDeck;

/// <summary>Palette claire / sombre de l'application ; suit le thème Windows par défaut.</summary>
public static class Theme
{
    public static bool Dark { get; private set; } = true;

    public static string Bar => Dark ? "#12141B" : "#E9EAEE";
    public static string Surface => Dark ? "#1A1D26" : "#FFFFFF";
    public static string SurfaceHi => Dark ? "#242835" : "#F1F2F6";
    public static string Line => Dark ? "#2C3142" : "#D5D8E0";
    public static string Grip => Dark ? "#3A4052" : "#B8BDCB";
    public static string Dim => Dark ? "#8B93A7" : "#5B6275";
    public static string Text => Dark ? "#FFFFFF" : "#1B1E27";
    public static string TextSoft => Dark ? "#C6CCDB" : "#3A4052";
    public static string Placeholder => Dark ? "#4A5068" : "#A4AABB";
    public static string Accent => "#5B7CFA";
    public static string Danger => Dark ? "#FF8A97" : "#C42B3C";
    public static string Scrim => Dark ? "#96000000" : "#66000000";
    public static string Input => Dark ? "#0F1117" : "#FFFFFF";
    public static string EditorBg => Dark ? "#14161D" : "#F3F3F3";

    /// <summary>Teinte de fond Mica de repli : la couleur de config par défaut laisse Mica visible.</summary>
    public const string DefaultBackground = "#0E1015";

    public static event Action? Changed;

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
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        PublishResources();
        Changed?.Invoke();
    }

    /// <summary>Expose la palette au XAML (DynamicResource) : les vues suivent le thème sans reconstruction.</summary>
    static void PublishResources()
    {
        var r = System.Windows.Application.Current.Resources;
        r["DeckBar"] = B(Bar);
        r["DeckTopBar"] = B(Dark ? "#33000000" : "#33FFFFFF");
        r["DeckSurface"] = B(Surface);
        r["DeckSurfaceHi"] = B(SurfaceHi);
        r["DeckLine"] = B(Line);
        r["DeckGrip"] = B(Grip);
        r["DeckDim"] = B(Dim);
        r["DeckText"] = B(Text);
        r["DeckTextSoft"] = B(TextSoft);
        r["DeckPlaceholder"] = B(Placeholder);
        r["DeckAccent"] = B(Accent);
        r["DeckDanger"] = B(Danger);
        r["DeckScrim"] = B(Scrim);
        r["DeckStatusBg"] = B(Dark ? "#3A1F26" : "#FBE3E6");
        r["DeckStatusLine"] = B(Dark ? "#7A3340" : "#E0A0A8");
        r["DeckStatusText"] = B(Dark ? "#FFB4BE" : "#8C1D2B");
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

    public static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}
