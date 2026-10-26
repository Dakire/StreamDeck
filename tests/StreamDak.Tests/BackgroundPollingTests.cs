using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Integrations;
using StreamDak.Integrations.WindowsSystem;
using Xunit;

namespace StreamDak.Tests;

/// <summary>Le connecteur système ralentit quand le deck est masqué et reprend au retour.</summary>
public class BackgroundPollingTests
{
    sealed class CountingProbe : ISystemProbe
    {
        int _cpu;
        public int CpuReads => Volatile.Read(ref _cpu);
        public double CpuPercent() { Interlocked.Increment(ref _cpu); return 12; }
        public double MemoryPercent() => 40;
        public (long UsedBytes, long TotalBytes) Memory() => (4L << 30, 16L << 30);
        public Task<MediaInfo?> GetMediaAsync() => Task.FromResult<MediaInfo?>(null);
        public Task<bool> SendMediaAsync(string command) => Task.FromResult(false);
        public void Dispose() { }
    }

    [Fact]
    public async Task ReadingsPauseWhileTheDeckIsHiddenAndResumeImmediatelyOnReturn()
    {
        var probe = new CountingProbe();
        using var sys = new WindowsSystemIntegration(() => probe);
        await sys.ConnectAsync(new IntegrationSettings(), CancellationToken.None);
        try
        {
            sys.SetBackground(true);
            await Task.Delay(1500);                   // laisse finir le relevé en cours
            int hidden = probe.CpuReads;
            await Task.Delay(2500);
            Assert.True(probe.CpuReads - hidden <= 1, $"{probe.CpuReads - hidden} relevés en 2,5 s alors que le deck est masqué");

            sys.SetBackground(false);
            var limit = DateTime.UtcNow.AddSeconds(2.5);
            while (probe.CpuReads - hidden < 2 && DateTime.UtcNow < limit) await Task.Delay(50);
            Assert.True(probe.CpuReads - hidden >= 2, "les relevés n'ont pas repris au retour du deck");
        }
        finally { await sys.DisconnectAsync(); }
    }

    [Fact]
    public void TheManagerForwardsTheBackgroundStateOnlyToIntegrationsThatAskForIt()
    {
        var aware = new AwareIntegration();
        var plain = new FakeIntegration("plain");
        var manager = new IntegrationManager(new IIntegration[] { aware, plain }, new DeckDocument(ConfigStore.DefaultConfig()), new MemorySecrets());
        manager.SetBackground(true);
        manager.SetBackground(false);
        Assert.Equal(new[] { true, false }, aware.Calls);
    }

    sealed class AwareIntegration : IntegrationBase, IBackgroundAware
    {
        public List<bool> Calls { get; } = new();
        public void SetBackground(bool background) => Calls.Add(background);
        public override string Id => "aware";
        public override string Name => "Aware";
        public override string Category => "Test";
        public override string Help => "";
        public override IReadOnlyList<SettingField> Fields { get; } = Array.Empty<SettingField>();
        public override IReadOnlyList<IntegrationCommand> Commands { get; } = Array.Empty<IntegrationCommand>();
        protected override Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation) => Task.CompletedTask;
        protected override Task DisconnectCoreAsync() => Task.CompletedTask;
        public override Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation) => Task.FromResult<string?>(null);
    }
}
