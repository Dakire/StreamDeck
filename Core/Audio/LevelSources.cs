using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StreamDak.Actions;
using StreamDak.Integrations;

namespace StreamDak.Audio;

/// <summary>Source de niveau : un canal du mélangeur Sonar, lu dans les valeurs du connecteur « sonar » et réglé par ses commandes.</summary>
public sealed class SonarLevelSource : ILevelSource, IDisposable
{
    const string Id = "sonar";
    readonly IntegrationManager _integrations;
    readonly string _channel;
    Action? _changed;

    /// <summary>Source pour le canal <paramref name="channel"/> (nom Sonar : « game », « chatCapture »…).</summary>
    public SonarLevelSource(IntegrationManager integrations, string channel)
    {
        _integrations = integrations;
        _channel = channel;
        _integrations.ValuesChanged += OnValues;
        _integrations.StateChanged += OnState;
    }

    /// <inheritdoc/>
    public string Title => "Sonar · " + LevelOptions.ChannelLabel(_channel);
    /// <inheritdoc/>
    public TimeSpan? PollInterval => null;
    /// <inheritdoc/>
    public event Action? Changed { add => _changed += value; remove => _changed -= value; }

    void OnValues(IReadOnlyCollection<string> keys)
    {
        foreach (var key in keys)
            if (key.StartsWith(Id + ".", StringComparison.OrdinalIgnoreCase)) { _changed?.Invoke(); return; }
    }

    void OnState(IIntegration integration)
    {
        if (integration.Id == Id) _changed?.Invoke();
    }

    /// <inheritdoc/>
    public LevelReading? Read(out string problem)
    {
        problem = "";
        var sonar = _integrations.Find(Id);
        if (sonar == null) { problem = "Le connecteur Sonar est indisponible"; return null; }
        if (!_integrations.IsEnabled(Id)) { problem = "Active Sonar dans Paramètres › Intégrations"; return null; }
        if (sonar.State != ConnectionState.Connected)
        {
            problem = sonar.State == ConnectionState.Error && sonar.Error != null ? sonar.Error : "Connexion à Sonar…";
            return null;
        }
        if (!int.TryParse(_integrations.GetValue($"{Id}.{_channel}.volume"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            problem = $"Sonar n'expose pas le canal « {_channel} » (commande Diagnostic)";
            return null;
        }
        bool? muted = _integrations.GetValue($"{Id}.{_channel}.muted") is { } m ? m == "true" : null;
        return new LevelReading(Math.Clamp(percent, 0, 100), muted);
    }

    IIntegration Connected() => _integrations.Find(Id) is { State: ConnectionState.Connected } sonar ? sonar : throw new InvalidOperationException("Sonar n'est pas connecté");

    /// <inheritdoc/>
    public Task SetLevelAsync(int percent) =>
        Connected().ExecuteAsync("volume", $"{_channel}={Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture)}", CancellationToken.None);

    /// <inheritdoc/>
    public Task SetMutedAsync(bool muted) => Connected().ExecuteAsync(muted ? "mute" : "unmute", _channel, CancellationToken.None);

    /// <summary>Cesse d'écouter le gestionnaire.</summary>
    public void Dispose()
    {
        _integrations.ValuesChanged -= OnValues;
        _integrations.StateChanged -= OnState;
    }
}

/// <summary>Source de niveau : un périphérique audio de Windows choisi par son nom, lu à intervalle régulier (Windows ne prévient pas ici).</summary>
public sealed class WindowsLevelSource : ILevelSource
{
    readonly IAudioDevices _audio;
    readonly AudioDeviceQuery _query;
    readonly object _gate = new();
    string? _id;
    string _title;

    /// <summary>Source pour le périphérique désigné par <paramref name="query"/> (« Focusrite », « capture:Focusrite »).</summary>
    public WindowsLevelSource(IAudioDevices audio, AudioDeviceQuery query, TimeSpan? pollInterval = null)
    {
        _audio = audio;
        _query = query;
        _title = query.Text;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc/>
    public string Title { get { lock (_gate) return _title; } }
    /// <inheritdoc/>
    public TimeSpan? PollInterval { get; }
    /// <inheritdoc/>
    public event Action? Changed { add { } remove { } }

    string Resolve()
    {
        var device = _query.Resolve(_audio.Endpoints());
        _id = device.Id;
        _title = Short(device.Name);
        return device.Id;
    }

    static string Short(string name)
    {
        int open = name.LastIndexOf('('), close = name.LastIndexOf(')');
        return open >= 0 && close > open + 1 ? name[(open + 1)..close] : name;
    }

    AudioEndpointState ReadState()
    {
        lock (_gate) return ReadStateLocked();
    }

    AudioEndpointState ReadStateLocked()
    {
        if (_id != null)
        {
            try { return _audio.ReadEndpoint(_id); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { _id = null; }   // débranché, réinstallé : on le cherche de nouveau
        }
        return _audio.ReadEndpoint(Resolve());
    }

    /// <inheritdoc/>
    public LevelReading? Read(out string problem)
    {
        problem = "";
        try
        {
            var state = ReadState();
            return new LevelReading(state.Percent, state.Muted);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            problem = ex is System.Runtime.InteropServices.COMException ? "Périphérique audio injoignable" : ex.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public Task SetLevelAsync(int percent)
    {
        lock (_gate)
        {
            ReadStateLocked();   // résout (ou re-résout) le périphérique
            _audio.SetEndpointVolume(_id!, Math.Clamp(percent, 0, 100));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetMutedAsync(bool muted)
    {
        lock (_gate)
        {
            ReadStateLocked();
            _audio.SetEndpointMute(_id!, muted);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Crée la source de niveau d'une cible (réglage <c>source</c> d'un widget).</summary>
public static class LevelSources
{
    /// <summary>Source de la cible, ou null si le texte est vide ou illisible.</summary>
    public static ILevelSource? Create(string? targetText, IntegrationManager integrations, IAudioDevices audio)
    {
        if (!LevelTarget.TryParse(targetText, out var target)) return null;
        try
        {
            return target.Backend == LevelBackend.Sonar
                ? new SonarLevelSource(integrations, target.Name)
                : new WindowsLevelSource(audio, AudioDeviceQuery.Parse(target.Name));
        }
        catch (ArgumentException) { return null; }
    }
}
