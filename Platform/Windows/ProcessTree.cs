using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamDak.Platform.Windows;

/// <summary>Processus descendants de l'application (les processus <c>msedgewebview2</c> qu'elle a lancés, sans compter ceux d'autres programmes).</summary>
internal static class ProcessTree
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    const uint TH32CS_SNAPPROCESS = 0x2;

    /// <summary>Nombre et mémoire de travail des descendants de ce processus dont l'exécutable porte ce nom (sans .exe).</summary>
    public static ProcessUsage Measure(string exeName)
    {
        var parentOf = new Dictionary<uint, uint>();
        var named = new List<uint>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return new ProcessUsage(0, 0);
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                parentOf[entry.th32ProcessID] = entry.th32ParentProcessID;
                if (string.Equals(entry.szExeFile, exeName + ".exe", StringComparison.OrdinalIgnoreCase)) named.Add(entry.th32ProcessID);
            }
        }
        finally { CloseHandle(snapshot); }

        uint self = (uint)Environment.ProcessId;
        int count = 0;
        long bytes = 0;
        foreach (var pid in named)
        {
            if (!DescendsFrom(pid, self, parentOf)) continue;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                bytes += process.WorkingSet64;
                count++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { /* terminé entre-temps */ }
        }
        return new ProcessUsage(count, bytes);
    }

    static bool DescendsFrom(uint pid, uint ancestor, Dictionary<uint, uint> parentOf)
    {
        // Borné : les identifiants de processus sont réutilisés, une chaîne peut boucler
        for (int depth = 0; depth < 32 && parentOf.TryGetValue(pid, out var parent); depth++)
        {
            if (parent == ancestor) return true;
            pid = parent;
        }
        return false;
    }
}
