using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>
/// Mode édition intégré au deck : les panneaux d'actions (gauche) et de propriétés (droite) remplacent la fenêtre d'édition.
/// La fenêtre n'est activable que pendant l'édition ; la grille garde le même nombre de cases.
/// </summary>
[Collection("ui")]
public class DeckEditModeTests
{
    readonly WpfHost _host;
    public DeckEditModeTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    [DllImport("user32.dll")] static extern int GetWindowLong(System.IntPtr hWnd, int nIndex);
    const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles =
        {
            new DeckProfile
            {
                Name = "P", Columns = 3, Rows = 2,
                Pages = { new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux"), Btn("Trois") } }, new DeckPage { Name = "B", Buttons = { Btn("Autre") } } },
            },
        },
    };

    (MainViewModel Vm, FakeDeckShell Shell, FakeAppWindows Windows) NewDeck()
    {
        _host.Reset(Config());
        var windows = new FakeAppWindows();
        var shell = new FakeDeckShell();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(shell, windows); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, shell, windows);
    }

    static void EnterEdit(MainViewModel vm)
    {
        vm.OpenMenuCommand.Execute(null);
        QuickEditTests.Pick(vm, "Modifier au toucher");
    }

    // ---------- entrée et sortie ----------

    [Fact]
    public void EnteringEditModeCreatesTheEmbeddedEditorMakesTheWindowActivatableAndOpensTheDrawers()
    {
        var (vm, shell, windows) = NewDeck();
        _host.Run(() =>
        {
            Assert.Null(vm.Editor);
            Assert.Equal(0, vm.LeftPanelWidth + vm.RightPanelWidth);

            EnterEdit(vm);
            Assert.NotNull(vm.Editor);
            Assert.True(vm.HasEditor);
            Assert.True(shell.Editing);                                      // la fenêtre devient activable pour la saisie
            Assert.InRange(vm.LeftPanelWidth, 260, 380);
            Assert.InRange(vm.RightPanelWidth, 320, 460);
            Assert.Empty(windows.Opened);                                    // aucune fenêtre d'édition séparée
        });
    }

    [Fact]
    public void LeavingEditModeDisposesTheEditorRestoresTheNoActivateWindowAndClosesTheDrawers()
    {
        var (vm, shell, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.EndEditCommand.Execute(null);
            Assert.False(vm.IsEdit);
            Assert.Null(vm.Editor);
            Assert.False(shell.Editing);                                     // le deck ne prend plus jamais le focus
            Assert.Equal(0, vm.LeftPanelWidth + vm.RightPanelWidth);
            Assert.Equal(2, shell.EditingChanges);
        });
    }

    [Fact]
    public void TheGridKeepsItsCellCountAndShrinksWhileThePanelsAreOpen()
    {
        var cfg = Config();
        cfg.Profiles[0].Columns = 0; cfg.Profiles[0].Rows = 0;               // format automatique
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1920, 1080); return v; });
        _host.Settle();
        _host.Run(() =>
        {
            var (cols, rows, width) = (vm.L.Columns, vm.L.Rows, vm.L.GridW);
            EnterEdit(vm);
            Assert.Equal((cols, rows), (vm.L.Columns, vm.L.Rows));            // même disposition qu'en usage normal
            Assert.True(vm.L.GridW < width, "la grille cède de la place aux panneaux");
            Assert.True(vm.L.GridW + vm.LeftPanelWidth + vm.RightPanelWidth <= 1920);
            vm.EndEditCommand.Execute(null);
            Assert.Equal(width, vm.L.GridW, 1);                               // retour à la taille d'origine
        });
    }

    // ---------- sélection ----------

    [Fact]
    public void TouchingATileSelectsItAndTheDrawerShowsItsProperties()
    {
        var (vm, _, windows) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            var deux = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Deux");
            deux.Click.Execute(null);
            Assert.Equal((Root, 1), (vm.Editor!.Place, vm.Editor.SelectedIndex));
            Assert.True(deux.IsSelected);
            Assert.False(vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un").IsSelected);
            Assert.True(vm.Editor.Properties.IsEditable);
            Assert.Equal("Deux", vm.Editor.Properties.Title);
            Assert.Empty(windows.Opened);

            vm.Tiles.OfType<AddTileViewModel>().First().Click.Execute(null);     // une case libre : proposition de la remplir
            Assert.False(deux.IsSelected);
            Assert.True(vm.Tiles.OfType<AddTileViewModel>().First().IsSelected);
            Assert.True(vm.Editor.Properties.IsEmpty);
        });
    }

    [Fact]
    public void ChangingPageClearsTheSelectionAndFollowsTheDisplayedPage()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un").Click.Execute(null);
            vm.Tabs[1].Command.Execute(null);                                    // page B
            Assert.Equal(-1, vm.Editor!.SelectedIndex);
            Assert.Equal(new Place("P", 1), vm.Editor.Place);
            Assert.DoesNotContain(vm.Tiles.OfType<DeckTileViewModel>(), t => t.IsSelected);
        });
    }

    // ---------- panneau d'actions ----------

    [Fact]
    public void TouchingAnActionPutsItInTheSelectedEmptyCellInOneUndoableStep()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Tiles.OfType<AddTileViewModel>().First().Click.Execute(null);     // case 3 (la première libre)
            var action = vm.Editor!.Catalog.Sections.SelectMany(s => s.Items).First(i => i.Action.Id == "media.playpause");
            action.AddCommand.Execute(null);

            Assert.Equal("Lecture / Pause", Doc.Get(Root, 3)!.Label);
            Assert.Equal("Ajouter « Lecture / Pause »", Doc.UndoLabel);
            Assert.Contains("media.playpause", Doc.Config.Recents);
            Assert.True(Doc.Undo());
            Assert.Null(Doc.Get(Root, 3));
            Assert.False(Doc.CanUndo);
            Assert.True(vm.Editor.CanRedo);
        });
    }

    [Fact]
    public void TouchingAnActionWithoutSelectionUsesTheFirstFreeCell()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Editor!.Catalog.Sections.SelectMany(s => s.Items).First(i => i.Action.Id == "audio.mute").AddCommand.Execute(null);
            Assert.Equal("Muet", Doc.Get(Root, 3)!.Label);
        });
    }

    [Fact]
    public void SearchingNarrowsTheActionsAndEnterAddsTheFirstResult()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Editor!.Catalog.Search = "chrome";
            Assert.True(vm.Editor.Catalog.IsSearching);
            vm.Editor.AddFirstCatalogResult();
            Assert.NotNull(Doc.Get(Root, 3));
            Assert.Equal("", vm.Editor.Catalog.Search);
        });
    }

    // ---------- annuler / rétablir ----------

    [Fact]
    public void UndoAndRedoWorkFromTheEmbeddedEditorAndSurviveLeavingEditMode()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un").Click.Execute(null);
            vm.Editor!.Properties.ResetAppearance();                             // une modification quelconque
            vm.Editor.DeleteSelection();
            vm.EndEditCommand.Execute(null);
            Assert.True(Doc.CanUndo);                                            // l'historique appartient au document, pas à l'éditeur intégré
            Assert.True(Doc.Undo());
        });
    }

    // ---------- clavier et mise au premier plan ----------

    [Fact]
    public void TheWindowIsActivatableOnlyWhileEditingAndKeepsTheNoActivateStyleOtherwise()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var hwnd = _host.Run(() => new WindowInteropHelper(win).EnsureHandle());   // poignée sans afficher la fenêtre
            bool NoActivate() => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0;
            Assert.True(_host.Run(NoActivate), "hors édition : jamais de focus");

            _host.Run(() => win.SetEditing(true));
            Assert.False(_host.Run(NoActivate), "en édition : fenêtre activable pour la saisie");
            Assert.True(_host.Run(() => MainWindow.IsEditing));

            _host.Run(() => win.SetEditing(false));
            Assert.True(_host.Run(NoActivate), "à la sortie : NOACTIVATE rétabli");
            Assert.False(_host.Run(() => MainWindow.IsEditing));
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- rendu ----------

    [Fact]
    public void TheDrawersRenderWithTouchSizedControlsAndNoBindingError()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Render(win, "deck-edition-tiroirs-0", 1600, 900);
            _host.Run(() =>
            {
                EnterEdit(vm);
                vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Deux").Click.Execute(null);
                vm.Editor!.Catalog.Search = "vol";                              // une recherche déplie les sections : des lignes sont à mesurer
            });
            _host.Render(win, "deck-edition-tiroirs", 1600, 900);
            _host.Run(() =>
            {
                var content = (DependencyObject)win.Content;
                var rows = WpfHost.Descendants<Button>(win.DeckCatalog).Where(b => b.DataContext is CatalogItemViewModel && b.ActualHeight > 0).ToList();
                Assert.NotEmpty(rows);
                Assert.All(rows, b => Assert.True(b.ActualHeight >= 56, $"{b.DataContext}: {b.ActualHeight} px"));
                var field = win.DeckProps.TitleBox;
                Assert.True(field.ActualHeight >= 48, $"champ titre : {field.ActualHeight} px");
                Assert.Equal("Deux", field.Text);
                Assert.True(win.DeckCatalog.ActualWidth > 200 && win.DeckProps.ActualWidth > 280);
                Assert.Contains(WpfHost.Descendants<Border>(content), b => b.DataContext is DeckTileViewModel { IsSelected: true });
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
