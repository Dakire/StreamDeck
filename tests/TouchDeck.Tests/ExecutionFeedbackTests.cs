using System.Linq;
using System.Threading.Tasks;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Retour d'exécution sur le deck : l'échec se voit sur la tuile, le message dit quoi faire, et se corrige en un toucher.</summary>
[Collection("ui")]
public class ExecutionFeedbackTests
{
    readonly WpfHost _host;
    public ExecutionFeedbackTests(WpfHost host) { _host = host; }

    static readonly Place Root = new("P", 0);

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
        Profiles =
        {
            new DeckProfile
            {
                Name = "P", Columns = 3, Rows = 2,
                Pages =
                {
                    new DeckPage
                    {
                        Name = "A",
                        Buttons =
                        {
                            new DeckButton { Label = "Cassé", Icon = "Star24", Actions = { new DeckAction { Type = "inexistant", Value = "x" } } },
                            new DeckButton { Label = "Bon", Icon = "Star24", Actions = { new DeckAction { Type = "media", Value = "playpause" } } },
                        },
                    },
                },
            },
        },
    };

    async Task<(MainViewModel Vm, FakeAppWindows Windows, DeckTileViewModel Tile, Task Run)> StartRun(string label)
    {
        _host.Reset(Config());
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); v.RunStatusActionCommand.Execute(null); return v; });
        var tile = _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == label));
        var run = _host.Run(() => vm.RunTileAsync(tile));
        await Task.Delay(50);
        return (vm, windows, tile, run);
    }

    [Fact]
    public async Task AFailedButtonShowsTheFailureOnItsTileAndSaysWhatToDo()
    {
        var (vm, _, tile, run) = await StartRun("Cassé");
        _host.Run(() =>
        {
            Assert.Equal(TileRunState.Error, tile.RunState);                       // visible sur la tuile, sans fenêtre modale
            Assert.True(vm.StatusIsError);
            Assert.Contains("Cassé", vm.Status);
            Assert.Contains("Type d'action inconnu", vm.Status);
            Assert.Contains("choisis une autre action", vm.Status);                // dit quoi faire, pas seulement ce qui a échoué
        });
        await run;
        _host.Run(() => Assert.Equal(TileRunState.Idle, tile.RunState));           // puis la tuile revient au repos
    }

    [Fact]
    public async Task TheFailureOffersToFixTheButtonInOneTouch()
    {
        var (vm, windows, _, run) = await StartRun("Cassé");
        _host.Run(() =>
        {
            Assert.True(vm.HasStatusAction);
            Assert.Equal("Modifier", vm.StatusActionText);
            vm.RunStatusActionCommand.Execute(null);
            Assert.Empty(windows.Opened);                                          // aucune fenêtre : le deck passe en mode édition
            Assert.True(vm.IsEdit);
            Assert.Equal((Root, 0), (vm.Editor!.Place, vm.Editor.SelectedIndex));  // le panneau de droite montre ce bouton
            Assert.False(vm.HasStatus);
        });
        await run;
    }

    [Fact]
    public async Task ASuccessfulButtonShowsNoErrorAndNoFixButton()
    {
        var (vm, windows, tile, run) = await StartRun("Bon");
        _host.Run(() =>
        {
            Assert.NotEqual(TileRunState.Error, tile.RunState);
            Assert.False(vm.HasStatusAction);
            Assert.Empty(windows.Opened);
        });
        await run;
    }
}
