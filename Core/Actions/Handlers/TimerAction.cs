using System;

namespace StreamDak.Actions;

/// <summary>Minuteur : démarre le compte à rebours du bouton, ou l'arrête s'il tourne déjà. Sans délai maximal.</summary>
public sealed class TimerAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Timer"/>.</summary>
    public TimerAction() : base(ActionTypes.Timer, null) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        if (!int.TryParse(action.Value, out var seconds) || seconds <= 0)
            throw new ArgumentException("La durée du minuteur doit être un nombre de secondes");
        var countdowns = context.Services.Countdowns;
        return countdowns.Toggle(context.Button, TimeSpan.FromSeconds(seconds))
            ? $"Minuteur lancé : {Countdowns.Format(TimeSpan.FromSeconds(seconds))}"
            : "Minuteur arrêté";
    }
}