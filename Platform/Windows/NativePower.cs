using System;
using System.Runtime.InteropServices;

namespace StreamDak.Platform.Windows;

/// <summary>État de l'alimentation par GetSystemPowerStatus (secteur, batterie, charge, autonomie).</summary>
internal static class NativePower
{
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    const byte NoBattery = 128, Unknown = 255, ChargingFlag = 8;

    /// <summary>État de la batterie, ou null si Windows ne répond pas.</summary>
    public static BatteryState? Read()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if (s.BatteryFlag == Unknown && s.BatteryLifePercent == Unknown) return null;
        if ((s.BatteryFlag & NoBattery) != 0) return new BatteryState(false, null, s.ACLineStatus == 1, null);
        int? percent = s.BatteryLifePercent == Unknown ? null : Math.Min(100, (int)s.BatteryLifePercent);
        bool charging = s.ACLineStatus == 1 || (s.BatteryFlag & ChargingFlag) != 0;
        TimeSpan? left = s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null;
        return new BatteryState(true, percent, charging, left);
    }
}
