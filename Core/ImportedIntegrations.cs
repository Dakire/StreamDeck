using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak;

/// <summary>
/// Garde-fou de l'import de configuration : les secrets des intégrations (clé Hue, mot de passe OBS) vivent dans le magasin de secrets de la
/// machine, pas dans le fichier. Un fichier importé qui change l'adresse d'une de ces intégrations enverrait donc ces secrets vers l'hôte de son
/// auteur. Une intégration importée dont l'adresse diffère de l'actuelle est remise à l'adresse actuelle et désactivée, et l'utilisateur en est informé.
/// </summary>
public static class ImportedIntegrations
{
    /// <summary>Intégrations à secret et leurs réglages d'adresse, avec la valeur par défaut qui s'applique quand le réglage est absent.</summary>
    static readonly Dictionary<string, (string Key, string Default)[]> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hue"] = [("host", ""), ("port", ""), ("scheme", "https")],
        ["obs"] = [("host", "localhost"), ("port", "4455")],
    };

    /// <summary>
    /// Neutralise, dans <paramref name="imported"/>, les intégrations à secret dont l'adresse diffère de <paramref name="current"/>.
    /// Retourne les noms des intégrations touchées (pour la confirmation d'import).
    /// </summary>
    public static IReadOnlyList<string> Neutralize(DeckConfig imported, DeckConfig current)
    {
        var touched = new List<string>();
        if (imported.Integrations == null) return touched;
        foreach (var (id, fields) in Sensitive)
        {
            var theirs = Find(imported, id);
            if (theirs == null) continue;
            var mine = Find(current, id);
            bool differs = fields.Any(f => !string.Equals(Effective(theirs, f), Effective(mine, f), StringComparison.OrdinalIgnoreCase));
            if (!differs) continue;

            theirs.Enabled = false;
            var values = theirs.Values != null ? new Dictionary<string, string>(theirs.Values, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, _) in fields)
            {
                if (mine?.Values != null && mine.Values.TryGetValue(key, out var own)) values[key] = own;
                else values.Remove(key);
            }
            theirs.Values = values.Count > 0 ? values : null;
            touched.Add(id);
        }
        return touched;
    }

    static IntegrationConfig? Find(DeckConfig cfg, string id) =>
        cfg.Integrations?.FirstOrDefault(p => string.Equals(p.Key, id, StringComparison.OrdinalIgnoreCase)).Value;

    static string Effective(IntegrationConfig? integration, (string Key, string Default) field) =>
        integration?.Values != null && integration.Values.TryGetValue(field.Key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : field.Default;
}
