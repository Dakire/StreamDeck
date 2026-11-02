using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Hotkeys;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Raccourcis globaux par <c>RegisterHotKey</c>. Un raccourci appartient au thread qui l'enregistre et ses messages arrivent dans la file de ce thread : un
/// thread dédié, avec sa boucle de messages, porte tous les enregistrements. Les demandes y sont envoyées par une file et un message de réveil.
/// </summary>
internal sealed class GlobalHotkeys : IHotkeyService
{
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const uint WM_HOTKEY = 0x0312, WM_APP = 0x8000, WM_QUIT = 0x0012;
    const uint MOD_NOREPEAT = 0x4000;
    const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    readonly ConcurrentQueue<Action> _work = new();
    readonly ManualResetEventSlim _ready = new();
    readonly Thread _thread;
    uint _threadId;
    bool _disposed;

    public GlobalHotkeys()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "StreamDak.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public event Action<int>? Pressed;

    void Loop()
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);   // crée la file de messages du thread avant de le déclarer prêt
        _threadId = GetCurrentThreadId();
        _ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                try { Pressed?.Invoke((int)msg.wParam); }
                catch (Exception ex) { Log.Error("Raccourci global : le gestionnaire a échoué", ex); }
            }
            else if (msg.message == WM_APP)
                while (_work.TryDequeue(out var job)) job();
        }
    }

    T OnThread<T>(Func<T> job, T fallback)
    {
        if (_disposed || _threadId == 0) return fallback;
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() => { try { done.SetResult(job()); } catch (Exception ex) { done.SetException(ex); } });
        if (!PostThreadMessage(_threadId, WM_APP, UIntPtr.Zero, IntPtr.Zero)) return fallback;
        return done.Task.Wait(TimeSpan.FromSeconds(3)) ? done.Task.Result : fallback;
    }

    public HotkeyResult Register(int id, string combo)
    {
        if (!NativeInput.TryParseHotkey(combo, out var modifiers, out var vk, out var error)) return new HotkeyResult(false, error);
        return OnThread(() =>
        {
            UnregisterHotKey(IntPtr.Zero, id);   // remplace un enregistrement précédent du même identifiant
            if (RegisterHotKey(IntPtr.Zero, id, modifiers | MOD_NOREPEAT, vk)) return new HotkeyResult(true);
            int code = Marshal.GetLastWin32Error();
            return new HotkeyResult(false, code == ERROR_HOTKEY_ALREADY_REGISTERED ? "Déjà utilisé par une autre application" : new Win32Exception(code).Message);
        }, new HotkeyResult(false, "Raccourcis globaux indisponibles"));
    }

    public void Unregister(int id) => OnThread(() => { UnregisterHotKey(IntPtr.Zero, id); return true; }, false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
