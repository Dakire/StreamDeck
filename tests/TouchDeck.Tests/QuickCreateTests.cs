using System.IO;
using System.Linq;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Création rapide : ajout en une seule écriture annulable, modèles, classement des suggestions et éléments déposés.</summary>
public class QuickCreateTests
{
    static string[] Labels(DeckDocument d) => d.Container(Samples.Root())!.Select(b => b.Blank ? "·" : b.Label).ToArray();

    // ---------- DeckDocument ----------

    [Fact]
    public void AddingAButtonIsASingleUndoableStep()
    {
        var d = Samples.Doc("a");
        var r = d.AddButton(Samples.Root(), 3, Samples.Btn("neuf"));
        Assert.True(r.Ok);
        Assert.Equal(3, r.Index);
        Assert.Equal(new[] { "a", "·", "·", "neuf" }, Labels(d));

        Assert.True(d.Undo());
        Assert.Equal(new[] { "a" }, Labels(d));
        Assert.False(d.CanUndo);
        Assert.True(d.Redo());
        Assert.Equal(new[] { "a", "·", "·", "neuf" }, Labels(d));
    }

    [Fact]
    public void AddingToAnOccupiedSlotIsRefusedAndWritesNothing()
    {
        var d = Samples.Doc("a", "b");
        var before = d.UndoLabel;
        var r = d.AddButton(Samples.Root(), 1, Samples.Btn("neuf"));
        Assert.False(r.Ok);
        Assert.Equal("La case n'est pas vide", r.Message);
        Assert.Equal(new[] { "a", "b" }, Labels(d));
        Assert.Equal(before, d.UndoLabel);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void AddingToAMissingPlaceOrWithInvalidInputFails()
    {
        var d = Samples.Doc("a");
        Assert.False(d.AddButton(new Place("Inconnu", 0), 0, Samples.Btn("x")).Ok);
        Assert.False(d.AddButton(Samples.Root(), -1, Samples.Btn("x")).Ok);
        Assert.False(d.AddButton(Samples.Root(), 5, new DeckButton { Blank = true }).Ok);
        Assert.False(d.AddButtons(Samples.Root(), 5, System.Array.Empty<DeckButton>()).Ok);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void FirstFreeSkipsOccupiedSlotsAndUsesGaps()
    {
        var d = Samples.Doc("a", "b", "c");
        d.ClearButton(Samples.Root(), 1);
        d.Undo();                       // état de départ : a b c
        d.ClearButton(Samples.Root(), 1);
        var r = d.AddButtonFirstFree(Samples.Root(), Samples.Btn("neuf"));
        Assert.Equal(1, r.Index);
        Assert.Equal(new[] { "a", "neuf", "c" }, Labels(d));
        var r2 = d.AddButtonFirstFree(Samples.Root(), Samples.Btn("suite"));
        Assert.Equal(3, r2.Index);      // page « pleine » : la fin de la liste (sous-page sur le deck)
    }

    [Fact]
    public void SeveralButtonsAreOneUndoStep()
    {
        var d = Samples.Doc("a");
        d.ClearButton(Samples.Root(), 0);
        d.Undo();
        var r = d.AddButtons(Samples.Root(), 2, new[] { Samples.Btn("x"), Samples.Btn("y"), Samples.Btn("z") }, "Déposer 3 éléments");
        Assert.True(r.Ok);
        Assert.Equal(new[] { "a", "·", "x", "y", "z" }, Labels(d));
        Assert.Equal("Déposer 3 éléments", d.UndoLabel);

        Assert.True(d.Undo());
        Assert.Equal(new[] { "a" }, Labels(d));
        Assert.False(d.CanUndo);        // une seule annulation pour les trois boutons
    }

    [Fact]
    public void ABatchLeavesOccupiedSlotsAlone()
    {
        var d = Samples.Doc("a", "b", "c");
        d.ClearButton(Samples.Root(), 0);
        d.ClearButton(Samples.Root(), 1);          // a b c → · · c
        d.Undo(); d.Undo();
        d.MoveButton(Samples.Root(), 0, Samples.Root(), 3);   // · b c a
        Assert.True(d.AddButtons(Samples.Root(), 0, new[] { Samples.Btn("x"), Samples.Btn("y") }).Ok);
        Assert.Equal(new[] { "x", "b", "c", "a", "y" }, Labels(d));
    }

    [Fact]
    public void AddingToAnotherPageOrFolderWorks()
    {
        var d = Samples.Doc("a");
        d.CreateFolder(Samples.Root(), 1, "Dossier");
        var inFolder = Samples.Root().Enter(1);
        Assert.True(d.AddButton(inFolder, 0, Samples.Btn("dedans")).Ok);
        Assert.Equal("dedans", d.Get(inFolder, 0)!.Label);
        Assert.True(d.AddButtonFirstFree(Samples.Root(1), Samples.Btn("page B")).Ok);
        Assert.Equal("page B", d.Get(Samples.Root(1), 0)!.Label);
    }

    // ---------- modèles et suggestions ----------

    [Fact]
    public void EveryTemplateIsAKnownCatalogAction()
    {
        Assert.NotEmpty(QuickCreate.Templates);
        foreach (var t in QuickCreate.Templates)
        {
            var b = QuickCreate.CreateFromCatalog(t.CatalogId);
            Assert.NotNull(b);
            Assert.True(b!.IsFolder || b.Actions.Count > 0, t.Title);
            Assert.True(IconCatalog.TryResolve(t.Icon, out _), $"icône {t.Icon}");
        }
        Assert.Null(QuickCreate.CreateFromCatalog("n'existe.pas"));
        Assert.Null(QuickCreate.CreateFromCatalog(null));
    }

    [Fact]
    public void TemplatesToCompleteAreFlaggedAndFinishedOnesAreNot()
    {
        var cfg = ConfigStore.DefaultConfig();
        var profile = cfg.Profiles[0];
        Assert.True(QuickCreate.NeedsSetup(QuickCreate.CreateFromCatalog("key.hotkey")!, cfg, profile));   // raccourci vide
        Assert.True(QuickCreate.NeedsSetup(QuickCreate.CreateFromCatalog("web.url")!, cfg, profile));      // « https:// » seul
        Assert.False(QuickCreate.NeedsSetup(QuickCreate.CreateFromCatalog("media.playpause")!, cfg, profile));
        Assert.False(QuickCreate.NeedsSetup(QuickCreate.CreateFromCatalog("audio.mute")!, cfg, profile));
        Assert.False(QuickCreate.NeedsSetup(QuickCreate.CreateFromCatalog("nav.folder")!, cfg, profile));
    }

    [Fact]
    public void ChromeAndVolumeAreSuggestedFirst()
    {
        Assert.Equal("chrome.focus", QuickCreate.Rank("chrome")[0].Id);
        Assert.StartsWith("Volume", QuickCreate.Rank("vol")[0].Name);
        Assert.Equal("audio.mute", QuickCreate.Rank("muet")[0].Id);
    }

    [Fact]
    public void RankingIgnoresCaseAndAccentsAndCapsTheList()
    {
        Assert.Equal(QuickCreate.Rank("CHROME").Select(a => a.Id), QuickCreate.Rank("chrome").Select(a => a.Id));
        Assert.Contains(QuickCreate.Rank("multimedia", 50), a => a.Category == ActionCatalog.Multimedia);
        Assert.True(QuickCreate.Rank("a", 5).Count <= 5);
    }

    [Fact]
    public void EmptyOrUnmatchedQueriesSuggestNothing()
    {
        Assert.Empty(QuickCreate.Rank(null));
        Assert.Empty(QuickCreate.Rank("   "));
        Assert.Empty(QuickCreate.Rank("zzqxj-introuvable"));
    }

    [Fact]
    public void ExactNameBeatsPartialMatches()
    {
        var ranked = QuickCreate.Rank("coller");
        Assert.Equal("key.paste", ranked[0].Id);
    }

    // ---------- éléments déposés ----------

    [Fact]
    public void ADroppedFileBecomesALaunchButtonTitledByItsName()
    {
        using var dir = new TempConfigDir();
        var exe = Path.Combine(dir.Path, "Mon Outil.exe");
        File.WriteAllText(exe, "x");
        Assert.True(DroppedItem.TryFromPath(exe, out var b, out _));
        Assert.Equal("Mon Outil", b.Label);
        Assert.Equal(ActionTypes.Launch, b.Actions.Single().Type);
        Assert.Equal(exe, b.Actions.Single().Value);
        Assert.False(QuickCreate.NeedsSetup(b, ConfigStore.DefaultConfig(), null));
    }

    [Fact]
    public void ADroppedShortcutKeepsItsPathAndAFolderGetsAFolderIcon()
    {
        using var dir = new TempConfigDir();
        var lnk = Path.Combine(dir.Path, "Chrome.lnk");
        File.WriteAllText(lnk, "x");
        var sub = Path.Combine(dir.Path, "Projets");
        Directory.CreateDirectory(sub);

        Assert.True(DroppedItem.TryFromPath("\"" + lnk + "\"", out var l, out _));   // guillemets du presse-papiers
        Assert.Equal("Chrome", l.Label);
        Assert.Equal(lnk, l.Actions[0].Value);

        Assert.True(DroppedItem.TryFromPath(sub + Path.DirectorySeparatorChar, out var f, out _));
        Assert.Equal("Projets", f.Label);
        Assert.Equal("Folder24", f.Icon);
        Assert.Equal(ActionTypes.Launch, f.Actions[0].Type);
    }

    [Fact]
    public void AnInternetShortcutFileOpensItsAddress()
    {
        using var dir = new TempConfigDir();
        var url = Path.Combine(dir.Path, "Mon wiki.url");
        File.WriteAllText(url, "[InternetShortcut]\r\nURL=https://wiki.exemple.fr/accueil\r\n");
        Assert.True(DroppedItem.TryFromPath(url, out var b, out _));
        Assert.Equal("Mon wiki", b.Label);
        Assert.Equal(ActionTypes.Url, b.Actions[0].Type);
        Assert.Equal("https://wiki.exemple.fr/accueil", b.Actions[0].Value);
    }

    [Theory]
    [InlineData("https://www.exemple.fr/page", "exemple.fr", "https://www.exemple.fr/page")]
    [InlineData("  http://localhost:8080  ", "localhost", "http://localhost:8080/")]
    [InlineData("www.exemple.fr", "exemple.fr", "https://www.exemple.fr/")]
    public void ADroppedAddressBecomesAnOpenButton(string text, string title, string value)
    {
        Assert.True(DroppedItem.TryFromUrl(text, out var b, out _));
        Assert.Equal(title, b.Label);
        Assert.Equal(ActionTypes.Url, b.Actions[0].Type);
        Assert.Equal(value, b.Actions[0].Value);
        Assert.Equal("Globe24", b.Icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas une adresse")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("ftp://exemple.fr")]
    [InlineData("https://")]
    public void InvalidAddressesAreRefusedWithAHelpfulMessage(string? text)
    {
        Assert.False(DroppedItem.TryFromUrl(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relatif\\fichier.exe")]
    [InlineData("C:\\n'existe\\pas\\du\\tout.exe")]
    [InlineData("C:\\dossier\0nul")]
    public void InvalidPathsAreRefused(string? path)
    {
        Assert.False(DroppedItem.TryFromPath(path, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DroppedTextIsRoutedToAnAddressOrAPath()
    {
        using var dir = new TempConfigDir();
        var file = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(ActionTypes.Url, DroppedItem.TryFromText("https://exemple.fr", out var u, out _) ? u.Actions[0].Type : "");
        Assert.Equal(ActionTypes.Launch, DroppedItem.TryFromText(file, out var l, out _) ? l.Actions[0].Type : "");
        Assert.False(DroppedItem.TryFromText("du texte quelconque", out _, out _));
    }

    [Fact]
    public void ALongFileNameIsShortenedForTheTile()
    {
        using var dir = new TempConfigDir();
        var file = Path.Combine(dir.Path, new string('x', 80) + ".exe");
        File.WriteAllText(file, "x");
        Assert.True(DroppedItem.TryFromPath(file, out var b, out _));
        Assert.True(b.Label.Length <= 40);
        Assert.EndsWith("…", b.Label);
    }
}
