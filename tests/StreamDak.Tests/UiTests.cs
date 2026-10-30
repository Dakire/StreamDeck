using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Remplace les boîtes de dialogue : les réponses sont programmées par le test.</summary>
sealed class FakeShell : IEditorShell
{
    public Queue<string?> Prompts { get; } = new();
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Calls { get; } = new();
    public Task<string?> PromptAsync(string title, string message, string initial, string confirmText = "OK", bool allowEmpty = false)
    { Calls.Add("prompt:" + title); return Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null); }
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    { Calls.Add("confirm:" + title); return Task.FromResult(ConfirmAnswer); }
    public Task InfoAsync(string title, string message) { Calls.Add("info:" + title); return Task.CompletedTask; }
    public string? PickOpenFile(string title, string filter) => null;
    public string? PickSaveFile(string title, string filter, string defaultName) => null;
    public void FocusProperties() => Calls.Add("focus:properties");
    public void FocusTitle() => Calls.Add("focus:title");
    public void FocusIcon() => Calls.Add("focus:icon");
    public void FocusCatalogSearch() => Calls.Add("focus:catalog");
    public void FocusSlot(int listIndex) => Calls.Add("focus:slot" + listIndex);
    public void Close() => Calls.Add("close");
}

[Collection("ui")]
public class UiTests
{
    readonly WpfHost _host;
    public UiTests(WpfHost host) { _host = host; }

    DeckDocument Doc => _host.Document;

    string[] BindingProblems()
    {
        lock (_host.Bindings.Messages) return _host.Bindings.Messages.ToArray();
    }

    EditorViewModel NewVm(FakeShell? shell = null) => _host.Run(() => _host.Create<EditorViewModel>(shell ?? new FakeShell()));

    // ---------- rendu des fenêtres réelles ----------

