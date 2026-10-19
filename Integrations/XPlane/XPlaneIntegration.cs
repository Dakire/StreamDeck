using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.XPlane;

/// <summary>
/// X-Plane 12 (12.1.1 et plus) par son API web locale (<c>http://hôte:8086/api</c>, documentation :
/// https://developer.x-plane.com/article/x-plane-web-api/). Lit des « datarefs » choisis par l'utilisateur et les publie sous
/// un nom court (<c>{xplane.gear}</c>), écrit un dataref, et active une commande du simulateur. Les noms de datarefs et de
/// commandes se trouvent dans DataRefTool ou dans la liste officielle ; ils sont résolus en identifiants au premier usage.
/// </summary>
public sealed class XPlaneIntegration : PolledHttpIntegration
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "xplane";
    static readonly char[] WatchSeparators = { ',', ';', '\n' };
    readonly Dictionary<string, long> _datarefIds = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _commandIds = new(StringComparer.Ordinal);
    Uri _api = new("http://localhost:8086/api/");
    IntegrationSettings? _settings;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "X-Plane";
    /// <inheritdoc/>
    public override string Category => "X-Plane";
    /// <inheritdoc/>
    public override string Icon => "Airplane24";
    /// <inheritdoc/>
    public override string Help => "X-Plane 12.1.1 ou plus récent doit tourner (le serveur web est actif par défaut, port 8086, sauf option --no_web_server). Indique les datarefs à afficher sous la forme nom=dataref, séparés par des virgules.";
    /// <inheritdoc/>
    protected override TimeSpan PollInterval => TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Adresse", SettingKind.Text, "localhost", "« localhost » si X-Plane tourne sur ce PC."),
        new SettingField("port", "Port", SettingKind.Number, "8086"),
        new SettingField("watch", "Datarefs à afficher", SettingKind.Text, null,
            "nom=dataref, séparés par des virgules. Exemple : gear=sim/cockpit2/controls/gear_handle_down, alt=sim/cockpit2/gauges/indicators/altitude_ft_pilot. Affichés par {xplane.gear}, {xplane.alt}."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("command", "Commande X-Plane", "Active une commande du simulateur (train d'atterrissage, feux, pilote automatique…).",
            "le nom de la commande (sim/lights/landing_lights_toggle)", true, null, "Airplane24", "#0369A1"),
        new IntegrationCommand("set", "Écrire un dataref", "Écrit une valeur dans un dataref accessible en écriture.",
            "« dataref=valeur » (sim/cockpit/electrical/battery_on=1)", true, null, "Settings24", "#0369A1"),
    };

    /// <inheritdoc/>
    protected override string Unreachable(HttpRequestException ex) =>
        "X-Plane ne répond pas. Vérifie qu'il est lancé (version 12.1.1 ou plus) et que le serveur web n'est pas désactivé (--no_web_server).";

    /// <summary>Datarefs à lire : « nom=dataref » séparés par des virgules ou des points-virgules.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParseWatch(string text)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var part in text.Split(WatchSeparators,StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || eq == part.Length - 1) throw new InvalidOperationException($"« {part} » : écris nom=dataref (exemple : gear=sim/cockpit2/controls/gear_handle_down)");
            list.Add(KeyValuePair.Create(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return list;
    }

    /// <inheritdoc/>
    protected override async Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _settings = settings;
        _api = new Uri($"http://{settings.Get("host", "localhost")}:{settings.GetInt("port", 8086)}/api/");
        var watch = ParseWatch(settings.Get("watch"));
        if (watch.Count == 0)
        {
            await Http.GetStringAsync(new Uri(_api, "capabilities"), cancellation);   // simple vérification de la présence du simulateur
            return;
        }
        var values = new List<KeyValuePair<string, string>>();
        foreach (var (name, dataref) in watch)
        {
            var id = await DatarefIdAsync(dataref, cancellation);
            var json = JsonNode.Parse(await Http.GetStringAsync(new Uri(_api, $"v1/datarefs/{id}/value"), cancellation));
            values.Add(KeyValuePair.Create(name, Format(json?["data"])));
        }
        SetValues(values);
    }

    /// <summary>Valeur d'un dataref sous forme de texte : entier, nombre arrondi à deux décimales, tableau ou texte.</summary>
    public static string Format(JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
            if (v.TryGetValue<double>(out var d)) return Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
            return v.ToString();
        }
        return node?.ToJsonString() ?? "";
    }

    async Task<long> DatarefIdAsync(string name, CancellationToken cancellation) => await ResolveAsync(_datarefIds, "v1/datarefs", name, "Dataref", cancellation);

    async Task<long> ResolveAsync(Dictionary<string, long> cache, string path, string name, string what, CancellationToken cancellation)
    {
        if (cache.TryGetValue(name, out var known)) return known;
        var json = JsonNode.Parse(await Http.GetStringAsync(new Uri(_api, $"{path}?filter[name]={Uri.EscapeDataString(name)}"), cancellation));
        foreach (var item in json?["data"]?.AsArray() ?? new JsonArray())
        {
            if (item?["name"]?.GetValue<string>() == name && item["id"] is JsonNode id)
                return cache[name] = id.GetValue<long>();
        }
        throw new InvalidOperationException($"{what} introuvable dans X-Plane : « {name} » (vérifie l'orthographe avec DataRefTool)");
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        switch (command.ToLowerInvariant())
        {
            case "command":
            {
                var name = RequireArgument(argument, "le nom de la commande");
                var id = await ResolveAsync(_commandIds, "v2/commands", name, "Commande", cancellation);
                using var body = new StringContent("""{"duration":0}""", Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(new Uri(_api, $"v2/command/{id}/activate"), body, cancellation);
                Ensure(response, name);
                return $"Commande {name}";
            }
            case "set":
            {
                var text = RequireArgument(argument, "« dataref=valeur »");
                var eq = text.IndexOf('=');
                if (eq <= 0 || eq == text.Length - 1) throw new ArgumentException("Écris dataref=valeur (exemple : sim/cockpit/electrical/battery_on=1)");
                var name = text[..eq].Trim();
                if (!double.TryParse(text[(eq + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new ArgumentException("La valeur doit être un nombre");
                var id = await DatarefIdAsync(name, cancellation);
                using var body = new StringContent(new JsonObject { ["data"] = value }.ToJsonString(), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(_api, $"v1/datarefs/{id}/value")) { Content = body };
                using var response = await Http.SendAsync(request, cancellation);
                Ensure(response, name);
                if (_settings != null) await RefreshNowAsync(_settings, cancellation);
                return $"{name} = {value.ToString("0.##", CultureInfo.InvariantCulture)}";
            }
            default: throw new ArgumentException($"Commande X-Plane inconnue : « {command} »");
        }
    }

    static void Ensure(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Forbidden
            ? $"X-Plane refuse l'écriture de {what} (dataref en lecture seule ?)"
            : $"X-Plane a répondu {(int)response.StatusCode} pour {what}");
    }
}
