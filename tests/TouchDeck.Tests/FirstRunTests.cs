using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Premier lancement et page vide : invitation à ajouter un bouton, indice de découverte unique, aucune fenêtre imposée.</summary>
[Collection("ui")]
public class FirstRunTests
{
    readonly WpfHost _host;
    public FirstRunTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config(params string[] labels)
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
            Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages = { new DeckPage { Name = "A" } } } },
        };
        foreach (var l in labels) cfg.Profiles[0].Pages[0].Buttons.Add(Btn(l));
        return cfg;
    }

    (MainViewModel Vm, FakeAppWindows Windows) NewDeck(DeckConfig cfg)
    {
        _host.Reset(cfg);
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, windows);
    }

    // ---------- état vide ----------

    [Fact]
    public void AnEmptyPageInvitesToAddAButtonOnItsFirstCellOnly()
    {
        var (vm, windows) = NewDeck(Config());
        _host.Run(() =>
        {
            Assert.Equal(6, vm.Tiles.Count);
            Assert.IsType<WelcomeTileViewModel>(vm.Tiles[0]);
            Assert.All(vm.Tiles.Skip(1), t => Assert.IsType<SpacerTileViewModel>(t));   // les autres cases restent discrètes

            ((WelcomeTileViewModel)vm.Tiles[0]).Click.Execute(null);                  // un toucher ouvre le menu de création
            Assert.True(vm.MenuOpen);
            Assert.Contains(vm.MenuEntries.OfType<MenuHeaderViewModel>(), h => h.Text == "Case vide");
            Assert.Empty(windows.Opened);                                              // aucune fenêtre imposée
        });
    }

    [Fact]
    public void TheInvitationDisappearsAsSoonAsAButtonExists()
    {
        var (vm, _) = NewDeck(Config());
        _host.Run(() =>
        {
            ((WelcomeTileViewModel)vm.Tiles[0]).Click.Execute(null);
            QuickEditTests.Pick(vm, "Média : lecture / pause");
            Assert.Equal("Lecture / Pause", Doc.Get(Root, 0)!.Label);
            Assert.DoesNotContain(vm.Tiles, t => t is WelcomeTileViewModel);
            Assert.Equal(5, vm.Tiles.OfType<SpacerTileViewModel>().Count());

            vm.RunStatusActionCommand.Execute(null);                                   // « Annuler » : la page redevient vide, l'invitation revient
            Assert.IsType<WelcomeTileViewModel>(vm.Tiles[0]);
        });
    }

    [Fact]
    public void AnEmptyFolderInvitesAfterItsBackArrowAndEditModeKeepsThePlusTiles()
    {
        var cfg = Config("Un");
        cfg.Profiles[0].Pages[0].Buttons.Add(DeckDocument.NewFolder("Outils"));
        var (vm, _) = NewDeck(cfg);
        _host.Run(() =>
        {
            Assert.DoesNotContain(vm.Tiles, t => t is WelcomeTileViewModel);           // la page a des boutons
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Outils").Click.Execute(null);
            Assert.Equal("Retour", ((DeckTileViewModel)vm.Tiles[0]).Label);
            Assert.IsType<WelcomeTileViewModel>(vm.Tiles[1]);                          // dossier vide : invitation juste après la flèche

            vm.OpenMenuCommand.Execute(null);
            QuickEditTests.Pick(vm, "Modifier le deck");
            Assert.DoesNotContain(vm.Tiles, t => t is WelcomeTileViewModel);
            Assert.NotEmpty(vm.Tiles.OfType<AddTileViewModel>());                      // en édition : emplacements « + » habituels
        });
    }

    [Fact]
    public void DroppingOnTheInvitationCreatesTheButton()
    {
        var (vm, _) = NewDeck(Config());
        _host.Run(() =>
        {
            Assert.True(vm.IsFreeSlot(vm.Tiles[0]));
            Assert.True(vm.TryGetSlot(vm.Tiles[0], out var place, out var index));
            Assert.Equal((Root, 0), (place, index));
        });
    }

    [Fact]
    public void TheDefaultConfigurationOpensNoWindowAndShowsNoInvitation()
    {
        var (vm, windows) = NewDeck(ConfigStore.DefaultConfig());
        _host.Run(() =>
        {
            Assert.DoesNotContain(vm.Tiles, t => t is WelcomeTileViewModel);
            Assert.Empty(windows.Opened);
            Assert.False(vm.MenuOpen);
            Assert.False(vm.HasStatus);                                                // pas d'indice non plus : la page est déjà garnie
        });
    }

    // ---------- indice de découverte ----------

    [Fact]
    public void TheHintAppearsOnceWhileThePageIsEmptyOrAlmostEmpty()
    {
        var (vm, _) = NewDeck(Config("Un"));
        _host.Run(() =>
        {
            Assert.True(vm.HasStatus);
            Assert.False(vm.StatusIsError);
            Assert.Contains("appui long", vm.Status);
            Assert.False(vm.HasStatusAction);

            vm.Resize(1500, 900);                                                      // la grille est reconstruite : pas de second indice
            vm.RunStatusActionCommand.Execute(null);                                   // sans action : efface le bandeau
        });
        _host.Run(() =>
        {
            Doc.UpdateSettings(c => c.Spacing = 20);                                   // autre reconstruction
            Assert.False(vm.HasStatus || vm.Status?.Contains("Astuce") == true);
        });
    }

    [Fact]
    public void TheHintIsNotShownOnAFullPageOverAnotherMessageOrWithTheMenuOpen()
    {
        var (full, _) = NewDeck(Config("Un", "Deux", "Trois"));
        _host.Run(() => Assert.False(full.HasStatus));

        _host.Reset(Config("Un"));
        var vm = _host.Run(() =>
        {
            var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows());
            Notifier.Error("Config invalide : à corriger");                            // un message important est déjà affiché
            v.Resize(1600, 900);
            return v;
        });
        _host.Run(() =>
        {
            Assert.Equal("Config invalide : à corriger", vm.Status);
            Assert.True(vm.StatusIsError);
        });
    }

    // ---------- affichage ----------

    [Fact]
    public void TheInvitationRendersWithATouchSizedButtonAndNoBindingError()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-vide", 1400, 800);
            _host.Run(() =>
            {
                var button = WpfHost.Descendants<Button>((DependencyObject)win.Content).Single(b => b.DataContext is WelcomeTileViewModel);
                Assert.True(button.ActualHeight >= 48 && button.ActualWidth >= 48, $"{button.ActualWidth} × {button.ActualHeight}");
                Assert.Equal("Ajouter un bouton", System.Windows.Automation.AutomationProperties.GetName(button));
                Assert.Contains(WpfHost.Descendants<TextBlock>(button), t => t.Text == "Ajouter un bouton");
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
