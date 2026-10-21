using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.Controls;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Panneau de création rapide : brouillon sans écriture, une seule écriture à « Créer », suggestions assistées.</summary>
[Collection("ui")]
public class QuickCreatePanelTests
{
    readonly WpfHost _host;
    public QuickCreatePanelTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;
    static readonly Place Root = new("P", 0);

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
        Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux") } } } } },
    };

    (QuickCreateViewModel Vm, FakeAppWindows Windows, Counter Closed) NewPanel(int index = 5)
    {
        _host.Reset(Config());
        var windows = new FakeAppWindows();
        var closed = new Counter();
        var vm = _host.Run(() =>
        {
            var v = _host.Create<QuickCreateViewModel>(windows, Root, index);
            v.CloseRequested += () => closed.Count++;
            return v;
        });
        return (vm, windows, closed);
    }

    sealed class Counter { public int Count; }

    // ---------- suggestions assistées ----------

    [Fact]
    public void WithoutTypingTheSuggestionsAreFavoritesRecentsAndTemplates()
    {
        _host.Reset(Config());
        _host.Run(() => Doc.UpdateSettings(c => { c.Favorites = new() { "media.next" }; c.Recents = new() { "audio.mic" }; }));
        var vm = _host.Run(() => _host.Create<QuickCreateViewModel>(new FakeAppWindows(), Root, 5));
        _host.Run(() =>
        {
            Assert.Equal(new[] { "media.next", "audio.mic" }, vm.Suggestions.Take(2).Select(s => s.Action.Id));
            Assert.Equal("media.next", vm.Selected!.Action.Id);              // la première est choisie d'office
            Assert.Equal("Suivant", vm.Title);
        });
    }

    [Fact]
    public void TypingChromeProposesChromeWithItsTitleAndIcon()
    {
        var (vm, _, _) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "chrome";
            Assert.Equal("chrome.focus", vm.Suggestions[0].Action.Id);
            Assert.Equal("chrome.focus", vm.Selected!.Action.Id);
            Assert.Equal("Chrome", vm.Title);
            Assert.Equal("Globe24", vm.Icon);
            Assert.True(vm.CanCreate);
            Assert.True(vm.HasIconPreview);
            Assert.NotEqual(Wpf.Ui.Controls.SymbolRegular.Empty, vm.IconPreview);
        });
    }

    [Fact]
    public void TypingVolProposesVolume()
    {
        var (vm, _, _) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "vol";
            Assert.StartsWith("Volume", vm.Suggestions[0].Name);
            Assert.StartsWith("Volume", vm.Title);
        });
    }

    [Fact]
    public void ArrowKeysMoveTheChoiceAndTheTitleFollows()
    {
        var (vm, _, _) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "volume";
            var first = vm.Title;
            vm.MoveSelection(1);
            Assert.NotEqual(first, vm.Title);
            vm.MoveSelection(-5);
            Assert.Equal(vm.Suggestions[0], vm.Selected);                   // borné au début de la liste
            vm.MoveSelection(100);
            Assert.Equal(vm.Suggestions[^1], vm.Selected);
        });
    }

    [Fact]
    public void ATitleOrIconTypedByHandIsKeptWhenTheChoiceChanges()
    {
        var (vm, _, _) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "chrome";
            vm.Title = "Navigateur";
            vm.Icon = "🔥";
            vm.Query = "volume";
            Assert.Equal("Navigateur", vm.Title);
            Assert.Equal("🔥", vm.Icon);
            Assert.Equal("🔥", vm.IconPreviewText);                          // emoji : aperçu en texte
            Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Empty, vm.IconPreview);

            vm.Title = "";                                                    // titre effacé : redevient automatique
            vm.MoveSelection(1);
            Assert.StartsWith("Volume", vm.Title);
        });
    }

    // ---------- une seule écriture ----------

    [Fact]
    public void TypingAndEditingWriteNothingToTheDocument()
    {
        var (vm, windows, closed) = NewPanel();
        _host.Run(() =>
        {
            var before = ConfigStore.Clone(Doc.Config);
            foreach (var q in new[] { "c", "ch", "chr", "chrome", "vol", "mu" }) vm.Query = q;
            vm.Title = "Autre titre";
            vm.Icon = "Star24";
            vm.MoveSelection(1);
            Assert.False(Doc.CanUndo);
            Assert.Null(Doc.Get(Root, 5));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before, ConfigStore.Options),
                         System.Text.Json.JsonSerializer.Serialize(Doc.Config, ConfigStore.Options));
            Assert.Equal(0, closed.Count);
            Assert.Empty(windows.Opened);
        });
    }

    [Fact]
    public void CreateWritesOnceIsOneUndoStepAndClosesThePanel()
    {
        var (vm, windows, closed) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "muet";
            vm.Title = "  Coupe-son  ";
            vm.CreateCommand.Execute(null);

            var b = Doc.Get(Root, 5)!;
            Assert.Equal("Coupe-son", b.Label);                              // titre nettoyé
            Assert.Equal("SpeakerMute24", b.Icon);
            Assert.Equal(ActionTypes.Media, b.Actions.Single().Type);
            Assert.Equal(1, closed.Count);
            Assert.Empty(windows.Opened);                                    // rien à compléter : pas d'éditeur
            Assert.Contains("audio.mute", Doc.Config.Recents);

            Assert.Equal("Ajouter « Muet »", Doc.UndoLabel);
            Assert.True(Doc.Undo());
            Assert.Null(Doc.Get(Root, 5));
            Assert.False(Doc.CanUndo);                                       // un seul pas d'annulation
            Assert.True(Doc.Redo());
            Assert.Equal("Coupe-son", Doc.Get(Root, 5)!.Label);
        });
    }

    [Fact]
    public void CancelClosesWithoutWriting()
    {
        var (vm, _, closed) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "chrome";
            vm.CancelCommand.Execute(null);
            Assert.Equal(1, closed.Count);
            Assert.False(Doc.CanUndo);
            Assert.Null(Doc.Get(Root, 5));
            Assert.DoesNotContain("chrome.focus", Doc.Config.Recents);
        });
    }

    [Fact]
    public void AnActionThatNeedsSettingsIsCreatedOnceThenOpenedInTheQuickEditor()
    {
        var (vm, windows, closed) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "raccourci clavier";
            Assert.Equal("key.hotkey", vm.Selected!.Action.Id);
            vm.CreateCommand.Execute(null);
            Assert.Equal("Raccourci clavier", Doc.Get(Root, 5)!.Label);
            Assert.Equal((Root, 5, true), windows.Opened.Single());
            Assert.Equal(1, closed.Count);
            Assert.True(Doc.Undo());
            Assert.False(Doc.CanUndo);
        });
    }

    // ---------- entrées invalides ----------

    [Fact]
    public void AnOccupiedSlotIsNeverOverwritten()
    {
        var (vm, _, closed) = NewPanel(index: 1);                            // « Deux » y est déjà
        _host.Run(() =>
        {
            vm.Query = "chrome";
            vm.CreateCommand.Execute(null);
            Assert.Equal("Deux", Doc.Get(Root, 1)!.Label);
            Assert.True(vm.HasMessage);
            Assert.Contains("déjà prise", vm.Message);
            Assert.Equal(0, closed.Count);                                   // le panneau reste ouvert pour qu'on lise le message
            Assert.False(Doc.CanUndo);
        });
    }

    [Fact]
    public void NoMatchMeansNoCreationAndAHelpfulMessage()
    {
        var (vm, _, closed) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "zzqxj-introuvable";
            Assert.Empty(vm.Suggestions);
            Assert.Null(vm.Selected);
            Assert.False(vm.CanCreate);
            Assert.Contains("Aucune action", vm.Message);
            vm.CreateCommand.Execute(null);
            Assert.Contains("Choisis", vm.Message);
            Assert.Equal(0, closed.Count);
            Assert.False(Doc.CanUndo);

            vm.Query = "chrome";                                             // une saisie qui aboutit efface le message
            Assert.False(vm.HasMessage);
        });
    }

    [Fact]
    public void AnEmptyTitleIsRefused()
    {
        var (vm, _, closed) = NewPanel();
        _host.Run(() =>
        {
            vm.Query = "chrome";
            vm.Title = "   ";
            Assert.False(vm.CanCreate);
            vm.CreateCommand.Execute(null);
            Assert.Contains("titre", vm.Message);
            Assert.Equal(0, closed.Count);
            Assert.Null(Doc.Get(Root, 5));
        });
    }

    [Fact]
    public void AnUnknownPlaceFailsCleanly()
    {
        _host.Reset(Config());
        var vm = _host.Run(() => _host.Create<QuickCreateViewModel>(new FakeAppWindows(), new Place("Disparu", 0), 0));
        _host.Run(() =>
        {
            vm.Query = "chrome";
            vm.CreateCommand.Execute(null);
            Assert.True(vm.HasMessage);
            Assert.False(Doc.CanUndo);
        });
    }

    // ---------- affichage ----------

    [Fact]
    public void ThePanelRendersWithTouchSizedControlsAndNoBindingError()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new QuickCreateWindow(_host.Services, Root, 5));
        try
        {
            _host.Run(() => ((QuickCreateViewModel)win.DataContext).Query = "chrome");
            _host.Render(win, "creation-rapide", 540, 640);
            _host.Run(() =>
            {
                var content = (DependencyObject)win.Content;
                var buttons = WpfHost.Descendants<Wpf.Ui.Controls.Button>(content).Where(b => b.Content is "Annuler" or "Créer").ToList();
                Assert.Equal(2, buttons.Count);
                Assert.All(buttons, b => Assert.True(b.ActualHeight >= 48, $"{b.Content} : {b.ActualHeight} px"));
                Assert.True(buttons.Single(b => b.IsDefault).Content is "Créer");
                Assert.True(buttons.Single(b => b.IsCancel).Content is "Annuler");
                var inputs = WpfHost.Descendants<Wpf.Ui.Controls.TextBox>(content).ToList();
                Assert.Equal(3, inputs.Count);                                // recherche, titre, icône : rien d'autre à remplir
                Assert.All(inputs, t => Assert.True(t.ActualHeight >= 48, $"{t.Name} : {t.ActualHeight} px"));
                var list = WpfHost.Descendants<ListBox>(content).Single();
                Assert.True(list.Items.Count > 0);
                Assert.Equal(Visibility.Visible, list.Visibility);
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
