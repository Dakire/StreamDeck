using System.Collections.Generic;

namespace StreamDak;

/// <summary>
/// Couleurs choisies par l'utilisateur par-dessus un thème (accent, fond, surface, texte, succès, erreur, ADR 0034) : les couleurs liées sont recalculées
/// (surfaces voisines, textes secondaires, texte posé sur l'accent) et les contrastes insuffisants sont signalés. Logique pure, testée.
/// </summary>
public static class ThemeCustom
{
    /// <summary>La palette avec les couleurs choisies ; la palette elle-même si rien n'est choisi.</summary>
    public static Palette Apply(Palette p, IReadOnlyDictionary<string, string>? o)
    {
        if (o == null || o.Count == 0) return p;
        string? Get(string key) => o.TryGetValue(key, out var v) && ThemeOverrideKeys.IsHex(v) ? v : null;
        var bg = Get("background") ?? p.Window;
        var surface = Get("surface") ?? p.Surface;
        var text = Get("text") ?? p.Text;
        bool dark = Theme.Luminance(bg) < 0.5;
        var r = p;
        if (Get("background") != null)
            r = r with { Window = bg, EditorBg = bg, Input = Theme.Mix(bg, dark ? "#000000" : "#FFFFFF", 0.3), Panel = Theme.Mix(bg, surface, 0.5), Bar = Theme.Mix(bg, surface, 0.5) };
        if (Get("surface") != null)
        {
            var hi = Theme.Mix(surface, text, 0.08);
            r = r with { Surface = surface, SurfaceHi = hi, TileTop = hi, TileBottom = surface };
        }
        if (Get("text") != null)
            r = r with { Text = text, TextSoft = Theme.Mix(text, bg, 0.12), Dim = Theme.Mix(text, bg, 0.25), Focus = text };
        if (Get("accent") is { } accent)
        {
            var container = Theme.Mix(accent, bg, 0.75);
            r = r with
            {
                Accent = accent, AccentText = dark ? accent : Theme.Mix(accent, "#000000", 0.25), OnAccent = OnColor(accent),
                AccentContainer = container, OnAccentContainer = OnColor(container),
            };
        }
        if (Get("success") is { } success) r = r with { Success = success };
        if (Get("danger") is { } danger) r = r with { Danger = danger };
        return r;
    }

    /// <summary>Noir ou blanc, selon ce qui se lit le mieux sur cette couleur.</summary>
    public static string OnColor(string background) => Theme.Contrast("#000000", background) >= Theme.Contrast("#FFFFFF", background) ? "#000000" : "#FFFFFF";

    /// <summary>Phrases décrivant les contrastes insuffisants (texte 4,5 : 1, repères 3 : 1). Vide si tout est lisible.</summary>
    public static IReadOnlyList<string> Warnings(Palette p)
    {
        var list = new List<string>();
        void Check(string what, string fg, string bg, double min)
        {
            double c = Theme.Contrast(fg, bg);
            if (c < min) list.Add($"{what} : contraste {c:0.0} : 1, il faut au moins {min:0.#} : 1.");
        }
        Check("Texte sur le fond", p.Text, p.Window, 4.5);
        Check("Texte sur les surfaces", p.Text, p.Surface, 4.5);
        Check("Texte atténué sur les surfaces", p.Dim, p.SurfaceHi, 4.5);
        Check("Accent sur le fond", p.AccentText, p.Window, 3.0);
        Check("Texte sur l'accent", p.OnAccent, p.Accent, 4.5);
        Check("Erreur sur les surfaces", p.Danger, p.Surface, 4.5);
        Check("Succès sur les surfaces", p.Success, p.Surface, 3.0);
        return list;
    }
}
