using System;

namespace StreamDak.Actions;

/// <summary>
/// Minuteur : démarre le compte à rebours du bouton, ou l'arrête s'il tourne déjà. Une durée de 0 en fait un chronomètre : le temps écoulé s'affiche sur
/// la touche, toucher de nouveau l'arrête et donne le temps mesuré. Sans délai maximal.
/// </summary>
public sealed class TimerAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Timer"/>.</summary>
    public TimerAction() : base(ActionTypes.Timer, null) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        if (!int.TryParse(action.Value, out var seconds) || seconds < 0)
            throw new ArgumentException("La durée du minuteur doit être un nombre de secondes (0 : chronomètre)");
        var countdowns = context.Services.Countdowns;
        if (seconds == 0)
            return countdowns.ToggleStopwatch(context.Button, out var elapsed) ? "Chronomètre lancé" : $"Chronomètre arrêté : {Countdowns.Format(elapsed)}";
        return countdowns.Toggle(context.Button, TimeSpan.FromSeconds(seconds))
            ? $"Minuteur lancé : {Countdowns.Format(TimeSpan.FromSeconds(seconds))}"
            : "Minuteur arrêté";
    }
}