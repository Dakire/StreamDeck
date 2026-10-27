using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StreamDak.Actions;
using StreamDak.Audio;
using StreamDak.Controls;
using StreamDak.Integrations;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Cibles de niveau (Discord compris), mélangeur et libellés : un volume Windows n'est jamais présenté comme un réglage de Sonar.</summary>
public class AudioTargetTests
{
    [Theory]
    [InlineData("discord:input", true, "input")]
    [InlineData("Discord:OUTPUT", true, "output")]
    [InlineData("discord:gain", false, "")]
    public void DiscordTargetsAreTheInputAndTheOutputOnly(string text, bool ok, string name)
    {
        Assert.Equal(ok, LevelTarget.TryParse(text, out var target));
        if (!ok) return;
        Assert.Equal(LevelBackend.Discord, target.Backend);
        Assert.Equal(name, target.Name);
        Assert.Equal("discord:" + name, target.ToText());
    }

    [Fact]
    public void TheMixerShowsFiveSonarChannelsByDefaultAndIgnoresUnreadableOrRepeatedTargets()
    {
        Assert.Equal(new[] { "sonar:game", "sonar:chatRender", "sonar:media", "sonar:aux", "sonar:chatCapture" }, LevelOptions.MixerTargets(null).Select(t => t.ToText()));
        Assert.Equal(new[] { "sonar:game", "discord:input" }, LevelOptions.MixerTargets("sonar:game, n'importe, discord:input, SONAR:game").Select(t => t.ToText()));
        Assert.Equal(8, LevelOptions.MixerTargets(string.Join(",", Enumerable.Range(0, 12).Select(i => "windows:périphérique " + i))).Count);
        Assert.Equal(5, LevelOptions.MixerTargets("illisible").Count);   // jamais un mélangeur vide
    }

    [Fact]
    public void EachSourceSaysPreciselyWhatItControls()
    {
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        using var manager = new IntegrationManager(Array.Empty<IIntegration>(), doc, new MemorySecrets());
        var sim = new SimulatedPlatform();

        var game = LevelSources.Create("sonar:game", manager, sim)!;
        Assert.Contains("Mélangeur Sonar", game.Controls);
        Assert.Contains("pas le volume Windows", game.Controls);
        Assert.Equal("Games24", game.Icon);

        var mic = LevelSources.Create("sonar:chatCapture", manager, sim)!;
        Assert.Contains("pas le gain de la carte", mic.Controls);

        var device = LevelSources.Create("windows:render:Haut-parleurs", manager, sim)!;
        Assert.StartsWith("Volume Windows", device.Controls);
        Assert.Contains("pas Sonar", device.Controls);
        Assert.Equal(50, device.ResetLevel);
        Assert.Contains("Niveau d'entrée Windows", LevelSources.Create("windows:capture:Microphone", manager, sim)!.Controls);

        Assert.Contains("Volume d'entrée de Discord", LevelSources.Create("discord:input", manager, sim)!.Controls);
        Assert.Equal("Headphones24", LevelSources.Create("discord:output", manager, sim)!.Icon);
    }
}

public class DiscordLevelSourceTests
{
    sealed class StubDiscord : IntegrationBase
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public override string Id => "discord";
        public override string Name => "Faux Discord";
        public override string Category => "Discord";
        public override string Help => "";
        public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();
        public override IReadOnlyList<IntegrationCommand> Commands { get; } = Array.Empty<IntegrationCommand>();
        protected override Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
        {
            SetValues(new Dictionary<string, string> { ["input.volume"] = "70", ["output.volume"] = "150", ["mute"] = "false", ["deaf"] = "true" });
            return Task.CompletedTask;
        }
        protected override Task DisconnectCoreAsync() => Task.CompletedTask;
        public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation) { Sent.Enqueue($"{command} {argument}".Trim()); return Task.FromResult<string?>(null); }
    }

    static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task DiscordVolumesAreReadFromTheIntegrationAndSetThroughItsCommands()
    {
        var discord = new StubDiscord();
        var doc = new DeckDocument(ConfigStore.DefaultConfig());
        using var manager = new IntegrationManager(new IIntegration[] { discord }, doc, new MemorySecrets());
        using var input = new DiscordLevelSource(manager, "input");
        Assert.Null(input.Read(out var problem));
        Assert.Contains("Active Discord", problem);

        doc.UpdateSettings(c => (c.Integrations ??= new())["discord"] = new IntegrationConfig { Enabled = true });
        manager.Start();
        await Until(() => discord.State == ConnectionState.Connected);

        Assert.Equal(new LevelReading(70, false), input.Read(out _));
        using var output = new DiscordLevelSource(manager, "output");
        Assert.Equal(new LevelReading(100, true), output.Read(out _));   // 150 % de Discord : le contrôle va jusqu'à 100 ; sourdine = sortie coupée

        await input.SetLevelAsync(42);
        await input.SetMutedAsync(true);
        await output.SetMutedAsync(false);
        Assert.Equal(new[] { "inputvolume 42", "mute", "undeafen" }, discord.Sent);
    }
}

