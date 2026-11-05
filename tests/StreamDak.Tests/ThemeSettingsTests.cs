using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Nouvelles clés de configuration (ADR 0034) : valeurs par défaut non écrites, anciens fichiers lus, bornes, thème TIXIA accepté.</summary>
public class ThemeConfigCoreTests
{
    [Fact]
    public void AnOldFileLoadsWithTheDefaultsAndNothingNewIsWritten()
    {
        var cfg = ConfigStore.Parse("""{ "Version": 2, "Theme": "dark", "Profiles": [] }""");
        Assert.Null(cfg.ThemeOverrides);
        Assert.Equal(0, cfg.WebIdleExitSeconds);
        Assert.False(cfg.LiftDragDisabled);
        Assert.Equal(0, cfg.LiftDelayMs);
        var json = JsonSerializer.Serialize(cfg, ConfigStore.Options);
        foreach (var key in new[] { "ThemeOverrides", "WebIdleExitSeconds", "LiftDragDisabled", "LiftDelayMs" }) Assert.DoesNotContain(key, json);
        Assert.Equal(TimeSpan30, ThemeOverrideKeys.WebIdle(cfg));
        Assert.Equal(500, ThemeOverrideKeys.LiftDelay(cfg));
    }

    static readonly System.TimeSpan TimeSpan30 = System.TimeSpan.FromSeconds(30);

    [Fact]
    public void TixiaIsAnAcceptedThemeAndUnknownOnesStillFallBackToSystem()
    {
        Assert.Equal("tixia", ConfigStore.Parse("""{ "Version": 2, "Theme": "TIXIA", "Profiles": [] }""").Theme);
        Assert.Equal("system", ConfigStore.Parse("""{ "Version": 2, "Theme": "néon", "Profiles": [] }""").Theme);
    }

    [Fact]
    public void OverridesKeepOnlyKnownKeysWithValidColors()
    {
        var cfg = ConfigStore.Parse("""
            { "Version": 2, "Profiles": [], "ThemeOverrides": { "Accent": "#ff7a1a", "text": "rouge", "inconnue": "#112233", "danger": "#12345" } }
            """);
        Assert.Equal(new Dictionary<string, string> { ["accent"] = "#FF7A1A" }, cfg.ThemeOverrides);
        Assert.Null(ConfigStore.Parse("""{ "Version": 2, "Profiles": [], "ThemeOverrides": { "x": "y" } }""").ThemeOverrides);
    }

    [Theory]
    [InlineData(1, 5)] [InlineData(60, 60)] [InlineData(9999, 300)] [InlineData(0, 0)]
    public void TheWebDelayIsKeptInItsRange(int stored, int expected) =>
        Assert.Equal(expected, ConfigStore.Parse($$"""{ "Version": 2, "Profiles": [], "WebIdleExitSeconds": {{stored}} }""").WebIdleExitSeconds);

    [Theory]
    [InlineData(10, 300)] [InlineData(700, 700)] [InlineData(99999, 1500)]
    public void TheLongPressDelayIsKeptInItsRange(int stored, int expected) =>
        Assert.Equal(expected, ConfigStore.Parse($$"""{ "Version": 2, "Profiles": [], "LiftDelayMs": {{stored}} }""").LiftDelayMs);

    [Fact]
    public void ExplicitValuesRoundTrip()
    {
        var cfg = ConfigStore.Parse("""{ "Version": 2, "Profiles": [], "LiftDragDisabled": true, "LiftDelayMs": 800, "WebIdleExitSeconds": 90, "ThemeOverrides": { "accent": "#FF7A1A" } }""");
        var back = ConfigStore.Parse(JsonSerializer.Serialize(cfg, ConfigStore.Options));
        Assert.True(back.LiftDragDisabled);
        Assert.Equal(800, back.LiftDelayMs);
        Assert.Equal(90, back.WebIdleExitSeconds);
        Assert.Equal("#FF7A1A", back.ThemeOverrides!["accent"]);
    }
}

/// <summary>Couleurs personnalisées : couleurs liées recalculées, textes lisibles sur l'accent, avertissements de contraste.</summary>
public class ThemeCustomTests
{
    [Fact]
    public void NothingChosenLeavesThePaletteUntouched() =>
        Assert.Same(Theme.DarkPalette, ThemeCustom.Apply(Theme.DarkPalette, new Dictionary<string, string>()));

    [Theory]
    [InlineData("#FF7A1A")] [InlineData("#FFFFFF")] [InlineData("#000000")] [InlineData("#2F7DF6")] [InlineData("#FFEB3B")]
    public void TheTextOnAnyChosenAccentIsReadable(string accent)
    {
        var p = ThemeCustom.Apply(Theme.DarkPalette, new Dictionary<string, string> { ["accent"] = accent });
        Assert.Equal(accent, p.Accent);
        Assert.True(Theme.Contrast(p.OnAccent, p.Accent) >= 4.5);
        Assert.True(Theme.Contrast(p.OnAccentContainer, p.AccentContainer) >= 4.5);
    }

