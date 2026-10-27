using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StreamDak.Actions;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Volume et coupure des périphériques audio par défaut via Core Audio (IAudioEndpointVolume), sans dépendance externe.
/// Sortie = périphérique de lecture « Console » ; micro = périphérique d'enregistrement « Communications ».
/// </summary>
internal static class NativeAudio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    /// <summary>PROPVARIANT (24 octets en 64 bits) : seul le texte (VT_LPWSTR) nous intéresse.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer;
        public IntPtr Extra;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

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
    const int DeviceStateActive = 1, StgmRead = 0, VtLpwstr = 31;
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

    // ---------- tous les périphériques actifs, par identifiant ----------

    /// <summary>Nom convivial d'un périphérique (« Microphone (Focusrite USB Audio) »), propriété PKEY_Device_FriendlyName.</summary>
    static string FriendlyName(IMMDevice device, string fallback)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) != 0 || store == null) return fallback;
        var key = new PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
        if (store.GetValue(ref key, out var value) != 0) return fallback;
        try { return value.Type == VtLpwstr && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) ?? fallback : fallback; }
        finally { _ = PropVariantClear(ref value); }
    }

    static string? DefaultId(IMMDeviceEnumerator enumerator, int flow)
    {
        if (enumerator.GetDefaultAudioEndpoint(flow, EConsole, out var device) != 0 || device == null) return null;
        return device.GetId(out var id) == 0 ? id : null;
    }

    /// <summary>Périphériques de lecture et d'enregistrement actifs.</summary>
    public static IReadOnlyList<AudioEndpoint> Endpoints()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        var list = new List<AudioEndpoint>();
        foreach (var (flow, kind) in new[] { (ERender, AudioFlow.Render), (ECapture, AudioFlow.Capture) })
        {
            if (enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out var collection) != 0 || collection == null) continue;
            var defaultId = DefaultId(enumerator, flow);
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0 || device == null || device.GetId(out var id) != 0) continue;
                list.Add(new AudioEndpoint(id, FriendlyName(device, id), kind, id == defaultId));
            }
        }
        return list;
    }

    static IAudioEndpointVolume EndpointById(string id)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        if (enumerator.GetDevice(id, out var dev) != 0 || dev == null) throw new InvalidOperationException("Ce périphérique audio n'est plus disponible");
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var endpoint));
        return (IAudioEndpointVolume)endpoint;
    }

    /// <summary>Niveau et coupure d'un périphérique.</summary>
    public static AudioEndpointState Read(string id)
    {
        var volume = EndpointById(id);
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return new AudioEndpointState((int)Math.Round(Math.Clamp(level, 0f, 1f) * 100), muted);
    }

    /// <summary>Règle le niveau d'un périphérique (0 à 100).</summary>
    public static void SetEndpointVolume(string id, int percent)
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(EndpointById(id).SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx));
    }

    /// <summary>Coupe ou rétablit un périphérique.</summary>
    public static void SetEndpointMute(string id, bool muted)
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(EndpointById(id).SetMute(muted, ref ctx));
    }
}
