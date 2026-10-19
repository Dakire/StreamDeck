using System;
using System.Runtime.InteropServices;
using TouchDeck.Actions;

namespace TouchDeck;

/// <summary>
/// Volume et coupure des périphériques audio par défaut via Core Audio (IAudioEndpointVolume), sans dépendance externe.
/// Sortie = périphérique de lecture « Console » ; micro = périphérique d'enregistrement « Communications ».
/// </summary>
public static class NativeAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    const int ERender = 0, ECapture = 1;
    const int EConsole = 0, ECommunications = 2;
    const int ClsCtxAll = 0x17;

    static IAudioEndpointVolume Endpoint(AudioDevice device)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        bool mic = device == AudioDevice.Microphone;
        int hr = enumerator.GetDefaultAudioEndpoint(mic ? ECapture : ERender, mic ? ECommunications : EConsole, out var dev);
        if (hr != 0 || dev == null)
            throw new InvalidOperationException(mic ? "Aucun micro par défaut" : "Aucune sortie audio par défaut");
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var endpoint));
        return (IAudioEndpointVolume)endpoint;
    }

    /// <summary>true si le périphérique par défaut est coupé.</summary>
    public static bool IsMuted(AudioDevice device)
    {
        Marshal.ThrowExceptionForHR(Endpoint(device).GetMute(out var muted));
        return muted;
    }

    /// <summary>Coupe ou rétablit le périphérique par défaut.</summary>
    public static void SetMute(AudioDevice device, bool muted)
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(Endpoint(device).SetMute(muted, ref ctx));
    }

    /// <summary>Règle le volume du périphérique par défaut (0 à 100).</summary>
    public static void SetVolume(AudioDevice device, int percent)
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(Endpoint(device).SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx));
    }
}