    [Fact]
    public void AChosenBackgroundMovesTheLinkedSurfacesAndTextFollows()
    {
        var p = ThemeCustom.Apply(Theme.DarkPalette, new Dictionary<string, string> { ["background"] = "#101820", ["text"] = "#EEEEEE", ["surface"] = "#1E2A36" });
        Assert.Equal("#101820", p.Window);
        Assert.Equal("#101820", p.EditorBg);
        Assert.Equal("#1E2A36", p.Surface);
        Assert.Equal("#EEEEEE", p.Text);
        Assert.Equal("#EEEEEE", p.Focus);
        Assert.Empty(ThemeCustom.Warnings(p));
    }

    [Fact]
    public void ALowContrastChoiceIsReportedInPlainFrench()
    {
        var p = ThemeCustom.Apply(Theme.DarkPalette, new Dictionary<string, string> { ["text"] = "#303030" });
        var warnings = ThemeCustom.Warnings(p);
        Assert.Contains(warnings, w => w.StartsWith("Texte sur le fond", System.StringComparison.Ordinal) && w.Contains(": 1", System.StringComparison.Ordinal));
        Assert.Empty(ThemeCustom.Warnings(Theme.DarkPalette));
        Assert.Empty(ThemeCustom.Warnings(Theme.LightPalette));
        Assert.Empty(ThemeCustom.Warnings(Theme.TixiaPalette));
    }
}

/// <summary>Éditeur de thème des paramètres et réglages du toucher, avec le vrai document et la vraie application hors écran.</summary>
[Collection("ui")]
public class ThemeEditorTests
{
    readonly WpfHost _host;
    public ThemeEditorTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;

    ThemeEditorViewModel NewEditor()
    {
        _host.Reset(ConfigStore.DefaultConfig());
        return _host.Run(() => new ThemeEditorViewModel(Doc));
    }

    [Fact]
    public void PickingAColorAppliesItLiveAndWritesOnlyThatKey()
    {
        var editor = NewEditor();
        _host.Run(() =>
        {
            editor.Colors.First(c => c.Key == "accent").PickCommand.Execute("#FF7A1A");
            Assert.Equal(new Dictionary<string, string> { ["accent"] = "#FF7A1A" }, Doc.Config.ThemeOverrides);
            var brush = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["DeckAccent"];
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF7A1A"), brush.Color);   // à chaud, via les brushes Deck*
            Assert.True(editor.Colors.First(c => c.Key == "accent").IsCustom);
            Assert.True(editor.AnyCustom);
        });
        _host.Run(() => editor.ResetAllCommand.Execute(null));
    }

    [Fact]
    public void ResettingOneColorKeepsTheOthersAndResetAllClearsTheKey()
    {
        var editor = NewEditor();
        _host.Run(() =>
        {
            editor.Colors.First(c => c.Key == "accent").PickCommand.Execute("#FF7A1A");
            editor.Colors.First(c => c.Key == "success").PickCommand.Execute("#00FF88");
            editor.Colors.First(c => c.Key == "accent").ResetCommand.Execute(null);
            Assert.Equal(new[] { "success" }, Doc.Config.ThemeOverrides!.Keys.ToArray());
            editor.ResetAllCommand.Execute(null);
            Assert.Null(Doc.Config.ThemeOverrides);
            Assert.False(editor.AnyCustom);
        });
    }

    [Fact]
    public void TypingAnInvalidHexShowsAnErrorAndWritesNothing()
    {
        var editor = NewEditor();
        _host.Run(() =>
        {
            var row = editor.Colors.First(c => c.Key == "text");
            row.Hex = "#12";
            Assert.NotEqual("", row.Error);
            Assert.Null(Doc.Config.ThemeOverrides);
            row.Hex = "ffffff";      // sans dièse : accepté
            Assert.Equal("", row.Error);
            Assert.Equal("#FFFFFF", Doc.Config.ThemeOverrides!["text"]);
        });
        _host.Run(() => editor.ResetAllCommand.Execute(null));
    }

    [Fact]
    public void AnInsufficientContrastRaisesAWarningThatClearsOnReset()
    {
        var editor = NewEditor();
        _host.Run(() =>
        {
            editor.Colors.First(c => c.Key == "text").PickCommand.Execute("#202020");
            Assert.True(editor.HasWarnings);
            Assert.Contains("contraste", editor.Warnings);
            editor.ResetAllCommand.Execute(null);
            Assert.False(editor.HasWarnings);
        });
    }

    [Fact]
    public void CardsListEveryPresetAndTheChosenOneIsSelected()
    {
        var editor = NewEditor();
        _host.Run(() =>
        {
            Assert.Equal(new[] { "system", "dark", "light", "amoled", "tixia" }, editor.Cards.Select(c => c.Key).ToArray());
            editor.Cards.First(c => c.Key == "tixia").Command.Execute(null);
            Assert.Equal("tixia", Doc.Config.Theme);
            Assert.Equal(new[] { "tixia" }, editor.Cards.Where(c => c.IsSelected).Select(c => c.Key).ToArray());
            Assert.True(Theme.Tixia);
            Assert.Equal(4, editor.Preview.Count);
            editor.Cards.First(c => c.Key == "dark").Command.Execute(null);
            Assert.False(Theme.Tixia);
        });
    }
}
