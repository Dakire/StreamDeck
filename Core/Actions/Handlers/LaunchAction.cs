namespace TouchDeck.Actions;

/// <summary>Lance un programme ou ouvre un dossier, avec des arguments facultatifs.</summary>
public sealed class LaunchAction : BackgroundActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Launch"/>.</summary>
    public LaunchAction() : base(ActionTypes.Launch, ActionTimeouts.Launch) { }

    /// <inheritdoc/>
    protected override void Run(DeckAction action, StepContext context) =>
        context.Services.Platform.Launcher.Open(action.Value, action.Args);
}