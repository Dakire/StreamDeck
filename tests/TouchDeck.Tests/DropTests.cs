using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Glisser-déposer sur le deck : lecture de l'objet déposé, création en une écriture, refus d'une case occupée.</summary>
[Collection("ui")]
public class DropTests : IDisposable
{
    readonly WpfHost _host;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "touchdeck-drop-" + Guid.NewGuid().ToString("N"));
    static readonly Place Root = new("P", 0);

    public DropTests(WpfHost host)
    {
        _host = host;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    DeckDocument Doc => _host.Document;

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark",
        Profiles = { new DeckProfile { Name = "P", Columns = 3, Rows = 2, Pages = { new DeckPage { Name = "A", Buttons = { Btn("Un"), Btn("Deux") } } } } },
    };

    string File_(string name, string content = "x")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    static DataObject Files(params string[] paths) => new(DataFormats.FileDrop, paths);

    DropPayload.DropResult Read(DataObject data) => _host.Run(() => DropPayload.Read(data));

    // ---------- lecture de l'objet déposé ----------

    [Fact]
    public void ADroppedExecutableBecomesALaunchButtonWithItsOwnIcon()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var result = Read(Files(notepad));
        var b = Assert.Single(result.Buttons);
        Assert.Empty(result.Problems);
        Assert.Equal("notepad", b.Label);
        Assert.Equal(ActionTypes.Launch, b.Actions.Single().Type);
        Assert.Equal(notepad, b.Actions.Single().Value);
        Assert.True(IconStore.IsValidName(b.ImagePath), "l'icône de l'application est enregistrée sous un nom de hachage");
        Assert.True(File.Exists(IconStore.Resolve(b.ImagePath)));
        Assert.Equal("contain", b.ImageFit);
    }

    [Fact]
    public void AShortcutAFolderAndAnInternetShortcutEachBecomeAButton()
    {
        var lnk = File_("Chrome.lnk");
        var folder = Path.Combine(_dir, "Projets");
        Directory.CreateDirectory(folder);
        var url = File_("Wiki.url", "[InternetShortcut]\r\nURL=https://wiki.exemple.fr\r\n");

        var result = Read(Files(lnk, folder, url));
        Assert.Empty(result.Problems);
        Assert.Equal(new[] { "Chrome", "Projets", "Wiki" }, result.Buttons.Select(b => b.Label));
        Assert.Equal(new[] { ActionTypes.Launch, ActionTypes.Launch, ActionTypes.Url }, result.Buttons.Select(b => b.Actions[0].Type));
        Assert.Null(result.Buttons[1].ImagePath);                              // un dossier garde son icône Fluent
        Assert.Equal("Folder24", result.Buttons[1].Icon);
    }

    [Fact]
    public void AnAddressDraggedFromABrowserOpensTheSite()
    {
        var data = new DataObject();
        data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://www.exemple.fr/page\0")));
        data.SetData(DataFormats.UnicodeText, "Titre de la page");               // le navigateur fournit aussi le titre : l'adresse l'emporte
        var b = Assert.Single(Read(data).Buttons);
        Assert.Equal(ActionTypes.Url, b.Actions[0].Type);
        Assert.Equal("https://www.exemple.fr/page", b.Actions[0].Value);
        Assert.Equal("exemple.fr", b.Label);
    }

    [Fact]
    public void DroppedTextIsReadAsAnAddressOrAPathUsingItsFirstLine()
    {
        var b = Assert.Single(Read(new DataObject(DataFormats.UnicodeText, "\r\n  https://exemple.fr  \r\nautre ligne")).Buttons);
        Assert.Equal("https://exemple.fr/", b.Actions[0].Value);
        var f = Assert.Single(Read(new DataObject(DataFormats.UnicodeText, File_("a.txt"))).Buttons);
        Assert.Equal(ActionTypes.Launch, f.Actions[0].Type);
    }

    [Fact]
    public void InvalidItemsAreReportedAndTheValidOnesStillCreated()
    {
        var result = Read(Files(Path.Combine(_dir, "disparu.exe"), File_("ok.lnk")));
        Assert.Equal("ok", Assert.Single(result.Buttons).Label);
        var problem = Assert.Single(result.Problems);
        Assert.StartsWith("disparu.exe", problem, StringComparison.Ordinal);

        var none = Read(new DataObject(DataFormats.UnicodeText, "du texte quelconque"));
        Assert.Empty(none.Buttons);
        Assert.NotEmpty(none.Problems);
        Assert.NotEmpty(Read(new DataObject()).Problems);                       // objet vide
    }

    [Fact]
    public void ADropIsCappedSoAWholeFolderCannotFloodThePage()
    {
        var paths = Enumerable.Range(0, DropPayload.MaxItems + 5).Select(i => File_($"f{i:00}.lnk")).ToArray();
        var result = Read(Files(paths));
        Assert.Equal(DropPayload.MaxItems, result.Buttons.Count);
        Assert.Contains(result.Problems, p => p.Contains("premiers", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyFilesAndTextAreAccepted()
    {
        _host.Run(() =>
        {
            Assert.True(DropPayload.CanAccept(Files("C:\\x")));
            Assert.True(DropPayload.CanAccept(new DataObject(DataFormats.UnicodeText, "x")));
            Assert.False(DropPayload.CanAccept(new DataObject("autre-format", "x")));
        });
    }

    // ---------- création sur la grille du deck ----------

    (MainViewModel Vm, FakeAppWindows Windows) NewDeck()
    {
        _host.Reset(Config());
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, windows);
    }

    static SpacerTileViewModel FreeTile(MainViewModel vm, int n = 0) => vm.Tiles.OfType<SpacerTileViewModel>().ElementAt(n);

    [Fact]
    public void DroppingOnAnEmptySlotCreatesTheButtonInOneUndoableStep()
    {
        var (vm, _) = NewDeck();
        var lnk = File_("Outil.lnk");
        _host.Run(() =>
        {
            var tile = FreeTile(vm);
            Assert.True(vm.IsFreeSlot(tile));
            Assert.True(vm.TryGetSlot(tile, out var place, out var index));
            Assert.Equal((Root, 2), (place, index));

            var dropped = DropPayload.Read(Files(lnk));
            vm.DropOnTile(tile, dropped.Buttons, dropped.Problems);

            Assert.Equal("Outil", Doc.Get(Root, 2)!.Label);
            Assert.Equal("Déposer « Outil »", Doc.UndoLabel);
            Assert.True(vm.HasStatusAction);
            Assert.Contains("Outil", vm.Status);
            vm.RunStatusActionCommand.Execute(null);                              // « Annuler » du bandeau
            Assert.Null(Doc.Get(Root, 2));
            Assert.False(Doc.CanUndo);
        });
    }

    [Fact]
    public void SeveralDroppedItemsFillTheFollowingFreeSlotsInOneUndoStep()
    {
        var (vm, _) = NewDeck();
        var files = new[] { File_("a.lnk"), File_("b.lnk"), File_("c.lnk") };
        _host.Run(() =>
        {
            var dropped = DropPayload.Read(Files(files));
            vm.DropOnTile(FreeTile(vm), dropped.Buttons, dropped.Problems);
            Assert.Equal(new[] { "Un", "Deux", "a", "b", "c" }, Doc.Container(Root)!.Select(b => b.Label));
            Assert.Equal("Déposer 3 éléments", Doc.UndoLabel);
            Assert.True(Doc.Undo());
            Assert.False(Doc.CanUndo);                                            // une seule annulation pour tout le dépôt
            Assert.Equal(2, Doc.Container(Root)!.Count);
        });
    }

    [Fact]
    public void DroppingOnAnOccupiedSlotOrTheBackArrowChangesNothing()
    {
        var (vm, _) = NewDeck();
        var lnk = File_("Outil.lnk");
        _host.Run(() =>
        {
            var dropped = DropPayload.Read(Files(lnk));
            var occupied = vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Un");
            Assert.False(vm.IsFreeSlot(occupied));
            vm.DropOnTile(occupied, dropped.Buttons, dropped.Problems);
            Assert.Equal("Un", Doc.Get(Root, 0)!.Label);
            Assert.True(vm.StatusIsError);
            Assert.Contains("déjà prise", vm.Status);
            Assert.False(Doc.CanUndo);

            Assert.False(vm.IsFreeSlot(new object()));                            // une tuile inconnue n'est pas une case
            vm.DropOnTile(new object(), dropped.Buttons, dropped.Problems);
            Assert.False(Doc.CanUndo);
        });
    }

    [Fact]
    public void ADropWithNothingUsableExplainsWhyAndWritesNothing()
    {
        var (vm, _) = NewDeck();
        _host.Run(() =>
        {
            var dropped = DropPayload.Read(new DataObject(DataFormats.UnicodeText, "pas une adresse"));
            vm.DropOnTile(FreeTile(vm), dropped.Buttons, dropped.Problems);
            Assert.True(vm.StatusIsError);
            Assert.Contains("adresse web", vm.Status);
            Assert.False(Doc.CanUndo);
        });
    }

    [Fact]
    public void ARefusedItemIsMentionedNextToTheOnesThatWereAdded()
    {
        var (vm, _) = NewDeck();
        var ok = File_("ok.lnk");
        var missing = Path.Combine(_dir, "disparu.exe");
        _host.Run(() =>
        {
            var dropped = DropPayload.Read(Files(ok, missing));
            vm.DropOnTile(FreeTile(vm), dropped.Buttons, dropped.Problems);
            Assert.Equal("ok", Doc.Get(Root, 2)!.Label);
            Assert.Contains("1 ignoré", vm.Status);
            Assert.Contains("disparu.exe", vm.Status);
            Assert.False(vm.StatusIsError);
        });
    }

    // ---------- fenêtre ----------

    [Fact]
    public void TheDeckGridAcceptsDropsAndOnlyFreeTilesQualify()
    {
        _host.Reset(Config());
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-depot", 1400, 800);
            _host.Run(() =>
            {
                Assert.True(win.DeckGrid.AllowDrop);
                var vm = (MainViewModel)win.DataContext;
                var content = (DependencyObject)win.Content;
                var spacer = WpfHost.Descendants<Border>(content).First(b => b.DataContext is SpacerTileViewModel);
                Assert.True(vm.IsFreeSlot(MainWindow.TileAt(spacer)!));
                var button = WpfHost.Descendants<Button>(content).First(b => b.DataContext is DeckTileViewModel { Label: "Un" });
                Assert.False(vm.IsFreeSlot(MainWindow.TileAt(button)!));
                Assert.Null(MainWindow.TileAt(WpfHost.Descendants<Button>(content).First(b => b.DataContext is MainViewModel)));   // le dock n'est pas une case
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
