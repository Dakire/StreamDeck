using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace StreamDak.Integrations.Sonar;

/// <summary>Niveau d'un canal du mélangeur : volume de 0 à 1 et coupure (null si l'API ne la donne pas).</summary>
/// <param name="Volume">Volume de 0 (silence) à 1 (maximum).</param>
/// <param name="Muted">true si le canal est coupé ; null si inconnu.</param>
public sealed record SonarLevel(double Volume, bool? Muted)
{
    /// <summary>Volume en pourcentage entier (0 à 100).</summary>
    public int Percent => (int)Math.Round(Math.Clamp(Volume, 0, 1) * 100);
}

/// <summary>Réglages du connecteur, lus dans les champs de l'intégration (rien n'est écrit en dur sans pouvoir être changé).</summary>
public sealed class SonarConfig
{
    /// <summary>Canaux suivis par défaut (noms du mélangeur gaming de Sonar).</summary>
    public const string DefaultChannels = "master,game,chatRender,media,aux,chatCapture";

    /// <summary>Chemin de <c>coreProps.json</c> (la découverte de GG).</summary>
    public string CorePropsPath { get; init; } = SonarApi.DefaultCorePropsPath;
    /// <summary>Adresse de Sonar fournie à la main : la découverte est alors sautée. Vide : découverte par <c>coreProps.json</c>.</summary>
    public string BaseUrl { get; init; } = "";
    /// <summary>« classic » ou « streamer ».</summary>
    public string Mode { get; init; } = "classic";
    /// <summary>En mode streamer : curseur lu et réglé (« streaming » ou « monitoring »).</summary>
    public string Slider { get; init; } = "streaming";
    /// <summary>Canaux suivis et publiés.</summary>
    public IReadOnlyList<string> Channels { get; init; } = SonarApi.SplitChannels(DefaultChannels);
    /// <summary>Route de la liste des sous-applications de GG.</summary>
    public string SubAppsRoute { get; init; } = "subApps";
    /// <summary>Route (gabarit) de lecture de tous les volumes ; vide : celle du mode.</summary>
    public string ReadRoute { get; init; } = "";
    /// <summary>Route (gabarit) d'écriture d'un volume ; vide : celle du mode.</summary>
    public string VolumeRoute { get; init; } = "";
    /// <summary>Route (gabarit) d'écriture de la coupure ; vide : celle du mode.</summary>
    public string MuteRoute { get; init; } = "";

    /// <summary>true en mode streamer.</summary>
    public bool Streamer => Mode.Equals("streamer", StringComparison.OrdinalIgnoreCase);
    /// <summary>Nom du nœud de niveau dans la réponse de lecture : « classic », ou le curseur du mode streamer.</summary>
    public string LevelKey => Streamer ? Slider : "classic";

    /// <summary>Route de lecture effective.</summary>
    public string EffectiveReadRoute => ReadRoute.Length > 0 ? ReadRoute : Streamer ? "volumeSettings/streamer" : "volumeSettings/classic";
    /// <summary>Gabarit d'écriture du volume effectif.</summary>
    public string EffectiveVolumeRoute => VolumeRoute.Length > 0 ? VolumeRoute
        : Streamer ? "volumeSettings/streamer/{slider}/{channel}/Volume/{value}" : "volumeSettings/classic/{channel}/Volume/{value}";
    /// <summary>Gabarit d'écriture de la coupure effectif.</summary>
    public string EffectiveMuteRoute => MuteRoute.Length > 0 ? MuteRoute
        : Streamer ? "volumeSettings/streamer/{slider}/{channel}/isMuted/{value}" : "volumeSettings/classic/{channel}/Mute/{value}";

    /// <summary>Réglages d'après les champs de l'intégration.</summary>
    public static SonarConfig From(IntegrationSettings settings)
    {
        var channels = SonarApi.SplitChannels(settings.Get("channels", DefaultChannels));
        return new SonarConfig
        {
            CorePropsPath = Environment.ExpandEnvironmentVariables(settings.Get("corepropspath", SonarApi.DefaultCorePropsPath)),
            BaseUrl = settings.Get("baseurl"),
            Mode = settings.Get("mode", "classic"),
            Slider = settings.Get("slider", "streaming"),
            Channels = channels.Count > 0 ? channels : SonarApi.SplitChannels(DefaultChannels),
            SubAppsRoute = settings.Get("subappsroute", "subApps"),
            ReadRoute = settings.Get("readroute"),
            VolumeRoute = settings.Get("volumeroute"),
            MuteRoute = settings.Get("muteroute"),
        };
    }
}

