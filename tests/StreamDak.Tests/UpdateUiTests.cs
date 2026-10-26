using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Controls;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Les mises à jour dans l'interface : carte des paramètres, indicateur discret du deck, notes en Markdown.</summary>
[Collection("ui")]
public class UpdateUiTests
{
    readonly WpfHost _host;
    public UpdateUiTests(WpfHost host) { _host = host; }

    static ReleaseInfo Release() => GitHubReleases.ParseRelease(System.Text.Json.Nodes.JsonNode.Parse(GitHubReleaseParsingTests.Release("v1.4.0",
        body: "# Nouveautés\n\n- **Widgets** de données : ping, météo, disque\n- Badges de notifications `99+`\n\n## Corrections\n1. Un défaut de *contraste* en thème clair\n2. [Détails](https://github.com/Dakire/StreamDeck)\n\n---\nMerci !",
        assets: new[] { GitHubReleaseParsingTests.Asset("v1.4.0", "StreamDak-1.4.0-self-contained-Setup.exe", 5000), GitHubReleaseParsingTests.Asset("v1.4.0", "checksums.txt", 100) })),
        new UpdateRepo("Dakire", "StreamDeck"))!;

    string[] BindingProblems() { lock (_host.Bindings.Messages) return _host.Bindings.Messages.Select(m => m.Length > 400 ? m[..400] : m).ToArray(); }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void TheSettingsShowTheVersionTheNotesAndTheThreeChoicesWithTouchSizedButtons(string theme)
    {
        _host.Reset();
        _host.SetTheme(theme);
        var updates = _host.Services.GetRequiredService<UpdateService>();
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        try
        {
            _host.Run(() =>
            {
                updates.Release = Release();
                updates.StatusText = "Version 1.4.0 disponible (tu as la 1.0.0).";
            });
            _host.Settle();
            _host.Render(win, $"parametres-mises-a-jour-{theme}", 820, 2100);

            _host.Run(() =>
            {
                var root = (DependencyObject)win.Content;
                var markdown = WpfHost.Descendants<MarkdownView>(root).Single();
                Assert.True(markdown.Children.Count >= 8, $"{markdown.Children.Count} blocs de notes");            // titres, puces, numéros, séparateur, paragraphe
                var buttons = WpfHost.Descendants<Wpf.Ui.Controls.Button>(root).Where(b => b.IsVisible || b.Visibility == Visibility.Visible).ToList();
                foreach (var name in new[] { "Rechercher des mises à jour", "Plus tard", "Ignorer cette version" })
                {
                    var button = buttons.Single(b => Equals(System.Windows.Automation.AutomationProperties.GetName(b), name));
                    Assert.True(button.ActualHeight >= 48, $"{name} : {button.ActualHeight} px");
                }
                Assert.DoesNotContain(buttons, b => Equals(System.Windows.Automation.AutomationProperties.GetName(b), "Installer maintenant") && b.ActualHeight > 0);   // copie non installée : « Voir la release »
                Assert.Contains(buttons, b => Equals(System.Windows.Automation.AutomationProperties.GetName(b), "Voir la release sur GitHub"));
            });
            { var problems = BindingProblems(); Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems)); }
        }
        finally { _host.Run(() => { updates.Release = null; win.Close(); }); }
    }

    [Fact]
    public void WithoutANewVersionThePanelIsFoldedAndOnlyTheSearchButtonShows()
    {
        _host.Reset();
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        try
        {
            _host.Settle();
            _host.Render(win, "parametres-mises-a-jour-aucune", 820, 1500);
            _host.Run(() =>
            {
                var root = (DependencyObject)win.Content;
                Assert.DoesNotContain(WpfHost.Descendants<MarkdownView>(root), m => m.IsVisible);
                Assert.Contains(WpfHost.Descendants<Wpf.Ui.Controls.Button>(root), b => Equals(System.Windows.Automation.AutomationProperties.GetName(b), "Rechercher des mises à jour") && b.ActualHeight >= 48);
            });
            { var problems = BindingProblems(); Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems)); }
        }
        finally { _host.Run(win.Close); }
    }

    [Fact]
    public void TheDeckShowsADiscreetDotAndMenuEntryOnlyWhileAVersionIsAvailable()
    {
        _host.Reset();
        var updates = _host.Services.GetRequiredService<UpdateService>();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        _host.Run(() =>
        {
            Assert.False(vm.UpdateAvailable);
            Assert.Equal("Menu", vm.MenuTooltip);

            updates.Release = Release();
            Assert.True(vm.UpdateAvailable);
            Assert.Equal("Menu · version 1.4.0 disponible", vm.MenuTooltip);

            updates.Ignore();                                              // ignorée : plus de point ni d'entrée
            Assert.False(vm.UpdateAvailable);
            updates.Release = null;
        });
    }

    [Fact]
    public void TheDeckGearRendersTheDotWithoutBindingErrors()
    {
        _host.Reset();
        var updates = _host.Services.GetRequiredService<UpdateService>();
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Run(() => updates.Release = Release());
            _host.Settle();
            _host.Render(win, "deck-mise-a-jour-disponible", 1600, 900);
            _host.Run(() =>
            {
                var dot = WpfHost.Descendants<System.Windows.Shapes.Ellipse>((DependencyObject)win.Content).Where(e => e.Width == 9 && e.Visibility == Visibility.Visible).ToList();
                Assert.Single(dot);
                Assert.False(dot[0].IsHitTestVisible);                    // jamais une cible à toucher : la touche reste le bouton de menu
            });
            { var problems = BindingProblems(); Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems)); }
        }
        finally { _host.Run(() => { updates.Release = null; win.Close(); }); }
    }

    [Fact]
    public void TheNotesViewRendersEveryBlockKindAsPlainTextWithNoLinkFollowing()
    {
        _host.Reset();
        var view = _host.Run(() => new MarkdownView { Blocks = SimpleMarkdown.Parse("# T\npara **g** *i* `c` [l](https://x.fr)\n- puce\n1. num\n---\n```\ncode\n```\n<script>x</script>") });
        _host.Run(() =>
        {
            var texts = WpfHost.Descendants<TextBlock>(view).Select(t => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))).ToList();
            Assert.Contains("T", texts);
            Assert.Contains("para g i c l", texts);
            Assert.Contains("code", texts);
            Assert.Contains("<script>x</script>", texts);                                                   // du texte, jamais du balisage
            var link = WpfHost.Descendants<TextBlock>(view).SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Run>()).Single(r => r.Text == "l");
            Assert.Equal("https://x.fr", link.ToolTip);
            Assert.Null(link.Parent is System.Windows.Documents.Hyperlink ? link.Parent : null);            // pas un Hyperlink : rien ne s'ouvre au toucher
        });
    }
}
