using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Changements de la fenêtre au premier plan par <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c> : plus de relevé périodique sur le thread d'interface.
/// Le crochet est posé par le premier abonné (qui doit avoir une boucle de messages : le thread d'interface) et retiré avec le dernier. Les changements
/// rapprochés sont fusionnés et le nom du programme est résolu hors de ce thread.
/// </summary>
internal static class ForegroundWatcher
{
    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    static readonly object Gate = new();
    // Le délégué reste référencé tant que le crochet existe : le ramasse-miettes ne doit pas le libérer
    static readonly WinEventProc Callback = OnEvent;
    static Action<string?>? _handlers;
    static IntPtr _hook;
    static int _ticket;

    /// <summary>Abonnés au changement de programme au premier plan.</summary>
    public static event Action<string?>? Changed
    {
        add
        {
            lock (Gate)
            {
                _handlers += value;
                if (_hook == IntPtr.Zero)
                {
                    _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, Callback, 0, 0, WINEVENT_OUTOFCONTEXT);
                    if (_hook == IntPtr.Zero) Log.Warn("Programme au premier plan : crochet Windows indisponible, relevé périodique conservé");
                }
            }
        }
        remove
        {
            lock (Gate)
            {
                _handlers -= value;
                if (_handlers == null && _hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            }
        }
    }

    static void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        int ticket = Interlocked.Increment(ref _ticket);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce);
                if (ticket != Volatile.Read(ref _ticket)) return;   // un changement plus récent suivra
                Action<string?>? handlers;
                lock (Gate) handlers = _handlers;
                handlers?.Invoke(WindowFocus.ForegroundProcessName());
            }
            catch (Exception ex) { Log.Error("Changement de programme au premier plan", ex); }
        });
    }
}
