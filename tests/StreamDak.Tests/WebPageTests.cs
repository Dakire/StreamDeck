using System.Linq;
using System.Text.Json;
using StreamDak.Actions;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Pages web plein cadre : modèle compatible, adresse saisie, opération annulable du document.</summary>
public class WebPageCoreTests
{
    // ---------- compatibilité de config.json ----------

    [Fact]
    public void AGridPageWritesNoKindOrUrlKey()
    {
        var json = JsonSerializer.Serialize(new DeckPage { Name = "A" }, ConfigStore.Options);
        Assert.DoesNotContain("Kind", json);
        Assert.DoesNotContain("Url", json);
    }

    [Fact]
    public void AWebPageRoundTripsAndAnOldConfigStillLoadsAsAGrid()
    {
        var json = JsonSerializer.Serialize(new DeckPage { Name = "Agenda", Kind = PageKinds.Web, Url = "https://example.org/" }, ConfigStore.Options);
        var back = JsonSerializer.Deserialize<DeckPage>(json, ConfigStore.Options)!;
        Assert.True(back.IsWeb);
        Assert.Equal("https://example.org/", back.Url);

        const string old = "{\"Version\":2,\"Profiles\":[{\"Name\":\"P\",\"Pages\":[{\"Name\":\"A\",\"Buttons\":[]}]}]}";
        var cfg = JsonSerializer.Deserialize<DeckConfig>(old, ConfigStore.Options)!;
        Assert.False(cfg.Profiles[0].Pages[0].IsWeb);
        Assert.Null(cfg.Profiles[0].Pages[0].Url);
    }

