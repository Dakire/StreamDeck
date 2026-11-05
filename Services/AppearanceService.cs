using System.Linq;

namespace StreamDak;

/// <summary>
/// Applique l'apparence de la configuration (thème, style des icônes, animations) au démarrage puis à chaque changement
/// de ces réglages. Le thème, les animations et le catalogue d'icônes sont des ressources globales de l'application WPF.
/// </summary>
public sealed class AppearanceService
{
    readonly DeckDocument _doc;
    (string Theme, string IconStyle, bool Motion, string Overrides)? _applied;

    /// <summary>Applique l'apparence courante tout de suite, puis suit les modifications du document.</summary>
    public AppearanceService(DeckDocument doc)
    {
        _doc = doc;
        Apply(force: true);
        _doc.Changed += _ => Apply(force: false);
    }

    static string OverridesKey(DeckConfig c) => c.ThemeOverrides == null ? "" : string.Join(';', c.ThemeOverrides.OrderBy(p => p.Key).Select(p => p.Key + '=' + p.Value));

    void Apply(bool force)
    {
        var c = _doc.Config;
        var now = (c.Theme, c.IconStyle, c.ReduceMotion || c.EconomyMode, OverridesKey(c));   // le mode économie coupe les animations
        if (!force && _applied == now) return;
        bool iconsChanged = _applied == null || _applied.Value.IconStyle != c.IconStyle;
        _applied = now;
        IconCatalog.UseFluentForEmoji = c.IconStyle != "emoji";
        Motion.Apply(c.ReduceMotion || c.EconomyMode);
        Controls.WebFramePool.Economy = c.EconomyMode;
        Theme.Apply(c.Theme, c.ThemeOverrides);
        if (iconsChanged && !force) Theme.RaiseChanged();   // redessine les icônes même si le thème n'a pas bougé
    }
}
