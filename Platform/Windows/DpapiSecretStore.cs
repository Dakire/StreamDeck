using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamDak.Integrations;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Magasin de secrets chiffré par DPAPI pour l'utilisateur Windows courant (<c>secrets.dat</c> dans le dossier de configuration) :
/// les mots de passe et jetons des intégrations n'apparaissent jamais en clair dans <c>config.json</c>, les sauvegardes, les
/// exports ni le journal. Le fichier copié sur un autre compte ou une autre machine est illisible, donc ignoré.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    // Valeur historique (ancien nom du produit) : elle fait partie du chiffrement des secrets déjà enregistrés, ne pas la renommer
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TouchDeck.secrets.v1");
    readonly string _path;
    readonly object _gate = new();
    Dictionary<string, string>? _cache;

    /// <summary>Magasin dans <paramref name="directory"/> (le dossier de configuration).</summary>
    public DpapiSecretStore(string directory) { _path = Path.Combine(directory, "secrets.dat"); }

    /// <inheritdoc/>
    public string? Get(string key)
    {
        lock (_gate)
        {
            var protectedValue = Load().GetValueOrDefault(key);
            if (protectedValue == null) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser)); }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                Log.Warn($"Secret illisible pour cet utilisateur : {key}");   // la clé seulement, jamais la valeur
                return null;
            }
        }
    }

    /// <inheritdoc/>
    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            var all = Load();
            if (string.IsNullOrEmpty(value)) all.Remove(key);
            else all[key] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
    }

    Dictionary<string, string> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Magasin de secrets illisible, ignoré : " + ex.Message);
            _cache = new();
        }
        return _cache;
    }
}