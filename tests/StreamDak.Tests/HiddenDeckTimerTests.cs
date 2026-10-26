using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Un deck masqué ou réduit n'a plus aucune minuterie qui tourne ; il se remet à jour à son retour.</summary>
[Collection("ui")]
public class HiddenDeckTimerTests
{
    readonly WpfHost _host;
    public HiddenDeckTimerTests(WpfHost host) { _host = host; }

    [Fact]
    public void TheLiveTimerStopsWhileTheDeckIsHiddenAndResumesWhenItReturns()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages[0].Buttons.Clear();
        cfg.Profiles[0].Pages[0].Buttons.Add(new DeckButton { Label = "Heure", Widget = WidgetTypes.Clock, ColSpan = 2, RowSpan = 1 });
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();

        Assert.True(_host.Run(() => vm.LiveTimerRunning));        // une horloge est affichée
        _host.Run(() => vm.SetActive(false));
        Assert.False(_host.Run(() => vm.LiveTimerRunning));
        Assert.False(_host.Run(() => vm.IsActive));

        _host.Run(() => vm.Resize(1500, 900));                    // la grille peut être reconstruite pendant l'absence
        Assert.False(_host.Run(() => vm.LiveTimerRunning));       // sans relancer la minuterie

        _host.Run(() => vm.SetActive(true));
        Assert.True(_host.Run(() => vm.LiveTimerRunning));
    }
}
