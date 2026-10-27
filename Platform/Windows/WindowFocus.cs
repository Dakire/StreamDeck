using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace StreamDak.Platform.Windows;

/// <summary>Recherche et mise au premier plan d'une fenêtre d'une autre application.</summary>
internal static class WindowFocus
{
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, [Out] char[] buffer, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnableWindow(IntPtr h, bool enable);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    const int TokenElevation = 20;

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int SW_RESTORE = 9;

    /// <summary>Fenêtre de premier niveau d'une autre application.</summary>
    /// <param name="Handle">Handle Win32.</param>
    /// <param name="Title">Titre de la fenêtre.</param>
    /// <param name="Process">Nom du programme, sans .exe.</param>
    public record Win(IntPtr Handle, string Title, string Process);

    /// <summary>Fenêtres de premier niveau visibles, de la plus haute à la plus basse dans l'ordre Z (hors StreamDak).</summary>
    public static List<Win> OpenWindows()
    {
        var list = new List<Win>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((h, lParam) =>
        {
            if (!IsWindowVisible(h)) return true;
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            var buffer = new char[len + 1];
            int copied = GetWindowText(h, buffer, buffer.Length);
            _ = GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            string proc;
            // Processus terminé entre l'énumération et la lecture, ou protégé : la fenêtre est simplement ignorée
            try { proc = Process.GetProcessById((int)pid).ProcessName; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
            catch (Exception ex) { Log.Error("Lecture du programme d'une fenêtre impossible", ex); return true; }
            list.Add(new Win(h, new string(buffer, 0, Math.Max(0, copied)), proc));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Nom du programme (sans .exe) de la fenêtre au premier plan, ou null (aucune, ou StreamDak lui-même).</summary>
    public static string? ForegroundProcessName()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            _ = GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
            return Process.GetProcessById((int)pid).ProcessName;
        }
        // Relevé toutes les 0,8 s : un processus qui vient de se terminer n'est pas une erreur, on ne journalise pas
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
        catch (Exception ex) { Log.Error("Lecture de l'application au premier plan impossible", ex); return null; }
    }

    /// <summary>
    /// true si le programme au premier plan tourne avec des droits plus élevés que StreamDak : Windows (UIPI) refuse alors toute touche simulée.
    /// Un processus dont le jeton est illisible (protégé, élevé) compte comme élevé.
    /// </summary>
    public static bool ForegroundIsElevated()
    {
        if (IsElevated(Environment.ProcessId)) return false;   // StreamDak élevé : rien ne lui est refusé
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(h, out uint pid);
        return pid != 0 && pid != (uint)Environment.ProcessId && IsElevated((int)pid);
    }

    static bool IsElevated(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == IntPtr.Zero) return true;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return true;
            try { return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    /// <summary>Programme au premier plan et son élévation, pour le journal (« Game (administrateur) »).</summary>
    public static string ForegroundDescription()
    {
        var name = ForegroundProcessName() ?? "aucun ou StreamDak";
        return ForegroundIsElevated() ? name + " (administrateur)" : name;
    }

    /// <summary>
    /// Active ou désactive une fenêtre enfant hébergée (cadre WebView2). Désactivée, Windows ne lui donne plus ni souris ni doigt ni clavier : le point
    /// sous le pointeur revient à la fenêtre parente. WPF ne le fait pas de lui-même (IsEnabled d'un HwndHost ne touche pas la fenêtre Win32).
    /// Sans effet si l'état est déjà le bon ou si la fenêtre n'existe pas encore.
    /// </summary>
    public static void SetInputEnabled(IntPtr window, bool enabled)
    {
        if (window == IntPtr.Zero || IsWindowEnabled(window) == enabled) return;
        EnableWindow(window, enabled);
    }

    /// <summary>Distinct par programme, pour proposer un choix dans l'éditeur.</summary>
    public static List<string> OpenProcessNames() =>
        OpenWindows().Select(w => w.Process).Where(p => !p.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(14).ToList();

    /// <summary>Active la première fenêtre dont le programme porte ce nom, sinon dont le titre contient ce texte.</summary>
    public static bool Activate(string query)
    {
        var q = (query ?? "").Trim();
        if (q.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) q = q[..^4];
        if (q.Length == 0) return false;

        var wins = OpenWindows();
        var match = wins.FirstOrDefault(w => w.Process.Equals(q, StringComparison.OrdinalIgnoreCase))
                    ?? wins.FirstOrDefault(w => w.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                    ?? wins.FirstOrDefault(w => w.Process.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (match == null) return false;

        var h = match.Handle;
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);

        // Windows n'autorise SetForegroundWindow que pour le thread qui a le focus : on s'y rattache brièvement.
        uint cur = GetCurrentThreadId();
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = fgThread != 0 && fgThread != cur && AttachThreadInput(cur, fgThread, true);
        SetForegroundWindow(h);
        BringWindowToTop(h);
        if (attached) AttachThreadInput(cur, fgThread, false);
        return true;
    }
}