public class LevelResetTests
{
    sealed class Source : ILevelSource
    {
        public ConcurrentQueue<int> Set { get; } = new();
        public string Title => "Essai";
        public int ResetLevel => 80;
        public TimeSpan? PollInterval => null;
        public event Action? Changed { add { } remove { } }
        public LevelReading? Read(out string problem) { problem = ""; return new LevelReading(20, false); }
        public Task SetLevelAsync(int percent) { Set.Enqueue(percent); return Task.CompletedTask; }
        public Task SetMutedAsync(bool muted) => Task.CompletedTask;
    }

    [Fact]
    public async Task ResetGoesBackToTheWidgetsLevelOrTheSourcesOne()
    {
        var source = new Source();
        var tile = new LevelWidgetViewModel(source, LevelShape.HorizontalSlider, a => a(), delay: _ => Task.CompletedTask) { Button = new DeckButton() };
        Assert.Equal("Revenir à 80 %", tile.ResetText);
        tile.ResetCommand.Execute(null);
        Assert.Equal(80, tile.Level);

        var own = new LevelWidgetViewModel(source, LevelShape.Wheel, a => a(), delay: _ => Task.CompletedTask, resetLevel: 35) { Button = new DeckButton() };
        own.ResetCommand.Execute(null);
        for (int i = 0; i < 100 && source.Set.Count < 2; i++) await Task.Delay(10);
        Assert.Equal(new[] { 80, 35 }, source.Set);
        Assert.False(own.ShowMuteButton);   // la roue coupe au toucher du centre
        Assert.True(tile.ShowMuteButton);
    }
}

[Collection("ui")]
public class DeckMixerTests
{
    readonly WpfHost _host;
    public DeckMixerTests(WpfHost host) => _host = host;

    static DeckConfig Config() => new()
    {
        Version = ConfigNormalizer.CurrentVersion, ActiveProfile = "P", Theme = "dark", Fullscreen = false,
        Profiles = { new DeckProfile { Name = "P", Columns = 5, Rows = 3, Pages =
        {
            new DeckPage { Name = "A", Buttons =
            {
                new DeckButton
                {
                    Label = "Mélangeur", Widget = WidgetTypes.Mixer, ColSpan = 3, RowSpan = 3, Color = "#C2410C",
                    WidgetOptions = new() { ["sources"] = "windows:capture:Microphone,windows:render:Haut-parleurs,sonar:game", ["reset"] = "60" },
                },
            } },
            new DeckPage { Name = "B" },
        } } },
    };

    [Fact]
    public async Task TheMixerShowsOneLineperChannelSendsTheResetLevelAndStopsWhenThePageChanges()
    {
        foreach (var theme in new[] { "dark", "light" })
        {
            _host.Reset(Config());
            _host.SetTheme(theme);
            var sim = (SimulatedPlatform)_host.Services.GetRequiredService<PlatformServices>().Audio;
            var win = _host.Run(() => new MainWindow(_host.Services));
            try
            {
                _host.Run(() => ((MainViewModel)win.DataContext).Resize(1600, 900));
                _host.Settle();
                var vm = _host.Run(() => (MainViewModel)win.DataContext);
                var mixer = _host.Run(() => vm.Tiles.OfType<MixerWidgetViewModel>().Single());
                Assert.Equal(new[] { "Mic24", "Speaker224", "Games24" }, mixer.Channels.Select(c => c.Symbol.ToString()));
                for (int i = 0; i < 300 && !_host.Run(() => mixer.Channels[0].HasReading && mixer.Channels[1].HasReading); i++) await Task.Delay(10);
                Assert.True(_host.Run(() => mixer.Channels[1].IsAvailable));
                Assert.False(_host.Run(() => mixer.Channels[2].IsAvailable));   // Sonar n'est pas activé : la ligne le dit, sans faux zéro
                Assert.Contains("Active Sonar", _host.Run(() => mixer.Channels[2].Status));
                _host.Render(win, "deck-melangeur-" + theme, 1600, 900);

                var sliders = _host.Run(() => WpfHost.Descendants<LevelControl>((DependencyObject)win.Content).Where(c => c.Shape == LevelShape.HorizontalSlider && c.Visibility == Visibility.Visible).ToList());
                Assert.Equal(2, sliders.Count);
                Assert.All(sliders, s => Assert.True(_host.Run(() => s.ActualHeight) >= 24, "curseur trop écrasé"));
                var buttons = _host.Run(() => WpfHost.Descendants<System.Windows.Controls.Button>((DependencyObject)win.Content)
                    .Where(b => b.DataContext is LevelWidgetViewModel && b.Visibility == Visibility.Visible).ToList());
                Assert.Equal(6, buttons.Count);                                   // couper et réinitialiser par ligne
                Assert.All(buttons, b => Assert.True(_host.Run(() => b.ActualHeight) >= 48, "cible tactile < 48 px"));

                _host.Run(() => mixer.Channels[1].ResetCommand.Execute(null));
                for (int i = 0; i < 300 && sim.ReadEndpoint("render-default").Percent != 60; i++) await Task.Delay(10);
                Assert.Equal(60, sim.ReadEndpoint("render-default").Percent);

                _host.Run(() => vm.Tabs.First(t => t.Text == "B").Command.Execute(null));
                Assert.All(mixer.Channels, c => Assert.True(c.IsStopped));        // relevés arrêtés, sources libérées
            }
            finally { _host.Run(win.Close); }
        }
    }
}
