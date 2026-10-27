using System;
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

    /// <summary>État de la batterie (widget Batterie), ou null si Windows ne le donne pas.</summary>
    public static BatteryState? ReadBattery() => NativePower.Read();

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
        // %USERPROFILE%, %LOCALAPPDATA%… : variables de l'utilisateur lui-même, développées comme dans l'Explorateur
        using var _ = Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target))
        {
            UseShellExecute = true, Arguments = Environment.ExpandEnvironmentVariables(arguments ?? ""),
        });
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
    public ScriptResult RunScriptObserved(string scriptOrCommand, string? arguments, TimeSpan wait)
    {
        var launch = ScriptCommand.Build(scriptOrCommand, arguments);
        var psi = new ProcessStartInfo(launch.FileName) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in launch.Arguments) psi.ArgumentList.Add(argument);
        var output = new TailBuffer();
        var error = new TailBuffer();
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        int handedOff = 0;
        // Toujours lu jusqu'au bout (un tuyau plein bloquerait le script) ; libéré à la fin du script s'il dure plus que l'observation
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.Add(e.Data); };
        process.Exited += (_, _) => { if (System.Threading.Volatile.Read(ref handedOff) == 1) process.Dispose(); };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            process.Dispose();
            throw;
        }
        if (process.WaitForExit(wait))
        {
            process.WaitForExit();   // fin des lectures asynchrones
            var finished = new ScriptResult(true, process.ExitCode, output.Text, error.Text);
            process.Dispose();
            return finished;
        }
        System.Threading.Volatile.Write(ref handedOff, 1);
        if (process.HasExited) process.Dispose();
        return new ScriptResult(false, 0, output.Text, error.Text);
    }

    /// <summary>Dernières lignes reçues (20 au plus, 4 000 caractères chacune) : un script bavard ne remplit pas la mémoire.</summary>
    sealed class TailBuffer
    {
        readonly Queue<string> _lines = new();
        public void Add(string line)
        {
            lock (_lines)
            {
                _lines.Enqueue(line.Length <= 4000 ? line : line[..4000]);
                while (_lines.Count > 20) _lines.Dequeue();
            }
        }
        public string Text { get { lock (_lines) return string.Join("\n", _lines); } }
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
