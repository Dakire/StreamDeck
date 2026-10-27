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
    /// <summary>Source du compteur de ce service (réglage par service, ADR 0021). Par défaut <see cref="BadgeMode.Auto"/>.</summary>
    BadgeMode Mode => BadgeMode.Auto;
    /// <summary>
    /// Script injecté à la création de chaque document de la page cachée : un <c>MutationObserver</c> sur le sélecteur configuré qui envoie le nombre par
    /// <c>postMessage</c> (anti-rebond 500 ms). Null si le mode ou l'absence de sélecteur ne le demande pas.
    /// </summary>
    string? ObserverScript => null;
}

/// <summary>D'où vient le nombre affiché sur le badge d'un service (réglage <c>badgeMode</c> de <c>notifications.json</c>).</summary>
public enum BadgeMode
{
    /// <summary>Le titre d'abord, puis le sélecteur CSS s'il est configuré, puis le favicon comme simple activité (point sans chiffre).</summary>
    Auto,
    /// <summary>Le titre de la page seulement.</summary>
    Title,
    /// <summary>Le sélecteur CSS seulement (nécessite un sélecteur).</summary>
    Selector,
    /// <summary>Aucun badge : le service n'est pas suivi.</summary>
    Off,
}

/// <summary>Lecture d'un service : un nombre (null : inconnu) et, à défaut de nombre, un signal d'activité.</summary>
/// <param name="Count">Nombre de non lus, null si inconnu.</param>
/// <param name="Activity">true : quelque chose a changé (favicon) sans nombre lisible.</param>
public readonly record struct BadgeReading(int? Count, bool Activity);

/// <summary>Titres de page : lecture pure et bornée d'un compteur en tête de titre.</summary>
public static partial class NotificationTitle
{
    /// <summary>Plus grand compteur retenu ; au-delà, la valeur est ramenée à cette borne.</summary>
    public const int MaxCount = 999_999;

    /// <summary>Longueur maximale de titre examinée (le reste est ignoré).</summary>
    public const int MaxLength = 512;

