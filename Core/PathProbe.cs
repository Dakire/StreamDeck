using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>
/// Existence d'un fichier ou d'un dossier pour la validation en direct des étapes. Un chemin réseau injoignable peut bloquer plusieurs secondes :
/// la vérification a donc un délai maximal et un cache, et un chemin qui ne répond pas à temps est tenu pour valide (pas d'avertissement à tort).
/// </summary>
public static class PathProbe
{
    /// <summary>Attente maximale d'une vérification.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    /// <summary>Durée de validité d'un résultat gardé.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    static readonly ConcurrentDictionary<string, (long At, bool Exists)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>true si le fichier (ou le dossier, si <paramref name="orDirectory"/>) existe, ou si on ne sait pas.</summary>
    public static bool Exists(string path, bool orDirectory = false)
    {
        string key = (orDirectory ? "d|" : "f|") + path;
        long now = Environment.TickCount64;
        if (Cache.TryGetValue(key, out var hit) && now - hit.At < Ttl.TotalMilliseconds) return hit.Exists;
        bool result;
        try
        {
            var probe = Task.Run(() => File.Exists(path) || (orDirectory && Directory.Exists(path)));
            result = !probe.Wait(Timeout) || probe.Result;   // pas de réponse à temps : inconnu, donc pas d'avertissement
        }
        catch (AggregateException) { result = false; }
        if (Cache.Count > 512) Cache.Clear();
        Cache[key] = (now, result);
        return result;
    }
}
