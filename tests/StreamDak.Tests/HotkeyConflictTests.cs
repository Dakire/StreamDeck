using System.Linq;
using System.Threading.Tasks;
using StreamDak.Actions;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Raccourcis globaux : StreamDak envoie les combinaisons à l'application au premier plan. Une combinaison que Teams utilise (Ctrl+Maj+M) ou qui n'est qu'un
/// raccourci local de Discord doit être signalée, et un rebond du doigt ne doit jamais basculer deux fois le micro.
/// </summary>
public class HotkeyConflictTests
{
    static DeckButton Hotkey(string label, string combo) => new() { Label = label, Actions = { new DeckAction { Type = ActionTypes.Hotkey, Value = combo } } };

    static DeckConfig With(params DeckButton[] buttons)
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages[0].Buttons = buttons.ToList();
        return cfg;
    }

    [Theory]
    [InlineData("Shift + Ctrl+M", "ctrl+shift+m", "Ctrl+Maj+M")]
    [InlineData("control+alt+shift+d", "ctrl+alt+shift+d", "Ctrl+Alt+Maj+D")]
    [InlineData("win+shift+s", "shift+win+s", "Maj+Win+S")]
    public void CombinationsAreComparedInOneCanonicalForm(string combo, string canonical, string display)
    {
        Assert.Equal(canonical, HotkeyConflicts.Normalize(combo));
        Assert.Equal(display, HotkeyConflicts.Display(combo));
    }

    [Fact]
    public void AnOldDiscordButtonOnCtrlShiftMIsFlaggedAsTeamsMuteAndAsDiscordsLocalShortcut()
    {
        var old = Hotkey("Discord : micro", "ctrl+shift+m");
        var warning = Assert.Single(HotkeyConflicts.Warnings(old, With(old)));
        Assert.Contains("Teams : micro", warning);
        Assert.Contains("seulement quand Discord a le focus", warning);
        Assert.Contains(ActionValidator.Validate(old, With(old), null), i => i.Severity == Severity.Warning && i.Message == warning);   // visible sur la touche en édition
    }

    [Fact]
    public void TheReadyMadeDiscordAndTeamsButtonsHaveNoConflict()
    {
        foreach (var id in new[] { "preset.discord.mute", "preset.discord.deafen", "preset.teams.mute" })
        {
            var button = ActionCatalog.Find(id)!.CreateButton();
            Assert.Empty(HotkeyConflicts.Warnings(button, With(button)));
        }
        // Teams vise sa fenêtre (étape « focus ») : sa combinaison n'est pas envoyée à l'application au premier plan
        Assert.Empty(HotkeyConflicts.UntargetedHotkeys(ActionCatalog.Find("preset.teams.mute")!.CreateButton()));
        Assert.Equal("ctrl+alt+shift+m", ActionCatalog.Find("preset.discord.mute")!.Actions.Single().Value);
    }

    [Fact]
    public void TwoDifferentButtonsSendingTheSameGlobalCombinationAreReported()
    {
        var a = Hotkey("Clip OBS", "ctrl+alt+f9");
        var b = Hotkey("Marqueur", "Alt+Ctrl+F9");
        var same = Hotkey("Clip OBS", "ctrl+alt+f9");   // même bouton copié sur une autre page : pas un conflit
        var cfg = With(a, b, same);
        Assert.Contains("« Marqueur »", Assert.Single(HotkeyConflicts.Warnings(a, cfg)));
        Assert.Empty(HotkeyConflicts.Warnings(Hotkey("Libre", "ctrl+alt+f10"), cfg));
    }
}

[Collection("ui")]
public class RepeatGuardTests
{
    readonly WpfHost _host;
    public RepeatGuardTests(WpfHost host) { _host = host; }

    [Fact]
    public async Task ABouncingTapRunsAToggleOnlyOnce()
    {
        var cfg = ConfigStore.DefaultConfig();
        cfg.Profiles[0].Pages[0].Buttons = new() { new DeckButton { Label = "Micro", Icon = "Mic24", Actions = { new DeckAction { Type = ActionTypes.Hotkey, Value = "ctrl+alt+shift+m" } } } };
        _host.Reset(cfg);
        var sim = (SimulatedPlatform)_host.Run(() => _host.Engine.Context.Platform.Keyboard);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });
        var tile = _host.Run(() => vm.Tiles.OfType<DeckTileViewModel>().First(t => t.Label == "Micro"));
        int before;
        lock (sim.Log) before = sim.Log.Count(l => l == "hotkey ctrl+alt+shift+m");

        var first = _host.Run(() => vm.RunTileAsync(tile));
        var bounce = _host.Run(() => vm.RunTileAsync(tile));   // second appui dans la même milliseconde
        await Task.WhenAll(first, bounce);
        lock (sim.Log) Assert.Equal(before + 1, sim.Log.Count(l => l == "hotkey ctrl+alt+shift+m"));

        await Task.Delay(MainViewModel.RepeatGuardMs + 50);    // un vrai second appui, plus tard, passe
        await _host.Run(() => vm.RunTileAsync(tile));
        lock (sim.Log) Assert.Equal(before + 2, sim.Log.Count(l => l == "hotkey ctrl+alt+shift+m"));
    }
}
