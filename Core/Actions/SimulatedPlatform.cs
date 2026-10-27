using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Actions;

/// <summary>
/// Plateforme simulée : rien n'est envoyé au système, chaque opération est consignée. Sert aux tests et au mode
/// <c>--simulate</c> (essayer la grille sans envoyer de touches ni lancer de programme).
/// </summary>
public sealed class SimulatedPlatform : IKeyboardService, IAppLauncher, IWindowService, IAudioService
{
    readonly Dictionary<AudioDevice, bool> _muted = new() { [AudioDevice.Output] = false, [AudioDevice.Microphone] = false };

    /// <summary>Opérations « effectuées », dans l'ordre (« hotkey ctrl+c », « open notepad.exe »…).</summary>
    public List<string> Log { get; } = new();
    /// <summary>Chaque opération simulée, au moment où elle a lieu (le mode --simulate l'affiche en toast).</summary>
    public event Action<string>? Performed;

    /// <summary>Programmes « ouverts » : Activate réussit pour eux.</summary>
    public HashSet<string> OpenWindows { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Programme supposé au premier plan (profil automatique par application).</summary>
    public string? Foreground { get; set; }
    /// <summary>Dernier volume réglé, en pourcentage.</summary>
    public int Volume { get; private set; } = 50;

    /// <summary>Périphériques audio « branchés » : les tests en ajoutent ; par défaut un haut-parleur et un micro.</summary>
    public List<AudioEndpoint> AudioEndpoints { get; } = new()
    {
        new("render-default", "Haut-parleurs (Simulation)", AudioFlow.Render, true),
        new("capture-default", "Microphone (Simulation)", AudioFlow.Capture, true),
    };
    readonly Dictionary<string, AudioEndpointState> _endpointStates = new();

    /// <summary>Les quatre services de plateforme, tous fournis par cette instance.</summary>
    public PlatformServices Services => new(this, this, this, this);

    void Record(string what)
    {
        lock (Log) Log.Add(what);
        Performed?.Invoke(what);
    }

    /// <inheritdoc/>
    public void SendHotkey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) throw new ArgumentException("Raccourci vide");
        Record("hotkey " + combo);
    }

    /// <inheritdoc/>
    public void TypeText(string text) => Record("text " + text);

    /// <inheritdoc/>
    public bool TryValidateHotkey(string combo, out string error) => HotkeySyntax.TryValidate(combo, out error);
    /// <inheritdoc/>
    public void Open(string target, string? arguments) => Record("open " + target + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
    /// <inheritdoc/>
    public void RunScript(string scriptOrCommand, string? arguments) => Record("script " + scriptOrCommand);

    /// <inheritdoc/>
    public bool Activate(string query)
    {
        bool ok = OpenWindows.Contains(query);
        Record((ok ? "focus " : "focus-missing ") + query);
        return ok;
    }

    /// <inheritdoc/>
    public string? ForegroundProcessName() => Foreground;

    /// <inheritdoc/>
    public IReadOnlyList<string> OpenProcessNames() => OpenWindows.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <inheritdoc/>
    public bool IsMuted(AudioDevice device) => _muted[device];

    /// <inheritdoc/>
    public void SetMute(AudioDevice device, bool muted)
    {
        _muted[device] = muted;
        Record($"mute {device} {muted}");
    }

    /// <inheritdoc/>
    public void SetVolume(AudioDevice device, int percent)
    {
        Volume = percent;
        Record($"volume {device} {percent}");
    }

    /// <inheritdoc/>
    public IReadOnlyList<AudioEndpoint> Endpoints() => AudioEndpoints.ToList();

    /// <inheritdoc/>
    public AudioEndpointState ReadEndpoint(string id)
    {
        if (AudioEndpoints.All(e => e.Id != id)) throw new InvalidOperationException("Ce périphérique audio n'est plus disponible");
        lock (_endpointStates) return _endpointStates.TryGetValue(id, out var state) ? state : new AudioEndpointState(50, false);
    }

    /// <inheritdoc/>
    public void SetEndpointVolume(string id, int percent)
    {
        var current = ReadEndpoint(id);
        lock (_endpointStates) _endpointStates[id] = current with { Percent = Math.Clamp(percent, 0, 100) };
        Record($"endpoint-volume {id} {percent}");
    }

    /// <inheritdoc/>
    public void SetEndpointMute(string id, bool muted)
    {
        var current = ReadEndpoint(id);
        lock (_endpointStates) _endpointStates[id] = current with { Muted = muted };
        Record($"endpoint-mute {id} {muted}");
    }
}
