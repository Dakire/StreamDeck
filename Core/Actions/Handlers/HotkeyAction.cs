namespace StreamDak.Actions;

/// <summary>Envoie une combinaison de touches (« ctrl+shift+m ») à l'application active.</summary>
public sealed class HotkeyAction : SyncActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Hotkey"/>.</summary>
    public HotkeyAction() : base(ActionTypes.Hotkey, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override string? Run(DeckAction action, StepContext context)
    {
        context.Services.Platform.Keyboard.SendHotkey(action.Value);
        return null;
    }
}