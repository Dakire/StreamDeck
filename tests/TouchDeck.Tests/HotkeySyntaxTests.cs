using System.Linq;
using TouchDeck.Platform.Windows;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Syntaxe des raccourcis, indépendante du système (Core) et accord avec la table de la plateforme Windows.</summary>
public class HotkeySyntaxTests
{
    [Theory]
    [InlineData("ctrl+shift+m")]
    [InlineData("win+d")]
    [InlineData("F13")]
    [InlineData(" alt + tab ")]
    [InlineData("playpause")]
    [InlineData("ctrl+f24")]
    public void ValidCombinations(string combo) => Assert.True(HotkeySyntax.TryValidate(combo, out _));

    [Theory]
    [InlineData("", "Le raccourci est vide")]
    [InlineData("  ", "Le raccourci est vide")]
    [InlineData("ctrl+shift", "Ajoute une touche en plus des modificateurs")]
    [InlineData("ctrl+tabulation", "Touche inconnue : « tabulation »")]
    [InlineData("f25", "Touche inconnue : « f25 »")]
    [InlineData("f0", "Touche inconnue : « f0 »")]
    public void InvalidCombinationsExplainWhy(string combo, string reason)
    {
        Assert.False(HotkeySyntax.TryValidate(combo, out var error));
        Assert.Equal(reason, error);
    }

    [Fact]
    public void SingleCharactersFollowTheKeyboardWhenOneIsProvided()
    {
        Assert.True(HotkeySyntax.TryValidate("ctrl+é", out _));                       // sans clavier : tout caractère imprimable
        Assert.False(HotkeySyntax.TryValidate("ctrl+é", out _, ch => ch < 128));      // clavier qui n'a pas ce caractère
        Assert.True(HotkeySyntax.TryValidate("ctrl+c", out _, ch => ch < 128));
    }

    [Fact]
    public void SplitIgnoresSpacesAndEmptyParts() =>
        Assert.Equal(new[] { "ctrl", "c" }, HotkeySyntax.Split(" ctrl + + c "));

    [Fact]
    public void WindowsKnowsExactlyTheNamesOfTheCommonSyntax() =>
        Assert.Equal(HotkeySyntax.KeyNames.OrderBy(n => n), NativeInput.KnownNames.OrderBy(n => n));

    [Fact]
    public void WindowsValidationMatchesTheCommonSyntaxForNamedKeys()
    {
        foreach (var name in HotkeySyntax.KeyNames)
            Assert.True(NativeInput.TryValidateHotkey(name, out _) || HotkeySyntax.Modifiers.Contains(name), name);
        Assert.False(NativeInput.TryValidateHotkey("ctrl+shift", out _));
        Assert.False(NativeInput.TryValidateHotkey("", out _));
    }

    [Theory]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90.png", true)]
    [InlineData("A1B2C3D4E5F60718293A4B5C6D7E8F90.png", false)]
    [InlineData("..\\x.png", false)]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90.png.exe", false)]
    [InlineData(null, false)]
    public void IconNamesAreHashesOnly(string? name, bool valid)
    {
        Assert.Equal(valid, IconNames.IsValid(name));
        Assert.Equal(valid, IconNames.Resolve(name) != null);
    }
}