    [Fact]
    public void TheAddPageButtonStaysVisibleInEditModeEvenWithManyLongPageNames()
    {
        var cfg = ConfigStore.DefaultConfig();
        var profile = cfg.Profiles[0];
        for (int i = 0; i < 8; i++) profile.Pages.Add(new DeckPage { Name = $"Page au nom assez long {i}" });
        _host.Reset(cfg);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(new Place(profile.Name, 0), 0));
            _host.Settle();
            _host.Render(win, "deck-edition-pages-nombreuses", 1600, 900);
            _host.Run(() =>
            {
                var add = WpfHost.Descendants<System.Windows.Controls.Button>((System.Windows.DependencyObject)win.Content)
                    .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Ajouter une page");
                Assert.Equal(System.Windows.Visibility.Visible, add.Visibility);
                Assert.True(add.ActualWidth >= 48, $"bouton de {add.ActualWidth} px de large");
                // Hors de la zone défilante des onglets : jamais poussé hors de l'écran par les pages
                Assert.Null(Ancestor<System.Windows.Controls.ScrollViewer>(add));
                var at = add.TransformToAncestor((System.Windows.Media.Visual)win.Content).Transform(new System.Windows.Point(0, 0));
                Assert.InRange(at.X, 0, 1600 - add.ActualWidth);
                Assert.True(add.ActualHeight >= 48, $"cible tactile de {add.ActualHeight} px");
            });
        }
        finally { _host.Run(win.Close); }
    }

    static T? Ancestor<T>(System.Windows.DependencyObject d) where T : System.Windows.DependencyObject
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void DeckEditPanelsRenderWithoutBindingErrors(string theme)
    {
        _host.Reset();
        _host.SetTheme(theme);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(new Place("Principal", 0), 0));
            _host.Settle();
            var path = _host.Render(win, $"deck-edition-{theme}", 1600, 900);
            Assert.True(new FileInfo(path).Length > 20_000, "image trop petite : fenêtre vide ?");
            Assert.Empty(BindingProblems());
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void SettingsWindowRenders()
    {
        _host.Reset();
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        try
        {
            _host.Settle();
            _host.Render(win, "parametres-dark", 820, 1500);
            Assert.Empty(BindingProblems());
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void MainDeckWindowRendersProfileTilesAndImages()
    {
        var cfg = ConfigStore.DefaultConfig();
        var icon = IconStore.StoreBytes(PackageTests_TinyPng());
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Insert(2, new DeckButton { Blank = true });
        page.Buttons.Add(new DeckButton { Label = "Avec image", ImagePath = icon, Color = "#272B38", Actions = { new DeckAction { Type = "delay", Value = "1" } } });
        page.Buttons.Add(new DeckButton { Label = "Titre haut", TitlePosition = "top", Icon = "Star24", Color = "#99620F", Actions = { new DeckAction { Type = "delay", Value = "1" } } });
        page.Buttons.Add(DeckDocument.NewFolder("Jeux"));
        page.Buttons.Add(new DeckButton { Label = "{time}", Icon = "Clock24", Color = "#334155" });
        page.Buttons.Add(new DeckButton { Label = "Réunion", Icon = "Video24", Color = "#7C3AED",
            Actions = { new DeckAction { Type = "launch", Value = "x" }, new DeckAction { Type = "delay", Value = "1" }, new DeckAction { Type = "hotkey", Value = "ctrl+d" } } });
        page.Buttons.Add(new DeckButton { Label = "Micro", Icon = "Mic24", Color = "#BE123C", Actions = { new DeckAction { Type = "audio", Value = "mic-toggle" } } });
        page.Buttons.Add(new DeckButton { Label = "Vide", Icon = "Star24", Color = "#334155" });
        cfg.Profiles.Add(new DeckProfile { Name = "Jeux" });
        _host.Reset(cfg);
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.SetTheme(theme);
            var win = _host.Run(() => new MainWindow(_host.Services));
            try
            {
                _host.Settle();
                var path = _host.Render(win, $"deck-{theme}", 1600, 900);
                _host.Render(win, $"deck-{theme}-1080p", 1920, 1080);
                _host.Render(win, $"deck-{theme}-portrait", 1080, 1600);
                _host.Render(win, $"deck-{theme}", 1600, 900);
                var tiles = _host.Run(() => WpfHost.Descendants<Button>((DependencyObject)win.Content).Count(b => b.DataContext is DeckTileViewModel));
                Assert.True(tiles >= 10, $"{tiles} boutons");
                // Les touches gardent des proportions raisonnables à toutes les tailles
                var vm = _host.Run(() => (MainViewModel)win.DataContext);
                var l = _host.Run(() => vm.L);
                double cw = l.GridW / l.Columns, ch = l.GridH / l.Rows;
                Assert.InRange(cw / ch, 1 / LayoutMetrics.MaxAspect - 0.01, LayoutMetrics.MaxAspect + 0.01);
                Assert.True(l.BarH >= 48, "dock tactile");
                Assert.Empty(BindingProblems());
            }
            finally { _host.Run(win.Close); }
        }
    }

    [Fact]
    public async Task DeckTilesShowRunStateClockAndTimerWithoutRebuildingTheGrid()
    {
        var cfg = ConfigStore.DefaultConfig();
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons.Clear();
        page.Buttons.Add(new DeckButton { Label = "{time}", Icon = "Clock24" });
        page.Buttons.Add(new DeckButton { Label = "Cassé", Icon = "Keyboard24", Actions = { new DeckAction { Type = "hotkey", Value = "" } } });
        page.Buttons.Add(new DeckButton { Label = "Thé", Icon = "Timer24", Actions = { new DeckAction { Type = "timer", Value = "300" } } });
        page.Buttons.Add(new DeckButton { Label = "Copier", Icon = "Copy24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } });
        _host.Reset(cfg);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Settle();
            _host.Render(win, "deck-etats", 1600, 900);
            var vm = _host.Run(() => (MainViewModel)win.DataContext);
            var tiles = _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().ToList());
            Assert.Equal(4, tiles.Count);

            // Horloge : le titre stocké reste « {time} », l'affichage est l'heure
            var shown = _host.Run(() => tiles[0].Face.Label);
            Assert.Matches(@"^\d\d:\d\d$", shown);
            Assert.Equal("{time}", page.Buttons[0].Label);

            // Échec : la tuile passe en erreur, puis revient au repos ; la grille n'est pas reconstruite
            var failing = _host.Run(() => vm.RunTileAsync(tiles[1]));
            await Task.Delay(200);
            Assert.Equal(TileRunState.Error, _host.Run(() => tiles[1].RunState));
            Assert.Contains("Cassé", _host.Run(() => vm.Status) ?? "");
            _host.Render(win, "deck-erreur", 1600, 900);
            await failing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(TileRunState.Idle, _host.Run(() => tiles[1].RunState));
            Assert.Same(tiles[1], _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().ElementAt(1)));

            // Minuteur : le temps restant remplace le titre ; toucher à nouveau l'arrête
            await _host.Run(() => vm.RunTileAsync(tiles[2])).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Matches(@"^[45]:\d\d$", _host.Run(() => tiles[2].Face.Label));
            await _host.Run(() => vm.RunTileAsync(tiles[2])).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Thé", _host.Run(() => tiles[2].Face.Label));
            Assert.Empty(BindingProblems());
        }
        finally { _host.Run(win.Close); }
    }

    static byte[] PackageTests_TinyPng()
    {
        // Illustration lisible (disque dégradé, coins transparents) : l'ancienne image de test, faite de bandes horizontales
        // (vert = i % 255 sur le tableau d'octets), passait pour un défaut de rendu du bouton « Avec image ».
        const int n = 96;
        var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = (y * n + x) * 4;
                double dx = x - n / 2.0 + 0.5, dy = y - n / 2.0 + 0.5, r = Math.Sqrt(dx * dx + dy * dy) / (n / 2.0);
                bool disc = r < 0.62;
                pixels[i] = (byte)(disc ? 240 : 120 + y);              // B
                pixels[i + 1] = (byte)(disc ? 160 - r * 120 : 60 + x / 2); // G
                pixels[i + 2] = (byte)(disc ? 80 : 40);                // R
                pixels[i + 3] = (byte)(r > 1.35 ? 0 : 255);            // A : coins transparents
            }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, n * 4);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void DeckEditStatesRenderWithoutBindingErrors()
    {
        var cfg = ConfigStore.DefaultConfig();
        var icon = IconStore.StoreBytes(PackageTests_TinyPng());
        var page = cfg.Profiles[0].Pages[0];
        page.Buttons[6].Actions[0].Value = "ctrl+blabla";                  // erreur visible sur la case
        page.Buttons[7].ImagePath = icon;
        page.Buttons[8].TitlePosition = "top";
        page.Buttons[8].Actions.Add(new DeckAction { Type = "delay", Value = "500" });
        page.Buttons[8].Actions.Add(new DeckAction { Type = "url", Value = "https://example.org" });
        var folder = DeckDocument.NewFolder("Jeux");
        folder.Children!.Add(new DeckButton { Label = "Dedans", Icon = "Games24", Color = "#4D7C0F", Actions = { new DeckAction { Type = "launch", Value = @"C:
ulle\part.exe" } } });
        page.Buttons.Add(new DeckButton { Blank = true });
        page.Buttons.Add(folder);
        cfg.Profiles.Add(new DeckProfile { Name = "Navigateur", AppMatch = "chrome", Columns = 3, Rows = 2 });
        _host.Reset(cfg);
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(new Place("Principal", 0), 6));
            _host.Settle();
            _host.Render(win, "edition-deck-erreur", 1600, 900);
            _host.Run(() => win.EditCell(new Place("Principal", 0), 8));
            _host.Settle();
            _host.Render(win, "edition-deck-multi-actions", 1600, 900);
            _host.Run(() => win.EditCell(new Place("Principal", 0), 10));      // case vide
            _host.Settle();
            _host.Render(win, "edition-deck-case-vide", 1600, 900);
            _host.Run(() => win.EditCell(new Place("Principal", 0, new[] { 11 }), 0));   // dans un dossier
            _host.Settle();
            _host.Render(win, "edition-deck-dossier", 1600, 900);
            Assert.Empty(BindingProblems());
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void TheBindingErrorListenerReallyCatchesErrors()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var tb = new TextBlock { DataContext = new object() };
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Inexistant"));
            tb.Measure(new Size(100, 100));
        });
        Assert.NotEmpty(BindingProblems());
        _host.Reset();
    }

    // ---------- scénarios de l'éditeur ----------

    [Fact]
    public void SelectingATileFillsThePropertiesAndEditsAreAppliedAndUndoable()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            Assert.True(vm.Properties.IsButton);
            Assert.Equal("Lecture / Pause", vm.Properties.Title);
            Assert.Single(vm.Properties.Steps);

            vm.Properties.Title = "Lecture";
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 0)!.Label);   // brouillon : le document attend la validation
            vm.Properties.Flush();
            Assert.Equal("Lecture", Doc.Get(vm.Place, 0)!.Label);
            vm.Properties.ShowTitle = false;
            Assert.True(Doc.Get(vm.Place, 0)!.HideTitle);
            vm.Properties.PositionChoices[1].Command.Execute(null);
            Assert.Equal("top", Doc.Get(vm.Place, 0)!.TitlePosition);
            vm.Properties.Color = "#123456";
            vm.Properties.Flush();
            Assert.Equal("#123456", Doc.Get(vm.Place, 0)!.Color);
            vm.Properties.Color = "pas une couleur";
            Assert.NotEmpty(vm.Properties.ColorError);
            vm.Properties.Flush();
            Assert.Equal("#123456", Doc.Get(vm.Place, 0)!.Color);      // la valeur invalide n'est pas appliquée
            vm.Properties.TextColor = "#00FF00";
            vm.Properties.Flush();
            Assert.Equal("#00FF00", Doc.Get(vm.Place, 0)!.TextColor);
            vm.Properties.TextColor = "";
            vm.Properties.Flush();
            Assert.Null(Doc.Get(vm.Place, 0)!.TextColor);

            Assert.True(vm.CanUndo);
            vm.UndoCommand.Execute(null);
            vm.UndoCommand.Execute(null);
        });
        Assert.Empty(BindingProblems());
    }

    [Fact]
    public void StepEditingValidatesLiveAndMultiStepOrderIsKept()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(6);                                       // « Bureau » : raccourci win+d
            var step = vm.Properties.Steps[0];
            step.Value = "ctrl+blabla";
            Assert.True(step.HasIssues);                                              // validation en direct, avant même l'écriture
            Assert.Contains("inconnue", step.Issues[0].Message);
            vm.Properties.Flush();
            Assert.Equal("ctrl+blabla", Doc.Get(vm.Place, 6)!.Actions[0].Value);    // enregistré, mais signalé
            Assert.Equal(Severity.Error, vm.Slots.First(s => s.Index == 6).Issue);   // repère visible sur la case
            step.Value = "ctrl+alt+d";
            Assert.False(step.HasIssues);
            vm.Properties.Flush();

            vm.Properties.AddStepCommand.Execute(null);
            vm.Properties.AddStepCommand.Execute(null);
            Assert.Equal(3, vm.Properties.Steps.Count);
            Assert.False(vm.Properties.Steps[2].IsTypePickerOpen);                     // seul le type choisi est affiché : plus de place pour les réglages
            vm.Properties.Steps[2].ToggleTypePickerCommand.Execute(null);
            Assert.True(vm.Properties.Steps[2].IsTypePickerOpen);                      // « Changer » déplie toutes les pastilles
            vm.Properties.Steps[2].TypeChoices[8].Command.Execute(null);               // Fenêtre → Pause (index 9) : on vérifie surtout l'ordre
            Assert.False(vm.Properties.Steps[2].IsTypePickerOpen);                     // choisir referme le sélecteur
            vm.Properties.Steps[2].MoveUpCommand.Execute(null);
            var types = Doc.Get(vm.Place, 6)!.Actions.Select(a => a.Type).ToArray();
            Assert.Equal(3, types.Length);
            Assert.Equal("focus", types[1]);
            vm.Properties.Steps[0].RemoveCommand.Execute(null);
            Assert.Equal(2, Doc.Get(vm.Place, 6)!.Actions.Count);
        });
    }

    [Fact]
    public void ClipboardShortcutsWorkOnTheSelectedTile()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            vm.CopyCommand.Execute(null);
            vm.SelectIndex(12);                                       // case vide
            vm.PasteCommand.Execute(null);
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 12)!.Label);
            vm.CutCommand.Execute(null);
            Assert.Null(Doc.Get(vm.Place, 12));
            vm.SelectIndex(13);
            vm.PasteCommand.Execute(null);
            vm.DuplicateCommand.Execute(null);
            Assert.NotNull(Doc.Get(vm.Place, 14));
            vm.DeleteSelection();
            Assert.Null(Doc.Get(vm.Place, 14));
            Assert.NotNull(Doc.Get(vm.Place, 13));
        });
    }

    [Fact]
    public async Task DeletingAFolderWithContentAsksForConfirmationAndCanBeUndone()
    {
        _host.Reset();
        var shell = new FakeShell { ConfirmAnswer = false };
        var vm = NewVm(shell);
        _host.Run(() =>
        {
            vm.SelectIndex(12);
            vm.CreateFolderHere();
            Assert.True(Doc.Get(vm.Place, 12)!.IsFolder);
            Doc.PutButton(vm.Place.Enter(12), 0, new DeckButton { Label = "x", Actions = { new DeckAction { Type = "delay", Value = "1" } } });
            vm.DeleteSelection();
        });
        await Task.Delay(100);
        _host.Settle();
        Assert.Contains("confirm:Supprimer le dossier", shell.Calls);
        Assert.NotNull(_host.Run(() => Doc.Get(vm.Place, 12)));        // refusé : toujours là
        shell.ConfirmAnswer = true;
        _host.Run(() => vm.DeleteSelection());
        await Task.Delay(100);
        _host.Settle();
        Assert.Null(_host.Run(() => Doc.Get(vm.Place, 12)));
        _host.Run(() => vm.UndoCommand.Execute(null));
        Assert.True(_host.Run(() => Doc.Get(vm.Place, 12)!.Children!.Count == 1));
    }

    [Fact]
    public void FolderNavigationShowsBackTileAndShiftsSlots()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(12);
            vm.CreateFolderHere();
            vm.OpenSelectedFolder();
            Assert.True(vm.InFolder);
            Assert.Equal(2, vm.Crumbs.Count);
            Assert.True(vm.Slots[0].IsBack);
            Assert.Equal(0, vm.Slots[1].Index);                     // la case 1 est la première du dossier
            Doc.PutButton(vm.Place, 0, new DeckButton { Label = "dedans" });
            Assert.False(vm.Slots[1].IsEmpty);
            vm.GoUp();
            Assert.False(vm.InFolder);
            Assert.Equal(12, vm.SelectedSlot!.Index);               // on retombe sur le dossier
        });
    }

    [Fact]
    public void DropRulesCoverMoveSwapFolderCopyAndInvalidTargets()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(15);
            vm.CreateFolderHere();                                   // dossier en 15
            var place = vm.Place;
            SlotViewModel S(int i) => vm.Slots.First(s => !s.IsBack && s.Index == i);
            var fromSlot0 = new DragPayload(place, 0, null);

            Assert.Equal(DropKind.None, vm.EvaluateDrop(fromSlot0, S(0), false));        // sur soi-même
            Assert.Equal(DropKind.Swap, vm.EvaluateDrop(fromSlot0, S(1), false));         // case occupée
            Assert.Equal(DropKind.None, vm.EvaluateDrop(fromSlot0, S(1), true));          // copier sur une case occupée : refusé
            Assert.Equal(DropKind.Move, vm.EvaluateDrop(fromSlot0, S(12), false));
            Assert.Equal(DropKind.Copy, vm.EvaluateDrop(fromSlot0, S(12), true));
            Assert.Equal(DropKind.IntoFolder, vm.EvaluateDrop(fromSlot0, S(15), false));
            Assert.Equal(DropKind.None, vm.EvaluateDrop(new DragPayload(place, 99, null), S(12), false));   // source disparue

            var cat = new DragPayload(null, -1, "sys.desktop");
            Assert.Equal(DropKind.Move, vm.EvaluateDrop(cat, S(12), false));
            Assert.Equal(DropKind.Replace, vm.EvaluateDrop(cat, S(1), false));
            Assert.Equal(DropKind.IntoFolder, vm.EvaluateDrop(cat, S(15), false));

            vm.Drop(fromSlot0, S(1), false);                                              // échange
            Assert.Equal("Précédent", Doc.Get(place, 0)!.Label);
            Assert.Equal("Lecture / Pause", Doc.Get(place, 1)!.Label);
            vm.Drop(new DragPayload(place, 1, null), S(12), true);                        // copie
            Assert.Equal("Lecture / Pause", Doc.Get(place, 12)!.Label);
            Assert.Equal("Lecture / Pause", Doc.Get(place, 1)!.Label);
            vm.Drop(new DragPayload(place, 12, null), S(15), false);                      // dans le dossier
            Assert.Null(Doc.Get(place, 12));
            Assert.Equal("Lecture / Pause", Doc.Get(place.Enter(15), 0)!.Label);
            vm.Drop(cat, S(13), false);                                                   // depuis le catalogue
            Assert.Equal("Bureau", Doc.Get(place, 13)!.Label);
            Assert.Contains("sys.desktop", Doc.Config.Recents);
            vm.Drop(cat, S(1), false);                                                    // remplace
            Assert.Equal("Bureau", Doc.Get(place, 1)!.Label);
            // chaque dépôt est annulable
            for (int i = 0; i < 6; i++) vm.UndoCommand.Execute(null);
        });
    }

    [Fact]
    public void DroppingOnAPageTabMovesTheButtonThere()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            var tab = vm.Pages[1];
            var payload = new DragPayload(vm.Place, 0, null);
            Assert.True(vm.CanDropOnPage(payload, tab));
            Assert.False(vm.CanDropOnPage(payload, vm.Pages[0]));
            int before = Doc.Config.Profiles[0].Pages[1].Buttons.Count;
            vm.DropOnPage(payload, tab);
            Assert.Equal(before + 1, Doc.Config.Profiles[0].Pages[1].Buttons.Count);
            Assert.Null(Doc.Get(new Place("Principal", 0), 0));
        });
    }

    [Fact]
    public void KeyboardMoveSwapsWithTheNeighbourAndStopsAtTheEdges()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            int cols = vm.Columns;
            vm.MoveSelection(-1, 0);                                  // bord gauche : rien
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 0)!.Label);
            vm.MoveSelection(1, 0);
            Assert.Equal("Précédent", Doc.Get(vm.Place, 0)!.Label);
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 1)!.Label);
            vm.MoveSelection(0, 1);
            Assert.Equal("Lecture / Pause", Doc.Get(vm.Place, 1 + cols)!.Label);
            Assert.Equal(1 + cols, vm.SelectedSlot!.Index);
        });
    }

    [Fact]
    public void ContextMenusAdaptToTheState()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectIndex(0);
            var occupied = Headers(ContextMenuFactory.Build(vm.BuildSlotMenu(vm.Slots[0])));
            foreach (var h in new[] { "Configurer l'action", "Modifier le titre", "Modifier l'icône", "Copier", "Couper", "Coller", "Dupliquer le bouton",
                         "Déplacer le bouton", "Ajouter une action", "Créer un dossier", "Réinitialiser le bouton", "Afficher les propriétés", "Supprimer le bouton" })
                Assert.Contains(h, occupied.Keys);
            Assert.True(occupied["Copier"]);
            Assert.False(occupied["Coller"]);                           // presse-papiers vide
            Assert.False(occupied["Créer un dossier"]);                  // case occupée

            vm.CopyCommand.Execute(null);
            Assert.True(Headers(ContextMenuFactory.Build(vm.BuildSlotMenu(vm.Slots[0])))["Coller"]);

            vm.SelectIndex(12);
            var empty = Headers(ContextMenuFactory.Build(vm.BuildSlotMenu(vm.Slots[12])));
            Assert.False(empty["Copier"]);
            Assert.False(empty["Supprimer le bouton"]);
            Assert.False(empty["Déplacer le bouton"]);
            Assert.True(empty["Créer un dossier"]);
            Assert.True(empty["Ajouter une action"]);
            Assert.True(empty["Coller"]);

            var menu = ContextMenuFactory.Build(vm.BuildSlotMenu(vm.Slots[12]));
            var add = menu.Items.OfType<MenuItem>().First(i => (string)i.Header == "Ajouter une action");
            Assert.Equal(ActionCatalog.Categories.Count, add.Items.Count);
            Assert.All(add.Items.OfType<MenuItem>(), c => Assert.NotEmpty(c.Items));

            var profile = Headers(ContextMenuFactory.Build(vm.BuildProfileMenu("Principal")));
            foreach (var h in new[] { "Renommer", "Dupliquer", "Exporter…", "Importer…", "Supprimer", "Définir comme profil actif" })
                Assert.Contains(h, profile.Keys);
            Assert.False(profile["Supprimer"]);                          // dernier profil
            Assert.False(profile["Définir comme profil actif"]);         // déjà actif

            var cat = vm.Catalog.Sections.SelectMany(s => s.Items).First();
            var catMenu = Headers(ContextMenuFactory.Build(vm.BuildCatalogMenu(cat)));
            foreach (var h in new[] { "Ajouter aux favoris", "Afficher les informations", "Configurer" })
                Assert.Contains(h, catMenu.Keys);
        });
    }

    static Dictionary<string, bool> Headers(ContextMenu m) =>
        m.Items.OfType<MenuItem>().ToDictionary(i => (string)i.Header, i => i.IsEnabled);

    [Fact]
    public async Task ProfileAndPageManagementThroughTheViewModel()
    {
        _host.Reset();
        var shell = new FakeShell();
        var vm = NewVm(shell);
        shell.Prompts.Enqueue("Jeux");
        _host.Run(() => vm.NewProfileCommand.Execute(null));
        await Task.Delay(150); _host.Settle();
        Assert.Equal(new[] { "Principal", "Jeux" }, _host.Run(() => Doc.Config.Profiles.Select(p => p.Name).ToArray()));
        Assert.Equal("Jeux", _host.Run(() => vm.SelectedProfile!.Name));

        _host.Run(() => vm.DuplicateProfile("Jeux"));
        shell.Prompts.Enqueue("Gaming");
        await _host.Run(() => vm.RenameProfileAsync("Jeux (copie)"));
        Assert.Contains("Gaming", _host.Run(() => Doc.Config.Profiles.Select(p => p.Name).ToArray()));

        shell.Prompts.Enqueue("steam.exe");
        await _host.Run(() => vm.SetAppMatchAsync("Gaming"));
        Assert.Equal("steam", _host.Run(() => Doc.FindProfile("Gaming")!.AppMatch));

        _host.Run(() => vm.SetActive("Gaming"));
        Assert.Equal("Gaming", _host.Run(() => Doc.Config.ActiveProfile));
        await _host.Run(() => vm.DeleteProfileAsync("Gaming"));
        Assert.Equal("Principal", _host.Run(() => Doc.Config.ActiveProfile));

        shell.Prompts.Enqueue("Streaming");
        _host.Run(() => vm.AddPageCommand.Execute(null));
        await Task.Delay(150); _host.Settle();
        Assert.Equal("Streaming", _host.Run(() => vm.Pages.Last().Name));
        shell.Prompts.Enqueue("Direct");
        await _host.Run(() => vm.RenamePageAsync(vm.Pages.Last()));
        Assert.Equal("Direct", _host.Run(() => vm.Pages.Last().Name));
        _host.Run(() => vm.TogglePageHidden(vm.Pages.Last()));
        Assert.True(_host.Run(() => Doc.Config.Profiles[0].Pages.Last().Hidden));
    }

    [Fact]
    public void CatalogSearchFavoritesAndRecentsWork()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            Assert.Equal(ActionCatalog.Categories.Count, vm.Catalog.Sections.Count);
            vm.Catalog.Search = "volume";
            Assert.All(vm.Catalog.Sections, s => Assert.True(s.IsExpanded));
            Assert.Contains(vm.Catalog.Sections.SelectMany(s => s.Items), i => i.Action.Id == "audio.up");
            vm.Catalog.Search = "zzzzzz";
            Assert.False(vm.Catalog.HasResults);
            Assert.Contains("Aucune action", vm.Catalog.ResultsText);
            vm.Catalog.Search = "";

            var item = vm.Catalog.FindItem("sys.desktop")!;
            item.ToggleFavoriteCommand.Execute(null);
            Assert.Equal("Favoris", vm.Catalog.Sections[0].Title);
            Assert.Contains("sys.desktop", Doc.Config.Favorites);

            vm.SelectIndex(12);
            vm.AddCatalogAction(ActionCatalog.Find("audio.mute")!);
            Assert.Equal("Muet", Doc.Get(vm.Place, 12)!.Label);
            Assert.Contains(vm.Catalog.Sections, s => s.Title == "Récemment utilisées");

            // une action à compléter amène l'utilisateur sur le champ à remplir
            var shell = new FakeShell();
            var vm2 = _host.Create<EditorViewModel>(shell);
            vm2.SelectIndex(13);
            vm2.AddCatalogAction(ActionCatalog.Find("key.hotkey")!);
            Assert.Contains("focus:properties", shell.Calls);
            Assert.True(vm2.Properties.Steps[0].HasIssues);
        });
    }

    [Fact]
    public void ProfileFormatAndScreenChoicesAreApplied()
    {
        _host.Reset();
        var vm = NewVm();
        _host.Run(() =>
        {
            vm.SelectedFormat = vm.Formats.First(f => f.Columns == 3 && f.Rows == 2);
            Assert.Equal(3, vm.Columns);
            Assert.Equal(5, vm.Rows);                                 // 10 boutons sur 3 colonnes = 4 lignes, plus une ligne libre
            // 10 boutons dans une grille de 6 : les cases en trop sont signalées comme sous-pages
            Assert.Contains(vm.Slots, s => s.IsOverflow);
            Assert.True(vm.HasOverflow);

            vm.SelectedFormat = vm.Formats.First(f => f.Columns == 0);
            Assert.False(vm.HasOverflow);

            vm.SelectedScreen = vm.Screens.First(s => s.Monitor == -1);
            Assert.NotEmpty(vm.DeviceText);
        });
    }

    [Fact]
    public async Task ImportedProfileThroughTheServiceIsAddedAndUndoable()
    {
        _host.Reset();
        var tmp = Path.Combine(Path.GetTempPath(), "td-import-" + Guid.NewGuid().ToString("N") + ".streamdak");
        try
        {
            var p = ConfigStore.Clone(Doc.Config.Profiles[0]);
            p.Name = "Importé";
            ProfilePackage.ExportProfile(p, tmp);
            var shell = new FakeShell();
            var name = await _host.Run(() => TransferService.ImportProfileAsync(shell, Doc, tmp));
            Assert.Equal("Importé", name);
            Assert.Contains("confirm:Importer un profil", shell.Calls);
            Assert.Equal(2, _host.Run(() => Doc.Config.Profiles.Count));
            _host.Run(() => Doc.Undo());
            Assert.Single(_host.Run(() => Doc.Config.Profiles));

            shell.ConfirmAnswer = false;
            Assert.Null(await _host.Run(() => TransferService.ImportProfileAsync(shell, Doc, tmp)));
            Assert.Single(_host.Run(() => Doc.Config.Profiles));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void DocumentChangesArePersistedByTheAutoSaver()
    {
        _host.Reset();
        _host.Run(() => Doc.UpdateButton(new Place("Principal", 0), 0, b => b.Label = "Persisté", "test"));
        _host.Run(() => _host.Persistence.Flush());
        var onDisk = ConfigStore.Parse(File.ReadAllText(ConfigStore.FilePath));
        Assert.Equal("Persisté", onDisk.Profiles[0].Pages[0].Buttons[0].Label);
    }

    [Fact]
    public void ChangingTheThemeRepublishesBrushesAndKeepsViewsAlive()
    {
        _host.Reset();
        _host.SetTheme("light");
        Assert.Equal(Theme.LightPalette.Window, Theme.Current.Window);
        var brush = (System.Windows.Media.SolidColorBrush)_host.Run(() => Application.Current.Resources["DeckWindow"]);
        Assert.Equal(System.Windows.Media.ColorConverter.ConvertFromString(Theme.LightPalette.Window), brush.Color);
        _host.SetTheme("dark");
        brush = (System.Windows.Media.SolidColorBrush)_host.Run(() => Application.Current.Resources["DeckWindow"]);
        Assert.Equal(System.Windows.Media.ColorConverter.ConvertFromString(Theme.DarkPalette.Window), brush.Color);
    }

    [Fact]
    public void ReduceMotionSwitchesTheTileStyleToTheStaticVariant()
    {
        _host.Reset();
        _host.Run(() => Doc.UpdateSettings(c => c.ReduceMotion = true));
        var style = _host.Run(() => Application.Current.Resources["DeckTileStyle"]);
        Assert.Same(_host.Run(() => Application.Current.Resources["DeckButtonStatic"]), style);
        Assert.False(Motion.Enabled);
        _host.Run(() => Doc.UpdateSettings(c => c.ReduceMotion = false));
        if (SystemParameters.ClientAreaAnimation)
            Assert.Same(_host.Run(() => Application.Current.Resources["DeckButton"]), _host.Run(() => Application.Current.Resources["DeckTileStyle"]));
    }
}
