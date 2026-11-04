using Xunit;

namespace StreamDak.Tests;

/// <summary>Audit 3 : la confirmation d'import doit signaler tout ce qui peut exécuter du code, pas seulement « launch » et « script ».</summary>
public class ImportRiskTests
{
    [Theory]
    [InlineData("launch", "calc.exe", true)]
    [InlineData("script", "a.ps1", true)]
    [InlineData("text", "powershell -w hidden", true)]
    [InlineData("hotkey", "win+r", true)]
    [InlineData("hotkey", "Win + R", true)]
    [InlineData("hotkey", "ctrl+shift+m", false)]
    [InlineData("url", "calc.exe", true)]
    [InlineData("url", "%SystemRoot%\\System32\\cmd.exe", true)]
    [InlineData("url", "ms-msdt:/id", true)]
    [InlineData("url", "https://example.com", false)]
    [InlineData("url", "mailto:a@b.fr", false)]
    [InlineData("media", "playpause", false)]
    public void IsRisky_couvre_les_chemins_d_execution(string type, string value, bool risky) =>
        Assert.Equal(risky, ActionValidator.IsRisky(new DeckAction { Type = type, Value = value }));
}
