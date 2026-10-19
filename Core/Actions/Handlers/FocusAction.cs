using System;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Actions;

/// <summary>
/// Met une fenêtre au premier plan. Si elle est absente et qu'un programme est indiqué dans <see cref="DeckAction.Args"/>,
/// le programme est lancé puis la fenêtre est activée : les étapes suivantes (pause, raccourci) lui parviennent.
/// </summary>
public sealed class FocusAction : ActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Focus"/>.</summary>
    public FocusAction() : base(ActionTypes.Focus, ActionTimeouts.Focus) { }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        var windows = context.Services.Platform.Windows;
        if (windows.Activate(action.Value)) return null;
        // Fenêtre absente : on lance le programme indiqué (s'il y en a un) et on laisse la main aux actions suivantes
        if (string.IsNullOrWhiteSpace(action.Args)) throw new InvalidOperationException($"Fenêtre « {action.Value} » introuvable");
        context.Services.Platform.Launcher.Open(action.Args, null);
        await context.Services.Delay(TimeSpan.FromMilliseconds(1500), cancellation);
        windows.Activate(action.Value);
        return null;
    }
}