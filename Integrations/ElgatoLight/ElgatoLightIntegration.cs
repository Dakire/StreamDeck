using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.ElgatoLight;

/// <summary>
/// Lampes Elgato (Key Light, Key Light Air, Ring Light) par leur API HTTP locale, sans authentification (<c>http://adresse:9123/elgato/lights</c>).
/// API non documentée officiellement par Elgato, observée sur les appareils. Plusieurs lampes peuvent être pilotées ensemble en
/// séparant leurs adresses par une virgule.
/// </summary>
public sealed class ElgatoLightIntegration : PolledHttpIntegration
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "elgato";
    /// <summary>Plage de température de couleur des lampes, en kelvins.</summary>
    public const int MinKelvin = 2900, MaxKelvin = 7000;
    static readonly char[] Separators = { ',', ';', ' ' };
    string[] _hosts = Array.Empty<string>();
    int _port = 9123;
    IntegrationSettings? _settings;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Lampes Elgato";
    /// <inheritdoc/>
    public override string Category => "Lampes Elgato";
    /// <inheritdoc/>
    public override string Icon => "LightbulbFilament24";
    /// <inheritdoc/>
    public override string Help => "Indique l'adresse IP de la lampe (visible dans l'application Elgato Control Center ou sur ton routeur), plusieurs séparées par une virgule. Réserve-lui une adresse fixe dans ton routeur.";
    /// <inheritdoc/>
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Adresse IP de la lampe", SettingKind.Text, null, "Exemple : 192.168.1.50. Plusieurs lampes : 192.168.1.50, 192.168.1.51."),
        new SettingField("port", "Port", SettingKind.Number, "9123"),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("toggle", "Allumer / éteindre", "Bascule la lampe ; le voyant est plein quand elle est allumée.", null, false, "on", "LightbulbFilament24", "#B45309"),
        new IntegrationCommand("on", "Allumer", "Allume la lampe.", null, false, "on", "Lightbulb24", "#B45309"),
        new IntegrationCommand("off", "Éteindre", "Éteint la lampe.", null, false, null, "LightbulbFilament24", "#334155"),
        new IntegrationCommand("brightness", "Luminosité", "Règle la luminosité de 0 à 100, ou la change de façon relative (« +10 », « -10 »).", "la luminosité (0 à 100, +10, -10)", true, null, "BrightnessHigh24", "#B45309"),
        new IntegrationCommand("temperature", "Température de couleur", "Règle la température de 2 900 K (chaud) à 7 000 K (froid).", "la température en kelvins", true, null, "Temperature24", "#B45309"),
    };

    /// <inheritdoc/>
    protected override string Unreachable(HttpRequestException ex) =>
        $"La lampe ne répond pas. Vérifie qu'elle est allumée, sur le même réseau que ce PC, et son adresse IP.";

    Uri Url(string host) => new($"http://{host}:{_port}/elgato/lights");

    /// <inheritdoc/>
    protected override async Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _settings = settings;
        _hosts = settings.Get("host").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_hosts.Length == 0) throw new InvalidOperationException("Indique l'adresse IP de la lampe");
        _port = settings.GetInt("port", 9123);

        var states = new List<JsonObject>();
        foreach (var host in _hosts) states.Add(await ReadAsync(host, cancellation));
        bool anyOn = states.Any(s => s["on"]?.GetValue<int>() == 1);
        var first = states[0];
        SetValues(new[]
        {
            KeyValuePair.Create("on", anyOn ? "true" : "false"),
            KeyValuePair.Create("brightness", (first["brightness"]?.GetValue<int>() ?? 0).ToString(CultureInfo.InvariantCulture)),
            KeyValuePair.Create("temperature", Kelvin(first["temperature"]?.GetValue<int>() ?? 213).ToString(CultureInfo.InvariantCulture)),
        });
    }

    async Task<JsonObject> ReadAsync(string host, CancellationToken cancellation)
    {
        var json = JsonNode.Parse(await Http.GetStringAsync(Url(host), cancellation));
        return json?["lights"]?.AsArray().FirstOrDefault()?.AsObject() ?? throw new InvalidOperationException($"{host} n'est pas une lampe Elgato");
    }

    /// <summary>Température en kelvins d'après la valeur de la lampe (en mireds : 1 000 000 / K, de 143 à 344).</summary>
    public static int Kelvin(int mired) => (int)Math.Round(1_000_000.0 / Math.Clamp(mired, 143, 344) / 50.0) * 50;

    /// <summary>Valeur de la lampe (mireds) pour une température en kelvins, bornée aux capacités de l'appareil.</summary>
    public static int Mired(int kelvin) => Math.Clamp((int)Math.Round(1_000_000.0 / Math.Clamp(kelvin, MinKelvin, MaxKelvin)), 143, 344);

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        if (_hosts.Length == 0) throw new InvalidOperationException("La lampe n'est pas connectée");
        string message = "";
        foreach (var host in _hosts)
        {
            var current = await ReadAsync(host, cancellation);
            var light = new JsonObject();
            switch (command.ToLowerInvariant())
            {
                case "toggle": var on = current["on"]?.GetValue<int>() != 1; light["on"] = on ? 1 : 0; message = on ? "Lampe allumée" : "Lampe éteinte"; break;
                case "on": light["on"] = 1; message = "Lampe allumée"; break;
                case "off": light["on"] = 0; message = "Lampe éteinte"; break;
                case "brightness":
                    var level = Brightness(RequireArgument(argument, "la luminosité"), current["brightness"]?.GetValue<int>() ?? 0);
                    light["brightness"] = level; light["on"] = level > 0 ? 1 : 0; message = $"Luminosité {level} %"; break;
                case "temperature":
                    var text = RequireArgument(argument, "la température en kelvins");
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kelvin) || kelvin is < MinKelvin or > MaxKelvin)
                        throw new ArgumentException($"La température doit être comprise entre {MinKelvin} et {MaxKelvin} kelvins");
                    light["temperature"] = Mired(kelvin); message = $"Température {kelvin} K"; break;
                default: throw new ArgumentException($"Commande inconnue : « {command} »");
            }
            var body = new JsonObject { ["numberOfLights"] = 1, ["lights"] = new JsonArray(light) };
            using var request = new HttpRequestMessage(HttpMethod.Put, Url(host)) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"La lampe {host} a répondu {(int)response.StatusCode}");
        }
        if (_settings != null) await RefreshNowAsync(_settings, cancellation);
        return message;
    }

    /// <summary>Luminosité demandée (« 40 », « +10 », « -10 ») bornée de 0 à 100.</summary>
    public static int Brightness(string text, int current)
    {
        bool relative = text.StartsWith('+') || text.StartsWith('-');
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            throw new ArgumentException("La luminosité doit être un nombre de 0 à 100 (ou +10, -10)");
        return Math.Clamp(relative ? current + n : n, 0, 100);
    }
}