    /// <summary>
    /// Compteur en tête de titre : « (3) Boîte » ou « [3] Boîte » (espaces initiaux tolérés), entier de 1 à 6 chiffres, borné à <see cref="MaxCount"/>. Null si le titre
    /// ne commence pas par un compteur (négatif, décimal, vide, nombre trop long, texte avant).
    /// </summary>
    public static int? Parse(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        if (title.Length > MaxLength) title = title[..MaxLength];
        var m = LeadingPattern().Match(title);
        if (!m.Success) return null;
        var digits = m.Groups["n"].Value;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 0, MaxCount) : null;
    }

    [GeneratedRegex(@"^\s*(?:\((?<n>\d{1,6})\)|\[(?<n>\d{1,6})\])", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingPattern();
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
    readonly string? _titlePattern;

    /// <summary>Service décrit par ses adresses, un motif de titre et un sélecteur facultatif.</summary>
    /// <param name="id">Identifiant stable.</param>
    /// <param name="name">Nom affiché.</param>
    /// <param name="startUrl">Page suivie.</param>
    /// <param name="hosts">Noms d'hôte du service (un hôte « slack.com » couvre « app.slack.com »).</param>
    /// <param name="titlePattern">Expression régulière du titre : le groupe <c>n</c> (sinon le premier groupe) est le nombre ; le groupe <c>m</c> vaut « au moins un ». Null : titre ignoré.</param>
    /// <param name="selector">Sélecteur CSS des pastilles de la page (leur nombre, ou la somme des nombres qu'elles contiennent), ou null.</param>
    /// <param name="attribute">Attribut qui porte le nombre (sinon le texte de l'élément).</param>
    /// <param name="mode">Source du compteur ; le sélecteur n'est lu qu'en <see cref="BadgeMode.Auto"/> et <see cref="BadgeMode.Selector"/>.</param>
    public NotificationProvider(string id, string name, string startUrl, IEnumerable<string> hosts, string? titlePattern, string? selector = null, string? attribute = null, BadgeMode mode = BadgeMode.Auto)
    {
        Id = id;
        Name = name;
        StartUrl = new Uri(startUrl);
        Mode = Enum.IsDefined(mode) ? mode : BadgeMode.Auto;
        _hosts = hosts.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).ToArray();
        if (_hosts.Length == 0) throw new ArgumentException("Au moins un nom d'hôte est nécessaire", nameof(hosts));
        _titlePattern = titlePattern;
        _title = titlePattern == null ? null : new Regex(titlePattern, RegexOptions.CultureInvariant, RegexTimeout);
        Selector = string.IsNullOrWhiteSpace(selector) ? null : selector.Trim();
        Attribute = string.IsNullOrWhiteSpace(attribute) ? null : attribute.Trim();
        if (Selector != null && Mode is BadgeMode.Auto or BadgeMode.Selector)
        {
            CountScript = BuildScript(Selector, Attribute);
            ObserverScript = BuildObserverScript(Selector, Attribute);
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
    public BadgeMode Mode { get; }
    /// <inheritdoc/>
    public string? ObserverScript { get; }
    /// <summary>Sélecteur CSS configuré (même s'il est ignoré par le mode), ou null.</summary>
    public string? Selector { get; }
    /// <summary>Attribut qui porte le nombre, ou null (texte de l'élément).</summary>
    public string? Attribute { get; }

    /// <summary>Copie du service avec d'autres réglages de badge (mode, sélecteur, attribut) ; adresses et motif de titre sont conservés.</summary>
    public NotificationProvider With(BadgeMode mode, string? selector, string? attribute) =>
        new(Id, Name, StartUrl.AbsoluteUri, _hosts, _titlePattern, selector, attribute, mode);

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

    /// <summary>Type du message envoyé par <see cref="BuildObserverScript"/>.</summary>
    public const string MessageType = "streamdak.badge";

    /// <summary>
    /// Script d'observation injecté à la création du document de la page cachée : même lecture que <see cref="BuildScript"/>, relancée (anti-rebond 500 ms) à chaque
    /// mutation du DOM ; le nombre n'est envoyé que s'il change, sous la forme <c>{"type":"streamdak.badge","count":N}</c>. Le sélecteur et l'attribut sont des chaînes
    /// JSON, jamais du code. Il ne remplace ni <c>window.Notification</c> ni aucun autre objet de la page.
    /// </summary>
    public static string BuildObserverScript(string selector, string? attribute) =>
        "(function(){if(window.__streamdakBadge)return;window.__streamdakBadge=1;try{var s=" + JsonSerializer.Serialize(selector) + ",a=" + (attribute == null ? "null" : JsonSerializer.Serialize(attribute)) + ",last=-1,t=null;" +
        "function read(){var e=document.querySelectorAll(s),n=0;for(var i=0;i<e.length&&i<500;i++){var x=a?e[i].getAttribute(a):e[i].textContent;var m=(x||'').match(/\\d+/);n+=m?parseInt(m[0],10):1}return n}" +
        "function send(){try{var n=read();if(n!==last&&window.chrome&&window.chrome.webview){last=n;window.chrome.webview.postMessage({type:'" + MessageType + "',count:n})}}catch(x){}}" +
        "function later(){if(t)return;t=setTimeout(function(){t=null;send()},500)}" +
        "function start(){new MutationObserver(later).observe(document.documentElement,{childList:true,subtree:true,characterData:true,attributes:true});later()}" +
        "if(document.documentElement)start();else document.addEventListener('DOMContentLoaded',start)}catch(x){}})()";
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
    const string LeadingCount = @"^\s*(?:\((?<n>\d{1,6})\)|\[(?<n>\d{1,6})\])";

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

    /// <summary>
    /// Services livrés, suivis de ceux fournis. <paramref name="overrides"/> (clé : identifiant) change le mode de badge, le sélecteur et l'attribut d'un service,
    /// livré ou non ; une clé sans service est ignorée.
    /// </summary>
    public NotificationProviders(IEnumerable<INotificationProvider>? custom = null, IReadOnlyDictionary<string, BadgeOverride>? overrides = null)
    {
        _all = BuiltIn.Concat(custom ?? Array.Empty<INotificationProvider>())
            .Select(p => overrides != null && p is NotificationProvider np && overrides.TryGetValue(p.Id, out var o)
                ? np.With(o.Mode ?? np.Mode, o.Selector ?? np.Selector, o.Selector == null ? o.Attribute ?? np.Attribute : o.Attribute)
                : p)
            .ToList();
    }

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
            var text = File.ReadAllText(path);
            return new NotificationProviders(Parse(text, Log.Warn), ParseOverrides(text, Log.Warn));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"{FileName} illisible : {ex.Message}");
            return new NotificationProviders();
        }
    }

    /// <summary>
    /// Services d'un texte JSON <c>{ "providers": [ { "id", "name", "url", "hosts", "titleRegex", "selector", "attribute", "badgeMode" } ] }</c> ; chaque entrée est
    /// validée (identifiant, adresse http(s), expression régulière compilable, sélecteur et attribut bornés, mode connu), les autres sont signalées à <paramref name="warn"/>.
    /// <c>badgeMode</c> (« auto », « title », « selector », « off ») est facultatif : absent, il vaut « auto », comme dans les fichiers antérieurs.
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
                var mode = ReadMode(item) ?? BadgeMode.Auto;
                if (regex == null && selector == null && mode != BadgeMode.Off) throw new FormatException("titleRegex ou selector est nécessaire");
                if (mode == BadgeMode.Selector && selector == null) throw new FormatException("badgeMode « selector » demande un selector");
                list.Add(new NotificationProvider(id, name, url.AbsoluteUri, hosts, regex, selector, attribute, mode));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or RegexParseException or UriFormatException)
            {
                warn($"{FileName} : service « {id ?? "?"} » ignoré ({ex.Message})");
            }
        }
        return list;
    }

    /// <summary>
    /// Réglages de badge par service d'un texte JSON <c>{ "badges": { "gmail": { "badgeMode": "off", "selector": "…", "attribute": "…" } } }</c> : ils s'appliquent à
    /// n'importe quel service, y compris ceux livrés. Une entrée invalide est signalée à <paramref name="warn"/> et ignorée.
    /// </summary>
    public static IReadOnlyDictionary<string, BadgeOverride> ParseOverrides(string json, Action<string> warn)
    {
        var result = new Dictionary<string, BadgeOverride>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("badges", out var badges) || badges.ValueKind != JsonValueKind.Object) return result;
        foreach (var entry in badges.EnumerateObject().Take(MaxCustom + BuiltIn.Count))
        {
            try
            {
                if (!IdPattern().IsMatch(entry.Name)) throw new FormatException("identifiant : 1 à 32 lettres minuscules, chiffres ou tirets");
                if (entry.Value.ValueKind != JsonValueKind.Object) throw new FormatException("objet attendu");
                var selector = Text(entry.Value, "selector");
                if (selector is { Length: > 200 }) throw new FormatException("selector : 200 caractères au plus");
                var attribute = Text(entry.Value, "attribute");
                if (attribute != null && !AttributePattern().IsMatch(attribute)) throw new FormatException("attribute : lettres, chiffres, tiret ou deux-points");
                var mode = ReadMode(entry.Value);
                if (mode == BadgeMode.Selector && selector == null) throw new FormatException("badgeMode « selector » demande un selector");
                result[entry.Name] = new BadgeOverride(mode, selector, attribute);
            }
            catch (FormatException ex) { warn($"{FileName} : réglage de badge « {entry.Name} » ignoré ({ex.Message})"); }
        }
        return result;
    }

    /// <summary>Mode de badge de <c>badgeMode</c> (sans casse) ; null si la clé est absente ; <see cref="FormatException"/> si la valeur est inconnue.</summary>
    static BadgeMode? ReadMode(JsonElement item)
    {
        if (!item.TryGetProperty("badgeMode", out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String && Enum.TryParse<BadgeMode>(v.GetString(), ignoreCase: true, out var mode) && Enum.IsDefined(mode)) return mode;
        throw new FormatException("badgeMode : auto, title, selector ou off");
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

    /// <summary>Pastille d'activité : un point, sans chiffre.</summary>
    public const string ActivityText = "•";

    /// <summary>Nom accessible d'un badge : « 3 notification(s) » ou « activité » pour le point.</summary>
    public static string Describe(string badge) => badge == ActivityText ? "activité" : badge + " notification(s)";

    /// <summary>Le nombre s'il y en a un à afficher, sinon le point d'activité, sinon null.</summary>
    public static string? Text(int? count, bool activity) => Text(count) ?? (activity ? ActivityText : null);

    /// <summary>
    /// Somme des compteurs en ignorant les services ou pages exclus et les valeurs inconnues ou négatives ; bornée (pas de dépassement).
    /// </summary>
    public static int Total(IEnumerable<(int? Count, bool Excluded)> items)
    {
        long total = 0;
        foreach (var (count, excluded) in items)
            if (!excluded && count is > 0) total += Math.Min(count.Value, NotificationTitle.MaxCount);
        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// Lecture d'un service d'après ses sources et son <paramref name="mode"/>. <paramref name="title"/> est le compteur du titre (0 : page du service sans compteur ;
    /// null : page hors du service ou illisible, donc résultat inconnu quel que soit le mode). <paramref name="selector"/> : nombre lu par le sélecteur (null : pas de lecture).
    /// <paramref name="activity"/> : le favicon a changé ; il ne compte qu'en <see cref="BadgeMode.Auto"/> et seulement sans nombre.
    /// </summary>
    public static BadgeReading Resolve(BadgeMode mode, int? title, int? selector, bool activity)
    {
        if (mode == BadgeMode.Off || title == null) return new BadgeReading(null, false);
        switch (mode)
        {
            case BadgeMode.Title: return new BadgeReading(title, false);
            case BadgeMode.Selector: return new BadgeReading(selector ?? 0, false);
            default:
                int count = title > 0 ? title.Value : selector ?? title.Value;
                return new BadgeReading(count, activity && count <= 0);
        }
    }

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

/// <summary>Réglages de badge d'un service dans <c>notifications.json</c> › <c>badges</c> ; une propriété null garde la valeur du service.</summary>
public sealed record BadgeOverride(BadgeMode? Mode, string? Selector, string? Attribute);

/// <summary>
/// Message de la page cachée (ADR 0021) : validation stricte, sans rien exécuter ni conserver. Tout ce qui n'est pas exactement
/// <c>{"type":"streamdak.badge","count":N}</c> (N entier de 0 à <see cref="NotificationTitle.MaxCount"/>), envoyé par une page de l'hôte du service, est ignoré.
/// </summary>
public static class NotificationMessage
{
    /// <summary>Taille maximale d'un message examiné.</summary>
    public const int MaxLength = 256;

    /// <summary>true et le nombre si <paramref name="json"/> est un message valide, que le mode du service demande le script, et que <paramref name="source"/> (adresse du document émetteur) est une page du service.</summary>
    public static bool TryRead(INotificationProvider provider, string? source, string? json, out int count)
    {
        count = 0;
        if (provider.Mode is BadgeMode.Off or BadgeMode.Title) return false;                 // le script n'est pas demandé : rien à écouter
        if (!Uri.TryCreate(source, UriKind.Absolute, out var origin) || !provider.Matches(origin)) return false;
        return TryParse(json, out count);
    }

    /// <summary>Forme du message seule, sans l'origine.</summary>
    public static bool TryParse(string? json, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(json) || json.Length > MaxLength) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            int properties = 0;
            foreach (var _ in root.EnumerateObject()) properties++;
            if (properties != 2) return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != NotificationProvider.MessageType) return false;
            if (!root.TryGetProperty("count", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var n)) return false;
            if (n < 0 || n > NotificationTitle.MaxCount) return false;
            count = n;
            return true;
        }
        catch (JsonException) { return false; }
    }
}