/// <summary>
/// Ce que StreamDak sait de l'API locale de SteelSeries GG / Sonar. <b>Vérifié le 30 octobre 2026 contre GG + Sonar en mode classic</b> :
/// <c>coreProps.json</c>, <c>/subApps</c>, la lecture <c>volumeSettings/classic</c> et les écritures <c>…/{canal}/Volume/{0..1}</c> et
/// <c>…/{canal}/Mute/{true|false}</c> (la route <c>isMuted</c> y répond 404). <b>Non vérifié</b> : le mode streamer en écriture (les routes
/// <c>streamer/{streaming|monitoring}/{canal}/Volume</c> existent mais refusent d'être appelées en mode classic ; la coupure y serait <c>isMuted</c>).
/// Routes, noms de canaux et formes des réponses restent réglables dans les paramètres de l'intégration, et la commande « Diagnostic »
/// montre ce que GG renvoie réellement.
/// </summary>
public static class SonarApi
{
    /// <summary>Canaux connus du mélangeur gaming et leurs surnoms français.</summary>
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["master"] = "master", ["principal"] = "master", ["general"] = "master", ["général"] = "master",
        ["game"] = "game", ["jeu"] = "game",
        ["chatrender"] = "chatRender", ["chat"] = "chatRender", ["discussion"] = "chatRender",
        ["media"] = "media", ["média"] = "media", ["musique"] = "media",
        ["aux"] = "aux",
        ["chatcapture"] = "chatCapture", ["micro"] = "chatCapture", ["mic"] = "chatCapture",
    };

    static readonly char[] Separators = { ',', ';', ' ' };

    /// <summary>Taille maximale lue dans une réponse de GG (une réponse plus grosse n'est pas la sienne).</summary>
    public const int MaxResponseBytes = 1_000_000;

    /// <summary>Emplacement habituel de <c>coreProps.json</c> (%PROGRAMDATA%\SteelSeries\SteelSeries Engine 3).</summary>
    public static string DefaultCorePropsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "SteelSeries Engine 3", "coreProps.json");

    /// <summary>Découpe une liste de canaux séparés par des virgules, point-virgules ou espaces, sans doublon.</summary>
    public static IReadOnlyList<string> SplitChannels(string text) => text
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Nom canonique d'un canal (« micro » devient « chatCapture ») ; un nom inconnu est gardé tel quel.</summary>
    public static string Normalize(string channel) => Aliases.TryGetValue(channel.Trim(), out var name) ? name : channel.Trim();

    /// <summary>Nom affiché d'un canal (« chatCapture » devient « Micro »).</summary>
    public static string Label(string channel) => channel.ToLowerInvariant() switch
    {
        "master" => "Principal", "game" => "Jeu", "chatrender" => "Discussion", "media" => "Média", "aux" => "Aux", "chatcapture" => "Micro", _ => channel,
    };

    /// <summary>true si l'adresse vise cette machine en HTTP ou HTTPS, sans identifiants : GG et Sonar ne se parlent jamais ailleurs.</summary>
    public static bool IsLocalAddress(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme is "http" or "https" && uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Adresse « hôte:port » ou « http(s)://hôte:port » en adresse locale, ou null si elle vise autre chose que cette machine.</summary>
    public static Uri? ParseLocalAddress(string? text, bool secure)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 300) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = (secure ? "https://" : "http://") + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && IsLocalAddress(uri) ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/") : null;
    }

    /// <summary>Adresses de GG à essayer d'après <c>coreProps.json</c>, de la plus probable à la moins probable ; celles qui ne sont pas locales sont ignorées.</summary>
    public static IReadOnlyList<Uri> GgAddresses(JsonNode? coreProps)
    {
        var list = new List<Uri>();
        if (coreProps is not JsonObject obj) return list;
        foreach (var (key, secure) in new[] { ("ggEncryptedAddress", true), ("encryptedAddress", true), ("address", false) })
        {
            var node = obj.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && ParseLocalAddress(text, secure) is { } uri && !list.Contains(uri)) list.Add(uri);
        }
        return list;
    }

    /// <summary>
    /// Adresse de Sonar d'après la réponse de la liste des sous-applications. Lève une exception à message clair si Sonar est absent, éteint
    /// ou annonce une adresse qui n'est pas locale.
    /// </summary>
    public static Uri SonarAddress(JsonNode? subApps)
    {
        var sonar = (subApps?["subApps"] ?? subApps)?["sonar"] ?? throw new InvalidOperationException("GG répond, mais Sonar n'y est pas (Sonar n'est pas installé ou pas activé dans GG)");
        if (sonar["isEnabled"]?.GetValue<bool>() == false) throw new InvalidOperationException("Sonar est désactivé dans SteelSeries GG");
        if (sonar["isRunning"]?.GetValue<bool>() == false || sonar["isReady"]?.GetValue<bool>() == false)
            throw new InvalidOperationException("Sonar démarre ou n'est pas prêt : réessaie dans un instant");
        var metadata = sonar["metadata"] as JsonObject;
        foreach (var key in new[] { "webServerAddress", "encryptedWebServerAddress" })
        {
            var node = metadata?.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var uri = ParseLocalAddress(text, key.StartsWith("encrypted", StringComparison.OrdinalIgnoreCase));
                if (uri != null) return uri;
                throw new InvalidOperationException("Sonar annonce une adresse qui n'est pas locale : refusée");
            }
        }
        throw new InvalidOperationException("GG n'annonce pas l'adresse de Sonar (API non reconnue : lance la commande « Diagnostic »)");
    }

    /// <summary>
    /// Niveaux des canaux d'après la réponse de lecture. Forme attendue (non vérifiée) :
    /// <c>{ "masters": { "classic": { "volume": 0.8, "muted": false } }, "devices": { "game": { "classic": { … } } } }</c> ; les variantes
    /// proches (canal à la racine, nœud de mode absent, « isMuted » au lieu de « muted ») sont acceptées.
    /// </summary>
    public static Dictionary<string, SonarLevel> ParseLevels(JsonNode? root, IEnumerable<string> channels, string levelKey)
    {
        var found = new Dictionary<string, SonarLevel>(StringComparer.OrdinalIgnoreCase);
        if (root is not JsonObject rootObject) return found;
        foreach (var channel in channels)
        {
            if (ChannelNode(rootObject, channel) is not JsonObject node) continue;
            var level = (Child(node, levelKey) ?? (levelKey == "streaming" ? Child(node, "stream") : null)
                ?? (Child(node, "streamer") is JsonObject streamer ? Child(streamer, levelKey) : null) ?? node) as JsonObject;
            if (level == null || !TryNumber(level, "volume", out var volume)) continue;
            bool? muted = null;
            foreach (var key in new[] { "muted", "isMuted", "mute" })
                if (Child(level, key) is JsonValue m && m.TryGetValue<bool>(out var b)) { muted = b; break; }
            found[channel] = new SonarLevel(Math.Clamp(volume, 0, 1), muted);
        }
        return found;
    }

    static JsonNode? ChannelNode(JsonObject root, string channel)
    {
        if (channel.Equals("master", StringComparison.OrdinalIgnoreCase))
            return Child(root, "masters") ?? Child(root, "master");
        return (Child(root, "devices") is JsonObject devices ? Child(devices, channel) : null) ?? Child(root, channel);
    }

    static JsonNode? Child(JsonObject obj, string key) => obj.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    static bool TryNumber(JsonObject obj, string key, out double value)
    {
        value = 0;
        return Child(obj, key) is JsonValue v && (v.TryGetValue(out value) || (v.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)));
    }

    /// <summary>Route relative d'après un gabarit : <c>{mode}</c>, <c>{slider}</c>, <c>{channel}</c> et <c>{value}</c> sont remplacés (valeurs encodées). Refuse une route absolue ou qui remonte.</summary>
    public static string BuildRoute(string template, SonarConfig config, string channel = "", string value = "")
    {
        var route = template.Replace("{mode}", Uri.EscapeDataString(config.Streamer ? "streamer" : "classic"), StringComparison.Ordinal)
            .Replace("{slider}", Uri.EscapeDataString(config.Slider), StringComparison.Ordinal)
            .Replace("{channel}", Uri.EscapeDataString(channel), StringComparison.Ordinal)
            .Replace("{value}", Uri.EscapeDataString(value), StringComparison.Ordinal)
            .TrimStart('/');
        if (route.Contains("://", StringComparison.Ordinal) || route.Split('/').Any(s => s == ".."))
            throw new InvalidOperationException("Route Sonar invalide : elle doit être relative à l'adresse de Sonar");
        return route;
    }

    /// <summary>Volume de 0 à 100 écrit dans la route (« 0.5 » pour 50 %).</summary>
    public static string FormatVolume(int percent) => (Math.Clamp(percent, 0, 100) / 100.0).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Niveau demandé : « 50 » (absolu) ou « +5 » / « -5 » (relatif au niveau actuel), borné de 0 à 100.</summary>
    public static int ParseLevel(string text, int current)
    {
        text = text.Trim().TrimEnd('%').Trim();
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            throw new ArgumentException("Le niveau doit être un nombre de 0 à 100 (ou +5, -5)");
        return Math.Clamp(text.StartsWith('+') || text.StartsWith('-') ? current + n : n, 0, 100);
    }
}
