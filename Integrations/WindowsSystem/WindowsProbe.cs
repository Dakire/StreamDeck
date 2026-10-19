using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace TouchDeck.Integrations.WindowsSystem;

/// <summary>Mesures réelles : compteur de performance du processeur, mémoire du système, session média courante de Windows.</summary>
sealed class WindowsProbe : ISystemProbe
{
    readonly PerformanceCounter? _cpu;
    GlobalSystemMediaTransportControlsSessionManager? _sessions;

    public WindowsProbe()
    {
        try { _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Log.Warn($"Système Windows : compteur du processeur indisponible : {ex.Message}");
        }
    }

    public double CpuPercent() => _cpu == null ? 0 : _cpu.NextValue();

    public double MemoryPercent()
    {
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes <= 0 ? 0 : info.MemoryLoadBytes * 100.0 / info.TotalAvailableMemoryBytes;
    }

    async Task<GlobalSystemMediaTransportControlsSession?> CurrentAsync()
    {
        _sessions ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _sessions.GetCurrentSession();
    }

    public async Task<MediaInfo?> GetMediaAsync()
    {
        var session = await CurrentAsync();
        if (session == null) return null;
        var props = await session.TryGetMediaPropertiesAsync();
        var status = session.GetPlaybackInfo()?.PlaybackStatus;
        return new MediaInfo(props?.Title ?? "", props?.Artist ?? "", Friendly(session.SourceAppUserModelId),
            status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
    }

    public async Task<bool> SendMediaAsync(string command)
    {
        var session = await CurrentAsync();
        if (session == null) return false;
        return command switch
        {
            "playpause" => await session.TryTogglePlayPauseAsync(),
            "next" => await session.TrySkipNextAsync(),
            "previous" => await session.TrySkipPreviousAsync(),
            _ => false,
        };
    }

    // « Spotify.exe » → « Spotify » ; « Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic » → « Microsoft.ZuneMusic »
    static string Friendly(string appId)
    {
        var name = appId.Contains('!') ? appId[(appId.IndexOf('!') + 1)..] : appId;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public void Dispose() => _cpu?.Dispose();
}
