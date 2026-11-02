using System;
using System.Diagnostics;
using System.Linq;
using StreamDak.ViewModels;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Test de charge scripté (audit 2) : 1 000 changements de page, ouvertures et fermetures répétées des paramètres, modifications de la configuration. La mémoire, le nombre de
/// threads et de handles doivent rester stables et aucun changement de page ne doit durer plus de 250 ms (le seuil visé est 100 ms ; la marge absorbe une machine de CI chargée).
/// Les pages web réelles et les services de badges demandent WebView2 et des comptes : leur mesure est décrite dans docs/phase-audit2-report.md.
/// </summary>
[Collection("ui")]
public class LoadTests
{
    readonly WpfHost _host;
    public LoadTests(WpfHost host) { _host = host; }

    static (long Memory, int Threads, int Handles) Snapshot()
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        using var p = Process.GetCurrentProcess();
        return (GC.GetTotalMemory(true), p.Threads.Count, p.HandleCount);
    }

    [Fact]
    public void ThousandPageChangesAndRepeatedSettingsWindowsKeepResourcesStableAndTheInterfaceResponsive()
    {
        var cfg = ConfigStore.DefaultConfig();
        _host.Reset(cfg);
        var vm = _host.Run(() => { var v = _host.Create<MainViewModel>(new FakeDeckShell(), new FakeAppWindows()); v.Resize(1600, 900); return v; });

        long slowestPage = 0, slowestClose = 0;
        void Timed(Action action, bool window = false)
        {
            long start = Stopwatch.GetTimestamp();
            _host.Run(action);
            long ms = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (window) slowestClose = Math.Max(slowestClose, ms); else slowestPage = Math.Max(slowestPage, ms);
        }

        // Chauffe : caches, JIT, premiers threads du pool
        for (int i = 0; i < 40; i++) Timed(() => { foreach (var tab in vm.Tabs.ToList()) tab.Command.Execute(null); });
        _host.Settle();
        slowestPage = slowestClose = 0;
        var before = Snapshot();

        for (int round = 0; round < 1000; round++)
            Timed(() =>
            {
                var tabs = vm.Tabs.ToList();
                if (tabs.Count > 0) tabs[round % tabs.Count].Command.Execute(null);
            });
        for (int i = 0; i < 20; i++)
        {
            var window = _host.Run(() => new SettingsWindow(_host.Services));
            Timed(window.Close, window: true);
            _host.Run(() => _host.Document.UpdateSettings(c => c.Spacing = 14 + (i % 3)));
        }
        _host.Settle();
        var after = Snapshot();

        double growthMb = (after.Memory - before.Memory) / 1048576.0;
        Assert.True(growthMb < 30, $"mémoire : +{growthMb:0.0} Mo après la charge");
        Assert.True(after.Threads - before.Threads < 25, $"threads : +{after.Threads - before.Threads}");
        Assert.True(after.Handles - before.Handles < 300, $"handles : +{after.Handles - before.Handles}");
        // Un changement de page reste court ; la fermeture d'une fenêtre (Mica, ramasse-miettes d'une suite chargée) est jugée plus largement
        Assert.True(slowestPage < 250, $"changement de page le plus lent : {slowestPage} ms");
        Assert.True(slowestClose < 1500, $"fermeture de fenêtre la plus lente : {slowestClose} ms");
    }
}
