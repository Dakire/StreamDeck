using System.IO;
using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>
/// Noms des images de boutons dans le dossier Icons : un hachage de 32 caractères hexadécimaux et « .png ».
/// La configuration ne contient que ce nom, jamais un chemin : c'est ce qui empêche un fichier de configuration ou une
/// archive importée de faire lire un fichier arbitraire.
/// </summary>
public static class IconNames
{
    static readonly Regex NameRx = new("^[0-9a-f]{32}\\.png$", RegexOptions.Compiled);

    /// <summary>true pour un nom produit par le magasin d'images (hachage + .png) : seul ce format est jamais lu.</summary>
    public static bool IsValid(string? name) => name != null && NameRx.IsMatch(name);

    /// <summary>Chemin complet d'une image stockée, ou null si le nom n'est pas valide (jamais de chemin libre).</summary>
    public static string? Resolve(string? name) => IsValid(name) ? Path.Combine(ConfigStore.IconsDir, name!) : null;
}
