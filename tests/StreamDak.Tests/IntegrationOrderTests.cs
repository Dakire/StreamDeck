using System.Linq;
using StreamDak.Integrations;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Ordre des intégrations choisi dans les paramètres : tri, enregistrement, compatibilité des anciens fichiers.</summary>
public class IntegrationOrderLogicTests
{
    static readonly string[] Items = { "obs", "hue", "sonar", "discord" };

    [Fact]
    public void WithoutASavedOrderTheOriginalOrderIsKept()
    {
        Assert.Equal(Items, IntegrationOrder.Sort(Items, null, i => i));
        Assert.Equal(Items, IntegrationOrder.Sort(Items, System.Array.Empty<string>(), i => i));
    }

    [Fact]
    public void TheSavedOrderWinsNewItemsFollowInTheirOriginalOrderAndUnknownIdsAreIgnored()
    {
        var sorted = IntegrationOrder.Sort(Items, new[] { "discord", "inconnu", "OBS" }, i => i);

        Assert.Equal(new[] { "discord", "obs", "hue", "sonar" }, sorted);   // « OBS » sans distinction de casse ; hue et sonar gardent leur ordre
    }

    [Fact]
    public void AnOldConfigWithoutTheKeyLoadsAndNeverWritesIt()
    {
        var config = ConfigStore.Parse("""{"Version":2,"Profiles":[{"Name":"P","Pages":[{"Name":"A","Buttons":[]}]}]}""", out _);

        Assert.Null(config.IntegrationOrder);
        Assert.DoesNotContain("IntegrationOrder", System.Text.Json.JsonSerializer.Serialize(config));
    }

    [Fact]
    public void TheNormalizerCleansTheSavedOrder()
    {
        var config = ConfigStore.Parse("""{"Version":2,"IntegrationOrder":["hue"," ","hue","OBS",""],"Profiles":[{"Name":"P","Pages":[{"Name":"A","Buttons":[]}]}]}""", out _);
        Assert.Equal(new[] { "hue", "OBS" }, config.IntegrationOrder);

        var empty = ConfigStore.Parse("""{"Version":2,"IntegrationOrder":[" "],"Profiles":[{"Name":"P","Pages":[{"Name":"A","Buttons":[]}]}]}""", out _);
        Assert.Null(empty.IntegrationOrder);
    }
}

/// <summary>Les cartes des paramètres suivent l'ordre enregistré, et le glisser-déposer l'enregistre (hors historique d'annulation).</summary>
[Collection("ui")]
public class SettingsIntegrationOrderTests
{
    readonly WpfHost _host;
    public SettingsIntegrationOrderTests(WpfHost host) { _host = host; }

    [Fact]
    public void MovingACardKeepsTheNewOrderInTheConfigAndTheNextWindowStartsWithIt()
    {
        _host.Reset();
        var window = _host.Run(() => new SettingsWindow(_host.Services));
        var vm = _host.Run(() => (SettingsViewModel)window.DataContext);
        _host.Run(() =>
        {
            var before = vm.Integrations.Select(c => c.Id).ToList();
            Assert.True(before.Count >= 3);

            vm.MoveIntegration(0, 2);
            var after = vm.Integrations.Select(c => c.Id).ToList();
            Assert.Equal(new[] { before[1], before[2], before[0] }, after.Take(3));
            Assert.Equal(after, _host.Document.Config.IntegrationOrder);

            vm.MoveIntegration(1, 1);        // sur lui-même : rien
            vm.MoveIntegration(-1, 2);       // rang invalide : rien
            vm.MoveIntegration(0, 999);
            Assert.Equal(after, vm.Integrations.Select(c => c.Id).ToList());
        });
        _host.Run(window.Close);
        var next = _host.Run(() => new SettingsWindow(_host.Services));
        try { _host.Run(() => Assert.Equal(_host.Document.Config.IntegrationOrder, ((SettingsViewModel)next.DataContext).Integrations.Select(c => c.Id).ToList())); }
        finally { _host.Run(next.Close); }
    }
}
