using System;
using System.Linq;

namespace StreamDak.Actions;

/// <summary>Envoie une touche multimédia (lecture, volume…) ; la valeur doit être l'une de <see cref="ActionValidator.MediaKeys"/>.</summary>
public sealed class MediaAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Media"/>.</summary>
    public MediaAction() : base(ActionTypes.Media, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        if (!ActionValidator.MediaKeys.Contains(action.Value.ToLowerInvariant()))
            throw new ArgumentException($"Touche média inconnue : « {action.Value} »");
        context.Services.Platform.Keyboard.SendHotkey(action.Value);
        return null;
    }
}