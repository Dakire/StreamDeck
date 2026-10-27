using System.Diagnostics;
using System.Linq;
using System.Windows;
using StreamDak.Integrations;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Fermeture des paramètres : elle gelait l'interface plusieurs secondes. Cause mesurée : le nettoyage des barres de titre (<see cref="WindowCleanup"/>)
/// parcourait l'arbre visuel et l'arbre logique sans mémoire des éléments vus, soit 44,8 millions de visites pour 2 552 éléments (6,6 s).
/// </summary>
[Collection("ui")]
public class SettingsCloseTests
{
    readonly WpfHost _host;
    public SettingsCloseTests(WpfHost host) { _host = host; }

    [Fact]
    public void TheCleanupVisitsEachElementOfTheSettingsWindowOnce()
    {
        _host.Reset();
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        _host.Render(win, "settings-close", 1100, 900);   // arbre visuel complet, comme une fenêtre affichée
        _host.Run(() =>
        {
            var visited = WindowCleanup.Descendants((DependencyObject)win.Content).ToList();
            Assert.True(visited.Count > 500, $"arbre trop petit pour que le test ait un sens : {visited.Count}");
            Assert.Equal(visited.Count, visited.Distinct().Count());
            win.Close();
        });
    }

    [Fact]
    public void ClosingTheRenderedSettingsWindowTakesWellUnderASecond()
    {
        _host.Reset();
        var win = _host.Run(() => new SettingsWindow(_host.Services));
        _host.Render(win, "settings-close-timing", 1100, 900);
        long ms = _host.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            win.Close();
            return sw.ElapsedMilliseconds;
        });
        Assert.True(ms < 1000, $"fermeture des paramètres en {ms} ms");
    }

    [Fact]
    public void TypingInAnIntegrationFieldIsSavedOnceAfterThePauseOrWhenTheSettingsClose()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var doc = new DeckDocument(ConfigStore.DefaultConfig());
            var secrets = new MemorySecrets();
            var fake = new FakeIntegration();
            using var manager = new IntegrationManager(new IIntegration[] { fake }, doc, secrets);
            var card = new IntegrationCardViewModel(fake, manager, doc, secrets);
            int changes = 0;
            doc.Changed += _ => changes++;

            var password = card.Fields.Single(f => f.IsSecret);
            foreach (var text in new[] { "s", "se", "sec", "secret" }) password.Text = text;
            Assert.Equal(0, changes);                                                   // rien d'écrit pendant la frappe
            Assert.Null(secrets.Get(IntegrationManager.SecretKey("fake", "password")));

            card.Dispose();                                                              // fermeture des paramètres : la saisie n'est pas perdue
            Assert.Equal("secret", secrets.Get(IntegrationManager.SecretKey("fake", "password")));
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void AnIntegrationCardChangeDoesNotReloadTheWholeSettingsPage()
    {
        _host.Reset();
        _host.Run(() =>
        {
            var win = new SettingsWindow(_host.Services);
            var vm = (SettingsViewModel)win.DataContext;
            var screens = vm.Screens.ToList();
            _host.Document.UpdateSettings(c => c.Integrations ??= new(), IntegrationCardViewModel.ChangeLabel);
            Assert.NotEmpty(screens);
            Assert.Same(screens[0], vm.Screens[0]);   // même instance : la liste des écrans n'a pas été reconstruite
            _host.Document.UpdateSettings(c => c.Spacing += 1);
            Assert.NotSame(screens.FirstOrDefault(), vm.Screens.FirstOrDefault());
            win.Close();
        });
    }
}
