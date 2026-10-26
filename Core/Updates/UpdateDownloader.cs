using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Avancement d'un téléchargement.</summary>
/// <param name="Received">Octets reçus (reprise comprise).</param>
/// <param name="Total">Taille totale, ou null si elle est inconnue.</param>
public readonly record struct DownloadProgress(long Received, long? Total)
{
    /// <summary>Part reçue (0 à 1), ou null si la taille est inconnue.</summary>
    public double? Fraction => Total is > 0 ? Math.Clamp((double)Received / Total.Value, 0, 1) : null;
}

/// <summary>Le fichier téléchargé n'est pas celui qui a été publié (somme SHA-256 différente, taille différente, somme absente) : il est supprimé et l'installation est refusée.</summary>
public sealed class UpdateIntegrityException : Exception
{
    /// <summary>Intégrité refusée, avec le message affichable.</summary>
    public UpdateIntegrityException(string message) : base(message) { }
}

/// <summary>Lecture d'un fichier de sommes <c>checksums.txt</c> (format de <c>sha256sum</c> : « &lt;64 chiffres hexadécimaux&gt;  nom »).</summary>
public static partial class ChecksumFile
{
    /// <summary>Taille maximale acceptée d'un fichier de sommes.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Somme SHA-256 (minuscules) annoncée pour <paramref name="fileName"/>, ou null ; le nom doit correspondre exactement (sans casse).</summary>
    public static string? Find(string text, string fileName)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = LinePattern().Match(line.TrimEnd('\r'));
            if (m.Success && string.Equals(m.Groups["name"].Value, fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups["hash"].Value.ToLowerInvariant();
        }
        return null;
    }

    [GeneratedRegex(@"^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>[^\\/\r\n]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LinePattern();

    /// <summary>Somme SHA-256 d'un fichier, en minuscules.</summary>
    public static async Task<string> HashFileAsync(string path, CancellationToken cancellation)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
    }
}

/// <summary>Variante de l'installation, lue dans <c>flavor.txt</c> (écrit par build.ps1) : la mise à jour reprend la même.</summary>
public static class InstallFlavor
{
    /// <summary>Avec le runtime .NET embarqué.</summary>
    public const string SelfContained = "self-contained";
    /// <summary>Sans le runtime .NET.</summary>
    public const string FrameworkDependent = "framework-dependent";

    /// <summary>Variante installée dans <paramref name="directory"/> ; le self-contained par défaut (version de développement, fichier absent).</summary>
    public static string Read(string directory)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(directory, "flavor.txt")).Trim();
            return text == FrameworkDependent ? FrameworkDependent : SelfContained;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return SelfContained; }
    }

    /// <summary>Nom du fichier d'installation d'une release : <c>StreamDak-1.4.0-self-contained-Setup.exe</c>.</summary>
    public static string SetupName(SemVer version, string flavor) => $"StreamDak-{version}-{flavor}-Setup.exe";
}

/// <summary>
/// Télécharge l'installateur d'une release GitHub et vérifie son intégrité avant tout lancement. Garde-fous : seules les adresses de fichiers de la release du dépôt
/// configuré sont acceptées ; les redirections (GitHub répond par un CDN) ne sont suivies que vers github.com et *.githubusercontent.com en HTTPS ; le fichier
/// <c>checksums.txt</c> de la release est obligatoire et la somme SHA-256 doit correspondre. Reprise propre : un fichier partiel est complété par une requête
/// <c>Range</c> et la somme est recalculée sur le fichier entier ; en cas de coupure, nouvelles tentatives avec attente. L'<see cref="HttpClient"/> fourni ne doit pas
/// suivre les redirections lui-même (<c>AllowAutoRedirect = false</c>).
/// </summary>
public sealed class UpdateDownloader
{
    /// <summary>Taille maximale d'un installateur.</summary>
    public const long MaxInstallerBytes = 600L * 1024 * 1024;
    /// <summary>Redirections suivies au plus.</summary>
    public const int MaxRedirects = 5;
    /// <summary>Tentatives au plus pour un téléchargement interrompu.</summary>
    public const int MaxAttempts = 4;

    readonly HttpClient _http;
    readonly string _userAgent;
    readonly Func<int, TimeSpan> _backoff;

