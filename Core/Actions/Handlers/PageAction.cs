namespace TouchDeck.Actions;

/// <summary>Affiche une autre page du profil courant (nom de page, « home » ou « back »).</summary>
public sealed class PageAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Page"/>.</summary>
    public PageAction() : base(ActionTypes.Page, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        context.Navigator.GoToPage(action.Value);
        return null;
    }
}