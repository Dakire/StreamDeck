using System.Linq;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Couleurs tonales Material Design 3 : tons, gamut, rôles des trois thèmes et lisibilité garantie par l'écart de ton.</summary>
public class MaterialColorTests
{
    [Fact]
    public void TonesZeroAndHundredAreBlackAndWhite()
    {
        Assert.Equal("#000000", MaterialColor.Tone(280, 50, 0));
        Assert.Equal("#FFFFFF", MaterialColor.Tone(280, 50, 100));
    }

    [Theory]
    [InlineData(10)] [InlineData(40)] [InlineData(60)] [InlineData(80)] [InlineData(95)]
    public void ToneSetsTheLightnessAndKeepsTheHue(int tone)
    {
        var (hue, _) = MaterialColor.HueChroma(Theme.Seed);
        var color = MaterialColor.Tone(hue, 48, tone);
        // La clarté CIELAB retombe sur le ton demandé (à l'arrondi de l'octet près) : le contraste WCAG croît donc avec l'écart de ton
        var (h2, c2) = MaterialColor.HueChroma(color);
        Assert.InRange(c2, 1, 120);
        Assert.InRange(System.Math.Abs(h2 - hue), 0, 6);
    }

    [Fact]
    public void ToneMapsOutOfGamutChromaToAnInGamutColorOfTheSameTone()
    {
        var vivid = MaterialColor.Tone(150, 130, 90);   // vert très saturé et clair : hors gamut
        var plain = MaterialColor.Tone(150, 20, 90);
        Assert.Matches("^#[0-9A-F]{6}$", vivid);
        Assert.InRange(System.Math.Abs(Theme.Luminance(vivid) - Theme.Luminance(plain)), 0, 0.02);   // même clarté
    }

    [Fact]
    public void TheSeedHasItsOwnHueAndANonZeroChroma()
    {
        var (hue, chroma) = MaterialColor.HueChroma("#6C5CE7");
        Assert.InRange(hue, 270, 310);   // violet-indigo
        Assert.InRange(chroma, 40, 90);
        Assert.Equal(0, MaterialColor.HueChroma("#808080").Chroma, 0);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(true, true)] [InlineData(false, false)]
    public void EveryTextRoleIsReadableOnItsContainer(bool dark, bool amoled)
    {
        var m = MaterialScheme.Create(Theme.Seed, dark, amoled);
        var surfaces = new[] { m.Surface, m.SurfaceContainerLowest, m.SurfaceContainerLow, m.SurfaceContainer, m.SurfaceContainerHigh, m.SurfaceContainerHighest };
        foreach (var s in surfaces)
        {
            Assert.True(Theme.Contrast(m.OnSurface, s) >= 7.0, $"onSurface sur {s}");
            Assert.True(Theme.Contrast(m.OnSurfaceVariant, s) >= 4.5, $"onSurfaceVariant sur {s}");
            Assert.True(Theme.Contrast(m.Error, s) >= 4.5, $"error sur {s}");
        }
        Assert.True(Theme.Contrast(m.OnPrimary, m.Primary) >= 4.5);
        Assert.True(Theme.Contrast(m.OnPrimaryContainer, m.PrimaryContainer) >= 4.5);
        Assert.True(Theme.Contrast(m.OnSecondaryContainer, m.SecondaryContainer) >= 4.5);
        Assert.True(Theme.Contrast(m.OnErrorContainer, m.ErrorContainer) >= 4.5);
        Assert.True(Theme.Contrast(m.OnError, m.Error) >= 4.5);
    }

    [Fact]
    public void SurfacesRiseInTheDarkThemesAndSinkInTheLightOne()
    {
        double L(string hex) => Theme.Luminance(hex);
        foreach (var amoled in new[] { false, true })
        {
            var m = MaterialScheme.Create(Theme.Seed, true, amoled);
            var order = new[] { m.SurfaceContainerLowest, m.SurfaceContainerLow, m.SurfaceContainer, m.SurfaceContainerHigh, m.SurfaceContainerHighest }.Select(L).ToArray();
            Assert.Equal(order.OrderBy(x => x), order);
            Assert.True(order[0] <= order[^1]);
        }
        var light = MaterialScheme.Create(Theme.Seed, false);
        var lightOrder = new[] { light.SurfaceContainerLowest, light.SurfaceContainerLow, light.SurfaceContainer, light.SurfaceContainerHigh, light.SurfaceContainerHighest }.Select(L).ToArray();
        Assert.Equal(lightOrder.OrderByDescending(x => x), lightOrder);
    }

    [Fact]
    public void AmoledKeepsAPureBlackWindowAndTheOthersDoNot()
    {
        Assert.Equal("#000000", Theme.AmoledPalette.Window);
        Assert.NotEqual("#000000", Theme.DarkPalette.Window);
        Assert.True(Theme.Luminance(Theme.DarkPalette.Window) < 0.02);   // reste très sombre
        Assert.True(Theme.Luminance(Theme.LightPalette.Window) > 0.9);
    }

    [Fact]
    public void ThePaletteComesFromTheSeedNotFromFixedHexValues()
    {
        var other = MaterialScheme.Create("#1E7F5F", true);          // une autre couleur source donne une autre famille de couleurs
        var mine = MaterialScheme.Create(Theme.Seed, true);
        Assert.NotEqual(other.Primary, mine.Primary);
        Assert.NotEqual(other.SecondaryContainer, mine.SecondaryContainer);
        Assert.Equal(mine.Primary, MaterialScheme.Create(Theme.Seed, true).Primary);   // déterministe
        Assert.Equal(mine.Primary, Theme.DarkPalette.Accent);
    }
}
