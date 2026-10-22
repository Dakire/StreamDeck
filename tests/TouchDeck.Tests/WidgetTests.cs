using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TouchDeck.ViewModels;
using Xunit;

namespace TouchDeck.Tests;

/// <summary>Widgets : modèle compatible, nettoyage, emprise sur plusieurs cases, texte de l'horloge, catalogue.</summary>
public class WidgetCoreTests
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ---------- compatibilité de config.json ----------

    [Fact]
    public void AnOrdinaryButtonWritesNoWidgetKey()
    {
        var json = JsonSerializer.Serialize(new DeckButton { Label = "Un", Icon = "Star24" }, ConfigStore.Options);
        Assert.DoesNotContain("Widget", json);
        Assert.DoesNotContain("ColSpan", json);
        Assert.DoesNotContain("RowSpan", json);
    }

    [Fact]
    public void AnOldConfigWithoutWidgetsStillLoads()
    {
        const string old = "{\"Version\":2,\"Profiles\":[{\"Name\":\"P\",\"Pages\":[{\"Name\":\"A\",\"Buttons\":[{\"Label\":\"Un\",\"Icon\":\"Star24\",\"Color\":\"#334155\",\"Actions\":[{\"Type\":\"hotkey\",\"Value\":\"ctrl+c\"}]}]}]}]}";
        var cfg = JsonSerializer.Deserialize<DeckConfig>(old, ConfigStore.Options)!;
        var button = cfg.Profiles[0].Pages[0].Buttons[0];
        Assert.Null(button.Widget);
        Assert.Null(button.WidgetOptions);
        Assert.Equal(0, button.ColSpan);
        Assert.Equal((1, 1), button.Span());
    }

    [Fact]
    public void AWidgetSurvivesASaveAndALoad()
    {
        var widget = ActionCatalog.Find("widget.web")!.CreateButton();
        widget.SetOption(WidgetOptionKeys.Url, "https://example.org/chat");
        var copy = ConfigStore.Clone(widget);
        Assert.Equal(WidgetTypes.Web, copy.Widget);
        Assert.Equal((3, 2), copy.Span());
        Assert.Equal("https://example.org/chat", copy.Option(WidgetOptionKeys.Url));
    }

    // ---------- nettoyage ----------

    [Fact]
    public void TheNormalizerRepairsWidgetsAndDropsUnknownOnes()
    {
        var cfg = ConfigStore.DefaultConfig();
        var buttons = cfg.Profiles[0].Pages[0].Buttons;
        buttons.Clear();
        buttons.Add(new DeckButton { Label = "?", Widget = "inconnu", ColSpan = 3, WidgetOptions = new() { ["a"] = "b" } });
        buttons.Add(new DeckButton { Label = "H", Widget = "CLOCK", ColSpan = 99, RowSpan = -4, IsFolder = true, WidgetOptions = new() { ["seconds"] = "1", ["vide"] = " " } });
        var result = ConfigNormalizer.Normalize(cfg);
        Assert.NotNull(result);
        buttons = cfg.Profiles[0].Pages[0].Buttons;
        Assert.Null(buttons[0].Widget);                                    // widget inconnu : redevient un bouton ordinaire
        Assert.Null(buttons[0].WidgetOptions);
        Assert.Equal(0, buttons[0].ColSpan);
        Assert.Equal(WidgetTypes.Clock, buttons[1].Widget);                // identifiant ramené à sa forme canonique
        Assert.Equal(WidgetTypes.MaxSpan, buttons[1].ColSpan);
        Assert.Equal(0, buttons[1].RowSpan);
        Assert.False(buttons[1].IsFolder);
        Assert.Equal(new[] { "seconds" }, buttons[1].WidgetOptions!.Keys);  // réglage vide retiré
    }

    // ---------- emprise ----------

    [Fact]
    public void ASpanIsKeptWhenTheNeighbourCellsAreFree()
    {
        Assert.Equal((2, 1), TileSpans.Effective(0, 2, 1, 5, 3, _ => false));
        Assert.Equal((3, 2), TileSpans.Effective(6, 3, 2, 5, 3, _ => false));
    }

    [Fact]
    public void ASpanIsClampedToTheGridAndShrinksOverOccupiedCells()
    {
        Assert.Equal((1, 1), TileSpans.Effective(4, 2, 1, 5, 3, _ => false));          // dernière colonne : pas de place à droite
        Assert.Equal((2, 1), TileSpans.Effective(13, 4, 3, 5, 3, _ => false));         // avant-dernière colonne, dernière ligne
        Assert.Equal((2, 1), TileSpans.Effective(0, 3, 1, 5, 3, c => c == 2));         // la case 2 est prise : 2 de large
        Assert.Equal((1, 1), TileSpans.Effective(0, 2, 2, 5, 3, c => c == 1 || c == 5)); // voisins pris : une case
        Assert.Equal((2, 1), TileSpans.Effective(0, 2, 2, 5, 3, c => c == 5));         // seule la case du dessous est prise
    }

    [Fact]
    public void CoveredCellsExcludeTheAnchor()
    {
        Assert.Equal(new[] { 1, 5, 6 }, TileSpans.Covered(0, 2, 2, 5));
    }

    // ---------- horloge ----------

    [Fact]
    public void TheClockFormatsTwentyFourAndTwelveHours()
    {
        var t = new DateTime(2026, 10, 22, 14, 5, 9);
        Assert.Equal("14:05", WidgetClock.Time(t, false, false, Fr));
        Assert.Equal("14:05:09", WidgetClock.Time(t, true, false, Fr));
        Assert.Equal("Jeudi 22 octobre", WidgetClock.Date(t, Fr));
        Assert.StartsWith("2:05", WidgetClock.Time(t, false, true, CultureInfo.GetCultureInfo("en-US")));
    }

    // ---------- catalogue ----------

    [Fact]
    public void TheCatalogOffersTheThreeWidgetsWithTheirDefaultSize()
    {
        var entries = ActionCatalog.All.Where(a => a.Category == ActionCatalog.Widgets).ToList();
        Assert.Equal(new[] { "widget.clock", "widget.agenda", "widget.web", "widget.gchat", "widget.gcalendar", "widget.gmail" }, entries.Select(e => e.Id));
        var clock = ActionCatalog.Find("widget.clock")!.CreateButton();
        Assert.Equal(WidgetTypes.Clock, clock.Widget);
        Assert.Empty(clock.Actions);
        Assert.Equal((2, 1), clock.Span());
        Assert.Equal(ActionCatalog.Widgets, ActionCatalog.Categories[0]);   // en tête du catalogue
    }

    [Fact]
    public void TheGooglePagesAreWebWidgetsWithTheirAddressAlreadyFilledIn()
    {
        foreach (var id in new[] { "widget.gchat", "widget.gcalendar", "widget.gmail" })
        {
            var button = ActionCatalog.Find(id)!.CreateButton();
            Assert.Equal(WidgetTypes.Web, button.Widget);
            Assert.True(WidgetTypes.IsWebAddress(button.Option(WidgetOptionKeys.Url), out var uri), id);
            Assert.EndsWith("google.com", uri.Host);
            Assert.Empty(ActionValidator.Validate(button, ConfigStore.DefaultConfig(), null));   // prêts à l'emploi : rien à compléter
        }
    }

    [Fact]
    public void AWidgetNeedsNoActionButAWebWidgetNeedsAnAddress()
    {
        var cfg = ConfigStore.DefaultConfig();
        var clock = ActionCatalog.Find("widget.clock")!.CreateButton();
        Assert.Empty(ActionValidator.Validate(clock, cfg, null));
        var web = ActionCatalog.Find("widget.web")!.CreateButton();
        Assert.Contains(ActionValidator.Validate(web, cfg, null), i => i.Message.Contains("adresse"));
        web.SetOption(WidgetOptionKeys.Url, "https://example.org");
        Assert.Empty(ActionValidator.Validate(web, cfg, null));
        web.SetOption(WidgetOptionKeys.Url, "javascript:alert(1)");
        Assert.Contains(ActionValidator.Validate(web, cfg, null), i => i.Message.Contains("adresse"));   // seul http(s) est accepté
    }

    [Theory]
    [InlineData("https://example.org/a?b=1", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("", false)]
    public void OnlyHttpAddressesAreAccepted(string text, bool ok) => Assert.Equal(ok, WidgetTypes.IsWebAddress(text, out _));
}

