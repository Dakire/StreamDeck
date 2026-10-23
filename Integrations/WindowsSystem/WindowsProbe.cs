using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace StreamDak.Integrations.WindowsSystem;

/// <summary>Mesures réelles : compteur de performance du processeur, mémoire du système, session média courante de Windows.</summary>
sealed class WindowsProbe : ISystemProbe
{
    readonly PerformanceCounter? _cpu;
    readonly PerformanceCounter? _availableMemory;
    GlobalSystemMediaTransportControlsSessionManager? _sessions;

    public WindowsProbe()
    {
        try { _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Log.Warn($"Système Windows : compteur du processeur indisponible : {ex.Message}");
        }
        try { _availableMemory = new PerformanceCounter("Memory", "Available Bytes", readOnly: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Log.Warn($"Système Windows : compteur de mémoire indisponible : {ex.Message}");
        }
    }

    public double CpuPercent() => _cpu == null ? 0 : _cpu.NextValue();

    public double MemoryPercent()
    {
        var (used, total) = Memory();
        return total <= 0 ? 0 : used * 100.0 / total;
    }

    public (long UsedBytes, long TotalBytes) Memory()
    {
        var info = GC.GetGCMemoryInfo();
        long total = info.TotalAvailableMemoryBytes;     // mémoire physique : constante
        if (total <= 0) return (0, 0);
        // « Available Bytes » est relevé à chaque appel ; MemoryLoadBytes ne l'est qu'à la fin d'un ramasse-miettes (valeur périmée sur un processus calme)
        if (_availableMemory == null) return (info.MemoryLoadBytes, total);
        return (Math.Clamp(total - (long)_availableMemory.NextValue(), 0, total), total);
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

    public void Dispose()
    {
        _cpu?.Dispose();
        _availableMemory?.Dispose();
    }
}
