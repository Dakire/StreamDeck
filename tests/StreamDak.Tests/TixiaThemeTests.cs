using Xunit;

namespace StreamDak.Tests;

/// <summary>Thème « TIXIA » : mêmes règles de contraste que les autres thèmes (WCAG AA pour le texte, 3 : 1 pour les repères).</summary>
public class TixiaThemeTests
{
    static readonly Palette P = Theme.TixiaPalette;

    [Fact]
    public void TextMeetsWcagAaOnEveryTixiaSurface()
    {
        foreach (var bg in new[] { P.Window, P.Panel, P.Surface, P.SurfaceHi, P.TileTop, P.TileBottom })
        {
            Assert.True(Theme.Contrast(P.Text, bg) >= 4.5, $"Text sur {bg}");
            Assert.True(Theme.Contrast(P.TextSoft, bg) >= 4.5, $"TextSoft sur {bg}");
            Assert.True(Theme.Contrast(P.Dim, bg) >= 4.5, $"Dim sur {bg}");
            Assert.True(Theme.Contrast(P.AccentText, bg) >= 4.5, $"AccentText sur {bg}");
            Assert.True(Theme.Contrast(P.Danger, bg) >= 4.5, $"Danger sur {bg}");
            Assert.True(Theme.Contrast(P.Success, bg) >= 3.0, $"Success sur {bg}");
        }
    }

    [Fact]
    public void TextOnTheOrangeAccentIsReadable()
    {
        Assert.True(Theme.Contrast(P.OnAccent, P.Accent) >= 4.5, "texte sur l'orange");
        Assert.True(Theme.Contrast(P.OnAccentContainer, P.AccentContainer) >= 4.5, "texte sur le conteneur d'accent");
        Assert.True(Theme.Contrast(P.OnSecondaryContainer, P.SecondaryContainer) >= 4.5, "texte sur l'onglet actif");
        Assert.True(Theme.Contrast(P.StatusText, P.StatusBg) >= 4.5, "bandeau d'état");
        Assert.True(Theme.Contrast(P.OnBadge, P.Badge) >= 4.5, "pastille");
    }

    [Fact]
    public void FocusRingsAndEmptySlotsMeetNonTextContrast()
    {
        Assert.True(Theme.Contrast(P.Focus, P.Window) >= 3.0);
        Assert.True(Theme.Contrast(P.Focus, P.Panel) >= 3.0);
        Assert.True(Theme.Contrast(P.Placeholder, P.Panel) >= 3.0);
        Assert.True(Theme.Contrast(P.AccentText, P.Window) >= 3.0);
    }

    [Fact]
    public void TheAccentIsOrangeAndTheBackgroundsAreWarmAndDark()
    {
        var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(P.Accent);
        Assert.True(accent.R > 200 && accent.G is > 90 and < 170 && accent.B < 80, "orange");
        foreach (var bg in new[] { P.Window, P.Panel, P.Surface, P.SurfaceHi })
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bg);
            Assert.True(c.R > c.G && c.G > c.B, $"fond chaud {bg}");     // brun : rouge > vert > bleu
            Assert.True(Theme.Luminance(bg) < 0.05, $"fond sombre {bg}");
        }
    }
}

/// <summary>Application du thème TIXIA aux ressources de l'application (brushes <c>Deck*</c>).</summary>
[Collection("ui")]
public class TixiaThemeApplyTests
{
    readonly WpfHost _host;
    public TixiaThemeApplyTests(WpfHost host) { _host = host; }

    [Fact]
    public void ApplyingTixiaPublishesItsBrushesAndLeavingItRestoresTheOthers()
    {
        _host.Reset();
        _host.Run(() =>
        {
            Theme.Apply("tixia");
            Assert.True(Theme.Tixia);
            Assert.True(Theme.Dark);
            var accent = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["DeckAccent"];
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Theme.TixiaPalette.Accent), accent.Color);
            Theme.Apply("dark");
            Assert.False(Theme.Tixia);
            var back = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["DeckAccent"];
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Theme.DarkPalette.Accent), back.Color);
        });
        _host.SetTheme("dark");
    }
}

/// <summary>Hiérarchie des surfaces et couches d'état de tous les thèmes (refonte des palettes).</summary>
public class PaletteHierarchyTests
{
    public static TheoryData<string> Names => new() { "dark", "light", "amoled", "tixia" };

    static Palette Of(string n) => n switch { "light" => Theme.LightPalette, "amoled" => Theme.AmoledPalette, "tixia" => Theme.TixiaPalette, _ => Theme.DarkPalette };

    [Theory, MemberData(nameof(Names))]
    public void EachSurfaceLevelIsDistinguishableFromTheNext(string name)
    {
        var p = Of(name);
        Assert.True(Theme.Contrast(p.Window, p.Panel) >= 1.06, $"{name} : fond → panneau");
        Assert.True(Theme.Contrast(p.Panel, p.Surface) >= 1.06, $"{name} : panneau → surface");
        Assert.True(Theme.Contrast(p.Surface, p.SurfaceHi) >= 1.06, $"{name} : surface → surface haute");
        Assert.True(Theme.Contrast(p.Window, p.SurfaceHi) >= 1.25, $"{name} : fond → surface haute");
    }

    [Theory, MemberData(nameof(Names))]
    public void TilesStandOutFromTheBackground(string name)
    {
        var p = Of(name);
        Assert.True(Theme.Contrast(p.TileTop, p.Window) >= 1.10, $"{name} : haut de touche / fond");
    }

    [Theory, MemberData(nameof(Names))]
    public void TextStaysReadableOnTheSuccessContainerAndStateLayersAreTranslucent(string name)
    {
        var p = Of(name);
        Assert.True(Theme.Contrast(p.Text, p.SuccessContainer) >= 4.5, $"{name} : texte sur le conteneur de succès");
        Assert.True(Theme.Contrast(p.Success, p.SuccessContainer) >= 3.0, $"{name} : succès sur son conteneur");
        foreach (var layer in new[] { p.Hover, p.Pressed })
            Assert.Matches("^#[0-9A-F]{8}$", layer);                       // #AARRGGBB : un voile, jamais une couleur pleine
        Assert.True(System.Convert.ToInt32(p.Pressed[1..3], 16) > System.Convert.ToInt32(p.Hover[1..3], 16), $"{name} : l'appui est plus marqué que le survol");
    }

    [Theory, MemberData(nameof(Names))]
    public void ErrorAndSuccessAreReadableOnEverySurface(string name)
    {
        var p = Of(name);
        foreach (var bg in new[] { p.Window, p.Panel, p.Surface, p.SurfaceHi })
        {
            Assert.True(Theme.Contrast(p.Danger, bg) >= 4.5, $"{name} : erreur sur {bg}");
            Assert.True(Theme.Contrast(p.Success, bg) >= 3.0, $"{name} : succès sur {bg}");
        }
    }
}
