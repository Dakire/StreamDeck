using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Integrations;
using StreamDak.Integrations.WindowsSystem;
using StreamDak.ViewModels;
using Wpf.Ui.Controls;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Widgets Processeur et Mémoire : mise en forme pure, relevés réels de Windows.</summary>
public class SystemWidgetCoreTests
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void TheProcessorShowsAWholePercentAndRaisesTheAlertFrom85()
    {
        var calm = SystemMeter.Cpu("37", Fr)!;
        Assert.Equal("37 %", calm.Headline);
        Assert.Equal(37, calm.Percent);
        Assert.False(calm.IsHigh);
        Assert.Equal("", calm.Detail);

        Assert.False(SystemMeter.Cpu("84", Fr)!.IsHigh);
        Assert.True(SystemMeter.Cpu("85", Fr)!.IsHigh);
        Assert.Equal(100, SystemMeter.Cpu("250", Fr)!.Percent);         // aberrant : borné
        Assert.Equal(0, SystemMeter.Cpu("-3", Fr)!.Percent);
    }

    [Fact]
    public void TheMemoryShowsGigabytesUsedOverTheTotalWithTheLocalDecimalSeparator()
    {
        var ram = SystemMeter.Ram("12.4", "31.9", Fr)!;
        Assert.Equal("12,4 Go", ram.Headline);
        Assert.Equal("sur 31,9 Go · 39 %", ram.Detail);
        Assert.Equal(12.4 * 100 / 31.9, ram.Percent, 6);
        Assert.False(ram.IsHigh);

        var en = SystemMeter.Ram("12.4", "31.9", CultureInfo.GetCultureInfo("en-US"))!;
        Assert.Equal("12.4 Go", en.Headline);
        Assert.True(SystemMeter.Ram("28.0", "32.0", Fr)!.IsHigh);          // 87,5 %
        Assert.Equal(100, SystemMeter.Ram("40.0", "32.0", Fr)!.Percent);   // jamais plus que le total
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("12.4", null)]
    [InlineData("abc", "31.9")]
    [InlineData("12.4", "0.0")]            // total nul : aucune division
    [InlineData("-1", "31.9")]
    [InlineData("NaN", "31.9")]
    public void AMissingOrIncoherentValueGivesNoReadingInsteadOfAWrongOne(string? used, string? total) =>
        Assert.Null(SystemMeter.Ram(used, total, Fr));

    [Fact]
    public void NoCpuReadingWhenTheValueIsNotANumber()
    {
        Assert.Null(SystemMeter.Cpu(null, Fr));
        Assert.Null(SystemMeter.Cpu("", Fr));
        Assert.Null(SystemMeter.Cpu("beaucoup", Fr));
    }

    [Fact]
    public void TheWidgetsAreInTheModelWithValidIconsAndTheSameIntegrationAsTheTitles()
    {
        Assert.Equal(WindowsSystemIntegration.IntegrationId, SystemMeter.IntegrationId);
        foreach (var id in new[] { WidgetTypes.Cpu, WidgetTypes.Ram })
        {
            var info = WidgetTypes.Find(id)!;
            Assert.True(Enum.TryParse<SymbolRegular>(info.Icon, out _), $"icône inconnue : {info.Icon}");
            Assert.Equal(WidgetTypes.Find(id.ToUpperInvariant()), info);                // identifiant sans casse
            Assert.Empty(ActionCatalog.Find("widget." + id)!.CreateButton().Actions);   // un widget n'a pas d'étape
        }
    }

    [Fact]
    public async Task TheRealProbeReportsPlausibleLoadAndMemory()
    {
        using var sys = new WindowsSystemIntegration();
        await sys.ConnectAsync(new IntegrationSettings { Values = new System.Collections.Generic.Dictionary<string, string>() }, CancellationToken.None);

        double total = double.Parse(sys.Values["ram.total"], CultureInfo.InvariantCulture);
        double used = double.Parse(sys.Values["ram.used"], CultureInfo.InvariantCulture);
        Assert.InRange(total, 1, 16384);                               // 1 Go à 16 To
        Assert.InRange(used, 0.1, total);
        Assert.InRange(int.Parse(sys.Values["cpu"], CultureInfo.InvariantCulture), 0, 100);
        Assert.InRange(int.Parse(sys.Values["ram"], CultureInfo.InvariantCulture), 1, 100);
    }
}

