using System.Linq;
using Xunit;

namespace StreamDak.Tests;

/// <summary>vMix, X-Plane, ClickUp et monday ont été retirés : un ancien config.json qui les mentionne reste lisible, sans rien perdre ni planter.</summary>
public class RemovedIntegrationsTests
{
    const string OldConfig = """
        {"Version":2,"ActiveProfile":"P","Integrations":{"vmix":{"Enabled":true,"Values":{"host":"127.0.0.1"}},"clickup":{"Enabled":false}},
         "Profiles":[{"Name":"P","Pages":[{"Name":"A","Buttons":[
           {"Label":"Coupe","Actions":[{"Type":"vmix","Value":"cut"}]},
           {"Label":"Train","Actions":[{"Type":"xplane.command","Value":"sim/x"}]},
           {"Label":"OK","Actions":[{"Type":"hotkey","Value":"ctrl+c"}]}]}]}]}
        """;

    [Fact]
    public void AnOldConfigWithRemovedIntegrationsStillLoadsKeepsItsStepsAndFlagsThemAsUnknown()
    {
        var config = ConfigStore.Parse(OldConfig, out _);
        var buttons = config.Profiles[0].Pages[0].Buttons;

        Assert.Equal(new[] { "Coupe", "Train", "OK" }, buttons.Select(b => b.Label));
        Assert.Equal("vmix", buttons[0].Actions[0].Type);                                    // rien n'est perdu
        Assert.NotNull(config.Integrations);
        Assert.Contains(ActionValidator.Validate(buttons[0].Actions[0], config, config.Profiles[0]), i => i.Message.Contains("inconnu"));
        Assert.Contains(ActionValidator.Validate(buttons[1].Actions[0], config, config.Profiles[0]), i => i.Message.Contains("inconnu"));
        Assert.DoesNotContain(ActionValidator.Validate(buttons[2].Actions[0], config, config.Profiles[0]), i => i.Message.Contains("inconnu"));
    }
}
