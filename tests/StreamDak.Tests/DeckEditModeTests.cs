using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

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
        QuickEditTests.Pick(vm, "Modifier le deck");
    }

    // ---------- entrée et sortie ----------

    [Fact]
    public void EnteringEditModeCreatesTheEmbeddedEditorMakesTheWindowActivatableAndOpensTheDrawers()
    {
        var (vm, shell, windows) = NewDeck();
        _host.Run(() =>
        {
            Assert.Null(vm.Editor);
            Assert.Equal(0, vm.CatalogWidth + vm.InspectorHeight);

            EnterEdit(vm);
            Assert.NotNull(vm.Editor);
            Assert.True(vm.HasEditor);
            Assert.True(shell.Editing);                                      // la fenêtre devient activable pour la saisie
            Assert.InRange(vm.CatalogWidth, 300, 420);                       // catalogue à droite
            Assert.InRange(vm.InspectorHeight, 320, 460);                    // inspecteur sous la grille
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
            Assert.Equal(0, vm.CatalogWidth + vm.InspectorHeight);
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
            Assert.True(vm.L.GridW + vm.CatalogWidth <= 1920);
            Assert.True(vm.L.GridH + vm.InspectorHeight <= 1080);
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

    // ---------- boîtes de dialogue et menus dans le deck (aucune fenêtre séparée) ----------

    // Les tâches des boîtes reprennent sur le thread d'interface : leur résultat se lit hors de _host.Run (sinon le thread serait bloqué)
    [Fact]
    public async System.Threading.Tasks.Task DialogsAreShownInTheDeckAndAnswerWithTheTypedTextOrNull()
    {
        var (vm, _, windows) = NewDeck();
        var ask = _host.Run(() => vm.AskAsync("Renommer", "Nouveau nom", "Un", "Renommer"));
        _host.Run(() =>
        {
            Assert.True(vm.HasDialog);
            Assert.True(vm.Dialog!.ShowInput);
            vm.Dialog.Text = "  Premier ";
            vm.Dialog.ConfirmCommand.Execute(null);
        });
        Assert.Equal("Premier", await ask);                                   // texte validé, espaces retirés
        _host.Run(() => Assert.False(vm.HasDialog));

        var cancelled = _host.Run(() => vm.AskAsync("Renommer", "Nouveau nom", "Un"));
        _host.Run(() => vm.Dialog!.CancelCommand.Execute(null));
        Assert.Null(await cancelled);

        var empty = _host.Run(() => vm.AskAsync("Renommer", "Nouveau nom", "Un"));
        _host.Run(() =>
        {
            vm.Dialog!.Text = "   ";
            Assert.False(vm.Dialog.ConfirmCommand.CanExecute(null));           // un nom vide est refusé
            Assert.True(vm.Dialog.HasError);
            vm.Dialog.CancelCommand.Execute(null);
        });
        Assert.Null(await empty);

        var yes = _host.Run(() => vm.ConfirmAsync("Supprimer", "Sûr ?", "Supprimer", danger: true));
        _host.Run(() => vm.Dialog!.ConfirmCommand.Execute(null));
        Assert.True(await yes);
        var no = _host.Run(() => vm.ConfirmAsync("Supprimer", "Sûr ?", "Supprimer"));
        _host.Run(() => vm.Dialog!.CancelCommand.Execute(null));
        Assert.False(await no);

        var info = _host.Run(() => vm.InfoAsync("Info", "Texte"));
        _host.Run(() =>
        {
            Assert.False(vm.Dialog!.ShowCancel);
            vm.Dialog.ConfirmCommand.Execute(null);
        });
        await info;
        Assert.Empty(windows.Opened);
    }

    [Fact]
    public async System.Threading.Tasks.Task ANewDialogReplacesTheOpenOneAndEndingTheEditDismissesIt()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() => EnterEdit(vm));
        var first = _host.Run(() => vm.ConfirmAsync("A", "a", "OK"));
        var second = _host.Run(() => vm.ConfirmAsync("B", "b", "OK"));
        Assert.False(await first);                                            // la première est annulée
        _host.Run(() => Assert.Equal("B", vm.Dialog!.Title));
        _host.Run(() => vm.EndEditCommand.Execute(null));
        Assert.False(await second);                                           // une boîte ne survit pas à la fin de l'édition
        _host.Run(() => Assert.False(vm.HasDialog));
    }

    [Fact]
    public void PageAndProfileOptionsOpenInTheDeckSheetDuringEditing()
    {
        var (vm, _, windows) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.OpenPageMenuCommand.Execute(null);
            Assert.True(vm.MenuOpen);
            Assert.Equal("Page « A »", vm.MenuEntries.OfType<MenuHeaderViewModel>().First().Text);
            var texts = vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToList();
            Assert.Contains("Renommer", texts);
            Assert.Contains("Supprimer la page", texts);

            vm.CloseMenuCommand.Execute(null);
            vm.OpenProfilesCommand.Execute(null);                              // la pastille de profil, en édition : options du profil
            Assert.Equal("Profil « P »", vm.MenuEntries.OfType<MenuHeaderViewModel>().First().Text);
            texts = vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToList();
            Assert.Contains("Format de la grille", texts);
            Assert.Contains("Renommer", texts);
            Assert.Empty(windows.Opened);
        });
    }

    [Fact]
    public void ALongPressOnATileDuringEditingOffersTheFullEditorMenu()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Deux").Edit!.Execute(null);
            var texts = vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToList();
            Assert.Contains("Dupliquer", texts);                               // menu à plat : plus de sous-menu « Déplacer », « Ajouter » ni « Propriétés »
            Assert.Contains("Réinitialiser", texts);
            Assert.Contains("Supprimer", texts);
            Assert.DoesNotContain("Déplacer le bouton", texts);
            Assert.DoesNotContain("Afficher les propriétés", texts);
            Assert.Equal(1, vm.Editor!.SelectedIndex);                         // le menu vise la case touchée
        });
    }

    [Fact]
    public void TheDialogOverlayRendersWithTouchSizedButtonsAndNoBindingError()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var vm = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() => win.EditCell(Root, 1));
            _ = _host.Run(() => vm.AskAsync("Renommer la page", "Choisis un nouveau nom pour cette page.", "A", "Renommer"));
            _host.Render(win, "deck-dialogue", 1600, 900);
            _host.Run(() =>
            {
                var buttons = WpfHost.Descendants<Wpf.Ui.Controls.Button>(win.DialogCard).Where(b => b.Content is string).ToList();   // hors bouton d'effacement du champ
                Assert.NotEmpty(buttons);
                Assert.All(buttons, b => Assert.True(b.ActualHeight >= 48, $"{b.Content}: {b.ActualHeight} px"));
                Assert.True(win.DialogInput.ActualHeight >= 48, $"champ : {win.DialogInput.ActualHeight} px");
            });
            vm.Dialog?.CancelCommand.Execute(null);
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- glisser-déposer d'une touche (souris ou doigt) ----------

    [Fact]
    public void DraggingATileMovesSwapsOrStoresItAndIsOneUndoStep()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            var un = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un");
            var trois = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Trois");
            vm.MoveTile(un, trois);                                            // hors édition : sans effet
            Assert.Equal("Un", Doc.Get(Root, 0)!.Label);

            EnterEdit(vm);
            un = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un");
            trois = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Trois");
            vm.MoveTile(un, trois);                                            // case occupée : échange
            Assert.Equal("Trois", Doc.Get(Root, 0)!.Label);
            Assert.Equal("Un", Doc.Get(Root, 2)!.Label);
            Assert.True(Doc.Undo());
            Assert.Equal("Un", Doc.Get(Root, 0)!.Label);

            var free = vm.Tiles.OfType<AddTileViewModel>().First();            // case vide : déplacement
            vm.MoveTile(vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un"), free);
            Assert.Null(Doc.Get(Root, 0));
            Assert.Contains(Doc.Config.Profiles[0].Pages[0].Buttons.Where(b => !b.Blank), b => b.Label == "Un");
            Assert.Equal(3, Doc.Config.Profiles[0].Pages[0].Buttons.Count(b => !b.Blank));   // rien perdu ni dupliqué
        });
    }

    [Fact]
    public void ADragDimsTheSourceLightsTheTargetAndAlwaysEndsClean()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            var un = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un");
            var trois = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Trois");
            var free = vm.Tiles.OfType<AddTileViewModel>().First();

            vm.BeginDrag(un);
            Assert.True(vm.IsDragging);
            Assert.True(un.IsDragSource);
            Assert.Equal("Un", vm.DragFace!.Label);
            vm.UpdateDrag(100, 50, trois);
            Assert.True(trois.IsDropTarget);
            Assert.Equal("Échanger", vm.DragHint);
            Assert.Equal((114, 64), (vm.DragX, vm.DragY));                    // l'étiquette suit le pointeur
            vm.UpdateDrag(120, 60, free);
            Assert.False(trois.IsDropTarget);                                 // une seule case allumée à la fois
            Assert.True(free.IsDropTarget);
            Assert.Equal("Déplacer ici", vm.DragHint);
            vm.UpdateDrag(120, 60, un);                                       // sur elle-même : rien à déposer
            Assert.False(un.IsDropTarget);
            Assert.Equal("Déposer sur une case", vm.DragHint);

            vm.EndDrag();
            Assert.False(vm.IsDragging);
            Assert.False(un.IsDragSource);
            Assert.False(free.IsDropTarget);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task QuitRevertsTheEditSessionAfterConfirmationWhileFinishKeepsIt()
    {
        var (vm, _, _) = NewDeck();
        _host.Run(() =>
        {
            EnterEdit(vm);
            Doc.ClearButton(Root, 0);                                          // une modification pendant l'édition
        });
        var quit = _host.Run(() => vm.QuitEditCommand.ExecuteAsync(null));
        _host.Run(() =>
        {
            Assert.True(vm.HasDialog);                                         // quelque chose à perdre : confirmation
            vm.Dialog!.CancelCommand.Execute(null);
        });
        await quit;
        _host.Run(() =>
        {
            Assert.True(vm.IsEdit);                                            // refusé : on reste en édition, rien n'est annulé
            Assert.Null(Doc.Get(Root, 0));
        });

        quit = _host.Run(() => vm.QuitEditCommand.ExecuteAsync(null));
        _host.Run(() => vm.Dialog!.ConfirmCommand.Execute(null));
        await quit;
        _host.Run(() =>
        {
            Assert.False(vm.IsEdit);
            Assert.Equal("Un", Doc.Get(Root, 0)!.Label);                       // état d'avant l'édition
            Assert.True(Doc.Undo());                                           // et le retour en arrière lui-même est annulable
            Assert.Null(Doc.Get(Root, 0));
        });

        _host.Run(() =>
        {
            EnterEdit(vm);
            Doc.ClearButton(Root, 1);
            vm.EndEditCommand.Execute(null);                                   // Terminer garde les modifications
            Assert.Null(Doc.Get(Root, 1));
        });

        _host.Run(() => EnterEdit(vm));
        await _host.Run(() => vm.QuitEditCommand.ExecuteAsync(null));         // rien à perdre : sortie directe, sans question
        _host.Run(() =>
        {
            Assert.False(vm.IsEdit);
            Assert.False(vm.HasDialog);
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

    [Fact]
    public void EnteringOrLeavingEditForgetsTheFingerThatOpenedIt()
    {
        // Un appui long ouvre l'édition : le relâchement du doigt est traité par WPF et n'arrive jamais au deck. Un doigt resté « posé »
        // ferait ignorer tous les appuis suivants une fois le deck de retour.
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win._tap.Down(7, 100, 100, 0));
            Assert.True(_host.Run(() => win.IsTouchTracking));
            _host.Run(() => win.SetEditing(true));
            Assert.False(_host.Run(() => win.IsTouchTracking));

            _host.Run(() => win._tap.Down(8, 100, 100, 0));
            _host.Run(() => win.SetEditing(false));
            Assert.False(_host.Run(() => win.IsTouchTracking));
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
                var catalog = win.DeckCatalog.TransformToVisual((System.Windows.Media.Visual)content).Transform(new Point(0, 0));
                var inspector = win.DeckProps.TransformToVisual((System.Windows.Media.Visual)content).Transform(new Point(0, 0));
                var grid = win.DeckGrid.TransformToVisual((System.Windows.Media.Visual)content).Transform(new Point(0, 0));
                Assert.True(catalog.X > win.DeckGrid.ActualWidth / 2, $"le catalogue est à droite de la grille : catalogue {catalog}, grille {grid} {win.DeckGrid.ActualWidth}×{win.DeckGrid.ActualHeight}");
                Assert.True(inspector.Y > grid.Y + win.DeckGrid.ActualHeight - 1, "l'inspecteur est sous la grille");
                Assert.True(win.DeckProps.ActualWidth > win.DeckCatalog.ActualWidth, "l'inspecteur est large, sous la grille");
                Assert.Contains(WpfHost.Descendants<Border>(content), b => b.DataContext is DeckTileViewModel { IsSelected: true });
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
