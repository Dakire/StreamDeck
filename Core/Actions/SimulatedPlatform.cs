using System;
using System.Collections.Generic;

namespace TouchDeck.Actions;

/// <summary>
/// Plateforme simulée : rien n'est envoyé au système, chaque opération est consignée. Sert aux tests et au mode
/// <c>--simulate</c> (essayer la grille sans envoyer de touches ni lancer de programme).
/// </summary>
public sealed class SimulatedPlatform : IKeyboardService, IAppLauncher, IWindowService, IAudioService
{
    readonly Dictionary<AudioDevice, bool> _muted = new() { [AudioDevice.Output] = false, [AudioDevice.Microphone] = false };

    public List<string> Log { get; } = new();
    public event Action<string>? Performed;

    /// <summary>Programmes « ouverts » : Activate réussit pour eux.</summary>
    public HashSet<string> OpenWindows { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Foreground { get; set; }
    public int Volume { get; private set; } = 50;

    public PlatformServices Services => new(this, this, this, this);

    void Record(string what)
    {
        lock (Log) Log.Add(what);
        Performed?.Invoke(what);
    }

    public void SendHotkey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) throw new ArgumentException("Raccourci vide");
        Record("hotkey " + combo);
    }

    public void TypeText(string text) => Record("text " + text);
    public void Open(string target, string? arguments) => Record("open " + target + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
    public void RunScript(string scriptOrCommand, string? arguments) => Record("script " + scriptOrCommand);

    public bool Activate(string query)
    {
        bool ok = OpenWindows.Contains(query);
        Record((ok ? "focus " : "focus-missing ") + query);
        return ok;
    }

    public string? ForegroundProcessName() => Foreground;

    public bool IsMuted(AudioDevice device) => _muted[device];

    public void SetMute(AudioDevice device, bool muted)
    {
        _muted[device] = muted;
        Record($"mute {device} {muted}");
    }

    public void SetVolume(AudioDevice device, int percent)
    {
        Volume = percent;
        Record($"volume {device} {percent}");
    }
}
