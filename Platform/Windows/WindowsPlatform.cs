using System.Diagnostics;
using System.IO;
using TouchDeck.Actions;

namespace TouchDeck.Platform.Windows;

/// <summary>Implémentation Windows des services de plateforme : SendInput, ShellExecute, fenêtres Win32, Core Audio.</summary>
public sealed class WindowsPlatform : IKeyboardService, IAppLauncher, IWindowService, IAudioService
{
    public static PlatformServices Create()
    {
        var p = new WindowsPlatform();
        return new PlatformServices(p, p, p, p);
    }

    public void SendHotkey(string combo) => NativeInput.Hotkey(combo);
    public void TypeText(string text) => NativeInput.TypeText(text);

    public void Open(string target, string? arguments)
    {
        using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" });
    }

    public void RunScript(string scriptOrCommand, string? arguments)
    {
        string value = scriptOrCommand, args = arguments ?? "";
        var ext = Path.GetExtension(value).ToLowerInvariant();
        var psi = new ProcessStartInfo { CreateNoWindow = true, UseShellExecute = false };
        if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{value}\" {args}";
        }
        else if (ext is ".bat" or ".cmd")
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c \"\"{value}\" {args}\"";
        }
        else
        {
            // Commande PowerShell en ligne
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(value);
        }
        using var _ = Process.Start(psi);
    }

    public bool Activate(string query) => WindowFocus.Activate(query);
    public string? ForegroundProcessName() => WindowFocus.ForegroundProcessName();

    public bool IsMuted(AudioDevice device) => NativeAudio.IsMuted(device);
    public void SetMute(AudioDevice device, bool muted) => NativeAudio.SetMute(device, muted);
    public void SetVolume(AudioDevice device, int percent) => NativeAudio.SetVolume(device, percent);
}
