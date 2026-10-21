using System.Linq;
using System.Windows;
using System.Windows.Media;
using Xunit;
using Xunit.Abstractions;

namespace TouchDeck.Tests;

[Collection("ui")]
public class PrimaryButtonTextTests
{
    readonly WpfHost _h; readonly ITestOutputHelper _o;
    public PrimaryButtonTextTests(WpfHost h, ITestOutputHelper o) { _h = h; _o = o; }

    string Dump() => _h.Run(() =>
    {
        var r = Application.Current.Resources;
        string F(string k) { var v = r[k]; return v is SolidColorBrush b ? b.Color.ToString() : v?.ToString() ?? "null"; }
        var keys = new[] { "TextOnAccentFillColorPrimaryBrush", "AccentButtonForeground", "AccentButtonForegroundPointerOver", "TextFillColorPrimaryBrush", "AccentFillColorDefaultBrush", "ControlFillColorDefaultBrush" };
        return string.Join(" | ", keys.Select(k => k + "=" + F(k)));
    });

    [Fact]
    public void PrimaryButtonsUseTheThemeTextColorOnTheAccent()
    {
        _h.Reset(); _o.WriteLine("dark0  " + Dump());
        _h.SetTheme("light"); _o.WriteLine("light  " + Dump());
        _h.SetTheme("dark"); _o.WriteLine("dark1  " + Dump());
        foreach (var t in new[] { "dark", "light" })
        {
            _h.SetTheme(t);
            var palette = t == "dark" ? Theme.DarkPalette : Theme.LightPalette;
            var expected = (Color)ColorConverter.ConvertFromString(palette.OnAccent);
            foreach (var k in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed", "TextOnAccentFillColorPrimaryBrush" })
                Assert.Equal(expected, ((SolidColorBrush)_h.Run(() => Application.Current.Resources[k])).Color);
            Assert.True(Theme.Contrast(palette.OnAccent, palette.Accent) >= 4.5, t);
        }
    }
}
