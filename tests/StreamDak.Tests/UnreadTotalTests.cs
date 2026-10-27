using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StreamDak.Integrations;
using StreamDak.Integrations.Notifications;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Nom accessible des badges, total de non lus sur tous les profils et InfoBadge de l'onglet.</summary>
public class BadgeDescriptionTests
{
    [Theory]
    [InlineData("1", "1 notification non lue")]
    [InlineData("3", "3 notifications non lues")]
    [InlineData("99", "99 notifications non lues")]
    [InlineData("99+", "plus de 99 notifications non lues")]
    [InlineData("•", "activité")]
    public void TheAccessibleNameUsesTheFrenchPlural(string badge, string expected) => Assert.Equal(expected, NotificationBadge.Describe(badge));

    [Fact]
    public void AnActivityBadgeHasNoValueSoTheInfoBadgeShowsADot()
    {
        var command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        var tab = new TabViewModel { Text = "Chat", Command = command, Badge = NotificationBadge.ActivityText };
        Assert.True(tab.HasBadge);
        Assert.Null(tab.BadgeValue);
        Assert.Equal("activité", tab.BadgeAutomationName);
        Assert.Equal("99+", new TabViewModel { Text = "x", Command = command, Badge = "99+" }.BadgeValue);
        Assert.False(new TabViewModel { Text = "x", Command = command }.HasBadge);
    }
}

[Collection("ui")]
public class UnreadTotalTests
{
    readonly WpfHost _host;
    public UnreadTotalTests(WpfHost host) { _host = host; }

    sealed class RaisingWatcher : INotificationWatcher
    {
        public event Action<string, int?>? CountChanged;
        public event Action<string, bool>? ActivityChanged;
        public void Start(IReadOnlySet<string> only) { }
        public void Stop() { }
        public void Refresh() { }
        public void SetBackground(bool background) { }
        public void Raise(string id, int? count) => CountChanged?.Invoke(id, count);
        public void RaiseActivity(string id, bool on) => ActivityChanged?.Invoke(id, on);
    }

    async Task WaitFor(Func<bool> condition, int seconds = 8)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < limit) { _host.Settle(); await Task.Delay(50); }
        Assert.True(condition(), "condition non atteinte à temps");
    }

    async Task<(MainViewModel Vm, RaisingWatcher Watcher)> StartAsync(DeckConfig cfg)
    {
        cfg.Integrations = new() { ["notify"] = new IntegrationConfig { Enabled = true } };
        _host.Reset(cfg);
        var watcher = new RaisingWatcher();
        var integration = new NotificationIntegration(watcher);
        var manager = new IntegrationManager(new IIntegration[] { integration }, _host.Document, new MemorySecrets());
        await integration.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows(), manager); v.Resize(1600, 900); return v; });
        _host.Settle();
        return (vm, watcher);
    }

    [Fact]
    public async Task TheTotalCoversEveryProfileAndFollowsCountsAndDocumentChanges()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Google Chat", Kind = "web", Url = "https://chat.google.com/" });
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Gmail", Kind = "web", Url = "https://mail.google.com/mail/u/0/", BadgeExcludeFromTotal = true });
        var other = new DeckProfile { Name = "Autre" };
        other.Pages.Add(new DeckPage { Name = "Discord", Kind = "web", Url = "https://discord.com/channels/@me" });
        other.Pages.Add(new DeckPage { Name = "Chat bis", Kind = "web", Url = "https://chat.google.com/u/1/" });   // même service : compté une fois
        cfg.Profiles.Add(other);
        var (vm, watcher) = await StartAsync(cfg);
        int Total() => _host.Run(() => vm.TotalUnread);

        Assert.Equal(0, Total());
        watcher.Raise("gchat", 4);
        watcher.Raise("discord", 6);
        watcher.Raise("gmail", 50);                                        // page exclue du total
        await WaitFor(() => Total() == 10);                                // le profil « Autre » n'est pas le profil courant

        watcher.Raise("discord", 500);
        await WaitFor(() => Total() == 504);                               // le total n'est pas plafonné ; seul l'affichage l'est
        Assert.Equal("99+", NotificationBadge.Text(Total()));

        var discord = other.Pages.FindIndex(p => p.Name == "Discord");
        Assert.True(_host.Run(() => _host.Document.SetPageBadgeEnabled("Autre", discord, false).Ok));
        await WaitFor(() => Total() == 4);                                 // badge masqué : retiré du total, sans nouvelle valeur

        var first = _host.Document.Config.Profiles[0];
        Assert.True(_host.Run(() => _host.Document.SetPageBadgeExcluded(first.Name, first.Pages.FindIndex(p => p.Name == "Google Chat"), true).Ok));
        await Task.Delay(100);
        _host.Settle();
        Assert.Equal(4, Total());                                          // « Chat bis » (autre profil, même service) prend le relais
        Assert.True(_host.Run(() => _host.Document.SetPageBadgeExcluded("Autre", other.Pages.FindIndex(p => p.Name == "Chat bis"), true).Ok));
        await WaitFor(() => Total() == 0);

        watcher.Raise("gchat", 0);
        watcher.RaiseActivity("gchat", true);
        await Task.Delay(100);
        _host.Settle();
        Assert.Equal(0, Total());                                          // une activité (point) ne compte pas
    }

    [Fact]
    public async Task TheTabBadgeIsAnInfoBadgeWithNoBindingErrorAndAFrenchName()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages.Add(new DeckPage { Name = "Google Chat", Kind = "web", Url = "https://chat.google.com/" });
        await StartAsync(cfg);   // prépare la configuration ; la fenêtre ci-dessous utilise les services de l'application
        var win = _host.Run(() => new MainWindow(_host.Services));
        try
        {
            _host.Render(win, "deck-onglet-infobadge", 1400, 800);
            var main = (MainViewModel)_host.Run(() => win.DataContext);
            _host.Run(() =>
            {
                var command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
                main.Tabs.Add(new TabViewModel { Text = "Chat", Command = command, Badge = "3" });
                main.Tabs.Add(new TabViewModel { Text = "Veille", Command = command, Badge = NotificationBadge.ActivityText });
                main.Tabs.Add(new TabViewModel { Text = "Rien", Command = command });
            });
            _host.Render(win, "deck-onglet-infobadge", 1400, 800);
            _host.Run(() =>
            {
                var shown = WpfHost.Descendants<Wpf.Ui.Controls.InfoBadge>((DependencyObject)win.Content)
                    .Where(b => b.DataContext is TabViewModel && b.Visibility == Visibility.Visible).ToList();
                Assert.Equal(2, shown.Count);
                Assert.Equal("3", shown[0].Value);
                Assert.Null(shown[1].Value);
                Assert.Equal("3 notifications non lues", System.Windows.Automation.AutomationProperties.GetName(shown[0]));
                Assert.Equal("activité", System.Windows.Automation.AutomationProperties.GetName(shown[1]));
                Assert.All(shown, b => Assert.Equal(Wpf.Ui.Controls.InfoBadgeSeverity.Critical, b.Severity));
            });
            lock (_host.Bindings.Messages) Assert.True(_host.Bindings.Messages.Count == 0, string.Join(" | ", _host.Bindings.Messages));
        }
        finally { _host.Run(win.Close); }
    }
}
