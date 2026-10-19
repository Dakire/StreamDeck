namespace TouchDeck.Actions;

/// <summary>Exécute un fichier .ps1, .bat ou .cmd, ou une commande PowerShell, avec les droits de l'utilisateur.</summary>
public sealed class ScriptAction : BackgroundActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Script"/>.</summary>
    public ScriptAction() : base(ActionTypes.Script, ActionTimeouts.Launch) { }

    /// <inheritdoc/>
    protected override void Run(DeckAction action, StepContext context) =>
        context.Services.Platform.Launcher.RunScript(action.Value, action.Args);
}