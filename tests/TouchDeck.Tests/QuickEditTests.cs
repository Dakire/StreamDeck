using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TouchDeck.Tests;

/// <summary>Gestionnaire de fenêtres factice : note les entrées en édition demandées hors du deck (API locale, icône de la zone de notification).</summary>
sealed class FakeAppWindows : IAppWindows
{
    public List<(Place? Place, int Index)> Opened { get; } = new();
    public void OpenEditor(Place? place = null, int index = -1) => Opened.Add((place, index));
    public void OpenSettings() { }
}

/// <summary>Appui long, seuil de déplacement et reconnaissance du doigt suivi.</summary>
public class LongPressTests
{
    [Fact]
    public void AFingerHeldStillBecomesALongPressOnceAndIsNotATap()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100, nowMs: 1000);
        Assert.False(t.TryLongPress(1000 + TapRecognizer.LongPressMs - 1));   // pas encore
        Assert.True(t.TryLongPress(1000 + TapRecognizer.LongPressMs));
        Assert.False(t.TryLongPress(1000 + TapRecognizer.LongPressMs + 50));  // une seule fois par appui
        Assert.False(t.Up(1, 100, 100));                                       // le relâchement n'exécute pas l'action
        Assert.False(t.Tracking);
    }

    [Fact]
    public void ABriefTouchStaysATap()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100, nowMs: 1000);
        Assert.False(t.TryLongPress(1200));
        Assert.True(t.Up(1, 102, 99));
    }

    [Fact]
    public void ADraggingFingerCancelsTheLongPress()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100, nowMs: 1000);
        Assert.True(t.Move(1, 100, 140));                                      // défilement
        Assert.False(t.TryLongPress(5000));
        Assert.False(t.Up(1, 100, 140));
    }

    [Fact]
    public void ASecondFingerNeitherStartsNorStealsTheLongPress()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100, nowMs: 1000);
        Assert.True(t.Tracking);
        t.Down(2, 400, 400, nowMs: 1400);                                      // ignoré : le premier doigt reste suivi
        Assert.True(t.TryLongPress(1000 + TapRecognizer.LongPressMs));
        Assert.False(t.Up(2, 400, 400));
    }

    [Fact]
    public void ANewTouchAfterALongPressIsATapAgain()
    {
        var t = new TapRecognizer { Slop = 10 };
        t.Down(1, 100, 100, nowMs: 0);
        Assert.True(t.TryLongPress(TapRecognizer.LongPressMs));
        t.Up(1, 100, 100);
        t.Down(1, 100, 100, nowMs: 5000);
        Assert.True(t.Up(1, 100, 100));
    }
}

/// <summary>Édition directe depuis le deck (clic droit, appui long), éditeur rapide et saisie fluide.</summary>
[Collection("ui")]
public class QuickEditTests
{
    readonly WpfHost _host;
    readonly ITestOutputHelper _out;
    public QuickEditTests(WpfHost host, ITestOutputHelper output) { _host = host; _out = output; }

