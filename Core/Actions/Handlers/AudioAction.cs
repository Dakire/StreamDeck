using System;
using System.Globalization;

namespace StreamDak.Actions;

/// <summary>Micro et volume : coupe ou rétablit, ou règle le volume de sortie (niveau dans <see cref="DeckAction.Args"/>).</summary>
public sealed class AudioAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Audio"/>.</summary>
    public AudioAction() : base(ActionTypes.Audio, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        var audio = context.Services.Platform.Audio;
        switch (action.Value.ToLowerInvariant())
        {
            case AudioCommands.MicToggle:
                bool mic = !audio.IsMuted(AudioDevice.Microphone);
                audio.SetMute(AudioDevice.Microphone, mic);
                return mic ? "Micro coupé" : "Micro actif";
            case AudioCommands.MicMute: audio.SetMute(AudioDevice.Microphone, true); return "Micro coupé";
            case AudioCommands.MicUnmute: audio.SetMute(AudioDevice.Microphone, false); return "Micro actif";
            case AudioCommands.OutToggle:
                bool muted = !audio.IsMuted(AudioDevice.Output);
                audio.SetMute(AudioDevice.Output, muted);
                return muted ? "Son coupé" : "Son rétabli";
            case AudioCommands.Volume:
                if (!int.TryParse(action.Args, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level is < 0 or > 100)
                    throw new ArgumentException("Le volume doit être un nombre entre 0 et 100");
                audio.SetVolume(AudioDevice.Output, level);
                return $"Volume {level} %";
            default:
                throw new ArgumentException($"Commande audio inconnue : « {action.Value} »");
        }
    }
}