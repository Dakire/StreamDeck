using System.Windows;

namespace TouchDeck;

/// <summary>
/// Animations : désactivées si l'utilisateur les a coupées dans TouchDeck (ReduceMotion) ou dans Windows (Effets d'animation).
/// Les styles consomment la ressource « DeckTileStyle » ; le code d'animation consulte <see cref="Enabled"/>.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; private set; } = true;

    public static void Apply(bool reduceMotion)
    {
        Enabled = !reduceMotion && SystemParameters.ClientAreaAnimation;
        var res = Application.Current.Resources;
        res["DeckTileStyle"] = res[Enabled ? "DeckButton" : "DeckButtonStatic"];
    }
}
