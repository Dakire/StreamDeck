using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Le curseur vertical 1 × 2 : l'icône du canal coupe au toucher, le niveau s'affiche, et une source indisponible grise la tuile sans faux zéro.</summary>
[Collection("ui")]
public class VerticalSliderTests
{
    readonly WpfHost _host;
    public VerticalSliderTests(WpfHost host) => _host = host;

    static DeckButton Slider(string source) => new()
    {
        Label = "Niveau", Widget = WidgetTypes.Level, ColSpan = 1, RowSpan = 2, Color = "#2563EB",
        WidgetOptions = new() { ["source"] = source, ["shape"] = "vertical" },
    };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 5, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons =
        {
            Slider("windows:render:Haut-parleurs"), Slider("sonar:game"), new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true },
            new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true }, new DeckButton { Blank = true },
        } } } } },
    };

    SimulatedPlatform Sim => (SimulatedPlatform)_host.Services.GetRequiredService<PlatformServices>().Audio;

    static async Task<bool> WaitAsync(Func<bool> condition, int ms = 5000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < limit) { if (condition()) return true; await Task.Delay(20); }
        return condition();
    }

    [Fact]
    public async Task TheChannelIconMutesOnTouchAndAnUnavailableSourceGreysTheTileWithoutAFalseZero()
    {
        _host.Reset(Config());
        Sim.SetEndpointVolume("render-default", 60);
        Sim.SetEndpointMute("render-default", false);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
            _host.Settle();
            _host.Render(win, "deck-curseurs-verticaux", 1600, 900);
            var vm = _host.Run(() => (MainViewModel)win.DataContext);
            var tiles = _host.Run(() => vm.Tiles.OfType<LevelWidgetViewModel>().ToList());
            Assert.Equal(2, tiles.Count);
            Assert.True(await WaitAsync(() => _host.Run(() => tiles[0].HasReading)), "lecture du périphérique manquante");

            var sliders = _host.Run(() => WpfHost.Descendants<VerticalSlider>((DependencyObject)win.Content).Where(s => s.IsVisible || s.Visibility == Visibility.Visible).ToList());
            Assert.Equal(2, sliders.Count);

            // Disponible : niveau affiché, curseur actif, cible tactile de l'icône ≥ 44 px
            var (iconButton, level, w, h) = _host.Run(() => (WpfHost.Descendants<Button>(sliders[0]).First(), WpfHost.Descendants<LevelControl>(sliders[0]).First(), sliders[0].ActualWidth, sliders[0].ActualHeight));
            Assert.True(_host.Run(() => level.IsEnabled));
            Assert.True(_host.Run(() => iconButton.ActualHeight) >= 44 && _host.Run(() => iconButton.ActualWidth) >= 44);
            Assert.True(w >= 48 && h >= 96, $"curseur de {w} × {h}");
            Assert.Equal(60, _host.Run(() => (int)level.Value));
            Assert.Equal("Couper ou rétablir " + _host.Run(() => tiles[0].ShortTitle), _host.Run(() => iconButton.GetValue(System.Windows.Automation.AutomationProperties.NameProperty)));

            // Toucher l'icône : coupé, puis rétabli
            _host.Run(() => iconButton.Command!.Execute(null));
            Assert.True(await WaitAsync(() => Sim.ReadEndpoint("render-default").Muted), "coupure non envoyée");
            Assert.True(_host.Run(() => tiles[0].IsMuted));
            _host.Run(() => iconButton.Command!.Execute(null));
            Assert.True(await WaitAsync(() => !Sim.ReadEndpoint("render-default").Muted), "rétablissement non envoyé");

            // Sonar n'est pas activé : tuile grisée, curseur désactivé (il ne reçoit plus le doigt), raison dite, rien d'inventé
            var (offLevel, opacity) = _host.Run(() => (WpfHost.Descendants<LevelControl>(sliders[1]).First(), ((UIElement)WpfHost.Descendants<Grid>(sliders[1]).First(g => g.Name == "Root")).Opacity));
            Assert.False(_host.Run(() => offLevel.IsEnabled));
            Assert.Equal(0.4, opacity, 2);
            Assert.False(_host.Run(() => tiles[1].IsAvailable));
            Assert.Contains("Sonar", _host.Run(() => tiles[1].Availability));
        }
        finally { _host.Run(win.Close); }
    }
}
