using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Wpf.Ui.Controls;
using Xunit;

namespace TouchDeck.Tests;

public class CatalogTests
{
    [Fact]
    public void IdsAreUniqueAndEveryCategoryIsListed()
    {
        Assert.Equal(ActionCatalog.All.Count, ActionCatalog.All.Select(a => a.Id).Distinct().Count());
        foreach (var a in ActionCatalog.All) Assert.Contains(a.Category, ActionCatalog.Categories);
        foreach (var c in ActionCatalog.Categories) Assert.Contains(ActionCatalog.All, a => a.Category == c);
    }

    [Fact]
    public void EveryIconIsAKnownFluentSymbol()
    {
        foreach (var a in ActionCatalog.All)
            Assert.True(Enum.TryParse(a.Icon, out SymbolRegular _), $"{a.Id} : icône inconnue « {a.Icon} »");
        foreach (var kv in ActionCatalog.CategoryIcons)
            Assert.True(Enum.TryParse(kv.Value, out SymbolRegular _), $"catégorie {kv.Key} : icône inconnue");
        foreach (var s in IconCatalog.Picker) Assert.True(Enum.IsDefined(s));
    }

    [Fact]
    public void ActionTypesAreAllSupportedAndColorsParse()
    {
        foreach (var a in ActionCatalog.All.Where(x => !x.IsFolder))
        {
            if (!LabelTemplate.IsDynamic(a.ButtonLabel)) Assert.NotEmpty(a.Actions);   // horloge et date : affichage seul
            foreach (var act in a.Actions) Assert.Contains(act.Type, ActionValidator.KnownTypes);
            Assert.True(Theme.TryBrush(a.Color, out _), $"{a.Id} : couleur invalide");
        }
    }

    [Fact]
    public void ReadyToUseActionsHaveNoError()
    {
        // Les actions « à compléter » (programme, raccourci, adresse vide…) sont signalées ; les autres sont correctes d'emblée
        var cfg = ConfigStore.DefaultConfig();
        var templates = new[] { "sys.desktop", "media.playpause", "audio.mute", "key.copy", "web.google", "chrome.newtab", "nav.home", "app.notepad" };
        foreach (var id in templates)
        {
            var b = ActionCatalog.Find(id)!.CreateButton();
            Assert.DoesNotContain(ActionValidator.Validate(b, cfg, cfg.Profiles[0]), i => i.Severity == Severity.Error);
        }
        foreach (var id in new[] { "key.hotkey", "app.launch", "auto.script", "nav.profile" })
        {
            var b = ActionCatalog.Find(id)!.CreateButton();
            Assert.Contains(ActionValidator.Validate(b, cfg, cfg.Profiles[0]), i => i.Severity == Severity.Error);
        }
    }

    [Fact]
    public void CreateButtonReturnsIndependentCopies()
    {
        var a = ActionCatalog.Find("sys.desktop")!;
        var b1 = a.CreateButton();
        b1.Actions[0].Value = "modifié";
        Assert.Equal("win+d", a.CreateButton().Actions[0].Value);
        Assert.Equal("win+d", a.Actions[0].Value);
    }

    [Fact]
    public void FolderEntryCreatesAnEmptyFolder()
    {
        var f = ActionCatalog.Find("nav.folder")!.CreateButton();
        Assert.True(f.IsFolder);
        Assert.NotNull(f.Children);
        Assert.Empty(f.Actions);
    }

    [Theory]
    [InlineData("volume", "audio.up")]
    [InlineData("VOLUME", "audio.down")]
    [InlineData("capture ecran", "sys.snip")]       // sans accents
    [InlineData("réseau social", null)]
    [InlineData("chrome onglet", "chrome.newtab")]  // tous les mots, dans n'importe quel ordre
    [InlineData("mot de passe", "keeper.vault")]    // mots-clés
    public void SearchIsInstantAccentAndCaseInsensitive(string query, string? expectedId)
    {
        var ids = ActionCatalog.Search(query).Select(a => a.Id).ToList();
        if (expectedId == null) Assert.Empty(ids);
        else Assert.Contains(expectedId, ids);
    }

    [Fact]
    public void EmptyQueryReturnsEverything() =>
        Assert.Equal(ActionCatalog.All.Count, ActionCatalog.Search("  ").Count());

    [Fact]
    public void IdentifyFindsTheCatalogOrigin()
    {
        var b = ActionCatalog.Find("media.next")!.CreateButton();
        Assert.Equal("media.next", ActionCatalog.Identify(b)?.Id);
        b.Actions[0].Type = "text";
        Assert.Null(ActionCatalog.Identify(b));
    }
}

public class ValidatorTests
{
    static readonly DeckConfig Cfg = new DeckConfig
    {
        Version = 2,
        Profiles = { new DeckProfile { Name = "P", Pages = { new DeckPage { Name = "Accueil" }, new DeckPage { Name = "Outils" } } },
                     new DeckProfile { Name = "Jeu" } },
    };

    static string[] Errors(string type, string value, string? args = null) =>
        ActionValidator.Validate(new DeckAction { Type = type, Value = value, Args = args }, Cfg, Cfg.Profiles[0])
            .Where(i => i.Severity == Severity.Error).Select(i => i.Message).ToArray();

    static string[] Warnings(string type, string value) =>
        ActionValidator.Validate(new DeckAction { Type = type, Value = value }, Cfg, Cfg.Profiles[0])
            .Where(i => i.Severity == Severity.Warning).Select(i => i.Message).ToArray();

