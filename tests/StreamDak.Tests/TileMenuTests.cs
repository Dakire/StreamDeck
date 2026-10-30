using System.Linq;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Clic droit et menus de case : en mode édition, chaque clic droit rechargeait tout l'éditeur (cases, pages, écrans, propriétés : 150 à 230 ms mesurés).
/// Une case de la grille affichée est maintenant seulement sélectionnée. Le menu dépend de ce qu'est la case, et s'ouvre aussi d'un toucher (bouton « Case »).
/// </summary>
[Collection("ui")]
public class TileMenuTests
{
    readonly WpfHost _host;
    public TileMenuTests(WpfHost host) { _host = host; }

    static DeckConfig Config()
    {
        var cfg = ConfigStore.DefaultConfig();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons = new()
        {
            new DeckButton { Label = "Un", Icon = "Star24", Actions = { new DeckAction { Type = ActionTypes.Media, Value = "playpause" } } },
            new DeckButton { Label = "Deux", Icon = "Star24", Actions = { new DeckAction { Type = ActionTypes.Media, Value = "next" } } },
            new DeckButton { Label = "Dossier", IsFolder = true, Children = new() { new DeckButton { Label = "Dedans", Actions = { new DeckAction { Type = ActionTypes.Media, Value = "stop" } } } } },
        };
        return cfg;
    }

    MainViewModel NewDeck()
    {
        _host.Reset(Config());
        return _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
    }

    static string[] Texts(MainViewModel vm) => vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToArray();

    [Fact]
    public void ARightClickInEditModeSelectsTheTileWithoutRebuildingTheEditor()
    {
        var vm = NewDeck();
        _host.Run(() =>
        {
            vm.StartEditing(vm.CurrentPlace, 0);
            var slots = vm.Editor!.Slots.ToList();
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Deux").Edit!.Execute(null);
            Assert.True(vm.MenuOpen);
            Assert.Equal(1, vm.Editor.SelectedIndex);
            Assert.Equal(slots, vm.Editor.Slots.ToList());              // mêmes instances : rien n'a été reconstruit
            Assert.Contains("Dupliquer", Texts(vm));                    // menu à plat de l'éditeur
        });
    }

    [Fact]
    public void AFolderMenuOpensTheFolderAndHasNoReplaceEntry()
    {
        var vm = NewDeck();
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Edit!.Execute(null);
            Assert.Equal("Ouvrir le dossier", Texts(vm)[0]);
            Assert.DoesNotContain("Remplacer par", Texts(vm));
            Assert.Contains("Supprimer le dossier", Texts(vm));
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Ouvrir le dossier").Command.Execute(null);
            Assert.False(vm.MenuOpen);
            Assert.Contains(vm.Tiles.OfType<DeckTileViewModel>(), t => t.Label == "Dedans");
        });
    }

    [Fact]
    public void ReplacingAButtonByACatalogActionIsOneUndoableStep()
    {
        var vm = NewDeck();
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un").Edit!.Execute(null);
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Remplacer par").Command.Execute(null);
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Multimédia").Command.Execute(null);
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Suivant").Command.Execute(null);
            var place = vm.CurrentPlace;
            Assert.Equal("next", _host.Document.Get(place, 0)!.Actions.Single().Value);
            _host.Document.Undo();
            Assert.Equal("Un", _host.Document.Get(place, 0)!.Label);
        });
    }

    [Fact]
    public void TheDockCaseButtonOpensTheMenuOfTheSelectedTileWithoutARightClick()
    {
        var vm = NewDeck();
        _host.Run(() =>
        {
            vm.StartEditing(vm.CurrentPlace, 2);
            vm.OpenSelectedTileMenuCommand.Execute(null);
            Assert.True(vm.MenuOpen);
            Assert.Equal("Dossier", vm.MenuEntries.OfType<MenuHeaderViewModel>().First().Text);
        });
    }
}
