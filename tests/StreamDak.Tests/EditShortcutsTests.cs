using System.Linq;
using System.Windows.Input;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Mode édition simplifié : raccourcis clavier, menu de page en clic droit sur l'onglet, glisser-déposer des onglets.</summary>
[Collection("ui")]
public class EditShortcutsTests
{
    readonly WpfHost _host;
    public EditShortcutsTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages =
        {
            new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux"), Btn("Trois") } },
            new DeckPage { Name = "B", Buttons = { Btn("Autre") } },
            new DeckPage { Name = "C", Buttons = { Btn("Dernier") } },
        } } },
    };

    MainViewModel NewDeckInEditMode()
    {
        _host.Reset(Config());
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() => { vm.OpenMenuCommand.Execute(null); QuickEditTests.Pick(vm, "Modifier le deck"); });
        return vm;
    }

    static bool Press(MainViewModel vm, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        MainWindow.HandleEditKey(vm.Editor!, key, modifiers, () => { });

    [Fact]
    public void DeleteRemovesTheSelectedButtonAndCtrlZBringsItBack()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            vm.Editor!.SelectIndex(1);
            Assert.True(Press(vm, Key.Delete));
            Assert.Null(Doc.Get(Root, 1));
            Assert.True(Press(vm, Key.Z, ModifierKeys.Control));
            Assert.Equal("Deux", Doc.Get(Root, 1)!.Label);
        });
    }

    [Fact]
    public void CtrlDDuplicatesAndCtrlCThenCtrlVPastesIntoAFreeSlot()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            vm.Editor!.SelectIndex(0);
            Assert.True(Press(vm, Key.D, ModifierKeys.Control));
            Assert.Equal(2, Doc.Container(Root)!.Count(b => !b.Blank && b.Label == "Un"));

            vm.Editor.SelectIndex(2);
            Assert.True(Press(vm, Key.C, ModifierKeys.Control));
            vm.Editor.SelectIndex(5);
            Assert.True(Press(vm, Key.V, ModifierKeys.Control));
            Assert.Equal("Trois", Doc.Get(Root, 5)!.Label);
        });
    }

    [Fact]
    public void AltArrowMovesTheButtonAndAKeyWithoutAShortcutIsLeftAlone()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            vm.Editor!.SelectIndex(2);
            Assert.True(Press(vm, Key.Down, ModifierKeys.Alt));
            Assert.Null(Doc.Get(Root, 2));
            Assert.Equal("Trois", Doc.Get(Root, 5)!.Label);

            Assert.False(Press(vm, Key.A));                                  // la frappe suit son cours
            Assert.False(Press(vm, Key.Delete, ModifierKeys.Control));       // Ctrl+Suppr n'est pas un raccourci
        });
    }

    [Fact]
    public void TheDockNoLongerHasCaseAndPageButtonsButEveryTabOpensItsPageMenuInEditMode()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            Assert.All(vm.Tabs, t => Assert.NotNull(t.Edit));
            vm.Tabs[1].Edit!.Execute(null);
            Assert.True(vm.MenuOpen);
            var texts = vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToList();
            Assert.Equal("Page « B »", vm.MenuEntries.OfType<MenuHeaderViewModel>().First().Text);
            foreach (var wanted in new[] { "Renommer", "Dupliquer", "Masquer des onglets", "Supprimer la page" }) Assert.Contains(wanted, texts);
            // « + » crée les pages et le glisser-déposer les déplace : ni l'un ni l'autre n'est dans le menu
            Assert.DoesNotContain("Nouvelle page…", texts);
            Assert.DoesNotContain("Déplacer à gauche", texts);
            Assert.DoesNotContain("Déplacer à droite", texts);
        });
    }

    [Fact]
    public void TabsHaveNoMenuOutsideEditMode()
    {
        _host.Reset(Config());
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() => Assert.All(vm.Tabs, t => Assert.Null(t.Edit)));
    }

    [Fact]
    public void DraggingATabChangesItsRankInOneUndoStepAndTheShownPageStaysTheSame()
    {
        var vm = NewDeckInEditMode();
        _host.Run(() =>
        {
            Assert.Equal("A", vm.Tabs.First(t => t.IsActive).Text);
            vm.MoveTab(0, 2);                                                // A déposée après C
            Assert.Equal(new[] { "B", "C", "A" }, Doc.Config.Profiles[0].Pages.Select(p => p.Name));
            Assert.Equal(new[] { "B", "C", "A" }, vm.Tabs.Select(t => t.Text));
            Assert.Equal("A", vm.Tabs.First(t => t.IsActive).Text);          // la page affichée suit son onglet

            vm.MoveTab(2, 0);
            Assert.Equal(new[] { "A", "B", "C" }, Doc.Config.Profiles[0].Pages.Select(p => p.Name));

            vm.MoveTab(1, 1);                                                // déposée sur elle-même : rien
            vm.MoveTab(0, 7);                                                // rang invalide : rien
            Assert.Equal(new[] { "A", "B", "C" }, Doc.Config.Profiles[0].Pages.Select(p => p.Name));
        });
    }
}
