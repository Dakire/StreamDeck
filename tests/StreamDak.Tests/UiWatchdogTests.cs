using System.Collections.Generic;
using System.Windows.Threading;
using Xunit;

namespace StreamDak.Tests;

public class UiWatchdogTests
{
    // Un dispatcher dont personne ne pompe la file : il ne répond jamais, comme un thread d'interface bloqué
    static Dispatcher DeadDispatcher()
    {
        Dispatcher? d = null;
        var started = new System.Threading.ManualResetEventSlim();
        var t = new System.Threading.Thread(() => { d = Dispatcher.CurrentDispatcher; started.Set(); System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); }) { IsBackground = true };
        t.Start();
        started.Wait();
        return d!;
    }

    [Fact]
    public void AnUnansweredInterfaceIsReportedOncePerBlockage()
    {
        var log = new List<string>();
        long now = 0;
        var dog = new UiWatchdog(DeadDispatcher(), () => now, log.Add);

        dog.Check();
        Assert.Empty(log);
        now = 2_999; dog.Check();
        Assert.Empty(log);                                   // sous le seuil
        now = 3_000; dog.Check();
        now = 9_000; dog.Check();
        Assert.Single(log);                                  // une seule fois par blocage
        Assert.Contains("sans réponse", log[0]);
    }
}