    [Fact]
    public void TheNormalizerRepairsAnUnknownKindAndDropsTheAddressOfAGridPage()
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P",
            Profiles = { new DeckProfile { Name = "P", Pages = {
                new DeckPage { Name = "Inconnue", Kind = "hologramme", Url = "https://example.org" },
                new DeckPage { Name = "Web", Kind = "WEB", Url = "  https://example.org/a  " },
                new DeckPage { Name = "Grille", Url = "https://example.org" },
            } } },
        };
        var result = ConfigNormalizer.Normalize(cfg);
        var pages = cfg.Profiles[0].Pages;
        Assert.Null(pages[0].Kind);
        Assert.Null(pages[0].Url);
        Assert.Contains(result.Warnings, w => w.Contains("hologramme"));
        Assert.Equal((PageKinds.Web, "https://example.org/a"), (pages[1].Kind, pages[1].Url));
        Assert.Null(pages[2].Url);
    }

    // ---------- zoom ----------

    [Theory]
    [InlineData(100, 1, 110)]
    [InlineData(100, -1, 90)]
    [InlineData(110, 1, 125)]
    [InlineData(500, 1, 500)]            // aux extrémités, on reste sur place
    [InlineData(25, -1, 25)]
    [InlineData(103, 1, 110)]            // le pincement du doigt donne n'importe quel pourcentage : on rejoint le palier voisin
    [InlineData(103, -1, 100)]
    [InlineData(9999, -1, 400)]          // hors limites : ramené d'abord
    public void ZoomStepsLikeABrowser(int from, int direction, int expected) => Assert.Equal(expected, WebZoom.Next(from, direction));

    [Fact]
    public void ZoomIsStoredOnlyWhenItIsNotTheNormalOne()
    {
        Assert.Equal(0, WebZoom.ForStorage(100));
        Assert.Equal(0, WebZoom.ForStorage(0));
        Assert.Equal(150, WebZoom.ForStorage(150));
        Assert.Equal(500, WebZoom.ForStorage(9000));
        Assert.Equal(25, WebZoom.ForStorage(3));
        Assert.DoesNotContain("Zoom", JsonSerializer.Serialize(new DeckPage { Name = "A", Kind = PageKinds.Web, Url = "https://example.org/" }, ConfigStore.Options));
    }

    [Fact]
    public void TheNormalizerBoundsAPageZoomAndDropsItFromAGrid()
    {
        var cfg = new DeckConfig
        {
            Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P",
            Profiles = { new DeckProfile { Name = "P", Pages = {
                new DeckPage { Name = "A", Kind = PageKinds.Web, Url = "https://example.org", Zoom = 9000 },
                new DeckPage { Name = "B", Kind = PageKinds.Web, Url = "https://example.org", Zoom = 100 },
                new DeckPage { Name = "C", Zoom = 150 },
            } } },
        };
        ConfigNormalizer.Normalize(cfg);
        var pages = cfg.Profiles[0].Pages;
        Assert.Equal(500, pages[0].Zoom);
        Assert.Equal(0, pages[1].Zoom);
        Assert.Equal(0, pages[2].Zoom);
    }

    // ---------- adresse saisie ----------

    [Theory]
    [InlineData("https://calendar.google.com/calendar/u/0/r/week", "https://calendar.google.com/calendar/u/0/r/week")]
    [InlineData("  calendar.google.com  ", "https://calendar.google.com/")]
    [InlineData("http://192.168.1.20:8123/dashboard", "http://192.168.1.20:8123/dashboard")]
    [InlineData("localhost:8080", "https://localhost:8080/")]
    public void AWebAddressIsCompletedWithHttps(string typed, string expected)
    {
        Assert.True(WebAddress.TryParse(typed, out var uri));
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.org")]
    [InlineData("mailto:a@b.c")]
    [InlineData("pas une adresse")]
    public void AnythingButAWebAddressIsRefused(string typed) => Assert.False(WebAddress.TryParse(typed, out _));

    // ---------- refus de Google dans un cadre intégré ----------

    [Theory]
    [InlineData("https://accounts.google.com/v3/signin/rejected?continue=https%3A%2F%2Fcalendar.google.com", true)]
    [InlineData("https://accounts.google.com/signin/rejected", true)]
    [InlineData("https://accounts.google.com/o/oauth2/auth?error=disallowed_useragent", true)]
    [InlineData("https://accounts.google.com/v3/signin/identifier", false)]            // la page de connexion normale n'est pas un refus
    [InlineData("https://calendar.google.com/calendar/u/0/r/week", false)]
    [InlineData("https://example.org/signin/rejected", false)]                          // seul Google est reconnu
    [InlineData("https://evilgoogle.com/signin/rejected", false)]
    [InlineData("pas une adresse", false)]
    [InlineData(null, false)]
    public void OnlyGoogleOwnRefusalPageIsRecognised(string? address, bool refusal) =>
        Assert.Equal(refusal, EmbeddedBrowserRefusal.IsRefusal(address));

    // ---------- document ----------

    [Fact]
    public void MakingAPageWebKeepsItsButtonsAndIsUndoable()
    {
        var d = Samples.Doc("a", "b");
        var r = d.SetPageWeb("P", 0, "calendar.google.com");
        Assert.True(r.Ok);
        var page = d.Config.Profiles[0].Pages[0];
        Assert.True(page.IsWeb);
        Assert.Equal("https://calendar.google.com/", page.Url);
        Assert.Equal(2, page.Buttons.Count);                              // les boutons sont conservés, masqués

        Assert.True(d.Undo());
        Assert.False(d.Config.Profiles[0].Pages[0].IsWeb);
        Assert.Null(d.Config.Profiles[0].Pages[0].Url);
    }

    [Fact]
    public void BackToAGridBringsTheButtonsBackAndNothingChangesWhenNothingChanges()
    {
        var d = Samples.Doc("a");
        Assert.True(d.SetPageWeb("P", 0, "https://example.org").Ok);
        Assert.False(d.SetPageWeb("P", 0, "https://example.org").Ok);       // inchangé : pas d'entrée d'historique
        Assert.True(d.SetPageWeb("P", 0, null).Ok);
        var page = d.Config.Profiles[0].Pages[0];
        Assert.False(page.IsWeb);
        Assert.Null(page.Url);
        Assert.Equal("a", page.Buttons.Single().Label);
        Assert.False(d.SetPageWeb("P", 0, null).Ok);                        // déjà une grille
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x")]
    [InlineData("")]
    public void ADocumentNeverStoresAnAddressThatIsNotWeb(string typed)
    {
        var d = Samples.Doc("a");
        var r = d.SetPageWeb("P", 0, typed);
        Assert.False(r.Ok);
        Assert.False(d.CanUndo);
        Assert.False(d.Config.Profiles[0].Pages[0].IsWeb);
    }

    [Fact]
    public void ADuplicatedWebPageIsAlsoAWebPage()
    {
        var d = Samples.Doc("a");
        d.SetPageWeb("P", 0, "https://example.org");
        Assert.True(d.DuplicatePage("P", 0).Ok);
        Assert.All(d.Config.Profiles[0].Pages.Take(2), p => Assert.Equal("https://example.org/", p.Url));
    }
}

/// <summary>Le deck affiche une page web sur toute la zone centrale, à la place de la grille.</summary>
[Collection("ui")]
public class DeckWebPageTests
{
    readonly WpfHost _host;
    public DeckWebPageTests(WpfHost host) => _host = host;

    static DeckButton Btn(string label) => new() { Label = label, Icon = "Star24", Actions = { new DeckAction { Type = "hotkey", Value = "ctrl+c" } } };

    static DeckConfig Config(string? url) => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 4, Rows = 2, Pages =
        {
            new DeckPage { Name = "Agenda", Kind = PageKinds.Web, Url = url, Buttons = { Btn("Caché 1"), Btn("Caché 2") } },
            new DeckPage { Name = "Boutons", Buttons = { Btn("Un"), Btn("Deux") } },
        } } },
    };

    (MainViewModel Vm, FakeDeckShell Shell) NewDeck(string? url)
    {
        _host.Reset(Config(url));
        var shell = new FakeDeckShell();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(shell, new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, shell);
    }

    [Fact]
    public void AWebPageShowsOneTileOverTheWholeGridInsteadOfTheButtons()
    {
        var (vm, _) = NewDeck("https://example.org/agenda");
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(Assert.Single(vm.Tiles));
            Assert.Equal(0, web.Cell);
            Assert.True(web.ColSpan >= vm.L.Columns && web.RowSpan >= vm.L.Rows);   // toute la largeur et toute la hauteur
            Assert.Equal("example.org", web.Title);
            Assert.True(web.ShowPage);
            Assert.False(vm.HasSubs);                                              // pas de sous-pages pour une page web
            Assert.DoesNotContain(vm.Tiles, t => t is DeckTileViewModel);          // les boutons cachés ne sont jamais affichés
        });
    }

    [Fact]
    public void ANeighbouringGridPageStillShowsItsButtonsAndComingBackShowsThePageAgain()
    {
        var (vm, _) = NewDeck("https://example.org/agenda");
        _host.Run(() =>
        {
            vm.Tabs[1].Command.Execute(null);
            Assert.DoesNotContain(vm.Tiles, t => t is WebWidgetViewModel);
            Assert.Contains(vm.Tiles, t => t is DeckTileViewModel { Label: "Un" });
            vm.Tabs[0].Command.Execute(null);
            Assert.IsType<WebWidgetViewModel>(Assert.Single(vm.Tiles));
        });
    }

    [Fact]
    public void ARebuildKeepsTheSameFrameSoThePageIsNotReloaded()
    {
        var (vm, _) = NewDeck("https://example.org/agenda");
        _host.Run(() =>
        {
            var first = vm.Tiles[0];
            vm.Resize(1500, 880);                                                  // reconstruction de la grille
            vm.OpenMenuCommand.Execute(null);
            vm.CloseMenuCommand.Execute(null);
            Assert.Same(first, vm.Tiles[0]);
        });
    }

    [Fact]
    public void AWebPageWithoutAValidAddressSaysHowToSetItAndLoadsNothing()
    {
        foreach (var url in new[] { null, "javascript:alert(1)", "file:///C:/x" })
        {
            var (vm, _) = NewDeck(url);
            _host.Run(() =>
            {
                var web = Assert.IsType<WebWidgetViewModel>(Assert.Single(vm.Tiles));
                Assert.Null(web.Source);
                Assert.False(web.ShowPage);
                Assert.Contains("Page", web.Status);
            });
        }
    }

    [Fact]
    public void OpenInBrowserLaunchesTheConfiguredAddressOnlyAndNeverAnythingElse()
    {
        var (vm, _) = NewDeck("https://example.org/agenda");
        var sim = (SimulatedPlatform)_host.Run(() => _host.Engine.Context.Platform.Keyboard);
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            sim.Log.Clear();
            web.OpenInBrowserCommand.Execute(null);
            Assert.Equal(new[] { "open https://example.org/agenda" }, sim.Log.ToArray());
        });

        var (noAddress, _) = NewDeck(null);
        _host.Run(() =>
        {
            sim.Log.Clear();
            Assert.IsType<WebWidgetViewModel>(noAddress.Tiles[0]).OpenInBrowserCommand.Execute(null);
            Assert.Empty(sim.Log);                                                  // pas d'adresse : rien à ouvrir
        });
    }

    [Fact]
    public void ZoomingAWebPageIsRememberedInTheConfigurationWithoutRebuildingTheFrame()
    {
        var (vm, _) = NewDeck("https://example.org/agenda");
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            Assert.Equal(100, web.ZoomPercent);
            web.ZoomInCommand.Execute(null);
            web.ZoomInCommand.Execute(null);
            Assert.Equal(125, web.ZoomPercent);
            Assert.Equal("125 %", web.ZoomText);
            Assert.Equal(125, _host.Document.Config.Profiles[0].Pages[0].Zoom);
            Assert.Same(web, vm.Tiles[0]);                                        // le cadre (donc la page chargée) n'est pas recréé

            web.ZoomOutCommand.Execute(null);
            Assert.Equal(110, web.ZoomPercent);
            web.ZoomResetCommand.Execute(null);
            Assert.Equal(100, web.ZoomPercent);
            Assert.Equal(0, _host.Document.Config.Profiles[0].Pages[0].Zoom);     // 100 % : rien n'est écrit
        });
    }

    [Fact]
    public void AWebPageOpensAtItsSavedZoom()
    {
        _host.Reset(Config("https://example.org/agenda"));
        _host.Run(() => _host.Document.Config.Profiles[0].Pages[0].Zoom = 150);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Settle();
        _host.Run(() => Assert.Equal(150, Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]).ZoomPercent));
    }

    [Fact]
    public void TheWebFrameHidesUnderAMenuLikeAWidgetDoes()
    {
        var (vm, _) = NewDeck("https://example.org");
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(vm.Tiles[0]);
            vm.OpenMenuCommand.Execute(null);
            Assert.True(web.Suspended);
            vm.CloseMenuCommand.Execute(null);
            Assert.False(web.Suspended);
        });
    }

    [Fact]
    public void TheWebFrameFillsTheCentralAreaOfTheWindow()
    {
        _host.Reset(Config("https://example.org/agenda"));
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-page-web", 1600, 900);
            _host.Run(() =>
            {
                var view = Assert.Single(WpfHost.Descendants<StreamDak.Controls.WebWidgetView>((System.Windows.DependencyObject)win.Content));
                Assert.True(view.ActualWidth > 1600 * 0.9, $"largeur {view.ActualWidth}");
                Assert.True(view.ActualHeight > 900 * 0.6, $"hauteur {view.ActualHeight}");
            });
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void ThePageMenuTurnsAGridPageIntoAWebPageAndBack()
    {
        var (vm, shell) = NewDeck(null);
        _host.Run(() =>
        {
            vm.Tabs[1].Command.Execute(null);
            vm.StartEditing(new Place("P", 1), -1);
            shell.Dialogs.Prompts.Enqueue("example.org/chat");
            vm.OpenPageMenuCommand.Execute(null);
            QuickEditTests.Pick(vm, "Afficher une page web…");
        });
        _host.Settle();
        _host.Run(() =>
        {
            var web = Assert.IsType<WebWidgetViewModel>(Assert.Single(vm.Tiles));
            Assert.Equal("example.org", web.Title);

            vm.OpenPageMenuCommand.Execute(null);
            Assert.Contains(vm.MenuEntries.OfType<MenuEntryViewModel>(), e => e.Text == "Adresse de la page web…");
            QuickEditTests.Pick(vm, "Afficher une grille de boutons");
        });
        _host.Settle();
        _host.Run(() => Assert.Contains(vm.Tiles, t => t is DeckTileViewModel { Label: "Un" }));
    }
}
