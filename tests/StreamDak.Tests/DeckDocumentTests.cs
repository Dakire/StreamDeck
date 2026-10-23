using System.Linq;
using Xunit;

namespace StreamDak.Tests;

public class DeckDocumentTests
{
    static string[] Labels(DeckDocument d, Place? p = null) =>
        d.Container(p ?? Samples.Root())!.Select(b => b.Blank ? "·" : b.Label).ToArray();

    [Fact]
    public void MoveToEmptySlotLeavesGapAndKeepsOtherPositions()
    {
        var d = Samples.Doc("a", "b", "c");
        var r = d.MoveButton(Samples.Root(), 0, Samples.Root(), 5);
        Assert.True(r.Ok);
        Assert.Equal(new[] { "·", "b", "c", "·", "·", "a" }, Labels(d));
    }

    [Fact]
    public void MoveOntoOccupiedSlotSwaps()
    {
        var d = Samples.Doc("a", "b", "c");
        Assert.True(d.MoveButton(Samples.Root(), 0, Samples.Root(), 2).Ok);
        Assert.Equal(new[] { "c", "b", "a" }, Labels(d));
    }

    [Fact]
    public void MovingToSameSlotIsRefusedWithoutHistoryEntry()
    {
        var d = Samples.Doc("a", "b");
        Assert.False(d.MoveButton(Samples.Root(), 1, Samples.Root(), 1).Ok);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void TrailingBlanksAreTrimmedAfterClear()
    {
        var d = Samples.Doc("a", "b", "c");
        d.ClearButton(Samples.Root(), 2);
        d.ClearButton(Samples.Root(), 1);
        Assert.Equal(new[] { "a" }, Labels(d));
    }

    [Fact]
    public void CopyPasteMakesIndependentCopies()
    {
        var d = Samples.Doc("a");
        Assert.True(d.Copy(Samples.Root(), 0));
        Assert.True(d.Paste(Samples.Root(), 3).Ok);
        d.UpdateButton(Samples.Root(), 3, b => b.Label = "copie", "x");
        Assert.Equal("a", d.Get(Samples.Root(), 0)!.Label);
        Assert.Equal("a", d.Clipboard!.Label);
        Assert.Equal(new[] { "a", "·", "·", "copie" }, Labels(d));
    }

    [Fact]
    public void CutRemovesAndPasteRestores()
    {
        var d = Samples.Doc("a", "b");
        Assert.True(d.Cut(Samples.Root(), 0).Ok);
        Assert.Null(d.Get(Samples.Root(), 0));
        Assert.True(d.Paste(Samples.Root(), 0).Ok);
        Assert.Equal(new[] { "a", "b" }, Labels(d));
    }

    [Fact]
    public void PasteWithEmptyClipboardFails()
    {
        var d = Samples.Doc("a");
        Assert.False(d.Paste(Samples.Root(), 1).Ok);
        Assert.False(d.HasClipboard);
    }

    [Fact]
    public void DuplicateGoesToNextEmptySlot()
    {
        var d = Samples.Doc("a", "b", "c");
        d.ClearButton(Samples.Root(), 1);
        var r = d.Duplicate(Samples.Root(), 0);
        Assert.Equal(1, r.Index);
        Assert.Equal(new[] { "a", "a", "c" }, Labels(d));
        var r2 = d.Duplicate(Samples.Root(), 0);
        Assert.Equal(3, r2.Index);
    }

    [Fact]
    public void FolderCreationMoveInAndOut()
    {
        var d = Samples.Doc("a", "b");
        Assert.True(d.CreateFolder(Samples.Root(), 2, "Jeux").Ok);
        Assert.False(d.CreateFolder(Samples.Root(), 0).Ok);            // case occupée

        var r = d.MoveIntoFolder(Samples.Root(), 0, Samples.Root(), 2);
        Assert.True(r.Ok);
        var inside = Samples.Root().Enter(2);
        Assert.Equal(new[] { "a" }, Labels(d, inside));
        Assert.Equal(new[] { "·", "b", "Jeux" }, Labels(d));

        var back = d.MoveToParent(inside, 0);
        Assert.True(back.Ok);
        Assert.Equal(new[] { "a", "b", "Jeux" }, Labels(d));
        Assert.Empty(Labels(d, inside));
    }

    [Fact]
    public void ButtonsCanBeDraggedBetweenFolderAndPage()
    {
        var d = Samples.Doc("a");
        d.CreateFolder(Samples.Root(), 1);
        var inside = Samples.Root().Enter(1);
        d.PutButton(inside, 0, Samples.Btn("x"));
        Assert.True(d.MoveButton(inside, 0, Samples.Root(), 0).Ok);   // échange avec « a »
        Assert.Equal("x", d.Get(Samples.Root(), 0)!.Label);
        Assert.Equal("a", d.Get(inside, 0)!.Label);
    }

    [Fact]
    public void FolderCannotBeMovedIntoItselfOrItsDescendants()
    {
        var d = Samples.Doc();
        d.CreateFolder(Samples.Root(), 0, "F");
        d.CreateFolder(Samples.Root().Enter(0), 0, "G");
        var g = Samples.Root().Enter(0);

        Assert.False(d.MoveIntoFolder(Samples.Root(), 0, Samples.Root(), 0).Ok);
        Assert.False(d.MoveButton(Samples.Root(), 0, g, 3).Ok);        // F dans F/G
        Assert.False(d.MoveIntoFolder(Samples.Root(), 0, g, 0).Ok);    // F dans G
        Assert.True(d.Get(Samples.Root(), 0)!.IsFolder);
    }

    [Fact]
    public void SwapCannotPutAFolderInsideItself()
    {
        var d = Samples.Doc();
        d.CreateFolder(Samples.Root(), 0, "F");
        d.PutButton(Samples.Root().Enter(0), 0, Samples.Btn("x"));
        // F échangé avec x : F se retrouverait dans F
        Assert.False(d.MoveButton(Samples.Root(), 0, Samples.Root().Enter(0), 0).Ok);
        Assert.Equal("x", d.Get(Samples.Root().Enter(0), 0)!.Label);
    }

    [Fact]
    public void UndoRedoRestoreEveryStep()
    {
        var d = Samples.Doc("a", "b");
        d.MoveButton(Samples.Root(), 0, Samples.Root(), 1);
        d.ClearButton(Samples.Root(), 0);
        Assert.Equal(new[] { "·", "a" }, Labels(d));

        Assert.True(d.Undo());
        Assert.Equal(new[] { "b", "a" }, Labels(d));
        Assert.True(d.Undo());
        Assert.Equal(new[] { "a", "b" }, Labels(d));
        Assert.False(d.Undo());
        Assert.True(d.Redo());
        Assert.True(d.Redo());
        Assert.Equal(new[] { "·", "a" }, Labels(d));
        Assert.False(d.CanRedo);
    }

    [Fact]
    public void NewEditAfterUndoClearsRedo()
    {
        var d = Samples.Doc("a", "b");
        d.ClearButton(Samples.Root(), 0);
        d.Undo();
        Assert.True(d.CanRedo);
        d.ClearButton(Samples.Root(), 1);
        Assert.False(d.CanRedo);
    }

    [Fact]
    public void TypingInTheSameFieldIsOneUndoStep()
    {
        var d = Samples.Doc("a");
        foreach (var t in new[] { "ab", "abc", "abcd" })
            d.UpdateButton(Samples.Root(), 0, b => b.Label = t, "Titre", "title");
        Assert.Equal("abcd", d.Get(Samples.Root(), 0)!.Label);
        d.Undo();
        Assert.Equal("a", d.Get(Samples.Root(), 0)!.Label);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void UndoRaisesReplacedAndContentEditsRaiseContent()
    {
        var d = Samples.Doc("a");
        var kinds = new System.Collections.Generic.List<ChangeKind>();
        d.Changed += c => kinds.Add(c.Kind);
        d.ClearButton(Samples.Root(), 0);
        d.Undo();
        d.ToggleFavorite("x");
        Assert.Equal(new[] { ChangeKind.Content, ChangeKind.Replaced, ChangeKind.Settings }, kinds);
    }

    [Fact]
    public void FavoritesAndRecentsAreNotUndoable()
    {
        var d = Samples.Doc("a");
        d.ToggleFavorite("sys.desktop");
        Assert.Contains("sys.desktop", d.Config.Favorites);
        d.ToggleFavorite("sys.desktop");
        Assert.Empty(d.Config.Favorites);
        for (int i = 0; i < 30; i++) d.AddRecent("id" + i);
        Assert.Equal(ConfigNormalizer.MaxRecents, d.Config.Recents.Count);
        Assert.Equal("id29", d.Config.Recents[0]);
        d.AddRecent("id20");
        Assert.Equal("id20", d.Config.Recents[0]);
        Assert.Equal(ConfigNormalizer.MaxRecents, d.Config.Recents.Distinct().Count());
        Assert.False(d.CanUndo);
    }

    // ---------- pages ----------

    [Fact]
    public void RenamingAPageUpdatesPageActionsEverywhereInTheProfile()
    {
        var d = Samples.Doc();
        d.PutButton(Samples.Root(1), 0, Samples.Btn("go", "page", "A"));
        d.CreateFolder(Samples.Root(1), 1);
        d.PutButton(Samples.Root(1).Enter(1), 0, Samples.Btn("deep", "page", "a"));
        Assert.True(d.RenamePage("P", 0, "Accueil").Ok);
        Assert.Equal("Accueil", d.Get(Samples.Root(1), 0)!.Actions[0].Value);
        Assert.Equal("Accueil", d.Get(Samples.Root(1).Enter(1), 0)!.Actions[0].Value);
    }

    [Fact]
    public void PageNamesStayUniqueAndNonEmpty()
    {
        var d = Samples.Doc();
        Assert.False(d.RenamePage("P", 0, "b").Ok);
        Assert.False(d.RenamePage("P", 0, "  ").Ok);
        Assert.True(d.AddPage("P", "A").Ok);
        Assert.Equal("A (2)", d.Config.Profiles[0].Pages[^1].Name);
    }

    [Fact]
    public void LastPageCannotBeDeletedAndMovingPagesWorks()
    {
        var d = Samples.Doc();
        Assert.True(d.MovePage("P", 0, 1).Ok);
        Assert.Equal(new[] { "B", "A" }, d.Config.Profiles[0].Pages.Select(p => p.Name));
        Assert.False(d.MovePage("P", 1, 1).Ok);
        Assert.True(d.DeletePage("P", 0).Ok);
        Assert.False(d.DeletePage("P", 0).Ok);
    }

    [Fact]
    public void DuplicatingAPageCopiesButtonsWithUniqueName()
    {
        var d = Samples.Doc("a");
        var r = d.DuplicatePage("P", 0);
        Assert.Equal(1, r.Index);
        Assert.Equal("A (copie)", d.Config.Profiles[0].Pages[1].Name);
        Assert.Equal("a", d.Get(Samples.Root(1), 0)!.Label);
        d.ClearButton(Samples.Root(1), 0);
        Assert.NotNull(d.Get(Samples.Root(0), 0));
    }

    // ---------- profils ----------

    [Fact]
    public void ProfilesAreCreatedDuplicatedAndNamesStayUnique()
    {
        var d = Samples.Doc("a");
        Assert.True(d.AddProfile("P").Ok);
        Assert.Equal("P (2)", d.Config.Profiles[1].Name);
        Assert.True(d.DuplicateProfile("P").Ok);
        Assert.Equal("P (copie)", d.Config.Profiles[1].Name);
        Assert.Equal("a", d.Get(new Place("P (copie)", 0), 0)!.Label);
    }

    [Fact]
    public void RenamingAProfileUpdatesProfileActionsAndActiveProfile()
    {
        var d = Samples.Doc();
        d.AddProfile("Jeu");
        d.PutButton(Samples.Root(), 0, Samples.Btn("vers jeu", "profile", "Jeu"));
        Assert.True(d.RenameProfile("Jeu", "Gaming").Ok);
        Assert.Equal("Gaming", d.Get(Samples.Root(), 0)!.Actions[0].Value);
        d.SetActiveProfile("Gaming");
        Assert.True(d.RenameProfile("Gaming", "Jeux").Ok);
        Assert.Equal("Jeux", d.Config.ActiveProfile);
        Assert.False(d.RenameProfile("Jeux", "p").Ok);                 // déjà pris (insensible à la casse)
    }

    [Fact]
    public void DeletingActiveProfileFallsBackAndLastProfileIsProtected()
    {
        var d = Samples.Doc();
        d.AddProfile("Jeu");
        d.SetActiveProfile("Jeu");
        Assert.True(d.DeleteProfile("Jeu").Ok);
        Assert.Equal("P", d.Config.ActiveProfile);
        Assert.False(d.DeleteProfile("P").Ok);
        Assert.True(d.Undo());
        Assert.Equal(2, d.Config.Profiles.Count);
    }

    [Fact]
    public void AppAssociationIsUniquePerProgram()
    {
        var d = Samples.Doc();
        d.AddProfile("Jeu");
        Assert.True(d.SetProfileApp("P", "chrome.exe").Ok);
        Assert.Equal("chrome", d.FindProfile("P")!.AppMatch);
        Assert.False(d.SetProfileApp("Jeu", "CHROME").Ok);
        Assert.True(d.SetProfileApp("P", " ").Ok);
        Assert.Null(d.FindProfile("P")!.AppMatch);
    }

    [Fact]
    public void ProfileLayoutIsClamped()
    {
        var d = Samples.Doc();
        d.SetProfileLayout("P", 99, 3);
        Assert.Equal(20, d.FindProfile("P")!.Columns);
        Assert.Equal(3, d.FindProfile("P")!.Rows);
    }

    [Fact]
    public void ReplaceAllIsUndoable()
    {
        var d = Samples.Doc("a");
        var other = ConfigStore.DefaultConfig();
        d.ReplaceAll(other, "Importer");
        Assert.Equal("Principal", d.Config.Profiles[0].Name);
        d.Undo();
        Assert.Equal("P", d.Config.Profiles[0].Name);
    }

    [Fact]
    public void ResetClearsHistory()
    {
        var d = Samples.Doc("a");
        d.ClearButton(Samples.Root(), 0);
        d.Reset(ConfigStore.DefaultConfig());
        Assert.False(d.CanUndo);
        Assert.False(d.CanRedo);
    }

    [Fact]
    public void ContainerReturnsNullForStalePaths()
    {
        var d = Samples.Doc("a");
        Assert.Null(d.Container(Samples.Root().Enter(0)));      // « a » n'est pas un dossier
        Assert.Null(d.Container(new Place("P", 9)));
        Assert.Null(d.Container(new Place("zzz", 0)));
        Assert.False(d.PutButton(new Place("zzz", 0), 0, Samples.Btn("x")).Ok);
    }
}
