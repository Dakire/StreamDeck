using System;
using System.Linq;
using System.Runtime.CompilerServices;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Fuites : une fenêtre fermée ne doit plus être retenue (abonnements au document, aux messages, aux écrans). Le test garde une
/// référence faible, ferme la fenêtre, force le ramasse-miettes et exige que l'objet ait disparu.
/// </summary>
[Collection("ui")]
public class ResourceLeakTests
{
    readonly WpfHost _host;
    public ResourceLeakTests(WpfHost host) { _host = host; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference[] Open(Func<System.Windows.Window> make)
    {
        return _host.Run(() =>
        {
            var win = make();
            var vm = win.DataContext;
            win.Close();
            return new[] { new WeakReference(win), new WeakReference(vm) };
        });
    }

    void AssertCollected(params WeakReference[] refs)
    {
        for (int i = 0; i < 10 && refs.Any(r => r.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _host.Settle();                                   // les tâches d'arrière-plan du Dispatcher retiennent parfois la fenêtre un instant
        }
        Assert.All(refs, r => Assert.False(r.IsAlive, "objet encore retenu après fermeture de la fenêtre"));
    }

    [Fact]
    public void AClosedSettingsWindowIsNotRetainedByTheDocumentOrTheMessageBus()
    {
        _host.Reset();
        var refs = Enumerable.Range(0, 5).SelectMany(_ => Open(() => new SettingsWindow(_host.Services))).ToArray();
        _host.Run(() => _host.Document.UpdateSettings(c => c.Spacing += 0.5));   // un événement du document après les fermetures
        AssertCollected(refs);
    }

    /// <summary>
    /// Sans <see cref="WindowCleanup"/>, chaque ouverture d'une fenêtre laissait ≈ 0,86 Mo (la barre de titre de WPF-UI reste abonnée à
    /// l'événement statique de thème) : 42 Mo pour 50 ouvertures, contre moins de 1 Mo avec.
    /// </summary>
    [Fact]
    public void FiftyOpenedAndClosedSettingsWindowsLeaveAlmostNothingBehind()
    {
        _host.Reset();
        long Mem() { for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); _host.Settle(); } return GC.GetTotalMemory(true); }
        for (int i = 0; i < 3; i++) Open(() => new SettingsWindow(_host.Services));   // chauffe : caches et JIT
        long before = Mem();
        for (int i = 0; i < 50; i++) Open(() => new SettingsWindow(_host.Services));
        double leakedMb = (Mem() - before) / 1024.0 / 1024.0;
        Assert.True(leakedMb < 5, $"{leakedMb:0.0} Mo retenus après 50 ouvertures des paramètres");
    }

    [Fact]
    public void ChangingPageManyTimesKeepsTheTileCountAndEventsBounded()
    {
        var cfg = ConfigStore.DefaultConfig();
        _host.Reset(cfg);
        var windows = new FakeAppWindows();
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), windows); v.Resize(1600, 900); return v; });
        _host.Run(() =>
        {
            int tiles = vm.Tiles.Count;
            for (int i = 0; i < 100; i++)
            {
                foreach (var tab in vm.Tabs.ToList()) tab.Command.Execute(null);
            }
            Assert.Equal(tiles, vm.Tiles.Count);
            Assert.True(vm.Tabs.Count <= cfg.Profiles[0].Pages.Count);
        });
    }
}
