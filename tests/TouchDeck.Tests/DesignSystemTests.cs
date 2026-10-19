using System.Linq;
using System.Windows;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Jetons de design (Themes/Tokens.xaml) et thème AMOLED.</summary>
[Collection("ui")]
public class DesignSystemTests
{
    readonly WpfHost _host;
    public DesignSystemTests(WpfHost host) => _host = host;

    object? Res(string key) => _host.Run(() => Application.Current.TryFindResource(key));

    [Theory]
    [InlineData("RadiusSm", 8.0)] [InlineData("RadiusMd", 12.0)] [InlineData("RadiusLg", 16.0)] [InlineData("RadiusXl", 20.0)]
    public void RadiiAreThoseOfTheDesignSystem(string key, double value) => Assert.Equal(new CornerRadius(value), Res(key));

    [Theory]
    [InlineData("Space1")] [InlineData("Space2")] [InlineData("Space3")] [InlineData("Space4")] [InlineData("Space5")] [InlineData("Space6")] [InlineData("Space8")]
    public void SpacingsAreOnAFourPixelGrid(string key) => Assert.Equal(0, (double)Res(key)! % 4);

    [Fact]
    public void TypographyScaleIsTitle20Section14Body13Caption11()
    {
        Assert.Equal(new[] { 20.0, 14.0, 13.0, 11.0 }, new[] { Res("FontSizeTitle"), Res("FontSizeSection"), Res("FontSizeBody"), Res("FontSizeCaption") }.Select(x => (double)x!).ToArray());
        Assert.Equal("Segoe UI Variable Text, Segoe UI", ((System.Windows.Media.FontFamily)Res("FontText")!).Source);
        Assert.NotNull(Res("TextTitle"));
    }

    [Theory]
    [InlineData("amoled", "#000000")]
    [InlineData("dark", "#0B0E17")]
    public void ThemesPublishTheirWindowColor(string theme, string window)
    {
        _host.Reset();
        _host.SetTheme(theme);
        var brush = (System.Windows.Media.SolidColorBrush)Res("DeckWindow")!;
        Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(window), brush.Color);
        Assert.Equal(theme == "amoled", Theme.Amoled);
        _host.SetTheme("dark");
    }

    [Fact]
    public void AmoledMeetsTheSameContrastRulesAsTheOtherThemes()
    {
        var p = Theme.AmoledPalette;
        foreach (var bg in new[] { p.Window, p.Panel, p.Surface, p.SurfaceHi })
        {
            Assert.True(Theme.Contrast(p.Text, bg) >= 4.5, $"Text sur {bg}");
            Assert.True(Theme.Contrast(p.Dim, bg) >= 4.5, $"Dim sur {bg}");
            Assert.True(Theme.Contrast(p.AccentText, bg) >= 4.5, $"AccentText sur {bg}");
            Assert.True(Theme.Contrast(p.Danger, bg) >= 4.5, $"Danger sur {bg}");
        }
        Assert.True(Theme.Contrast(p.Focus, p.Window) >= 3.0);
        Assert.True(Theme.Contrast(p.Placeholder, p.Panel) >= 3.0);
    }

    [Fact]
    public void AmoledIsAnAcceptedThemeValueAndOthersFallBackToSystem()
    {
        var cfg = ConfigStore.Parse("""{ "Version": 2, "Theme": "AMOLED", "Profiles": [] }""");
        Assert.Equal("amoled", cfg.Theme);
        Assert.Equal("system", ConfigStore.Parse("""{ "Version": 2, "Theme": "néon", "Profiles": [] }""").Theme);
    }
}