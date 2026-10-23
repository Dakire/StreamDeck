namespace StreamDak.Actions;

/// <summary>Saisit un texte dans l'application active.</summary>
public sealed class TextAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Text"/>.</summary>
    public TextAction() : base(ActionTypes.Text, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        context.Services.Platform.Keyboard.TypeText(action.Value);
        return null;
    }
}