/// <summary>Le deck place les widgets sur plusieurs cases et laisse les cases recouvertes sans tuile.</summary>
[Collection("ui")]
public class DeckWidgetTests
{
    readonly WpfHost _host;
    public DeckWidgetTests(WpfHost host) { _host = host; }

    static readonly Place Root = new("P", 0);

    static DeckButton Btn(string label) =>
        new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckButton Widget(string id, int cols, int rows)
    {
        var b = ActionCatalog.Find("widget." + id)!.CreateButton();
        b.ColSpan = cols; b.RowSpan = rows;
        return b;
    }

    static DeckConfig Config(params DeckButton[] buttons)
    {
        var page = new DeckPage { Name = "A" };
        page.Buttons.AddRange(buttons);
        return new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
            Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 2, Pages = { page } } },
        };
    }

    MainViewModel NewDeck(DeckConfig cfg)
    {
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return vm;
    }

    [Fact]
    public void AWideWidgetCoversTheNextCellAndItsTileIsOmitted()
    {
        var vm = NewDeck(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }, Btn("Un")));
        _host.Run(() =>
        {
            Assert.Equal(7, vm.Tiles.Count);                                   // 8 cases moins la case recouverte
            var clock = Assert.IsType<ClockWidgetViewModel>(vm.Tiles[0]);
            Assert.Equal((0, 2, 1), (clock.Cell, clock.ColSpan, clock.RowSpan));
            var un = Assert.IsType<DeckTileViewModel>(vm.Tiles[1]);
            Assert.Equal(2, un.Cell);                                          // la tuile suivante garde sa vraie case
            Assert.Equal(1, un.ColSpan);
            Assert.False(string.IsNullOrEmpty(clock.Time));
        });
    }

    [Fact]
    public void AWidgetOverAnOccupiedCellShrinksInsteadOfHidingTheButton()
    {
        var vm = NewDeck(Config(Widget("clock", 2, 2), Btn("Voisin")));
        _host.Run(() =>
        {
            var clock = Assert.IsType<ClockWidgetViewModel>(vm.Tiles[0]);
            Assert.Equal((1, 2), (clock.ColSpan, clock.RowSpan));              // la case de droite est prise : deux lignes de haut, une colonne
            Assert.Contains(vm.Tiles, t => t is DeckTileViewModel { Label: "Voisin" });
        });
    }

    [Fact]
    public void InEditModeAWidgetIsASelectableTouchWithItsSpan()
    {
        var vm = NewDeck(Config(Widget("agenda", 2, 2), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            Assert.IsType<AgendaWidgetViewModel>(vm.Tiles[0]);
            vm.StartEditing(Root, 0);
            var tile = Assert.IsType<DeckTileViewModel>(vm.Tiles[0]);          // en édition : une touche comme les autres
            Assert.Equal((2, 2), (tile.ColSpan, tile.RowSpan));
            Assert.True(vm.TryGetSlot(tile, out _, out int index));
            Assert.Equal(0, index);
            vm.EndEditCommand.Execute(null);
            Assert.IsType<AgendaWidgetViewModel>(vm.Tiles[0]);
        });
    }

    [Fact]
    public void ALongPressOnAWidgetOpensTheMenuOfItsCell()
    {
        var vm = NewDeck(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            var clock = Assert.IsType<ClockWidgetViewModel>(vm.Tiles[0]);
            Assert.NotNull(clock.Edit);
            clock.Edit!.Execute(null);
            Assert.True(vm.MenuOpen);
            Assert.Contains("Modifier…", vm.MenuEntries.OfType<MenuEntryViewModel>().Select(e => e.Text));
        });
    }

    [Fact]
    public void TheAgendaWidgetExplainsWhatIsMissingWhenGoogleIsNotConnected()
    {
        var vm = NewDeck(Config(Widget("agenda", 2, 2), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            var agenda = Assert.IsType<AgendaWidgetViewModel>(vm.Tiles[0]);
            Assert.True(agenda.HasStatus);
            Assert.Contains("Google Workspace", agenda.Status);                // dit quoi faire, pas seulement « rien à afficher »
            Assert.False(agenda.HasEntries);
            agenda.Show(new[] { new AgendaEntry("10:00", "Réunion", "Aujourd'hui", true) }, "");
            Assert.True(agenda.HasEntries);
            Assert.False(agenda.HasStatus);
        });
    }

    // ---------- page web ----------

    static DeckButton WebWidget(string? url)
    {
        var b = Widget("web", 2, 1);
        b.SetOption(WidgetOptionKeys.Url, url);
        return b;
    }

    [Fact]
    public void AWebWidgetWithoutAValidAddressSaysWhatToFillInAndNeverLoadsAnything()
    {
        foreach (var url in new[] { null, "javascript:alert(1)", "file:///C:/secret.txt", "pas une adresse" })
        {
            var vm = NewDeck(Config(WebWidget(url), new DeckButton { Blank = true }));
            _host.Run(() =>
            {
                var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
                Assert.Null(web.Source);
                Assert.True(web.HasStatus);
                Assert.False(web.ShowPage);
                Assert.Contains("adresse", web.Status);
            });
        }
    }

    [Fact]
    public void AWebWidgetWithAnAddressShowsThePageUntilAMenuOrADialogCoversIt()
    {
        var vm = NewDeck(Config(WebWidget("https://example.org/chat"), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            Assert.Equal("example.org", web.Title);
            Assert.True(web.ShowPage);

            vm.OpenMenuCommand.Execute(null);                                  // le cadre est une fenêtre Win32 : il se masque sous la feuille
            Assert.True(web.Suspended);
            Assert.False(web.ShowPage);
            vm.CloseMenuCommand.Execute(null);
            Assert.False(web.Suspended);
            Assert.True(web.ShowPage);

            _ = vm.ConfirmAsync("Titre", "Message", "OK");                      // idem pour une boîte de dialogue
            Assert.True(web.Suspended);
            vm.Dialog!.CancelCommand.Execute(null);
        });
    }

    [Fact]
    public void ALoadFailureReplacesThePageWithAMessageAndReloadingClearsIt()
    {
        var vm = NewDeck(Config(WebWidget("https://example.org"), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            int reloads = 0;
            web.ReloadRequested = () => reloads++;
            web.Fail("La page n'a pas pu être chargée");
            Assert.True(web.HasStatus);
            Assert.False(web.ShowPage);
            web.ReloadCommand.Execute(null);
            Assert.Equal(1, reloads);
            Assert.False(web.HasStatus);
            Assert.True(web.ShowPage);
        });
    }

    [Fact]
    public void TypingInAWebWidgetMakesTheWindowActivatableThenGivesTheFocusBack()
    {
        _host.Reset(Config(WebWidget("https://example.org"), new DeckButton { Blank = true }, Btn("Un")));
        var shell = new FakeDeckShell();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(shell, new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            Assert.False(shell.Editing);
            web.ToggleTypingCommand.Execute(null);
            Assert.True(web.IsTyping);
            Assert.True(shell.Editing);                                        // la fenêtre devient activable : le clavier va à la page
            web.ToggleTypingCommand.Execute(null);
            Assert.False(web.IsTyping);
            Assert.False(shell.Editing);                                       // sans focus de nouveau, focus rendu

            web.ToggleTypingCommand.Execute(null);
            vm.OpenMenuCommand.Execute(null);
            vm.MenuEntries.OfType<MenuEntryViewModel>().First(e => e.Text == "Modifier le deck").Command.Execute(null);
            Assert.False(web.IsTyping);                                        // le mode édition prend la main
            Assert.True(shell.Editing);
            vm.EndEditCommand.Execute(null);
            Assert.False(shell.Editing);
        });
    }

    [Fact]
    public void AWebWidgetRendersItsHeaderWithTouchSizedCommands()
    {
        _host.Reset(Config(WebWidget("https://example.org"), new DeckButton { Blank = true }, WebWidget(null), new DeckButton { Blank = true }));
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-widget-web", 1600, 900);
            _host.Run(() =>
            {
                var views = WpfHost.Descendants<TouchDeck.Controls.WebWidgetView>((DependencyObject)win.Content).ToList();
                Assert.Equal(2, views.Count);
                foreach (var view in views)
                {
                    var buttons = WpfHost.Descendants<System.Windows.Controls.Primitives.ButtonBase>(view).ToList();
                    Assert.Equal(2, buttons.Count);                            // recharger, saisie au clavier
                    Assert.All(buttons, b => Assert.True(b.ActualHeight >= 48 && b.ActualWidth >= 48, $"{b.ActualWidth}×{b.ActualHeight} px"));
                }
            });
        }
        finally { _host.Run(win.Close); }
    }

    // ---------- panneau de propriétés ----------

    [Fact]
    public void SelectingAWidgetShowsItsSettingsInsteadOfTheActionSteps()
    {
        var vm = NewDeck(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }, Btn("Un")));
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            Assert.True(props.IsWidget);
            Assert.True(props.IsClockWidget);
            Assert.False(props.IsPlainButton);                                 // pas de liste d'étapes pour un widget
            Assert.Equal("Horloge", props.HeaderText);
            Assert.Empty(props.ButtonIssues);                                  // un widget n'a pas besoin d'action
            Assert.True(props.SizeChoices.Single(c => c.IsSelected).Text == "2 × 1");

            vm.Editor.GoTo(Root, 2);
            Assert.False(props.IsWidget);
            Assert.True(props.IsPlainButton);
        });
    }

    [Fact]
    public void TheClockTogglesWriteTheirOptionsAndAreUndoable()
    {
        var vm = NewDeck(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            props.ClockSeconds = true;
            props.ClockDate = false;
            var button = _host.Document.Get(Root, 0)!;
            Assert.Equal("1", button.Option(WidgetOptionKeys.Seconds));
            Assert.Equal("0", button.Option(WidgetOptionKeys.Date));
            Assert.True(_host.Document.Undo());
            Assert.Equal("", _host.Document.Get(Root, 0)!.Option(WidgetOptionKeys.Date));
            props.ClockSeconds = false;
            Assert.Null(_host.Document.Get(Root, 0)!.WidgetOptions);                          // plus aucun réglage : rien d'inutile dans config.json
        });
    }

    [Fact]
    public void ResizingAWidgetIsRefusedOverOccupiedCellsAndAcceptedOverFreeOnes()
    {
        var vm = NewDeck(Config(Widget("clock", 1, 1), Btn("Voisin"), new DeckButton { Blank = true }, new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            props.SizeChoices.First(c => c.Text == "2 × 1").Command.Execute(null);    // la case de droite est prise
            Assert.Equal((1, 1), _host.Document.Get(Root, 0)!.Span());
            Assert.True(vm.HasStatus);                                                 // message d'erreur : pas la place

            _host.Document.ClearButton(Root, 1);
            props.Reload();
            props.SizeChoices.First(c => c.Text == "2 × 1").Command.Execute(null);
            Assert.Equal((2, 1), _host.Document.Get(Root, 0)!.Span());
            Assert.True(props.SizeChoices.Single(c => c.IsSelected).Text == "2 × 1");
        });
    }

    [Fact]
    public void AWebWidgetAddressIsValidatedBeforeItIsWritten()
    {
        var vm = NewDeck(Config(Widget("web", 3, 2), new DeckButton { Blank = true }));
        _host.Run(() =>
        {
            vm.StartEditing(Root, 0);
            var props = vm.Editor!.Properties;
            Assert.True(props.IsWebWidget);
            props.WidgetUrl = "javascript:alert(1)";
            Assert.NotEmpty(props.WidgetUrlError);
            props.Flush();
            Assert.Equal("", _host.Document.Get(Root, 0)!.Option(WidgetOptionKeys.Url));   // rien n'est écrit

            props.WidgetUrl = "https://example.org/chat";
            Assert.Empty(props.WidgetUrlError);
            props.Flush();
            Assert.Equal("https://example.org/chat", _host.Document.Get(Root, 0)!.Option(WidgetOptionKeys.Url));
        });
    }

    [Fact]
    public void TheWidgetPanelRendersWithTouchSizedControlsAndNoBindingError()
    {
        _host.Reset(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }, Btn("Un")));
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => win.EditCell(Root, 0));
            _host.Settle();
            _host.Render(win, "deck-widget-edition", 1600, 900);
            _host.Run(() =>
            {
                var toggles = WpfHost.Descendants<Wpf.Ui.Controls.ToggleSwitch>(win.DeckProps).Where(t => t.ActualHeight > 0).ToList();
                Assert.True(toggles.Count >= 3, $"{toggles.Count} bascules");
                Assert.All(toggles, t => Assert.True(t.ActualHeight >= 48, $"{t.Content}: {t.ActualHeight} px"));
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void WidgetsRenderWithoutBindingErrorsAndTheClockTextIsVisible()
    {
        _host.Reset(Config(Widget("clock", 2, 1), new DeckButton { Blank = true }, Widget("agenda", 2, 2), new DeckButton { Blank = true }, new DeckButton { Blank = true },
            Btn("Un"), new DeckButton { Blank = true }, new DeckButton { Blank = true }, Widget("web", 2, 1)));
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            var path = _host.Render(win, "deck-widgets", 1600, 900);
            Assert.True(new FileInfo(path).Length > 20_000);
            _host.Run(() =>
            {
                var clockBorder = WpfHost.Descendants<Border>((DependencyObject)win.Content).First(b => b.DataContext is ClockWidgetViewModel);
                var button = WpfHost.Descendants<Button>((DependencyObject)win.Content).First(b => b.DataContext is DeckTileViewModel { Label: "Un" });
                Assert.True(clockBorder.ActualWidth > button.ActualWidth * 1.8, $"horloge {clockBorder.ActualWidth} px, touche {button.ActualWidth} px");   // 2 cases de large
                Assert.Equal(button.ActualHeight, clockBorder.ActualHeight, 2);
            });
        }
        finally { _host.Run(win.Close); }
    }
}
