using System;

namespace StreamDak.Actions;

/// <summary>
/// Volume et coupure d'un périphérique audio choisi par son nom (micro Focusrite, casque Arctis…), même s'il n'est pas le périphérique par défaut
/// de Windows, que cette action ne change jamais. Commande dans <see cref="DeckAction.Value"/> (<see cref="AudioDeviceCommands"/>),
/// périphérique (et niveau) dans <see cref="DeckAction.Args"/> : « Focusrite », « capture:Focusrite », « Arctis=50 ».
/// </summary>
public sealed class AudioDeviceAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.AudioDevice"/>.</summary>
    public AudioDeviceAction() : base(ActionTypes.AudioDevice, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        var audio = context.Services.Platform.Audio;
        var command = action.Value.ToLowerInvariant();
        if (command == AudioDeviceCommands.List)
        {
            var all = audio.Endpoints();
            return all.Count == 0 ? "Aucun périphérique audio actif" : AudioDeviceQuery.Describe(all);
        }
        if (command == AudioDeviceCommands.Volume)
        {
            var (query, level) = AudioDeviceSyntax.SplitLevel(action.Args);
            var device = query.Resolve(audio.Endpoints());
            AudioDeviceSyntax.TryParseLevel(level, audio.ReadEndpoint(device.Id).Percent, out var percent);
            audio.SetEndpointVolume(device.Id, percent);
            return $"{Short(device)} : {percent} %";
        }
        if (command is not (AudioDeviceCommands.Mute or AudioDeviceCommands.Unmute or AudioDeviceCommands.Toggle))
            throw new ArgumentException($"Commande audio inconnue : « {action.Value} »");
        var target = AudioDeviceQuery.Parse(action.Args).Resolve(audio.Endpoints());
        bool muted = command == AudioDeviceCommands.Mute || (command == AudioDeviceCommands.Toggle && !audio.ReadEndpoint(target.Id).Muted);
        audio.SetEndpointMute(target.Id, muted);
        return $"{Short(target)} {(muted ? "coupé" : "rétabli")}";
    }

    /// <summary>Nom court pour un message : « Microphone (Focusrite USB Audio) » devient « Focusrite USB Audio » quand il y a des parenthèses.</summary>
    static string Short(AudioEndpoint device)
    {
        int open = device.Name.LastIndexOf('('), close = device.Name.LastIndexOf(')');
        return open >= 0 && close > open + 1 ? device.Name[(open + 1)..close] : device.Name;
    }
}
