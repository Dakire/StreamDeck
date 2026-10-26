using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>
/// Un service web dont on sait lire le nombre de notifications non lues : adresse de départ, adresses reconnues (pour savoir quel bouton porte le badge) et
/// façon de lire le compteur dans la page (titre, ou script en lecture seule). Pur : aucun accès réseau ni WPF, testé avec des titres types.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Identifiant stable (« gmail »), clé de la valeur d'état <c>notify.gmail</c>.</summary>
    string Id { get; }
    /// <summary>Nom affiché.</summary>
    string Name { get; }
    /// <summary>Page ouverte en arrière-plan pour suivre le compteur (la session connectée dans le profil WebView2 de StreamDak s'y applique).</summary>
    Uri StartUrl { get; }
    /// <summary>true si l'adresse d'un bouton appartient à ce service (le bouton porte alors le badge).</summary>
    bool Matches(Uri url);
    /// <summary>
    /// Nombre de non lus d'après la page suivie : 0 si la page est celle du service sans compteur, null si on ne sait pas (autre page, connexion en cours,
    /// titre illisible).
    /// </summary>
    int? CountFromTitle(Uri page, string title);
    /// <summary>Script en lecture seule exécuté dans la page, qui rend un nombre ou null ; null si le titre suffit.</summary>
    string? CountScript { get; }
    /// <summary>Intervalle de relecture du script (le titre, lui, se lit sur événement) ; null si aucun script.</summary>
    TimeSpan? ScriptInterval { get; }
}

/// <summary>Service dont le compteur se lit dans le titre de la page (« (3) Discord »), et facultativement avec un sélecteur CSS.</summary>
public sealed class NotificationProvider : INotificationProvider
{
    /// <summary>Délai maximal d'une recherche d'expression régulière : une expression saisie ne peut pas figer l'interface.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    /// <summary>Relecture d'un script de sélecteur CSS.</summary>
    public static readonly TimeSpan DefaultScriptInterval = TimeSpan.FromSeconds(60);

    readonly string[] _hosts;
    readonly Regex? _title;