/// <summary>
/// Activité d'après le favicon : la première icône vue sur la page est la référence ; une icône différente signale une activité (point sans chiffre), le retour à la référence l'efface.
/// Heuristique : suffisante pour les services qui changent d'icône quand il y a du nouveau.
/// </summary>
public sealed class FaviconActivity
{
    string? _baseline;

    /// <summary>Retient l'icône courante (null : aucune) ; rend true si elle diffère de la référence.</summary>
    public bool Observe(string? favicon)
    {
        if (string.IsNullOrEmpty(favicon)) return false;
        _baseline ??= favicon;
        return !string.Equals(_baseline, favicon, StringComparison.Ordinal);
    }

    /// <summary>Oublie la référence (autre page, autre service).</summary>
    public void Reset() => _baseline = null;
}

/// <summary>
/// Suit en arrière-plan les services dont un bouton porte l'adresse et publie leur nombre de non lus. L'application fournit l'implémentation
/// (WebView2 caché, session du profil de StreamDak) ; le connecteur « Notifications » la pilote.
/// </summary>
public interface INotificationWatcher
{
    /// <summary>Le compteur d'un service a changé (identifiant, nombre) ; null : inconnu (connexion nécessaire, page indisponible). N'importe quel thread.</summary>
    event Action<string, int?>? CountChanged;
    /// <summary>
    /// Un service montre une activité sans nombre lisible (favicon changé) : identifiant, présence. Facultatif : une implémentation qui ne l'a pas ne lève rien
    /// (membre par défaut sans effet), donc rétrocompatible avec les implémentations antérieures.
    /// </summary>
    event Action<string, bool>? ActivityChanged { add { } remove { } }
    /// <summary>Commence à suivre les services (de <paramref name="only"/> si renseigné, sinon tous) dont une adresse figure dans la configuration.</summary>
    void Start(IReadOnlySet<string> only);
    /// <summary>Arrête tout et libère les pages cachées.</summary>
    void Stop();
    /// <summary>Relit tout de suite le compteur de chaque service suivi.</summary>
    void Refresh();
    /// <summary>true : deck masqué, les pages suivies sont mises en veille ; false : elles reprennent.</summary>
    void SetBackground(bool background);
}
