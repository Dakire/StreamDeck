using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Menu des cases du deck : création en un clic, modèles, duplication, annulation, case occupée et affichage tactile.</summary>
[Collection("ui")]
public class DeckMenuTests
{
    readonly WpfHost _host;
    public DeckMenuTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    /// <summary>Profil « P » en 3×2 : page A avec 5 boutons (la case 5 est libre), page B vide.</summary>
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
                    new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux"), Btn("Trois"), Btn("Quatre"), Btn("Cinq") } },
                    new DeckPage { Name = "B" },
                },
            },
        },
    };

    (MainViewModel Vm, FakeAppWindows Windows) NewDeck(DeckConfig? cfg = null)
    {
        _host.Reset(cfg ?? Config());
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, windows);
    }

    static string[] Texts(MainViewModel vm) => vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text).ToArray();
    static void Pick(MainViewModel vm, string text) => QuickEditTests.Pick(vm, text);
    static string Header(MainViewModel vm) => vm.MenuEntries.OfType<MenuHeaderViewModel>().First().Text;
    static void OpenFreeSlotMenu(MainViewModel vm) => vm.Tiles.OfType<SpacerTileViewModel>().First().Edit!.Execute(null);
    static void OpenMenuOf(MainViewModel vm, string label) => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == label).Edit!.Execute(null);

    // ---------- contenu des menus ----------

    [Fact]
    public void AnEmptySlotMenuOffersNewButtonAndTemplates()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Assert.True(vm.MenuOpen);
            Assert.Equal("Case vide", Header(vm));
            var texts = Texts(vm);
            Assert.Equal("Nouveau bouton…", texts[0]);
            Assert.Equal("Parcourir les actions", texts[1]);
            foreach (var t in QuickCreate.Templates) Assert.Contains(t.Title, texts);
            Assert.DoesNotContain("Éditeur complet…", texts);                    // plus de fenêtre : l'édition se fait dans le deck
        });
    }

    [Fact]
    public void PasteIsOfferedOnlyWithSomethingInTheClipboard()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            Doc.Copy(Root, 0);
            OpenFreeSlotMenu(vm);
            Assert.Contains("Coller", Texts(vm));
            Pick(vm, "Coller");
            Assert.Equal("Un", Doc.Get(Root, 5)!.Label);
            Assert.Equal("Coller le bouton", Doc.UndoLabel);
        });
    }

    [Fact]
    public void AButtonMenuOffersEditDuplicateCopyCutAndDelete()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenMenuOf(vm, "Trois");
            Assert.Equal("Trois", Header(vm));
            Assert.Equal(new[] { "Modifier…", "Dupliquer", "Copier", "Couper", "Supprimer" }, Texts(vm));
            Assert.True(vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Supprimer").IsDanger);
        });
    }

    // ---------- création : une écriture, un pas d'annulation ----------

    [Fact]
    public void ATemplateCreatesTheButtonInOneUndoableStepWithATouchableUndo()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Média : lecture / pause");
            Assert.False(vm.MenuOpen);
            var created = Doc.Get(Root, 5)!;
            Assert.Equal("Lecture / Pause", created.Label);
            Assert.Equal(ActionTypes.Media, created.Actions.Single().Type);
            Assert.Equal("Ajouter « Lecture / Pause »", Doc.UndoLabel);
            Assert.Empty(windows.Opened);                                   // aucune fenêtre : rien à compléter
            Assert.True(vm.HasStatus);
            Assert.True(vm.HasStatusAction);
            Assert.Equal("Annuler", vm.StatusActionText);
            Assert.Contains("media.playpause", Doc.Config.Recents);

            vm.RunStatusActionCommand.Execute(null);                        // le bouton « Annuler » du bandeau
            Assert.Null(Doc.Get(Root, 5));
            Assert.False(Doc.CanUndo);
            Assert.False(vm.HasStatusAction);
        });
    }

    [Fact]
    public void NewButtonEntersEditModeOnThatSlotWithoutWriting()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Nouveau bouton…");
            Assert.False(vm.MenuOpen);
            Assert.True(vm.IsEdit);                                          // panneaux d'actions et de propriétés autour du deck
            Assert.Equal((Root, 5), (vm.Editor!.Place, vm.Editor.SelectedIndex));
            Assert.Empty(windows.Opened);                                    // aucune fenêtre
            Assert.False(Doc.CanUndo);
        });
    }

    [Fact]
    public void ATemplateThatNeedsSettingsOpensTheDeckPropertiesPanelOnTheNewButton()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Raccourci clavier");
            Assert.Empty(windows.Opened);                                    // aucune fenêtre séparée
            Assert.True(vm.IsEdit);
            Assert.Equal((Root, 5), (vm.Editor!.Place, vm.Editor.SelectedIndex));
            Assert.Equal("Raccourci clavier", Doc.Get(Root, 5)!.Label);      // posé par le mode édition, à compléter dans le panneau de droite
            Assert.Equal("Ajouter « Raccourci clavier »", Doc.UndoLabel);

            vm.Editor.CancelButtonCommand.Execute(null);                     // « Annuler » du panneau : la case redevient vide
            Assert.Null(Doc.Get(Root, 5));
            Assert.Equal("Annuler la création", Doc.UndoLabel);                // l'annulation est elle-même annulable
        });
    }

    [Fact]
    public void BrowsingTheCatalogByCategoryCreatesWithOneTouchEach()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Parcourir les actions");
            Assert.Equal("Parcourir les actions", Header(vm));
            Assert.Contains("Retour", Texts(vm));
            Assert.Contains(ActionCatalog.Multimedia, Texts(vm));

            Pick(vm, ActionCatalog.Multimedia);
            Assert.Equal(ActionCatalog.Multimedia, Header(vm));
            Pick(vm, "Retour");                                              // revient aux catégories
            Assert.Equal("Parcourir les actions", Header(vm));

            Pick(vm, ActionCatalog.Multimedia);
            Pick(vm, "Stop");
            Assert.Equal("Stop", Doc.Get(Root, 5)!.Label);
            Assert.False(vm.MenuOpen);
        });
    }

    [Fact]
    public void ClosingTheMenuForgetsTheOpenedCategory()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Parcourir les actions");
            vm.CloseMenuCommand.Execute(null);
            vm.OpenMenuCommand.Execute(null);                                // menu général
            Assert.Contains("Paramètres", Texts(vm));
            Assert.DoesNotContain("Retour", Texts(vm));
        });
    }

    [Fact]
    public void AnOccupiedSlotRefusesTheCreationAndWritesNothing()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);                                            // menu bâti quand la case 5 était libre
            Doc.AddButton(Root, 5, Btn("Arrivé entre-temps"));
            var undoSteps = Doc.UndoLabel;
            Pick(vm, "Média : suivant");
            Assert.Equal("Arrivé entre-temps", Doc.Get(Root, 5)!.Label);
            Assert.Equal(undoSteps, Doc.UndoLabel);
            Assert.True(vm.HasStatus);
            Assert.True(vm.StatusIsError);
            Assert.Equal("La case n'est pas vide", vm.Status);
        });
    }

    // ---------- duplication, copie, coupe, suppression ----------

    [Fact]
    public void DuplicateGoesToTheNextFreeSlotInOneUndoableStep()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenMenuOf(vm, "Deux");
            Pick(vm, "Dupliquer");
            Assert.Equal("Deux", Doc.Get(Root, 5)!.Label);
            Assert.Equal("Dupliquer le bouton", Doc.UndoLabel);
            Assert.True(vm.HasStatusAction);
            vm.RunStatusActionCommand.Execute(null);
            Assert.Null(Doc.Get(Root, 5));
            Assert.Equal("Deux", Doc.Get(Root, 1)!.Label);                   // l'original reste
        });
    }

    [Fact]
    public void DuplicatingOnAFullPageContinuesOnTheNextSubPage()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenMenuOf(vm, "Un"); Pick(vm, "Dupliquer");                     // remplit la 6e case : la grille 3×2 est pleine
            OpenMenuOf(vm, "Un"); Pick(vm, "Dupliquer");                     // 7e bouton : sous-page 2
            Assert.Equal("Un", Doc.Get(Root, 6)!.Label);
            Assert.True(vm.HasSubs);
            Assert.Equal("1 / 2", vm.SubText);
        });
    }

    [Fact]
    public void CopyThenPasteCutAndDeleteAreUndoable()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            OpenMenuOf(vm, "Cinq"); Pick(vm, "Copier");
            Assert.Equal("Cinq", Doc.Clipboard!.Label);
            Assert.False(Doc.CanUndo);                                       // copier ne modifie pas le document

            OpenMenuOf(vm, "Quatre"); Pick(vm, "Couper");
            Assert.Null(Doc.Get(Root, 3));
            Assert.True(Doc.Undo());
            Assert.Equal("Quatre", Doc.Get(Root, 3)!.Label);

            OpenMenuOf(vm, "Trois"); Pick(vm, "Supprimer");
            Assert.Null(Doc.Get(Root, 2));
            Assert.Equal("Supprimer le bouton", Doc.UndoLabel);
            vm.RunStatusActionCommand.Execute(null);
            Assert.Equal("Trois", Doc.Get(Root, 2)!.Label);
        });
    }

    [Fact]
    public void DeletingAFolderRemovesItsContentAndUndoBringsItBack()
    {
        var cfg = Config();
        var folder = DeckDocument.NewFolder("Outils");
        folder.Children!.Add(Btn("Marteau"));
        cfg.Profiles[0].Pages[0].Buttons[4] = folder;
        var (vm, _) = NewDeck(cfg);
        _host.Run(() =>
        {
            OpenMenuOf(vm, "Outils");
            Assert.Contains("Supprimer le dossier", Texts(vm));
            Pick(vm, "Supprimer le dossier");
            Assert.Null(Doc.Get(Root, 4));
            Assert.True(Doc.Undo());
            Assert.Equal("Marteau", Doc.Get(Root, 4)!.Children![0].Label);
        });
    }

    // ---------- éditeur : le mode « Modifier le deck » garde son comportement ----------

    [Fact]
    public void ATapOnTheAddTileInEditModeOpensTheCreationMenu()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            vm.OpenMenuCommand.Execute(null);
            Pick(vm, "Modifier le deck");
            var add = vm.Tiles.OfType<AddTileViewModel>().First();
            add.Click.Execute(null);                                                  // un toucher sélectionne la case : propriétés à droite
            Assert.False(vm.MenuOpen);
            Assert.Equal(5, vm.Editor!.SelectedIndex);
            add.Edit!.Execute(null);                                                  // un appui long ouvre le menu de création
            Assert.Equal("Case vide", Header(vm));
            Assert.Contains("Ajouter une action", Texts(vm));                         // en édition : le menu complet de l'éditeur
        });
    }

    // ---------- annuler / rétablir, exécution, focus ----------

    [Fact]
    public void EveryKindOfCreationUndoesAndRedoesFromTheEditorButtonsAndShortcuts()
    {
        var (vm, _) = NewDeck();
        var editor = _host.Run(() => _host.Create<EditorViewModel>(new FakeShell()));
        _host.Run(() =>
        {
            OpenFreeSlotMenu(vm);
            Pick(vm, "Média : suivant");                                     // modèle du menu
            Assert.True(Doc.AddButton(Root, 6, ActionCatalog.Find("media.playpause")!.CreateButton(), "Ajouter « Lecture / pause »").Ok);   // seconde création
            Assert.NotNull(Doc.Get(Root, 5));
            Assert.NotNull(Doc.Get(Root, 6));

            Assert.True(editor.CanUndo);
            editor.UndoCommand.Execute(null);                                // le bouton « Annuler » de l'éditeur (Ctrl+Z)
            Assert.Null(Doc.Get(Root, 6));
            editor.UndoCommand.Execute(null);
            Assert.Null(Doc.Get(Root, 5));
            Assert.True(editor.CanRedo);
            editor.RedoCommand.Execute(null);                                // Ctrl+Y
            editor.RedoCommand.Execute(null);
            Assert.NotNull(Doc.Get(Root, 5));
            Assert.NotNull(Doc.Get(Root, 6));
            Assert.False(editor.CanRedo);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task ACreatedButtonRunsLikeAnyOtherOnTheSimulatedPlatform()
    {
        var (vm, _) = NewDeck();
        _host.Run(() => { OpenFreeSlotMenu(vm); Pick(vm, "Parcourir les actions"); Pick(vm, ActionCatalog.Apps); Pick(vm, "Bloc-notes"); });
        var tile = _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Bloc-notes"));
        await _host.Run(() => vm.RunTileAsync(tile));                        // plateforme simulée : rien n'est lancé sur le bureau
        _host.Settle();
        _host.Run(() =>
        {
            Assert.Equal(TileRunState.Idle, tile.RunState);
            Assert.Contains("notepad.exe", vm.Status);
            Assert.False(vm.StatusIsError);
        });
    }

    [Fact]
    public void TheDeckStillNeverTakesTheFocus()
    {
        var t = typeof(MainWindow);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        Assert.Equal(0x08000000, (int)t.GetField("WS_EX_NOACTIVATE", flags)!.GetRawConstantValue()!);
        Assert.Equal(0x0021, (int)t.GetField("WM_MOUSEACTIVATE", flags)!.GetRawConstantValue()!);
        Assert.Equal(3, (int)t.GetField("MA_NOACTIVATE", flags)!.GetRawConstantValue()!);
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try { Assert.False(_host.Run(() => win.ShowActivated)); }
        finally { _host.Run(win.Close); }
    }

    // ---------- affichage : cibles tactiles et liaisons ----------

    [Fact]
    public void TheMenuSheetRendersWithTouchSizedRowsAndNoBindingError()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-menu-case-vide", 1400, 800);
            var main = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() => main.Tiles.OfType<SpacerTileViewModel>().First().Edit!.Execute(null));
            _host.Render(win, "deck-menu-case-vide", 1400, 800);
            _host.Run(() =>
            {
                var rows = WpfHost.Descendants<Button>((DependencyObject)win.Content)
                    .Where(b => b.DataContext is MenuEntryViewModel && b.ActualHeight > 0).ToList();   // IsVisible est faux hors écran
                Assert.NotEmpty(rows);
                Assert.All(rows, b => Assert.True(b.ActualHeight >= 48, $"« {((MenuEntryViewModel)b.DataContext).Text} » : {b.ActualHeight} px"));
                Assert.Contains(WpfHost.Descendants<TextBlock>((DependencyObject)win.Content), t => t.DataContext is MenuHeaderViewModel);
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void TheUndoButtonOfTheStatusBarIsTouchSizedAndFindable()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-annuler", 1400, 800);
            var main = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() =>
            {
                main.Tiles.OfType<SpacerTileViewModel>().First().Edit!.Execute(null);
                Pick(main, "Média : suivant");
            });
            _host.Render(win, "deck-annuler", 1400, 800);
            _host.Run(() =>
            {
                var undo = WpfHost.Descendants<Button>((DependencyObject)win.Content).Single(b => Equals(b.Content, "Annuler") && b.DataContext is MainViewModel);
                Assert.Equal(Visibility.Visible, undo.Visibility);
                Assert.True(undo.ActualHeight >= 48, $"{undo.ActualHeight} px");
                Assert.Equal("Annuler", System.Windows.Automation.AutomationProperties.GetName(undo));
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
