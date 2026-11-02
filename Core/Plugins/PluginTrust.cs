using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamDak;

/// <summary>
/// Liste d'autorisation des plugins : <c>plugins\trusted.json</c> associe le nom d'un dossier de plugin à l'empreinte SHA-256 de son contenu (toutes ses
/// bibliothèques). Un plugin dont l'empreinte n'est pas listée, ou a changé, n'est pas chargé : son code ne s'exécute jamais sans l'accord de l'utilisateur.
/// Au premier démarrage après l'arrivée de cette protection, les plugins déjà posés sont autorisés (ils l'étaient jusque-là) ; tout ajout ou
/// toute modification ensuite demande d'ajouter l'empreinte affichée dans le journal.
/// </summary>
public sealed class PluginTrust
{
    /// <summary>Nom du fichier, dans le dossier des plugins.</summary>
    public const string FileName = "trusted.json";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly string _path;
    readonly Dictionary<string, string> _trusted = new(StringComparer.OrdinalIgnoreCase);

    PluginTrust(string path, bool exists, bool valid)
    {
        _path = path;
        FileExists = exists;
        IsValid = valid;
    }

    /// <summary>true si le fichier existe déjà.</summary>
    public bool FileExists { get; private set; }
    /// <summary>false si le fichier existe mais est illisible : alors aucun plugin n'est autorisé (on ne repart pas d'une liste vide en silence).</summary>
    public bool IsValid { get; }

    /// <summary>Lit la liste du dossier des plugins ; absente, elle est vide et <see cref="FileExists"/> est faux.</summary>
    public static PluginTrust Open(string pluginsDir)
    {
        var path = Path.Combine(pluginsDir, FileName);
        if (!File.Exists(path)) return new PluginTrust(path, exists: false, valid: true);
        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            var trust = new PluginTrust(path, exists: true, valid: true);
            foreach (var (name, hash) in entries) trust._trusted[name] = hash.Trim().ToUpperInvariant();
            return trust;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Plugins : {FileName} illisible ({ex.Message}), aucun plugin n'est chargé tant qu'il n'est pas corrigé");
            return new PluginTrust(path, exists: true, valid: false);
        }
    }

    /// <summary>true si ce plugin, avec cette empreinte, est autorisé.</summary>
    public bool IsTrusted(string name, string folderHash) =>
        IsValid && _trusted.TryGetValue(name, out var hash) && string.Equals(hash, folderHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>Autorise ces plugins et enregistre la liste.</summary>
    public void TrustAndSave(IEnumerable<(string Name, string Hash)> plugins)
    {
        foreach (var (name, hash) in plugins) _trusted[name] = hash.ToUpperInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_trusted.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(p => p.Key, p => p.Value), Json), new UTF8Encoding(false));
        FileExists = true;
    }

    /// <summary>Empreinte SHA-256 d'un dossier de plugin : noms et contenus de toutes ses bibliothèques (.dll), dans l'ordre des noms.</summary>
    public static string FolderHash(string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file).ToUpperInvariant() + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
