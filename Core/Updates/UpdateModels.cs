using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>Dépôt GitHub dont on lit les releases. Les deux noms sont validés : ils entrent dans une adresse d'API, jamais un chemin libre.</summary>
public sealed partial record UpdateRepo(string Owner, string Repo)
{
    /// <summary>Hôte de l'API GitHub (HTTPS seulement).</summary>
    public const string ApiHost = "api.github.com";
    /// <summary>Hôte des pages et des fichiers d'une release.</summary>
    public const string WebHost = "github.com";

    /// <summary>true si propriétaire et dépôt sont des noms GitHub valides.</summary>
    public bool IsValid => OwnerPattern().IsMatch(Owner) && RepoPattern().IsMatch(Repo) && Repo is not ("." or "..");

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepoPattern();

    /// <summary>Dernière release stable : <c>GET /repos/{owner}/{repo}/releases/latest</c>.</summary>
    public Uri LatestUrl => new($"https://{ApiHost}/repos/{Owner}/{Repo}/releases/latest");
    /// <summary>Liste des releases, pré-versions comprises (canal bêta).</summary>
    public Uri ListUrl => new($"https://{ApiHost}/repos/{Owner}/{Repo}/releases?per_page=20");

    /// <summary>
    /// true si l'adresse est un fichier de la release <paramref name="tag"/> de CE dépôt (<c>https://github.com/{owner}/{repo}/releases/download/{tag}/{fichier}</c>) :
    /// aucune adresse de téléchargement venue d'ailleurs n'est suivie.
    /// </summary>
    public bool IsAssetUrl(Uri url, string tag)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals(WebHost, StringComparison.OrdinalIgnoreCase) || !url.IsDefaultPort || url.Query.Length > 0 || url.UserInfo.Length > 0) return false;
        var prefix = $"/{Owner}/{Repo}/releases/download/{tag}/";
        var path = url.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var name = path[prefix.Length..];
        return name.Length > 0 && !name.Contains('/') && !name.Contains('\\') && !name.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>
    /// true si une redirection de téléchargement mène à un hôte de GitHub (le CDN des fichiers de release) en HTTPS : <c>github.com</c> ou un sous-domaine de
    /// <c>githubusercontent.com</c>. Tout autre hôte interrompt le téléchargement.
    /// </summary>
    public static bool IsAllowedRedirect(Uri target) =>
        target.Scheme == Uri.UriSchemeHttps && target.IsDefaultPort && target.UserInfo.Length == 0
        && (target.Host.Equals(WebHost, StringComparison.OrdinalIgnoreCase)
            || target.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Un fichier joint à une release.</summary>
/// <param name="Name">Nom du fichier.</param>
/// <param name="Url">Adresse de téléchargement (validée par <see cref="UpdateRepo.IsAssetUrl"/>).</param>
/// <param name="Size">Taille annoncée par GitHub, en octets.</param>
public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>Une release GitHub lisible : version, notes, fichiers valides.</summary>
/// <param name="Tag">Tag Git (« v1.4.0 »).</param>
/// <param name="Version">Version SemVer du tag.</param>
/// <param name="Name">Titre de la release.</param>
/// <param name="Notes">Notes de version (Markdown), tronquées à <see cref="GitHubReleases.MaxNotesLength"/>.</param>
/// <param name="PreRelease">true pour une pré-version.</param>
/// <param name="PageUrl">Page de la release sur GitHub.</param>
/// <param name="PublishedAt">Date de publication.</param>
/// <param name="Assets">Fichiers joints dont l'adresse appartient à ce dépôt.</param>
public sealed record ReleaseInfo(string Tag, SemVer Version, string Name, string Notes, bool PreRelease, Uri PageUrl, DateTimeOffset? PublishedAt, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Lecture de la réponse de l'API de releases de GitHub.</summary>
public static class GitHubReleases
{
    /// <summary>Longueur maximale gardée des notes de version.</summary>
    public const int MaxNotesLength = 20_000;
    /// <summary>Taille maximale lue dans une réponse de l'API.</summary>
    public const int MaxResponseBytes = 512 * 1024;

    /// <summary>
    /// Une release (objet JSON) ; null si c'est un brouillon, si le tag n'est pas une version SemVer, ou si la réponse est incomplète.
    /// Les fichiers dont l'adresse n'est pas celle d'un fichier de cette release dans <paramref name="repo"/> sont écartés.
    /// </summary>
    public static ReleaseInfo? ParseRelease(JsonNode? node, UpdateRepo repo)
    {
        if (node is not JsonObject obj) return null;
        if (obj["draft"]?.GetValue<bool>() == true) return null;
        var tag = obj["tag_name"]?.GetValue<string>();
        if (tag == null || !SemVer.TryParse(tag, out var version)) return null;
        var page = Uri.TryCreate(obj["html_url"]?.GetValue<string>(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals(UpdateRepo.WebHost, StringComparison.OrdinalIgnoreCase)
            ? u : new Uri($"https://{UpdateRepo.WebHost}/{repo.Owner}/{repo.Repo}/releases/tag/{Uri.EscapeDataString(tag)}");
        var assets = new List<ReleaseAsset>();
        if (obj["assets"] is JsonArray array)
            foreach (var a in array)
            {
                var name = a?["name"]?.GetValue<string>();
                if (name == null || !Uri.TryCreate(a?["browser_download_url"]?.GetValue<string>(), UriKind.Absolute, out var url) || !repo.IsAssetUrl(url, tag)) continue;
                assets.Add(new ReleaseAsset(name, url, a!["size"]?.GetValue<long>() ?? 0));
            }
        var notes = obj["body"]?.GetValue<string>() ?? "";
        if (notes.Length > MaxNotesLength) notes = notes[..MaxNotesLength] + "\n…";
        DateTimeOffset? published = DateTimeOffset.TryParse(obj["published_at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
        return new ReleaseInfo(tag, version, obj["name"]?.GetValue<string>() ?? tag, notes, obj["prerelease"]?.GetValue<bool>() == true || version.IsPreRelease, page, published, assets);
    }

    /// <summary>
    /// La release la plus récente d'une réponse : un objet (<c>/releases/latest</c>, stable) ou un tableau (<c>/releases</c>). Les pré-versions et les brouillons
    /// n'y comptent que si <paramref name="includePreReleases"/> est vrai (brouillons : jamais).
    /// </summary>
    public static ReleaseInfo? Newest(JsonNode? response, UpdateRepo repo, bool includePreReleases)
    {
        IEnumerable<JsonNode?> items = response is JsonArray array ? array : new[] { response };
        return items.Select(n => ParseRelease(n, repo))
            .Where(r => r != null && (includePreReleases || !r.PreRelease))
            .OrderByDescending(r => r!.Version).FirstOrDefault();
    }
}

/// <summary>Réglages des mises à jour, dans <c>update.json</c> (dossier de configuration) : jamais dans <c>config.json</c>.</summary>
public sealed class UpdateSettings
{
    /// <summary>Nom du fichier de réglages de l'utilisateur.</summary>
    public const string FileName = "update.json";
    /// <summary>Nom du fichier de valeurs par défaut livré avec l'application (à côté de l'exécutable).</summary>
    public const string DefaultsFileName = "update.defaults.json";

    /// <summary>Propriétaire du dépôt GitHub des releases.</summary>
    public string Owner { get; set; } = "";
    /// <summary>Nom du dépôt GitHub des releases.</summary>
    public string Repo { get; set; } = "";
    /// <summary>true : le canal bêta propose aussi les pré-versions.</summary>
    public bool Beta { get; set; }
    /// <summary>true : une vérification en arrière-plan au démarrage, une fois par jour au plus.</summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>Le dépôt configuré, ou null s'il est absent ou invalide.</summary>
    [JsonIgnore]
    public UpdateRepo? Repository => new UpdateRepo(Owner ?? "", Repo ?? "") is { IsValid: true } repo ? repo : null;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,   // « owner », « repo », « beta », « autoCheck »
    };

    /// <summary>
    /// Réglages : ceux de l'utilisateur (<c>update.json</c>), complétés par les valeurs livrées (<c>update.defaults.json</c>) pour le dépôt. Un fichier illisible est ignoré.
    /// </summary>
    public static UpdateSettings Load(string userDirectory, string defaultsDirectory)
    {
        var settings = Read(Path.Combine(defaultsDirectory, DefaultsFileName)) ?? new UpdateSettings();
        if (Read(Path.Combine(userDirectory, FileName)) is { } user)
        {
            if (user.Owner.Length > 0 && user.Repo.Length > 0) { settings.Owner = user.Owner; settings.Repo = user.Repo; }
            settings.Beta = user.Beta;
            settings.AutoCheck = user.AutoCheck;
        }
        return settings;
    }

    static UpdateSettings? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024) return null;
            var s = JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(path), Options);
            if (s == null) return null;
            s.Owner = (s.Owner ?? "").Trim();
            s.Repo = (s.Repo ?? "").Trim();
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{Path.GetFileName(path)} illisible : {ex.Message}");
            return null;
        }
    }

    /// <summary>Écrit les réglages de l'utilisateur (écriture atomique).</summary>
    public void Save(string userDirectory)
    {
        Directory.CreateDirectory(userDirectory);
        var path = Path.Combine(userDirectory, FileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Mémoire des vérifications (<c>update-state.json</c>) : dernier passage, ETag pour les requêtes conditionnelles, version ignorée.</summary>
public sealed class UpdateState
{
    /// <summary>Nom du fichier.</summary>
    public const string FileName = "update-state.json";

    /// <summary>Dernière vérification réussie (UTC).</summary>
    public DateTime? LastCheckUtc { get; set; }
    /// <summary>ETag de la dernière réponse de l'API.</summary>
    public string? ETag { get; set; }
    /// <summary>Canal de cet ETag (« stable » ou « beta ») : un ETag ne sert qu'à la même adresse.</summary>
    public string? ETagChannel { get; set; }
    /// <summary>Dépôt de cet ETag.</summary>
    public string? ETagRepo { get; set; }
    /// <summary>Corps de la dernière réponse, rejoué quand GitHub répond « 304 non modifié ».</summary>
    public string? CachedBody { get; set; }
    /// <summary>Tag « ignoré » par l'utilisateur : plus d'indicateur pour lui.</summary>
    public string? IgnoredVersion { get; set; }
    /// <summary>Pas de nouvelle requête avant cet instant (limite de l'API atteinte).</summary>
    public DateTime? NextAllowedUtc { get; set; }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Lit l'état ; absent ou illisible : un état neuf.</summary>
    public static UpdateState Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * GitHubReleases.MaxResponseBytes) return new UpdateState();
            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path), Options) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{FileName} illisible : {ex.Message}");
            return new UpdateState();
        }
    }

    /// <summary>Écrit l'état ; un échec d'écriture n'empêche rien (la prochaine vérification refera la requête).</summary>
    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"{FileName} non écrit : {ex.Message}"); }
    }
}

/// <summary>Résultat d'une vérification.</summary>
public enum UpdateStatus
{
    /// <summary>La version installée est la plus récente.</summary>
    UpToDate,
    /// <summary>Une version plus récente existe.</summary>
    Available,
    /// <summary>La vérification a échoué (réseau, limite de l'API, réponse illisible) ; <see cref="UpdateCheck.Message"/> dit pourquoi.</summary>
    Error,
    /// <summary>Aucun dépôt n'est configuré.</summary>
    Disabled,
}

/// <summary>Ce que la vérification a trouvé.</summary>
/// <param name="Status">Issue.</param>
/// <param name="Release">La version plus récente (<see cref="UpdateStatus.Available"/>), ou la plus récente connue.</param>
/// <param name="Message">Texte affichable : explication d'une erreur, ou « Aucune version publiée ».</param>
/// <param name="FromCache">true si GitHub a répondu « non modifié » et que le résultat vient de la mémoire.</param>
public sealed record UpdateCheck(UpdateStatus Status, ReleaseInfo? Release, string? Message, bool FromCache = false);
