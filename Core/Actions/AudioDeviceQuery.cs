using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StreamDak.Actions;

/// <summary>
/// Désignation d'un périphérique audio par son nom : « Focusrite » (n'importe quel sens), « capture:Focusrite » (micro, entrée),
/// « render:Arctis Game » (sortie). La correspondance est une sous-chaîne, sans tenir compte de la casse ni des accents de majuscule ;
/// un nom exact l'emporte sur une simple sous-chaîne. L'identifiant complet du système est accepté aussi.
/// </summary>
public sealed record AudioDeviceQuery(AudioFlow? Flow, string Text)
{
    static readonly (string Prefix, AudioFlow Flow)[] Prefixes =
    {
        ("capture", AudioFlow.Capture), ("entree", AudioFlow.Capture), ("entrée", AudioFlow.Capture), ("micro", AudioFlow.Capture), ("in", AudioFlow.Capture),
        ("render", AudioFlow.Render), ("sortie", AudioFlow.Render), ("lecture", AudioFlow.Render), ("out", AudioFlow.Render),
    };

    /// <summary>Découpe « sens:nom » (le sens est facultatif) ; lève <see cref="ArgumentException"/> si le nom est vide.</summary>
    public static AudioDeviceQuery Parse(string? text)
    {
        text = text?.Trim() ?? "";
        var colon = text.IndexOf(':');
        if (colon > 0)
            foreach (var (prefix, flow) in Prefixes)
                if (text[..colon].Trim().Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return Check(new AudioDeviceQuery(flow, text[(colon + 1)..].Trim()));
        return Check(new AudioDeviceQuery(null, text));
    }

    static AudioDeviceQuery Check(AudioDeviceQuery query) =>
        query.Text.Length == 0 ? throw new ArgumentException("Indique le nom du périphérique audio (par exemple Focusrite)") : query;

    /// <summary>Le périphérique visé parmi ceux du système. Lève <see cref="InvalidOperationException"/> (message lisible) s'il n'y en a aucun ou plusieurs.</summary>
    public AudioEndpoint Resolve(IReadOnlyList<AudioEndpoint> endpoints)
    {
        var inFlow = endpoints.Where(e => Flow == null || e.Flow == Flow).ToList();
        var byId = inFlow.FirstOrDefault(e => e.Id.Equals(Text, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;
        var matches = inFlow.Where(e => e.Name.Contains(Text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1)
        {
            var exact = matches.Where(e => e.Name.Equals(Text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
        }
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0)
            throw new InvalidOperationException($"Aucun périphérique audio ne correspond à « {Text} » (commande « Lister » pour voir les noms)");
        throw new InvalidOperationException($"Plusieurs périphériques correspondent à « {Text} » : {Describe(matches)}. Précise le nom, ou « capture: » / « render: »");
    }

    /// <summary>Liste lisible : « Sortie : Haut-parleurs (Focusrite) ; Entrée : Micro (Focusrite) ».</summary>
    public static string Describe(IEnumerable<AudioEndpoint> endpoints) =>
        string.Join(" ; ", endpoints.Select(e => (e.Flow == AudioFlow.Capture ? "Entrée : " : "Sortie : ") + e.Name));
}

/// <summary>Argument d'une commande « audiodevice » : « périphérique » ou « périphérique=niveau ».</summary>
public static class AudioDeviceSyntax
{
    /// <summary>« Focusrite=50 » ou « capture:Focusrite=+5 » en désignation et niveau (« 50 », « +5 »). Le dernier signe égal sépare le niveau.</summary>
    public static (AudioDeviceQuery Device, string Level) SplitLevel(string? args)
    {
        var text = args?.Trim() ?? "";
        var eq = text.LastIndexOf('=');
        if (eq <= 0 || eq == text.Length - 1) throw new ArgumentException("Écris périphérique=niveau, par exemple Focusrite=50 ou capture:Focusrite=+5");
        var level = text[(eq + 1)..].Trim();
        if (!TryParseLevel(level, 0, out _)) throw new ArgumentException("Le niveau doit être un nombre de 0 à 100 (ou +5, -5)");
        return (AudioDeviceQuery.Parse(text[..eq]), level);
    }

    /// <summary>Niveau demandé (« 50 », « +5 », « -5 », « 40 % ») par rapport au niveau actuel, borné de 0 à 100 ; false si le texte n'est pas un nombre.</summary>
    public static bool TryParseLevel(string text, int current, out int percent)
    {
        text = text.Trim().TrimEnd('%').Trim();
        percent = 0;
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return false;
        percent = Math.Clamp(text.StartsWith('+') || text.StartsWith('-') ? current + n : n, 0, 100);
        return true;
    }
}