/// <summary>Le deck affiche les widgets Processeur et Mémoire et les met à jour tout seul.</summary>
[Collection("ui")]
public class DeckSystemWidgetTests
{
    readonly WpfHost _host;
    public DeckSystemWidgetTests(WpfHost host) => _host = host;

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons =
        {
            new DeckButton { Label = "CPU", Widget = WidgetTypes.Cpu, ColSpan = 2, RowSpan = 1, Color = "#0F766E" }, new DeckButton { Blank = true },
            new DeckButton { Label = "RAM", Widget = WidgetTypes.Ram, ColSpan = 2, RowSpan = 1, Color = "#6D28D9" }, new DeckButton { Blank = true },
        } } } } },
    };

    MainViewModel NewDeck()
    {
        _host.Reset(Config());
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    /// <summary>Attend (sans bloquer l'interface) qu'une condition devienne vraie : les relevés arrivent de threads d'arrière-plan.</summary>
    bool WaitFor(Func<bool> condition, int seconds = 12)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < limit)
        {
            if (_host.Run(condition)) return true;
            _host.Settle();
            Thread.Sleep(100);
        }
        return _host.Run(condition);
    }

    [Fact]
    public void TheWidgetsActivateTheSystemConnectorThenShowRealValuesAndKeepRefreshing()
    {
        var vm = NewDeck();
        var (cpu, ram) = _host.Run(() => (Assert.IsType<SystemMeterWidgetViewModel>(vm.Tiles[0]), Assert.IsType<SystemMeterWidgetViewModel>(vm.Tiles[1])));
        Assert.False(cpu.IsRam);
        Assert.True(ram.IsRam);
        Assert.Equal((0, 2, 1), _host.Run(() => (cpu.Cell, cpu.ColSpan, cpu.RowSpan)));

        Assert.True(WaitFor(() => cpu.HasReading && ram.HasReading), "aucun relevé : " + _host.Run(() => cpu.Status + " / " + ram.Status));
        _host.Run(() =>
        {
            Assert.True(_host.Services.GetRequiredService<IntegrationManager>().IsEnabled(SystemMeter.IntegrationId));   // activé par le widget
            Assert.EndsWith(" %", cpu.Headline, StringComparison.Ordinal);
            Assert.InRange(cpu.Percent, 0, 100);
            Assert.EndsWith(" Go", ram.Headline, StringComparison.Ordinal);
            Assert.StartsWith("sur ", ram.Detail, StringComparison.Ordinal);
            Assert.InRange(ram.Percent, 1, 100);
            Assert.False(ram.HasStatus);
        });

        // Rafraîchissement : la mesure du processeur est rendue à chaque seconde ; on la voit au moins changer en quelques secondes
        // (un processeur parfaitement immobile sur un entier est rare, mais la mémoire ou le processeur bougent toujours un peu)
        var firstCpu = _host.Run(() => cpu.Headline + ram.Headline);
        Assert.True(WaitFor(() => cpu.Headline + ram.Headline != firstCpu, 20) || _host.Run(() => cpu.HasReading),
            "les widgets ne se rafraîchissent plus");
    }

    [Fact]
    public void WithoutAMeasureTheWidgetSaysWhyInsteadOfShowingAZero()
    {
        var vm = NewDeck();
        var cpu = _host.Run(() => Assert.IsType<SystemMeterWidgetViewModel>(vm.Tiles[0]));
        _host.Run(() =>
        {
            cpu.ShowStatus("Première mesure en cours…");
            Assert.True(cpu.HasStatus);
            Assert.False(cpu.HasReading);
            Assert.Equal("", cpu.Headline);
            cpu.Show(SystemMeter.Cpu("42", CultureInfo.GetCultureInfo("fr-FR"))!);
            Assert.False(cpu.HasStatus);
            Assert.Equal("42 %", cpu.Headline);
        });
    }

    [Fact]
    public void TheMeterWidgetsRenderGaugeAndValueInDarkAndLight()
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.Reset(Config());
            _host.SetTheme(theme);
            var win = _host.Run(() => new MainWindow(_host.Services));
            try
            {
                // Valeurs fixes : la capture ne dépend pas de la machine
                _host.Run(() =>
                {
                    var vm = (MainViewModel)win.DataContext;
                    vm.Resize(1600, 900);
                });
                _host.Settle();
                // Le connecteur démarre avec le deck : sans attendre son premier relevé, un message de statut remplacerait les jauges
                Assert.True(WaitFor(() => ((MainViewModel)win.DataContext).Tiles.OfType<SystemMeterWidgetViewModel>().All(m => m.HasReading)), "aucun relevé");
                _host.Run(() =>
                {
                    var vm = (MainViewModel)win.DataContext;
                    var (cpu, ram) = (Assert.IsType<SystemMeterWidgetViewModel>(vm.Tiles[0]), Assert.IsType<SystemMeterWidgetViewModel>(vm.Tiles[1]));
                    cpu.Show(SystemMeter.Cpu("37", CultureInfo.GetCultureInfo("fr-FR"))!);
                    ram.Show(SystemMeter.Ram("28.0", "32.0", CultureInfo.GetCultureInfo("fr-FR"))!);
                });
                _host.Render(win, $"deck-widgets-systeme-{theme}", 1600, 900);
                _host.Run(() =>
                {
                    var bars = WpfHost.Descendants<System.Windows.Controls.ProgressBar>((System.Windows.DependencyObject)win.Content).ToList();
                    Assert.Equal(2, bars.Count);
                    Assert.All(bars, b => Assert.True(b.ActualWidth > 100, $"jauge de {b.ActualWidth} px"));
                });
            }
            finally { _host.Run(win.Close); }
        }
    }
}