    /// <summary>Service décrit par ses adresses, un motif de titre et un sélecteur facultatif.</summary>
    /// <param name="id">Identifiant stable.</param>
    /// <param name="name">Nom affiché.</param>
    /// <param name="startUrl">Page suivie.</param>
    /// <param name="hosts">Noms d'hôte du service (un hôte « slack.com » couvre « app.slack.com »).</param>
    /// <param name="titlePattern">Expression régulière du titre : le groupe <c>n</c> (sinon le premier groupe) est le nombre ; le groupe <c>m</c> vaut « au moins un ». Null : titre ignoré.</param>
    /// <param name="selector">Sélecteur CSS des pastilles de la page (leur nombre, ou la somme des nombres qu'elles contiennent), ou null.</param>
    /// <param name="attribute">Attribut qui porte le nombre (sinon le texte de l'élément).</param>
    public NotificationProvider(string id, string name, string startUrl, IEnumerable<string> hosts, string? titlePattern, string? selector = null, string? attribute = null)
    {
        Id = id;
        Name = name;
        StartUrl = new Uri(startUrl);
        _hosts = hosts.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).ToArray();
        if (_hosts.Length == 0) throw new ArgumentException("Au moins un nom d'hôte est nécessaire", nameof(hosts));
        _title = titlePattern == null ? null : new Regex(titlePattern, RegexOptions.CultureInvariant, RegexTimeout);
        if (!string.IsNullOrWhiteSpace(selector))
        {
            CountScript = BuildScript(selector.Trim(), string.IsNullOrWhiteSpace(attribute) ? null : attribute.Trim());
            ScriptInterval = DefaultScriptInterval;
        }
    }

    /// <inheritdoc/>
    public string Id { get; }
    /// <inheritdoc/>
    public string Name { get; }
    /// <inheritdoc/>
    public Uri StartUrl { get; }
    /// <inheritdoc/>
    public string? CountScript { get; }
    /// <inheritdoc/>
    public TimeSpan? ScriptInterval { get; }

    /// <inheritdoc/>
    public bool Matches(Uri url) => url.Scheme is "http" or "https" && OnHost(url);

    bool OnHost(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        return _hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public int? CountFromTitle(Uri page, string title)
    {
        if (!Matches(page)) return null;          // page de connexion d'un autre domaine, erreur, autre site : aucun badge
        if (_title == null) return 0;
        try
        {
            var m = _title.Match(title ?? "");
            if (!m.Success) return 0;
            var number = m.Groups["n"].Success ? m.Groups["n"] : m.Groups.Count > 1 ? m.Groups[1] : null;
            if (number is { Success: true } && int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return Math.Clamp(n, 0, 999_999);
            return m.Groups["m"].Success ? 1 : 0;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>
    /// Script en lecture seule : le sélecteur et l'attribut sont passés comme des chaînes JSON, jamais collés dans le code. Rend la somme des nombres lus
    /// (un élément sans nombre compte pour 1), 0 s'il n'y en a aucun, null si le sélecteur est invalide.
    /// </summary>
    public static string BuildScript(string selector, string? attribute) =>
        "(function(){try{var s=" + JsonSerializer.Serialize(selector) + ",a=" + (attribute == null ? "null" : JsonSerializer.Serialize(attribute)) + ";" +
        "var e=document.querySelectorAll(s),t=0;for(var i=0;i<e.length&&i<500;i++){var x=a?e[i].getAttribute(a):e[i].textContent;var m=(x||'').match(/\\d+/);t+=m?parseInt(m[0],10):1}return t}catch(x){return null}})()";
}

/// <summary>Services connus : Gmail, Google Chat, Discord, Teams, Slack, plus ceux que l'utilisateur décrit dans <c>notifications.json</c>.</summary>
public sealed partial class NotificationProviders
{
    /// <summary>Nom du fichier des services définis par l'utilisateur, dans le dossier de configuration.</summary>
    public const string FileName = "notifications.json";
    /// <summary>Taille maximale du fichier.</summary>
    public const int MaxFileBytes = 64 * 1024;
    /// <summary>Nombre maximal de services définis par l'utilisateur.</summary>
    public const int MaxCustom = 10;

    // Les titres ci-dessous sont ceux qu'affichent ces services pour un compte connecté ; ils ne sont pas garantis (les services changent leur page) :
    // un service qui ne suit plus ce format n'affiche simplement plus de badge, et le fournisseur générique permet de le décrire soi-même.
    const string LeadingCount = @"^\s*\((?<n>\d{1,6})\)";

    /// <summary>Services livrés avec l'application.</summary>
    public static IReadOnlyList<INotificationProvider> BuiltIn { get; } = new INotificationProvider[]
    {
        new NotificationProvider("gmail", "Gmail", "https://mail.google.com/mail/u/0/#inbox", new[] { "mail.google.com" }, @"\((?<n>\d{1,6})\)"),
        new NotificationProvider("gchat", "Google Chat", "https://chat.google.com/", new[] { "chat.google.com" }, LeadingCount),
        new NotificationProvider("discord", "Discord", "https://discord.com/channels/@me", new[] { "discord.com", "discordapp.com" }, LeadingCount),
        new NotificationProvider("teams", "Microsoft Teams", "https://teams.microsoft.com/", new[] { "teams.microsoft.com", "teams.live.com", "teams.cloud.microsoft" }, LeadingCount),
        new NotificationProvider("slack", "Slack", "https://app.slack.com/client", new[] { "slack.com" }, LeadingCount + @"|^(?<m>[*!])\s"),
    };

    readonly IReadOnlyList<INotificationProvider> _all;

    /// <summary>Services livrés, suivis de ceux fournis.</summary>
    public NotificationProviders(IEnumerable<INotificationProvider>? custom = null) { _all = BuiltIn.Concat(custom ?? Array.Empty<INotificationProvider>()).ToList(); }

    /// <summary>Tous les services, livrés d'abord.</summary>
    public IReadOnlyList<INotificationProvider> All => _all;

    /// <summary>Le service d'identifiant donné (sans casse), ou null.</summary>
    public INotificationProvider? Find(string? id) => _all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Le service auquel appartient une adresse, ou null.</summary>
    public INotificationProvider? ForUrl(Uri url) => _all.FirstOrDefault(p => p.Matches(url));

    /// <summary>Le service d'un texte d'adresse (adresse d'un bouton), ou null si le texte n'est pas une adresse web.</summary>
    public INotificationProvider? ForUrl(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ? ForUrl(uri) : null;

    /// <summary>
    /// Services définis dans <c>notifications.json</c> du dossier donné. Un fichier absent donne les seuls services livrés ; une entrée invalide est
    /// ignorée avec un avertissement, jamais une exception.
    /// </summary>
    public static NotificationProviders Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new NotificationProviders();
            if (info.Length > MaxFileBytes) { Log.Warn($"{FileName} ignoré : plus de {MaxFileBytes / 1024} Ko"); return new NotificationProviders(); }
            return new NotificationProviders(Parse(File.ReadAllText(path), Log.Warn));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{FileName} illisible : {ex.Message}");
            return new NotificationProviders();
        }
    }

    /// <summary>
    /// Services d'un texte JSON <c>{ "providers": [ { "id", "name", "url", "hosts", "titleRegex", "selector", "attribute" } ] }</c> ; chaque entrée est
    /// validée (identifiant, adresse http(s), expression régulière compilable, sélecteur et attribut bornés), les autres sont signalées à <paramref name="warn"/>.
    /// </summary>
    public static IReadOnlyList<INotificationProvider> Parse(string json, Action<string> warn)
    {
        var list = new List<INotificationProvider>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in providers.EnumerateArray().Take(MaxCustom))
        {
            string? id = Text(item, "id");
            try
            {
                if (id == null || !IdPattern().IsMatch(id)) throw new FormatException("identifiant : 1 à 32 lettres minuscules, chiffres ou tirets");
                if (BuiltIn.Any(b => b.Id == id) || list.Any(p => p.Id == id)) throw new FormatException("identifiant déjà pris");
                var name = Text(item, "name") ?? id;
                if (name.Length > 60) throw new FormatException("nom de plus de 60 caractères");
                if (!WidgetTypes.IsWebAddress(Text(item, "url"), out var url)) throw new FormatException("adresse : http ou https attendu");
                var hosts = item.TryGetProperty("hosts", out var h) && h.ValueKind == JsonValueKind.Array
                    ? h.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Take(5).ToArray() : new[] { url.Host };
                if (hosts.Length == 0 || hosts.Any(x => !WidgetFormat.IsHostName(x))) throw new FormatException("hosts : noms d'hôte valides attendus");
                var regex = Text(item, "titleRegex");
                if (regex is { Length: > 200 }) throw new FormatException("titleRegex : 200 caractères au plus");
                var selector = Text(item, "selector");
                if (selector is { Length: > 200 }) throw new FormatException("selector : 200 caractères au plus");
                var attribute = Text(item, "attribute");
                if (attribute != null && !AttributePattern().IsMatch(attribute)) throw new FormatException("attribute : lettres, chiffres, tiret ou deux-points");
                if (regex == null && selector == null) throw new FormatException("titleRegex ou selector est nécessaire");
                list.Add(new NotificationProvider(id, name, url.AbsoluteUri, hosts, regex, selector, attribute));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or RegexParseException or UriFormatException)
            {
                warn($"{FileName} : service « {id ?? "?"} » ignoré ({ex.Message})");
            }
        }
        return list;
    }

    static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s.Trim() : null;

    [GeneratedRegex(@"^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9:_-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();
}

/// <summary>Texte du badge rouge d'un bouton et adresses suivies.</summary>
public static class NotificationBadge
{
    /// <summary>Nombre au-delà duquel le badge affiche « 99+ ».</summary>
    public const int Cap = 99;

    /// <summary>« 3 », « 99 », « 99+ » ; null si rien n'est à afficher (zéro ou moins).</summary>
    public static string? Text(int? count) =>
        count is not > 0 ? null : count > Cap ? Cap.ToString(CultureInfo.InvariantCulture) + "+" : count.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Adresses web de la configuration qu'un service pourrait suivre : étapes « url », widgets et pages web.</summary>
    public static IEnumerable<Uri> Urls(DeckConfig config)
    {
        foreach (var profile in config.Profiles)
            foreach (var page in profile.Pages)
            {
                if (page.IsWeb && Uri.TryCreate(page.Url, UriKind.Absolute, out var pageUrl) && pageUrl.Scheme is "http" or "https") yield return pageUrl;
                foreach (var button in DeckDocument.AllButtons(page.Buttons))
                {
                    foreach (var action in button.Actions)
                        if (string.Equals(action.Type, ActionTypes.Url, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(action.Value?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
                            yield return url;
                    if (button.Widget == WidgetTypes.Web && WidgetTypes.IsWebAddress(button.Option(WidgetOptionKeys.Url), out var widgetUrl)) yield return widgetUrl;
                }
            }
    }
}

/// <summary>
/// Suit en arrière-plan les services dont un bouton porte l'adresse et publie leur nombre de non lus. L'application fournit l'implémentation
/// (WebView2 caché, session du profil de StreamDak) ; le connecteur « Notifications » la pilote.
/// </summary>
public interface INotificationWatcher
{
    /// <summary>Le compteur d'un service a changé (identifiant, nombre) ; null : inconnu (connexion nécessaire, page indisponible). N'importe quel thread.</summary>
    event Action<string, int?>? CountChanged;
    /// <summary>Commence à suivre les services (de <paramref name="only"/> si renseigné, sinon tous) dont une adresse figure dans la configuration.</summary>
    void Start(IReadOnlySet<string> only);
    /// <summary>Arrête tout et libère les pages cachées.</summary>
    void Stop();
    /// <summary>Relit tout de suite le compteur de chaque service suivi.</summary>
    void Refresh();
    /// <summary>true : deck masqué, les pages suivies sont mises en veille ; false : elles reprennent.</summary>
    void SetBackground(bool background);
}
