using System.Collections.Generic;

namespace StreamDak.Actions;

/// <summary>
/// Les types d'action fournis avec StreamDak, un gestionnaire par type (dossier <c>Handlers/</c>). L'identifiant de chaque
/// type est la clé stable de <c>config.json</c> (<see cref="ActionTypes"/>). D'autres gestionnaires (intégrations) s'ajoutent
/// au conteneur de services sans modifier cette liste.
/// </summary>
public static class BuiltInActions
{
    /// <summary>Un gestionnaire par type d'action de <see cref="ActionTypes"/>.</summary>
    public static IReadOnlyList<IActionHandler> All { get; } = new IActionHandler[]
    {
        new HotkeyAction(),
        new MediaAction(),
        new TextAction(),
        new PageAction(),
        new ProfileAction(),
        new DelayAction(),
        new UrlAction(),
        new LaunchAction(),
        new ScriptAction(),
        new FocusAction(),
        new HttpAction(),
        new AudioAction(),
        new TimerAction(),
        new AudioDeviceAction(),
    };
}