using System.Linq;
using StreamDak.Audio;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Les curseurs Sonar sont proposés tout prêts dans la catégorie Sonar du catalogue : un curseur vertical tactile par canal, et le mélangeur.</summary>
public class SonarCatalogTests
{
    [Fact]
    public void EachSonarChannelHasAReadyVerticalSliderAndThereIsAMixer()
    {
        var entries = LevelOptions.SonarSliderEntries();

        Assert.Equal(new[] { "master", "game", "chatRender", "media", "aux", "chatCapture" }.Select(c => $"sonar.slider.{c}").Append("sonar.mixer"), entries.Select(e => e.Id));
        Assert.All(entries, e => Assert.Equal("Sonar", e.Category));
    }

    [Fact]
    public void ASliderEntryCreatesAOneByTwoLevelWidgetOnItsChannelWithNoAction()
    {
        var game = LevelOptions.SonarSliderEntries().First(e => e.Id == "sonar.slider.game").CreateButton();

        Assert.Equal(WidgetTypes.Level, game.Widget);
        Assert.Equal((1, 2), game.Span());
        Assert.Equal("sonar:game", game.Option(LevelOptions.Source));
        Assert.Equal(LevelShape.VerticalSlider, LevelShapes.Parse(game.Option(LevelOptions.Shape)));
        Assert.Empty(game.Actions);
        Assert.Equal("Sonar Jeu", game.Label);
    }

    [Fact]
    public void TheMixerEntryCreatesAMixerWidgetWithTheDefaultChannels()
    {
        var mixer = LevelOptions.SonarSliderEntries().First(e => e.Id == "sonar.mixer").CreateButton();

        Assert.Equal(WidgetTypes.Mixer, mixer.Widget);
        Assert.Equal((3, 2), mixer.Span());
        Assert.Equal(5, LevelOptions.MixerTargets(mixer.Option(LevelOptions.Sources)).Count);
    }

    [Fact]
    public void ASliderEntryIsFoundBySearchingSliderOrTheChannelName()
    {
        ActionCatalog.Register("Sonar", "Speaker224", LevelOptions.SonarSliderEntries());

        Assert.Contains(ActionCatalog.Search("curseur micro"), e => e.Id == "sonar.slider.chatCapture");
        Assert.Contains(ActionCatalog.Search("slider"), e => e.Id == "sonar.slider.game");
    }
}
