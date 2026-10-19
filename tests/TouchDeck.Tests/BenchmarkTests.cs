using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TouchDeck.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TouchDeck.Tests;

/// <summary>
/// Mesures de performance du deck hors écran (création de la fenêtre, rendu d'une grille de 32 touches, mémoire, CPU au repos).
/// Désactivées par défaut : définir <c>TOUCHDECK_BENCH</c> (dossier) pour les lancer ; les résultats y sont ajoutés
/// dans <c>bench.md</c>. Le rendu logiciel (RenderTargetBitmap) amplifie le coût des effets, ce qui suffit à comparer avant / après.
/// </summary>
[Collection("ui")]
public class BenchmarkTests
{
    readonly WpfHost _host;
    readonly ITestOutputHelper _out;
    public BenchmarkTests(WpfHost host, ITestOutputHelper output) { _host = host; _out = output; }

    static DeckConfig Grid32()
    {
        var cfg = ConfigStore.DefaultConfig();
        var profile = cfg.Profiles[0];
        profile.Columns = 8; profile.Rows = 4;
        var page = profile.Pages[0];
        page.Buttons.Clear();
        string[] icons = { "Play24", "Mic24", "Copy24", "Globe24", "Folder24", "Keyboard24", "Timer24", "Star24" };
        for (int i = 0; i < 32; i++)
            page.Buttons.Add(new DeckButton
            {
                Label = "Touche " + (i + 1), Icon = icons[i % icons.Length], Color = i % 2 == 0 ? "#7C3AED" : "#1E7F5F",
                Actions = { new DeckAction { Type = "delay", Value = "1" } },
            });
        return cfg;
    }

    [Fact]
    public void DeckRenderingBenchmark()
    {
        var dir = Environment.GetEnvironmentVariable("TOUCHDECK_BENCH");
        if (string.IsNullOrEmpty(dir)) { _out.WriteLine("TOUCHDECK_BENCH non défini : mesure ignorée"); return; }
        Directory.CreateDirectory(dir);

        var lines = new System.Collections.Generic.List<string> { $"## Mesure du {DateTime.Now:yyyy-MM-dd HH:mm}", "" };
        // Premier passage de chauffe non compté : JIT et recompilation en arrière-plan fausseraient la première variante
        foreach (var (reduce, warmup) in new[] { (false, true), (false, false), (true, false) })
        {
            var cfg = Grid32();
            cfg.ReduceMotion = reduce;
            _host.Reset(cfg);
            _host.Run(() => AppServices.Document.UpdateSettings(c => c.ReduceMotion = reduce));
            _host.Settle();

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var win = _host.Run(() => new MainWindow());
            _host.Render(win, "bench-first", 1920, 1080);
            double firstMs = sw.Elapsed.TotalMilliseconds;
            try
            {
                int tiles = _host.Run(() => ((MainViewModel)win.DataContext).Tiles.OfType<DeckTileViewModel>().Count());
                Assert.Equal(32, tiles);

                // Rendu répété : médiane de 15 rendus complets de la fenêtre en 1920×1080
                var times = Enumerable.Range(0, 15).Select(_ => _host.Run(() =>
                {
                    var root = (FrameworkElement)win.Content;
                    var t = Stopwatch.StartNew();
                    var rtb = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    return t.Elapsed.TotalMilliseconds;
                })).OrderBy(x => x).ToArray();
                double median = times[times.Length / 2];

                // Reconstruction de la grille (changement de document) : médiane de 15
                var rebuilds = Enumerable.Range(0, 15).Select(i => _host.Run(() =>
                {
                    var t = Stopwatch.StartNew();
                    AppServices.Document.UpdateSettings(c => c.Spacing = 14 + (i % 2));
                    ((FrameworkElement)win.Content).UpdateLayout();
                    return t.Elapsed.TotalMilliseconds;
                })).OrderBy(x => x).ToArray();

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long memAfter = GC.GetTotalMemory(true);

                // CPU au repos : 5 s sans interaction, fenêtre ouverte (minuteurs du deck)
                var proc = Process.GetCurrentProcess();
                var cpu0 = proc.TotalProcessorTime;
                Thread.Sleep(5000);
                proc.Refresh();
                double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;

                if (warmup) continue;
                var variant = reduce ? "sans animations (style statique, sans ombre de touche)" : "style animé (par défaut)";
                lines.Add($"### Deck 32 touches, {variant}");
                lines.Add("");
                lines.Add("| Mesure | Valeur |");
                lines.Add("|---|---|");
                lines.Add($"| Création + premier rendu | {firstMs:0} ms |");
                lines.Add($"| Rendu complet 1920×1080 (médiane de 15) | {median:0.0} ms |");
                lines.Add($"| Reconstruction de la grille (médiane de 15) | {rebuilds[rebuilds.Length / 2]:0.0} ms |");
                lines.Add($"| Mémoire managée ajoutée par la fenêtre | {(memAfter - memBefore) / 1024.0 / 1024.0:0.0} Mo |");
                lines.Add($"| CPU du processus de test en 5 s de repos | {cpuMs:0} ms |");
                lines.Add("");
            }
            finally { _host.Run(win.Close); }
        }
        var text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        File.AppendAllText(Path.Combine(dir, "bench.md"), text);
        _out.WriteLine(text);
    }
}
