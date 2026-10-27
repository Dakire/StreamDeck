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
    public string Controls => _channel.Equals("chatCapture", StringComparison.OrdinalIgnoreCase)
        ? "Micro dans le mélangeur Sonar (pas le gain de la carte)"
        : "Mélangeur Sonar (pas le volume Windows)";
    /// <inheritdoc/>
    public string Icon => LevelOptions.ChannelIcon(_channel);
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
    AudioFlow? _flow;

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
    public string Controls
    {
        get
        {
            lock (_gate)
                return (_flow ?? _query.Flow) == AudioFlow.Capture
                    ? "Niveau d'entrée Windows (pas le gain de la carte)"
                    : "Volume Windows du périphérique (pas Sonar)";
        }
    }
    /// <inheritdoc/>
    public string Icon { get { lock (_gate) return (_flow ?? _query.Flow) == AudioFlow.Capture ? "Mic24" : "Speaker224"; } }
    /// <inheritdoc/>
    public int ResetLevel => 50;
    /// <inheritdoc/>
    public TimeSpan? PollInterval { get; }
    /// <inheritdoc/>
    public event Action? Changed { add { } remove { } }

    string Resolve()
    {
        var device = _query.Resolve(_audio.Endpoints());
        _id = device.Id;
        _flow = device.Flow;
        _title = (device.Flow == AudioFlow.Capture ? "Entrée · " : "Sortie · ") + Short(device.Name);   // une carte son a souvent une entrée et une sortie du même nom
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

/// <summary>
/// Source de niveau : volume d'entrée (micro) ou de sortie de Discord, lus dans les valeurs du connecteur « discord » (mises à jour par les événements de Discord)
/// et réglés par son RPC local. La sortie de Discord va jusqu'à 200 % : le contrôle couvre 0 à 100 %, une valeur plus haute s'affiche à 100.
/// </summary>
public sealed class DiscordLevelSource : ILevelSource, IDisposable
{
    const string Id = "discord";
    readonly IntegrationManager _integrations;
    readonly bool _input;
    Action? _changed;

    /// <summary>Source pour <paramref name="target"/> : « input » (micro) ou « output » (sortie).</summary>
    public DiscordLevelSource(IntegrationManager integrations, string target)
    {
        _integrations = integrations;
        _input = !target.Equals("output", StringComparison.OrdinalIgnoreCase);
        _integrations.ValuesChanged += OnValues;
        _integrations.StateChanged += OnState;
    }

    /// <inheritdoc/>
    public string Title => _input ? "Discord · Micro" : "Discord · Sortie";
    /// <inheritdoc/>
    public string Controls => _input ? "Volume d'entrée de Discord (pas Windows)" : "Volume de sortie de Discord (pas Windows)";
    /// <inheritdoc/>
    public string Icon => _input ? "Mic24" : "Headphones24";
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
        var discord = _integrations.Find(Id);
        if (discord == null) { problem = "Le connecteur Discord est indisponible"; return null; }
        if (!_integrations.IsEnabled(Id)) { problem = "Active Discord dans Paramètres › Intégrations"; return null; }
        if (discord.State != ConnectionState.Connected)
        {
            problem = discord.State == ConnectionState.Error && discord.Error != null ? discord.Error : "Connexion à Discord…";
            return null;
        }
        if (!int.TryParse(_integrations.GetValue($"{Id}.{(_input ? "input" : "output")}.volume"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume))
        {
            problem = "Discord n'a pas encore donné ses réglages vocaux";
            return null;
        }
        bool? muted = _integrations.GetValue($"{Id}.{(_input ? "mute" : "deaf")}") is { } m ? m == "true" : null;
        return new LevelReading(Math.Clamp(volume, 0, 100), muted);
    }

    IIntegration Connected() => _integrations.Find(Id) is { State: ConnectionState.Connected } discord ? discord : throw new InvalidOperationException("Discord n'est pas connecté");

    /// <inheritdoc/>
    public Task SetLevelAsync(int percent) =>
        Connected().ExecuteAsync(_input ? "inputvolume" : "outputvolume", Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture), CancellationToken.None);

    /// <inheritdoc/>
    public Task SetMutedAsync(bool muted) =>
        Connected().ExecuteAsync(_input ? (muted ? "mute" : "unmute") : (muted ? "deafen" : "undeafen"), null, CancellationToken.None);

    /// <summary>Cesse d'écouter le gestionnaire.</summary>
    public void Dispose()
    {
        _integrations.ValuesChanged -= OnValues;
        _integrations.StateChanged -= OnState;
    }
}

/// <summary>Crée la source de niveau d'une cible (réglage <c>source</c> d'un widget).</summary>
public static class LevelSources
{
    /// <summary>Source de la cible, ou null si le texte est vide ou illisible.</summary>
    public static ILevelSource? Create(string? targetText, IntegrationManager integrations, IAudioDevices audio) =>
        LevelTarget.TryParse(targetText, out var target) ? Create(target, integrations, audio) : null;

    /// <summary>Source d'une cible lue, ou null si sa désignation est illisible.</summary>
    public static ILevelSource? Create(LevelTarget target, IntegrationManager integrations, IAudioDevices audio)
    {
        try
        {
            return target.Backend switch
            {
                LevelBackend.Sonar => new SonarLevelSource(integrations, target.Name),
                LevelBackend.Discord => new DiscordLevelSource(integrations, target.Name),
                _ => new WindowsLevelSource(audio, AudioDeviceQuery.Parse(target.Name)),
            };
        }
        catch (ArgumentException) { return null; }
    }
}
