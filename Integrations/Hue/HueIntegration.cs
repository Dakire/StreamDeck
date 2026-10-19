using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.Hue;

/// <summary>
/// Philips Hue par l'API CLIP v2 du pont (HTTPS local, en-tête <c>hue-application-key</c>) : allumer, éteindre, régler la
/// luminosité d'une lumière ou d'une pièce, activer une scène. L'état (allumé ou éteint) est relevé toutes les 2 s.
/// Documentation : https://developers.meethue.com/develop/hue-api-v2/. Le pont présente un certificat signé par l'autorité Hue,
/// que Windows ne connaît pas : il est accepté pour l'adresse du pont configurée, et uniquement pour elle.
/// </summary>
public sealed class HueIntegration : PolledHttpIntegration, IPairableIntegration
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "hue";
    sealed record Target(string Kind, string Id, string Name, bool On, int? Brightness);

    string _origin = "";
    IntegrationSettings? _settings;
    List<Target> _targets = new();
    Dictionary<string, string> _scenes = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Philips Hue";
    /// <inheritdoc/>
    public override string Category => "Philips Hue";
    /// <inheritdoc/>
    public override string Icon => "Lightbulb24";
    /// <inheritdoc/>
    public override string Help => "Indique l'adresse IP du pont Hue (application Hue ou routeur), puis clique sur « Associer » après avoir appuyé sur le gros bouton du pont. Nécessite un pont Hue de deuxième génération (carré) à jour.";
    /// <inheritdoc/>
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Adresse IP du pont", SettingKind.Text, null, "Exemple : 192.168.1.20."),
        new SettingField("appkey", "Clé d'application", SettingKind.Secret, null, "Remplie par le bouton « Associer »."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("toggle", "Allumer / éteindre", "Bascule une lumière ou une pièce ; le voyant est plein quand elle est allumée.", "le nom de la lumière ou de la pièce", true, "on.{arg}", "Lightbulb24", "#B45309"),
        new IntegrationCommand("on", "Allumer", "Allume une lumière ou une pièce.", "le nom de la lumière ou de la pièce", true, "on.{arg}", "Lightbulb24", "#B45309"),
        new IntegrationCommand("off", "Éteindre", "Éteint une lumière ou une pièce.", "le nom de la lumière ou de la pièce", true, null, "LightbulbFilament24", "#334155"),
        new IntegrationCommand("brightness", "Luminosité", "Règle la luminosité de 0 à 100 d'une lumière ou d'une pièce, ou la change de façon relative.", "« nom=valeur » (Salon=40, Salon=+10)", true, null, "BrightnessHigh24", "#B45309"),
        new IntegrationCommand("scene", "Activer une scène", "Active une scène Hue.", "le nom de la scène", true, null, "Color24", "#7C3AED"),
    };

    /// <inheritdoc/>
    public string PairLabel => "Associer au pont";
    /// <inheritdoc/>
    public string PairHelp => "Appuie sur le bouton du pont Hue, puis clique sur « Associer » dans les 30 secondes.";
    /// <inheritdoc/>
    public string PairedSecretField => "appkey";

    static string Origin(IntegrationSettings settings) => $"{settings.Get("scheme", "https")}://{settings.Get("host")}{(settings.Get("port").Length > 0 ? ":" + settings.Get("port") : "")}";

    /// <inheritdoc/>
    protected override HttpClient CreateClient(IntegrationSettings settings)
    {
        var host = settings.Get("host");
        if (host.Length == 0) throw new InvalidOperationException("Indique l'adresse IP du pont Hue");
        var handler = new HttpClientHandler
        {
            // Certificat de l'autorité Hue : accepté pour l'adresse du pont configurée, et pour elle seule
            ServerCertificateCustomValidationCallback = (message, _, _, _) => string.Equals(message.RequestUri?.Host, host, StringComparison.OrdinalIgnoreCase),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        if (settings.Secret("appkey").Length == 0) { client.Dispose(); throw new InvalidOperationException("Clé d'application manquante : clique sur « Associer » après avoir appuyé sur le bouton du pont"); }
        client.DefaultRequestHeaders.Add("hue-application-key", settings.Secret("appkey"));
        return client;
    }

    /// <inheritdoc/>
    protected override string Unreachable(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized
            ? "Clé d'application refusée : refais l'association (bouton du pont puis « Associer »)"
            : "Le pont Hue ne répond pas. Vérifie son adresse IP et qu'il est sur le même réseau que ce PC.";

    async Task<JsonArray> GetAsync(string resource, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync($"{_origin}/clip/v2/resource/{resource}", cancellation);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))?["data"]?.AsArray() ?? new JsonArray();
    }

    /// <inheritdoc/>
    protected override async Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _settings = settings;
        _origin = Origin(settings);
        var lights = await GetAsync("light", cancellation);
        var rooms = await GetAsync("room", cancellation);
        var groups = await GetAsync("grouped_light", cancellation);
        var scenes = await GetAsync("scene", cancellation);

        var targets = new List<Target>();
        foreach (var light in lights)
            if (light?["metadata"]?["name"]?.GetValue<string>() is { } name)
                targets.Add(new Target("light", light["id"]!.GetValue<string>(), name, light["on"]?["on"]?.GetValue<bool>() == true, (int?)light["dimming"]?["brightness"]?.GetValue<double>()));
        var groupById = groups.Where(g => g != null).ToDictionary(g => g!["id"]!.GetValue<string>(), g => g!);
        foreach (var room in rooms)
        {
            var name = room?["metadata"]?["name"]?.GetValue<string>();
            var rid = room?["services"]?.AsArray().FirstOrDefault(s => s?["rtype"]?.GetValue<string>() == "grouped_light")?["rid"]?.GetValue<string>();
            if (name != null && rid != null && groupById.TryGetValue(rid, out var group))
                targets.Add(new Target("grouped_light", rid, name, group["on"]?["on"]?.GetValue<bool>() == true, (int?)group["dimming"]?["brightness"]?.GetValue<double>()));
        }
        _targets = targets;
        _scenes = scenes.Where(s => s?["metadata"]?["name"] != null).GroupBy(s => s!["metadata"]!["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()!["id"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);

        var values = new List<KeyValuePair<string, string>> { KeyValuePair.Create("anyon", targets.Any(t => t.On) ? "true" : "false") };
        foreach (var t in targets)
        {
            values.Add(KeyValuePair.Create("on." + t.Name, t.On ? "true" : "false"));
            if (t.Brightness is { } b) values.Add(KeyValuePair.Create("brightness." + t.Name, b.ToString(CultureInfo.InvariantCulture)));
        }
        SetValues(values);
    }

    Target Find(string name) =>
        _targets.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Lumière ou pièce introuvable : « {name} ». Disponibles : {string.Join(", ", _targets.Select(t => t.Name).Take(12))}");

    async Task PutAsync(string path, JsonObject body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{_origin}/clip/v2/resource/{path}") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await Http.SendAsync(request, cancellation);
        var text = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode || JsonNode.Parse(text)?["errors"]?.AsArray() is { Count: > 0 } errors)
            throw new InvalidOperationException("Le pont Hue a refusé : " + (JsonNode.Parse(text)?["errors"]?.AsArray().FirstOrDefault()?["description"]?.GetValue<string>() ?? $"code {(int)response.StatusCode}"));
    }

    static string PathOf(Target t) => $"{t.Kind}/{t.Id}";

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        string message;
        switch (command.ToLowerInvariant())
        {
            case "toggle":
            {
                var t = Find(RequireArgument(argument, "le nom de la lumière ou de la pièce"));
                await PutAsync(PathOf(t), new JsonObject { ["on"] = new JsonObject { ["on"] = !t.On } }, cancellation);
                message = t.On ? $"{t.Name} éteint" : $"{t.Name} allumé"; break;
            }
            case "on":
            case "off":
            {
                var t = Find(RequireArgument(argument, "le nom de la lumière ou de la pièce"));
                bool on = command.Equals("on", StringComparison.OrdinalIgnoreCase);
                await PutAsync(PathOf(t), new JsonObject { ["on"] = new JsonObject { ["on"] = on } }, cancellation);
                message = on ? $"{t.Name} allumé" : $"{t.Name} éteint"; break;
            }
            case "brightness":
            {
                var spec = RequireArgument(argument, "« nom=valeur »");
                var cut = spec.LastIndexOf('=');
                if (cut <= 0 || cut == spec.Length - 1) throw new ArgumentException("Écris « nom=valeur », par exemple Salon=40 ou Salon=+10");
                var t = Find(spec[..cut].Trim());
                var text = spec[(cut + 1)..].Trim();
                if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) throw new ArgumentException("La luminosité doit être un nombre de 0 à 100 (ou +10, -10)");
                var level = Math.Clamp(text.StartsWith('+') || text.StartsWith('-') ? (t.Brightness ?? 0) + n : n, 0, 100);
                var body = new JsonObject { ["dimming"] = new JsonObject { ["brightness"] = level } };
                if (level > 0 && !t.On) body["on"] = new JsonObject { ["on"] = true };
                await PutAsync(PathOf(t), body, cancellation);
                message = $"{t.Name} : {level} %"; break;
            }
            case "scene":
            {
                var name = RequireArgument(argument, "le nom de la scène");
                if (!_scenes.TryGetValue(name, out var id)) throw new InvalidOperationException($"Scène introuvable : « {name} ». Disponibles : {string.Join(", ", _scenes.Keys.Take(12))}");
                await PutAsync($"scene/{id}", new JsonObject { ["recall"] = new JsonObject { ["action"] = "active" } }, cancellation);
                message = $"Scène : {_scenes.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))}"; break;
            }
            default: throw new ArgumentException($"Commande Hue inconnue : « {command} »");
        }
        if (_settings != null) await RefreshNowAsync(_settings, cancellation);
        return message;
    }

    /// <inheritdoc/>
    public async Task<string> PairAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var host = settings.Get("host");
        if (host.Length == 0) throw new InvalidOperationException("Indique d'abord l'adresse IP du pont Hue");
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, _, _, _) => string.Equals(message.RequestUri?.Host, host, StringComparison.OrdinalIgnoreCase),
        }) { Timeout = TimeSpan.FromSeconds(8) };
        var body = new JsonObject { ["devicetype"] = "touchdeck#" + Environment.MachineName, ["generateclientkey"] = true };
        string text;
        try
        {
            using var response = await http.PostAsync($"{Origin(settings)}/api", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cancellation);
            text = await response.Content.ReadAsStringAsync(cancellation);
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Le pont Hue ne répond pas. Vérifie son adresse IP."); }
        var first = JsonNode.Parse(text)?.AsArray().FirstOrDefault();
        if (first?["success"]?["username"]?.GetValue<string>() is { Length: > 0 } key) return key;
        var type = first?["error"]?["type"]?.GetValue<int>();
        throw new InvalidOperationException(type == 101 ? "Le bouton du pont n'a pas été pressé : appuie dessus, puis réessaie dans les 30 secondes."
            : first?["error"]?["description"]?.GetValue<string>() ?? "Le pont Hue a refusé l'association");
    }
}