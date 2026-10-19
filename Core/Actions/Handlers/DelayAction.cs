using System;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Actions;

/// <summary>Attend un nombre de millisecondes (100 par défaut si la valeur est invalide) ; annulable, sans délai maximal.</summary>
public sealed class DelayAction : ActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Delay"/>.</summary>
    public DelayAction() : base(ActionTypes.Delay, null) { }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        var ms = int.TryParse(action.Value, out var value) && value >= 0 ? value : 100;
        await context.Services.Delay(TimeSpan.FromMilliseconds(ms), cancellation);
        return null;
    }
}