    [Theory]
    [InlineData("ctrl+shift+m")] [InlineData("win+d")] [InlineData("f13")] [InlineData("alt+tab")] [InlineData("ctrl+c")]
    public void ValidHotkeys(string combo) => Assert.Empty(Errors("hotkey", combo));

    [Theory]
    [InlineData("")] [InlineData("ctrl")] [InlineData("ctrl+shift")] [InlineData("ctrl+blabla")] [InlineData("+++")]
    public void InvalidHotkeys(string combo) => Assert.Single(Errors("hotkey", combo));

    [Fact]
    public void MediaMustBeAKnownKey()
    {
        Assert.Empty(Errors("media", "volup"));
        Assert.Single(Errors("media", "volume"));
    }

    [Fact]
    public void PagesAndProfilesMustExist()
    {
        Assert.Empty(Errors("page", "Outils"));
        Assert.Empty(Errors("page", "HOME"));
        Assert.Empty(Errors("page", "back"));
        Assert.Single(Errors("page", "Absente"));
        Assert.Single(Errors("page", ""));
        Assert.Empty(Errors("profile", "jeu"));
        Assert.Single(Errors("profile", "Fantôme"));
    }

    [Fact]
    public void DelayMustBeANonNegativeInteger()
    {
        Assert.Empty(Errors("delay", "500"));
        Assert.Single(Errors("delay", "abc"));
        Assert.Single(Errors("delay", "-5"));
        Assert.Single(Warnings("delay", "9999999").Concat(Errors("delay", "9999999")));
    }

    [Fact]
    public void UrlsAreChecked()
    {
        Assert.Empty(Errors("url", "https://example.org"));
        Assert.Empty(Errors("url", "steam://run/570"));
        Assert.Empty(Errors("url", "tel:+33123456789"));
        Assert.Single(Errors("url", ""));
        Assert.Single(Errors("url", "https://"));
        Assert.Single(Errors("url", "javascript:alert(1)"));
        Assert.Single(Errors("url", "file:///C:/Windows/System32/cmd.exe"));
        Assert.Single(Warnings("url", "pas une adresse"));
    }

    [Fact]
    public void LaunchAndScriptReportMissingFilesOnlyForRealPaths()
    {
        Assert.Single(Errors("launch", ""));
        Assert.Empty(Errors("launch", "notepad.exe"));
        Assert.Empty(Warnings("launch", "notepad.exe"));                  // résolu par le PATH : invérifiable, donc pas d'alerte
        Assert.Single(Warnings("launch", @"C:\Dossier\Qui\N\Existe\Pas.exe"));
        Assert.Empty(Warnings("launch", Environment.SystemDirectory));
        Assert.Single(Errors("script", " "));
        Assert.Empty(Warnings("script", "Get-Date"));                     // commande en ligne
        Assert.Single(Warnings("script", @"C:\nulle\part.ps1"));
    }

    [Fact]
    public void UnknownTypeIsAnError() => Assert.Single(Errors("teleport", "x"));

    [Fact]
    public void ButtonLevelChecks()
    {
        var empty = new DeckButton { Label = "x" };
        Assert.Contains(ActionValidator.Validate(empty, Cfg, null), i => i.Severity == Severity.Warning);
        Assert.Empty(ActionValidator.Validate(new DeckButton { IsFolder = true, Children = new() }, Cfg, null));
        Assert.Empty(ActionValidator.Validate(new DeckButton { Blank = true }, Cfg, null));
        var missingImage = new DeckButton { Actions = { new DeckAction { Type = "delay", Value = "1" } }, ImagePath = new string('a', 32) + ".png" };
        Assert.Contains(ActionValidator.Validate(missingImage, Cfg, null), i => i.Message.Contains("image"));
    }

    [Fact]
    public void WorstSeverityOrNull()
    {
        Assert.Null(ActionValidator.Worst(Array.Empty<Issue>()));
        Assert.Equal(Severity.Error, ActionValidator.Worst(new[] { new Issue(Severity.Warning, "a"), new Issue(Severity.Error, "b") }));
    }
}

public class ResourceTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TouchDeck.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TouchDeck.csproj introuvable");
    }

    /// <summary>Un nom d'icône faux dans un XAML ne se voit qu'à l'ouverture de la fenêtre : on les vérifie tous ici.</summary>
    [Fact]
    public void EverySymbolNamedInXamlExists()
    {
        var rx = new Regex(@"(?:Symbol=""|SymbolIcon\s+)([A-Z][A-Za-z0-9]+)(?:""|\})");
        var bad = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoRoot(), "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && !f.Contains(@"\tests\")))
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                if (!Enum.TryParse(m.Groups[1].Value, out SymbolRegular _)) bad.Add($"{Path.GetFileName(file)} : {m.Groups[1].Value}");
        Assert.Empty(bad);
    }

    [Fact]
    public void EverySymbolUsedInCSharpExists()
    {
        // Les valeurs d'enum sont vérifiées à la compilation ; ce test couvre les noms passés en chaînes (ActionCatalog, config par défaut)
        foreach (var b in DeckDocument.AllButtons(ConfigStore.DefaultConfig().Profiles.SelectMany(p => p.Pages).SelectMany(p => p.Buttons)))
            Assert.True(IconCatalog.TryResolve(b.Icon, out _), $"icône par défaut inconnue : {b.Icon}");
    }
}
