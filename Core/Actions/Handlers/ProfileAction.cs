namespace StreamDak.Actions;

/// <summary>Affiche un autre profil sur le deck.</summary>
public sealed class ProfileAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Profile"/>.</summary>
    public ProfileAction() : base(ActionTypes.Profile, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        context.Navigator.SwitchProfile(action.Value);
        return null;
    }
}