namespace StreamDak.Actions;

/// <summary>
/// Envoie une combinaison de touches (« ctrl+shift+m ») à l'application active, hors du thread d'interface : la combinaison reste enfoncée un
/// court instant (voir la plateforme Windows) et le deck ne doit pas se figer pendant ce temps.
/// </summary>
public sealed class HotkeyAction : BackgroundActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Hotkey"/>.</summary>
    public HotkeyAction() : base(ActionTypes.Hotkey, ActionTimeouts.Quick) { }

    /// <inheritdoc/>
    protected override void Run(DeckAction action, StepContext context)
    {
        context.Services.Platform.Keyboard.SendHotkey(action.Value);
    }
}