    DeckDocument Doc => _host.Document;

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    /// <summary>Profil « P » en 3×2 : page A (5 boutons dont un dossier, une case libre), page B (2 boutons).</summary>
    static DeckConfig DeckConfigWithTwoPages()
    {
        var folder = DeckDocument.NewFolder("Dossier");
        folder.Children!.Add(Btn("Dedans"));
        return new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
            Profiles =
            {
                new DeckProfile
                {
                    Name = "P", Columns = 3, Rows = 2,
                    Pages =
                    {
                        new DeckPage { Name = "A", Buttons = { Btn("Copier"), folder, Btn("Coller"), Btn("Couper"), Btn("Annuler") } },
                        new DeckPage { Name = "B", Buttons = { Btn("Accueil"), Btn("Autre") } },
                    },
                },
            },
        };
    }

    (MainViewModel Vm, FakeAppWindows Windows) NewDeck()
    {
        _host.Reset(DeckConfigWithTwoPages());
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, windows);
    }

    /// <summary>Touche une entrée de la feuille de menu du deck.</summary>
    internal static void Pick(MainViewModel vm, string text) =>
        vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == text).Command.Execute(null);

    EditorViewModel NewEditor(FakeShell? shell = null) => _host.Run(() => _host.Create<EditorViewModel>(shell ?? new FakeShell()));

    // ---------- deck : le clic droit et l'appui long visent la bonne case ----------

    [Fact]
    public void EveryButtonAndEmptySlotOfTheDeckCanBeEditedDirectly()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            var copy = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Copier");
            Assert.NotNull(copy.Edit);
            copy.Edit!.Execute(null);
            Assert.True(vm.MenuOpen);
            Assert.Empty(windows.Opened);                                      // le geste ouvre un menu, pas l'éditeur
            Pick(vm, "Modifier…");
            Assert.Empty(windows.Opened);                                      // aucune autre fenêtre : le deck passe en mode édition
            Assert.True(vm.IsEdit);
            Assert.Equal(0, vm.Editor!.SelectedIndex);
            Assert.False(vm.MenuOpen);
            vm.EndEditCommand.Execute(null);

            // Case vide (6e emplacement, hors édition : un « espaceur »)
            var spacer = vm.Tiles.OfType<SpacerTileViewModel>().Single();
            Assert.NotNull(spacer.Edit);
            spacer.Edit!.Execute(null);
            Assert.Empty(windows.Opened);                                      // toujours aucune fenêtre
            Pick(vm, "Nouveau bouton…");
            Assert.Equal((new Place("P", 0), 5), (vm.Editor!.Place, vm.Editor.SelectedIndex));   // les panneaux s'ouvrent sur cette case
            Assert.Empty(windows.Opened);
        });
    }

    [Fact]
    public void EditTargetsStayCorrectAfterChangingPage()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            vm.Tabs[1].Command.Execute(null);                                   // page B
            var home = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Accueil");
            home.Edit!.Execute(null);
            Pick(vm, "Modifier…");
            Assert.True(vm.IsEdit);
            Assert.Equal((new Place("P", 1), 0), (vm.Editor!.Place, vm.Editor.SelectedIndex));   // le panneau de droite montre ce bouton
            vm.EndEditCommand.Execute(null);
            var spacers = vm.Tiles.OfType<SpacerTileViewModel>().ToList();
            Assert.Equal(4, spacers.Count);
            spacers[0].Edit!.Execute(null);
            Pick(vm, "Nouveau bouton…");
            Assert.Equal((new Place("P", 1), 2), (vm.Editor!.Place, vm.Editor.SelectedIndex));   // première case libre de la page B
        });
    }

    [Fact]
    public void FolderBackArrowIsNotEditableButFolderContentIs()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dossier").Click.Execute(null);   // ouvre le dossier
            var back = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Retour");
            Assert.Null(back.Edit);
            var inside = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Dedans");
            inside.Edit!.Execute(null);
            Pick(vm, "Modifier…");
            Assert.Equal((new Place("P", 0, new[] { 1 }), 0), (vm.Editor!.Place, vm.Editor.SelectedIndex));
            vm.EndEditCommand.Execute(null);
        });
    }

    [Fact]
    public void EditModeStillOpensTheEditorWithATouch()
    {
        var (vm, windows) = NewDeck();
        _host.Run(() =>
        {
            vm.OpenMenuCommand.Execute(null);
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text.StartsWith("Modifier le deck", StringComparison.Ordinal)).Command.Execute(null);
            Assert.True(vm.IsEdit);
            vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Copier").Click.Execute(null);   // sélectionne la touche
            Assert.Empty(windows.Opened);
            Assert.Equal(0, vm.Editor!.SelectedIndex);
            var add = vm.Tiles.OfType<AddTileViewModel>().First();
            add.Click.Execute(null);                                                  // « + » : sélectionne la case vide
            Assert.Equal(5, vm.Editor.SelectedIndex);
            add.Edit!.Execute(null);                                                  // appui long : menu de création de la case
            Assert.True(vm.MenuOpen);
            Assert.Contains("Ajouter une action", vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text));   // menu complet de l'éditeur, dans le deck
            Assert.Empty(windows.Opened);
        });
    }

    [Fact]
    public void WindowResolvesTheEditCommandOfTheTileUnderThePointer()
    {
        _host.Reset(DeckConfigWithTwoPages());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-edition-directe", 1400, 800);
            _host.Run(() =>
            {
                var content = (DependencyObject)win.Content;
                var copy = WpfHost.Descendants<Button>(content).First(b => b.DataContext is DeckTileViewModel { Label: "Copier" });
                var tile = (DeckTileViewModel)copy.DataContext;
                Assert.Same(tile.Edit, MainWindow.EditTargetAt(copy));                  // sur le bouton lui-même
                var inner = WpfHost.Descendants<TouchDeck.Controls.TileFace>(copy).First();
                Assert.Same(tile.Edit, MainWindow.EditTargetAt(inner));                 // sur son contenu

                var spacer = WpfHost.Descendants<Border>(content).First(b => b.DataContext is SpacerTileViewModel);
                Assert.True(spacer.IsHitTestVisible, "une case vide doit recevoir le clic droit et l'appui long");
                Assert.NotNull(MainWindow.EditTargetAt(spacer));

                var dock = WpfHost.Descendants<Button>(content).First(b => b.DataContext is MainViewModel);
                Assert.Null(MainWindow.EditTargetAt(dock));                              // barre, dock et menu ne s'éditent pas
            });
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- éditeur rapide : ouverture directe, enregistrer, annuler ----------

    [Fact]
    public void QuickEditOnAButtonFocusesTheTitleAndSavingClosesTheEditor()
    {
        _host.Reset();
        var shell = new FakeShell();
        var vm = NewEditor(shell);
        _host.Run(() =>
        {
            vm.GoTo(new Place("Principal", 0), 0, quick: true);
            Assert.True(vm.QuickEdit);
            Assert.True(vm.Properties.IsButton);
            Assert.Equal("Lecture / Pause", vm.Properties.Title);                // champs préremplis
            Assert.Contains("focus:title", shell.Calls);
            Assert.DoesNotContain("focus:catalog", shell.Calls);

            vm.Properties.Title = "Musique";
            vm.SaveButtonCommand.Execute(null);                                  // valide, enregistre, ferme
            Assert.Equal("Musique", Doc.Get(vm.Place, 0)!.Label);
            Assert.Equal("close", shell.Calls[^1]);
        });
        Assert.Contains("Musique", File.ReadAllText(ConfigStore.FilePath));      // écrit sur le disque sans attendre la temporisation
    }

    [Fact]
    public void QuickEditOnAnEmptySlotStartsFromTheActionSearchAndCancelRemovesTheNewButton()
    {
        _host.Reset();
        var shell = new FakeShell();
        var vm = NewEditor(shell);
        _host.Run(() =>
        {
            Assert.Null(Doc.Get(new Place("Principal", 0), 12));
            vm.GoTo(new Place("Principal", 0), 12, quick: true);
            Assert.True(vm.Properties.IsEmpty);
            Assert.Contains("focus:catalog", shell.Calls);                       // création : on part de la recherche d'actions

            vm.Catalog.Search = "volume";
            vm.AddFirstCatalogResult();                                          // Entrée dans la recherche
            Assert.NotNull(Doc.Get(vm.Place, 12));
            Assert.Equal("", vm.Catalog.Search);

            vm.CancelButtonCommand.Execute(null);
            Assert.Null(Doc.Get(vm.Place, 12));                                  // la case redevient vide
            Assert.Equal("close", shell.Calls[^1]);
        });
    }

    [Fact]
    public void CancelRestoresTheButtonAsItWasWhenTheEditorOpened()
    {
        _host.Reset();
        var shell = new FakeShell();
        var vm = NewEditor(shell);
        _host.Run(() =>
        {
            vm.GoTo(new Place("Principal", 0), 0, quick: true);
            var before = ConfigStore.Serialize(Doc.Config);
            vm.Properties.Title = "Brouillon";
            vm.Properties.Flush();
            vm.Properties.Color = "#123456";
            vm.Properties.PositionChoices[1].Command.Execute(null);
            vm.Properties.Title = "Encore en cours de frappe";                  // jamais validé
            Assert.NotEqual(before, ConfigStore.Serialize(Doc.Config));

            vm.CancelButtonCommand.Execute(null);
            Assert.Equal(before, ConfigStore.Serialize(Doc.Config));              // aucune modification conservée
            Assert.False(vm.Properties.HasDraft);
            Assert.Equal("close", shell.Calls[^1]);
        });
    }

    [Fact]
    public void OutsideQuickEditSavingAndCancellingKeepTheEditorOpen()
    {
        _host.Reset();
        var shell = new FakeShell();
        var vm = NewEditor(shell);
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            Assert.False(vm.QuickEdit);
            vm.Properties.Title = "A";
            vm.SaveButtonCommand.Execute(null);
            vm.Properties.Title = "B";
            vm.CancelButtonCommand.Execute(null);
            Assert.DoesNotContain("close", shell.Calls);
            Assert.Equal("A", Doc.Get(vm.Place, 0)!.Label);                      // « Annuler » ne défait que depuis l'ouverture de la case
        });
    }

    [Fact]
    public void PickingAnotherSlotLeavesQuickMode()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.GoTo(new Place("Principal", 0), 0, quick: true);
            Assert.True(vm.QuickEdit);
            vm.SelectedSlot = vm.Slots.First(s => s.Index == 1);
            Assert.False(vm.QuickEdit);
        });
    }

    // ---------- saisie : aucun travail global pendant la frappe ----------

    [Fact]
    public void TypingATitleOnlyUpdatesThePreviewUntilValidated()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            int changes = 0;
            Doc.Changed += _ => changes++;
            const string name = "Mon nouveau bouton";
            for (int i = 1; i <= name.Length; i++)
            {
                vm.Properties.Title = name[..i];
                Assert.Equal(name[..i], vm.Properties.Preview.Label);            // aperçu à jour à chaque caractère
            }
            Assert.Equal(0, changes);                                            // ni historique, ni sauvegarde, ni grille, ni deck
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 0)!.Label);
            Assert.True(vm.Properties.HasDraft);

            vm.Properties.Flush();
            Assert.Equal(1, changes);                                            // une seule écriture, à la validation
            Assert.Equal(name, Doc.Get(vm.Place, 0)!.Label);
            Assert.False(vm.Properties.HasDraft);
            vm.Properties.Flush();
            Assert.Equal(1, changes);                                            // rien à revalider

            vm.UndoCommand.Execute(null);
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 0)!.Label);        // un seul « annuler » défait toute la saisie
        });
    }

    [Fact]
    public void TypingDoesNotRebuildTheCatalogProfilesPagesOrGrid()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            int catalog = 0, profiles = 0, pages = 0, slots = 0;
            ((INotifyCollectionChanged)vm.Catalog.Sections).CollectionChanged += (_, _) => catalog++;
            ((INotifyCollectionChanged)vm.Profiles).CollectionChanged += (_, _) => profiles++;
            ((INotifyCollectionChanged)vm.Pages).CollectionChanged += (_, _) => pages++;
            ((INotifyCollectionChanged)vm.Slots).CollectionChanged += (_, _) => slots++;
            var firstSection = vm.Catalog.Sections[0];

            for (int i = 1; i <= 25; i++) vm.Properties.Title = new string('x', i);
            Assert.Equal(0, catalog + profiles + pages + slots);

            // La validation d'un titre ne recalcule pas non plus ce qui n'en dépend pas
            vm.Properties.Flush();
            Assert.Equal(0, catalog);                                            // la liste des actions disponibles n'est jamais régénérée
            Assert.Equal(0, profiles);
            Assert.Equal(0, pages);
            Assert.Same(firstSection, vm.Catalog.Sections[0]);
            Assert.Equal(new string('x', 25), vm.Slots.First(s => s.Index == 0).Face.Label);   // la grille de l'éditeur reflète le nouveau nom
        });
    }

    [Fact]
    public void EscapeDropsTheTypedTextAndKeepsTheDocument()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            int changes = 0;
            Doc.Changed += _ => changes++;
            vm.Properties.Title = "Brouillon";
            Assert.True(vm.Properties.RevertDraft());
            Assert.Equal("Lecture / Pause", vm.Properties.Title);                // champ revenu à la valeur du document
            Assert.Equal("Lecture / Pause", vm.Properties.Preview.Label);
            Assert.False(vm.Properties.RevertDraft());                           // plus rien à abandonner
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void SelectingAnotherSlotValidatesThePendingTextInsteadOfLosingIt()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            vm.Properties.Title = "Conservé";
            vm.SelectIndex(1);
            Assert.Equal("Conservé", Doc.Get(vm.Place, 0)!.Label);
            Assert.False(vm.Properties.HasDraft);
        });
    }

    [Fact]
    public void ExternalReloadDropsTheDraftInsteadOfApplyingItToTheNewDocument()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            vm.Properties.Title = "Périmé";
            Doc.Reset(ConfigStore.DefaultConfig());                              // config.json rechargé depuis le disque
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 0)!.Label);
            Assert.False(vm.Properties.HasDraft);
        });
    }

    [Fact]
    public void StepValuesAreDraftsToo()
    {
        _host.Reset();
        var vm = NewEditor();
        _host.Run(() =>
        {
            vm.SelectIndex(6);                                                   // « Bureau » : raccourci
            string original = Doc.Get(vm.Place, 6)!.Actions[0].Value;
            int changes = 0;
            Doc.Changed += _ => changes++;
            foreach (var typed in new[] { "c", "ct", "ctrl", "ctrl+", "ctrl+alt+d" }) vm.Properties.Steps[0].Value = typed;
            Assert.Equal(0, changes);
            Assert.Equal(original, Doc.Get(vm.Place, 6)!.Actions[0].Value);
            vm.Properties.Flush();
            Assert.Equal(1, changes);
            Assert.Equal("ctrl+alt+d", Doc.Get(vm.Place, 6)!.Actions[0].Value);
        });
    }

    // ---------- document : écriture ciblée ----------

    [Fact]
    public void InPlaceButtonEditsAreFlaggedAndOtherChangesAreNot()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var seen = new List<DocumentChange>();
            Doc.Changed += seen.Add;
            var place = new Place("Principal", 0);
            Doc.UpdateButton(place, 0, b => b.Label = "x", "Titre", "title");
            Doc.AddPage("Principal");
            Doc.UpdateSettings(c => c.Spacing = 13);
            Assert.True(seen[0].InPlace);
            Assert.False(seen[1].InPlace);
            Assert.False(seen[2].InPlace);
        });
    }

    [Fact]
    public void CoalescedEditsUndoInOneStepWithoutSerializingTheWholeConfigEachTime()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var place = new Place("Principal", 0);
            string original = ConfigStore.Serialize(Doc.Config);
            for (int i = 1; i <= 30; i++) Doc.UpdateButton(place, 0, b => b.Label = new string('a', i), "Modifier le titre", "title");
            Assert.Equal(new string('a', 30), Doc.Get(place, 0)!.Label);
            Assert.True(Doc.Undo());
            Assert.Equal(original, ConfigStore.Serialize(Doc.Config));            // l'état d'avant la série est intact
            Assert.True(Doc.Redo());
            Assert.Equal(new string('a', 30), Doc.Get(place, 0)!.Label);
        });
    }

    // ---------- mesure : avant / après sur une grande configuration ----------

    [Fact]
    public void TypingIsMuchCheaperThanWritingEveryKeystrokeToTheDocument()
    {
        var cfg = ConfigStore.DefaultConfig();
        var profile = cfg.Profiles[0];
        profile.Pages.Clear();
        for (int p = 0; p < 8; p++)
        {
            var page = new DeckPage { Name = "Page " + (p + 1) };
            for (int i = 0; i < 25; i++) page.Buttons.Add(Btn($"Touche {p}-{i}"));
            profile.Pages.Add(page);
        }
        _host.Reset(cfg);
        var vm = NewEditor();
        const int keystrokes = 40;
        var place = new Place(profile.Name, 0);

        var (draftMs, directMs) = _host.Run(() =>
        {
            vm.SelectIndex(3);
            var sw = Stopwatch.StartNew();
            for (int i = 1; i <= keystrokes; i++) vm.Properties.Title = new string('n', i);
            vm.Properties.Flush();
            double draft = sw.Elapsed.TotalMilliseconds;

            // Chaque caractère écrit dans le document (chemin allégé : pas de cliché de la configuration, pas de rafraîchissement global)
            sw.Restart();
            for (int i = 1; i <= keystrokes; i++) Doc.UpdateButton(place, 4, b => b.Label = new string('m', i), "Modifier le titre", "title");
            double direct = sw.Elapsed.TotalMilliseconds;
            return (draft, direct);
        });
        _out.WriteLine($"{keystrokes} caractères, 200 boutons : brouillon + 1 validation = {draftMs:0.0} ms ; écriture dans le document à chaque caractère = {directMs:0.0} ms");
        Assert.True(draftMs < directMs, $"{draftMs:0.0} ms contre {directMs:0.0} ms");
        Assert.True(draftMs < 250, $"la saisie devrait rester instantanée : {draftMs:0.0} ms");
    }
}
