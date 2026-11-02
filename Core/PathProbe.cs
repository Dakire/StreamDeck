using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>
/// Existence d'un fichier ou d'un dossier pour la validation en direct des étapes. Un chemin réseau injoignable peut bloquer plusieurs secondes (disques locaux : appel direct) :
/// la vérification a donc un délai maximal et un cache, et un chemin qui ne répond pas à temps est tenu pour valide (pas d'avertissement à tort).
/// </summary>
public static class PathProbe
{
    /// <summary>Attente maximale d'une vérification.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    /// <summary>Durée de validité d'un résultat gardé.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    static readonly ConcurrentDictionary<string, (long At, bool Exists)> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Chemin UNC ou lecteur réseau : seuls cas où l'accès disque peut bloquer plusieurs secondes
    static bool MayBlock(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try
        {
            var root = Path.GetPathRoot(path);
            return root is { Length: >= 2 } && root[1] == ':' && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    /// <summary>true si le fichier (ou le dossier, si <paramref name="orDirectory"/>) existe, ou si on ne sait pas.</summary>
    public static bool Exists(string path, bool orDirectory = false)
    {
        // Disque local : File.Exists répond en microsecondes, sans pool de threads (saturé, il ferait attendre la validation) et sans cache (un fichier créé à l'instant doit être vu)
        if (!MayBlock(path)) return File.Exists(path) || (orDirectory && Directory.Exists(path));

        string key = (orDirectory ? "d|" : "f|") + path;
        long now = Environment.TickCount64;
        if (Cache.TryGetValue(key, out var hit) && now - hit.At < Ttl.TotalMilliseconds) return hit.Exists;
        bool result;
        try
        {
            // Chemin réseau : un thread dédié (jamais le pool) et un délai maximal ; sans réponse à temps le chemin est tenu pour valide
            var probe = Task.Factory.StartNew(() => File.Exists(path) || (orDirectory && Directory.Exists(path)), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            result = !probe.Wait(Timeout) || probe.Result;
        }
        catch (AggregateException) { result = false; }
        if (Cache.Count > 512) Cache.Clear();
        Cache[key] = (now, result);
        return result;
    }
}
