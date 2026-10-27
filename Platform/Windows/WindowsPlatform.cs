using System.Collections.Generic;
using System.Diagnostics;
using StreamDak.Actions;

namespace StreamDak.Platform.Windows;

/// <summary>Implémentation Windows des services de plateforme : SendInput, ShellExecute, fenêtres Win32, Core Audio.</summary>
public sealed class WindowsPlatform : IKeyboardService, IAppLauncher, IWindowService, IAudioService
{
    /// <summary>Services de plateforme réels de Windows.</summary>
    public static PlatformServices Create()
    {
        var p = new WindowsPlatform();
        return new PlatformServices(p, p, p, p);
    }

    /// <summary>Vérificateur de signature Authenticode (mise à jour : l'installateur téléchargé).</summary>
    public static ISignatureVerifier CreateSignatureVerifier() => new AuthenticodeVerifier();

    /// <summary>Active ou désactive une fenêtre Win32 hébergée (cadre web) : désactivée, elle ne capte plus la souris, le doigt ni le clavier.</summary>
    public static void SetHostedWindowInput(System.IntPtr window, bool enabled) => WindowFocus.SetInputEnabled(window, enabled);

    /// <inheritdoc/>
    public void SendHotkey(string combo) => NativeInput.Hotkey(combo);
    /// <inheritdoc/>
    public void TypeText(string text) => NativeInput.TypeText(text);
    /// <inheritdoc/>
    public bool TryValidateHotkey(string combo, out string error) => NativeInput.TryValidateHotkey(combo, out error);

    /// <inheritdoc/>
    public void Open(string target, string? arguments)
    {
        using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" });
    }

    /// <inheritdoc/>
    public void RunScript(string scriptOrCommand, string? arguments)
    {
        // Arguments transmis un à un (ArgumentList) : aucune ligne de commande n'est assemblée à la main
        var launch = ScriptCommand.Build(scriptOrCommand, arguments);
        var psi = new ProcessStartInfo(launch.FileName) { CreateNoWindow = true, UseShellExecute = false };
        foreach (var argument in launch.Arguments) psi.ArgumentList.Add(argument);
        using var _ = Process.Start(psi);
    }

    /// <inheritdoc/>
    public bool Activate(string query) => WindowFocus.Activate(query);
    /// <inheritdoc/>
    public string? ForegroundProcessName() => WindowFocus.ForegroundProcessName();
    /// <inheritdoc/>
    public IReadOnlyList<string> OpenProcessNames() => WindowFocus.OpenProcessNames();

    /// <inheritdoc/>
    public bool IsMuted(AudioDevice device) => NativeAudio.IsMuted(device);
    /// <inheritdoc/>
    public void SetMute(AudioDevice device, bool muted) => NativeAudio.SetMute(device, muted);
    /// <inheritdoc/>
    public void SetVolume(AudioDevice device, int percent) => NativeAudio.SetVolume(device, percent);

    /// <inheritdoc/>
    public IReadOnlyList<AudioEndpoint> Endpoints() => NativeAudio.Endpoints();
    /// <inheritdoc/>
    public AudioEndpointState ReadEndpoint(string id) => NativeAudio.Read(id);
    /// <inheritdoc/>
    public void SetEndpointVolume(string id, int percent) => NativeAudio.SetEndpointVolume(id, percent);
    /// <inheritdoc/>
    public void SetEndpointMute(string id, bool muted) => NativeAudio.SetEndpointMute(id, muted);
}