    /// <summary>Téléchargeur ; <paramref name="backoff"/> donne l'attente avant la tentative n (tests : instantanée).</summary>
    public UpdateDownloader(HttpClient http, SemVer currentVersion, Func<int, TimeSpan>? backoff = null)
    {
        _http = http;
        _userAgent = "StreamDak/" + currentVersion;
        _backoff = backoff ?? (attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    /// <summary>L'installateur et le fichier de sommes d'une release pour la variante donnée ; null si la release n'a pas les deux (elle n'est alors pas installable).</summary>
    public static (ReleaseAsset Installer, ReleaseAsset Checksums)? Select(ReleaseInfo release, string flavor)
    {
        var installer = release.Assets.FirstOrDefault(a => string.Equals(a.Name, InstallFlavor.SetupName(release.Version, flavor), StringComparison.OrdinalIgnoreCase));
        var checksums = release.Assets.FirstOrDefault(a => string.Equals(a.Name, "checksums.txt", StringComparison.OrdinalIgnoreCase));
        return installer != null && checksums != null ? (installer, checksums) : null;
    }

    /// <summary>
    /// Télécharge et vérifie. Rend le chemin du fichier vérifié dans <paramref name="folder"/>. Lève <see cref="UpdateIntegrityException"/> si la somme ne correspond pas
    /// (le fichier est supprimé), <see cref="InvalidOperationException"/> pour une adresse refusée ou une réponse aberrante, <see cref="HttpRequestException"/> après les
    /// tentatives, <see cref="OperationCanceledException"/> si l'utilisateur annule (le fichier partiel est gardé pour une reprise).
    /// </summary>
    public async Task<string> DownloadAsync(UpdateRepo repo, ReleaseInfo release, string flavor, string folder, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        var (installer, checksums) = Select(release, flavor) ?? throw new InvalidOperationException(
            $"La release {release.Tag} ne contient pas {InstallFlavor.SetupName(release.Version, flavor)} et checksums.txt : installation refusée.");
        foreach (var asset in new[] { installer, checksums })
            if (!repo.IsAssetUrl(asset.Url, release.Tag)) throw new InvalidOperationException($"Adresse de téléchargement refusée : {asset.Name}");
        if (installer.Size is <= 0 or > MaxInstallerBytes) throw new InvalidOperationException("Taille de l'installateur aberrante : installation refusée.");

        Directory.CreateDirectory(folder);
        var sums = Encoding.UTF8.GetString(await GetBytesAsync(checksums.Url, ChecksumFile.MaxBytes, cancellation));
        var expected = ChecksumFile.Find(sums, installer.Name)
            ?? throw new UpdateIntegrityException($"checksums.txt ne contient pas de somme pour {installer.Name} : installation refusée.");

        var final = Path.Combine(folder, installer.Name);
        var part = final + ".part";
        if (File.Exists(final) && await ChecksumFile.HashFileAsync(final, cancellation) == expected) { progress?.Report(new DownloadProgress(installer.Size, installer.Size)); return final; }   // déjà là et vérifié

        for (int attempt = 1; ; attempt++)
        {
            try { await FetchToAsync(installer, part, progress, cancellation); break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException && attempt < MaxAttempts && !cancellation.IsCancellationRequested)
            {
                Log.Warn($"Mise à jour : téléchargement interrompu ({ex.Message}), tentative {attempt + 1} sur {MaxAttempts}");
                await Task.Delay(_backoff(attempt), cancellation);
            }
        }

        var actual = await ChecksumFile.HashFileAsync(part, cancellation);
        if (new FileInfo(part).Length != installer.Size || actual != expected)
        {
            File.Delete(part);
            throw new UpdateIntegrityException("Le fichier téléchargé ne correspond pas à la somme SHA-256 publiée : il a été supprimé, l'installation est refusée.");
        }
        File.Move(part, final, overwrite: true);
        return final;
    }

    /// <summary>Télécharge l'installateur dans <paramref name="part"/>, en reprenant à la taille du fichier partiel s'il existe et que le serveur accepte <c>Range</c>.</summary>
    async Task FetchToAsync(ReleaseAsset asset, string part, IProgress<DownloadProgress>? progress, CancellationToken cancellation)
    {
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > asset.Size) { File.Delete(part); have = 0; }
        if (have == asset.Size) { progress?.Report(new DownloadProgress(have, asset.Size)); return; }   // complet : seule la somme reste à vérifier

        using var response = await SendFollowingAsync(asset.Url, have > 0 ? have : null, cancellation);
        bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed) have = 0;                                        // le serveur a renvoyé le fichier entier (200) : on repart de zéro
        long total = (response.Content.Headers.ContentLength ?? asset.Size - have) + have;
        if (total > MaxInstallerBytes || (total != asset.Size && response.Content.Headers.ContentLength != null)) throw new UpdateIntegrityException("La taille reçue ne correspond pas à celle de la release : installation refusée.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long received = have;
        progress?.Report(new DownloadProgress(received, total));
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
            received += read;
            if (received > MaxInstallerBytes) throw new InvalidOperationException("Fichier trop volumineux : téléchargement interrompu.");
            progress?.Report(new DownloadProgress(received, total));
        }
        if (received < total) throw new IOException("Connexion coupée avant la fin du fichier.");
    }

    /// <summary>GET d'un petit fichier (checksums.txt), redirections contrôlées, taille bornée.</summary>
    async Task<byte[]> GetBytesAsync(Uri url, int max, CancellationToken cancellation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await SendFollowingAsync(url, null, cancellation);
                if (response.Content.Headers.ContentLength > max) throw new InvalidOperationException("checksums.txt trop volumineux");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                using var buffer = new MemoryStream();
                var chunk = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > max) throw new InvalidOperationException("checksums.txt trop volumineux");
                }
                return buffer.ToArray();
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < MaxAttempts && !cancellation.IsCancellationRequested)
            {
                await Task.Delay(_backoff(attempt), cancellation);
            }
        }
    }

    /// <summary>Envoie la requête et suit les redirections une à une, chacune contrôlée (HTTPS, hôtes de GitHub). Rend une réponse 200 ou 206.</summary>
    async Task<HttpResponseMessage> SendFollowingAsync(Uri url, long? from, CancellationToken cancellation)
    {
        var current = url;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            if (from is { } offset) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!UpdateRepo.IsAllowedRedirect(next)) throw new InvalidOperationException($"Redirection refusée vers {next.Host} : téléchargement interrompu.");
                current = next;
                continue;
            }
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent) return response;
            var code = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Le serveur a répondu {code}", null, (HttpStatusCode)code);
        }
        throw new InvalidOperationException("Trop de redirections : téléchargement interrompu.");
    }
}
