using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>
/// Syntaxe des raccourcis (« ctrl+shift+m », « win+d », « f13 », « playpause ») : noms de touches reconnus et validation
/// sans rien envoyer. La correspondance avec les codes du système appartient à chaque plateforme
/// (Windows : <c>NativeInput</c>) ; un test vérifie qu'elle reconnaît exactement les mêmes noms.
/// </summary>
public static class HotkeySyntax
{
    /// <summary>Modificateurs : écrits d'abord dans la séquence envoyée, relâchés en dernier.</summary>
    public static readonly IReadOnlySet<string> Modifiers =
        new HashSet<string>(new[] { "ctrl", "control", "shift", "alt", "win" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>Noms de touches reconnus, hors touches de fonction (F1 à F24) et caractères seuls.</summary>
    public static readonly IReadOnlySet<string> KeyNames = new HashSet<string>(new[]
    {
        "ctrl", "control", "shift", "alt", "win",
        "enter", "return", "esc", "escape", "tab", "space", "backspace", "delete", "del", "insert",
        "home", "end", "pageup", "pagedown", "left", "up", "right", "down", "printscreen",
        "playpause", "next", "prev", "stop", "mute", "voldown", "volup",
    }, StringComparer.OrdinalIgnoreCase);

    /// <summary>Découpe « ctrl + shift+m » en parties sans espaces ni parties vides.</summary>
    public static string[] Split(string? combo) =>
        (combo ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>true pour F1 à F24 (« f13 »).</summary>
    public static bool IsFunctionKey(string key, out int number)
    {
        number = 0;
        return key.Length >= 2 && (key[0] == 'f' || key[0] == 'F') && int.TryParse(key.AsSpan(1), out number) && number is >= 1 and <= 24;
    }

    /// <summary>
    /// Vérifie une combinaison sans l'envoyer. <paramref name="canType"/> décide si un caractère seul est une touche valide
    /// (sur Windows : présent sur la disposition du clavier courante) ; par défaut, tout caractère non de contrôle.
    /// <paramref name="error"/> : raison en français si invalide.
    /// </summary>
    public static bool TryValidate(string? combo, out string error, Func<char, bool>? canType = null)
    {
        error = "";
        var parts = Split(combo);
        if (parts.Length == 0) { error = "Le raccourci est vide"; return false; }
        if (parts.All(Modifiers.Contains)) { error = "Ajoute une touche en plus des modificateurs"; return false; }
        foreach (var p in parts)
        {
            bool known = KeyNames.Contains(p) || IsFunctionKey(p, out _)
                         || (p.Length == 1 && (canType?.Invoke(p[0]) ?? !char.IsControl(p[0])));
            if (!known) { error = $"Touche inconnue : « {p} »"; return false; }
        }
        return true;
    }
}
