using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TouchDeck.Integrations.Vmix;

/// <summary>
/// vMix par son API web (<c>http://hôte:8088/api</c>, documentation : https://www.vmix.com/help29/DeveloperAPI.html) : couper,
/// fondre, choisir l'entrée en aperçu ou en direct, couper le son d'une entrée, enregistrer, diffuser, ou envoyer n'importe quelle
/// fonction de raccourci. L'état (entrée active, aperçu, enregistrement, diffusion, entrées muettes) est relevé chaque seconde.
/// </summary>
public sealed class VmixIntegration : PolledHttpIntegration
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "vmix";
    Uri? _api;
    IntegrationSettings? _settings;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "vMix";
    /// <inheritdoc/>
    public override string Category => "vMix";
    /// <inheritdoc/>
    public override string Icon => "VideoClip24";
    /// <inheritdoc/>
    public override string Help => "Dans vMix : Réglages › Web Controller › Activer. Le port est 8088 par défaut. Une licence vMix est nécessaire (l'API est incluse dans toutes les éditions sauf Basic).";
    /// <inheritdoc/>
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Adresse", SettingKind.Text, "localhost", "« localhost » si vMix tourne sur ce PC."),
        new SettingField("port", "Port du Web Controller", SettingKind.Number, "8088"),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("cut", "Couper (Cut)", "Passe l'aperçu en direct, sans transition.", null, false, null, "Cut24", "#BE123C"),
        new IntegrationCommand("fade", "Fondu (Fade)", "Passe l'aperçu en direct par un fondu ; l'argument est la durée en millisecondes (1000 par défaut).", "la durée en millisecondes", false, null, "ArrowSwap24", "#BE123C"),
        new IntegrationCommand("preview", "Mettre en aperçu", "Place l'entrée indiquée en aperçu ; le voyant est plein quand elle y est.", "le numéro ou le nom de l'entrée", true, "preview.{arg}", "Eye24", "#2563EB"),
        new IntegrationCommand("active", "Mettre en direct", "Envoie l'entrée indiquée directement en sortie ; le voyant est plein quand elle est en direct.", "le numéro ou le nom de l'entrée", true, "active.{arg}", "Video24", "#BE123C"),
        new IntegrationCommand("mute", "Couper le son d'une entrée", "Bascule le son de l'entrée ; le voyant est plein quand elle est muette.", "le numéro ou le nom de l'entrée", true, "mute.{arg}", "SpeakerMute24", "#7C3AED"),
        new IntegrationCommand("record", "Enregistrer", "Démarre ou arrête l'enregistrement ; le voyant est plein pendant l'enregistrement.", null, false, "recording", "Record24", "#BE123C"),
        new IntegrationCommand("stream", "Diffuser", "Démarre ou arrête la diffusion ; le voyant est plein pendant le direct.", null, false, "streaming", "Live24", "#BE123C"),
        new IntegrationCommand("function", "Fonction vMix", "Envoie une fonction de raccourci de vMix, avec ses paramètres (« OverlayInput1&Input=3 »).", "la fonction et ses paramètres", true, null, "Flash24", "#334155"),
    };

    /// <inheritdoc/>
    protected override string Unreachable(HttpRequestException ex) =>
        "vMix ne répond pas. Vérifie que vMix est lancé et que le Web Controller est activé (Réglages › Web Controller), puis l'adresse et le port.";

    /// <inheritdoc/>
    protected override async Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        _settings = settings;
        _api = new Uri($"http://{settings.Get("host", "localhost")}:{settings.GetInt("port", 8088)}/api/");
        var xml = await Http.GetStringAsync(_api, cancellation);
        SetValues(Parse(xml));
    }

    /// <summary>Valeurs d'état d'après le XML de vMix. Exposé pour les tests.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string xml)
    {
        XElement root;
        try { root = XDocument.Parse(xml).Root ?? throw new InvalidOperationException("Réponse vide"); }
        catch (System.Xml.XmlException) { throw new InvalidOperationException("Ce n'est pas l'API de vMix : vérifie l'adresse et le port du Web Controller"); }
        if (root.Name.LocalName != "vmix") throw new InvalidOperationException("Ce n'est pas l'API de vMix : vérifie l'adresse et le port du Web Controller");

        var inputs = root.Element("inputs")?.Elements("input").ToList() ?? new List<XElement>();
        string active = root.Element("active")?.Value ?? "", preview = root.Element("preview")?.Value ?? "";
        var values = new List<KeyValuePair<string, string>>
        {
            KeyValuePair.Create("recording", Flag(root.Element("recording")?.Value)),
            KeyValuePair.Create("streaming", Flag(root.Element("streaming")?.Value)),
            KeyValuePair.Create("activenumber", active),
            KeyValuePair.Create("previewnumber", preview),
        };
        foreach (var input in inputs)
        {
            var number = input.Attribute("number")?.Value ?? "";
            var title = input.Attribute("title")?.Value ?? input.Value;
            bool isActive = number == active, isPreview = number == preview, muted = Flag(input.Attribute("muted")?.Value) == "true";
            if (isActive) values.Add(KeyValuePair.Create("active", title));
            if (isPreview) values.Add(KeyValuePair.Create("preview", title));
            foreach (var name in new[] { number, title }.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                values.Add(KeyValuePair.Create("active." + name, Flag(isActive)));
                values.Add(KeyValuePair.Create("preview." + name, Flag(isPreview)));
                values.Add(KeyValuePair.Create("mute." + name, Flag(muted)));
            }
        }
        return values;
    }

    static string Flag(string? text) => text != null && text.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
    static string Flag(bool value) => value ? "true" : "false";

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        string function, message;
        switch (command.ToLowerInvariant())
        {
            case "cut": function = "Cut"; message = "Coupé"; break;
            case "fade":
                var duration = string.IsNullOrWhiteSpace(argument) ? "1000" : argument.Trim();
                if (!int.TryParse(duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) || ms < 0) throw new ArgumentException("La durée doit être un nombre de millisecondes");
                function = $"Fade&Duration={ms}"; message = "Fondu lancé"; break;
            case "preview": var p = RequireArgument(argument, "le numéro ou le nom de l'entrée"); function = $"PreviewInput&Input={Uri.EscapeDataString(p)}"; message = $"Aperçu : {p}"; break;
            case "active": var a = RequireArgument(argument, "le numéro ou le nom de l'entrée"); function = $"ActiveInput&Input={Uri.EscapeDataString(a)}"; message = $"En direct : {a}"; break;
            case "mute": var m = RequireArgument(argument, "le numéro ou le nom de l'entrée"); function = $"Audio&Input={Uri.EscapeDataString(m)}"; message = $"Son basculé : {m}"; break;
            case "record": function = "StartStopRecording"; message = "Enregistrement basculé"; break;
            case "stream": function = "StartStopStreaming"; message = "Diffusion basculée"; break;
            case "function": function = RequireArgument(argument, "la fonction et ses paramètres").TrimStart('?', '&'); message = $"Fonction envoyée : {function.Split('&')[0]}"; break;
            default: throw new ArgumentException($"Commande vMix inconnue : « {command} »");
        }
        var api = _api ?? throw new InvalidOperationException("vMix n'est pas connecté");
        using var response = await Http.GetAsync(new Uri(api, "?Function=" + function), cancellation);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"vMix a répondu {(int)response.StatusCode} {response.ReasonPhrase}");
        if (_settings != null) await RefreshNowAsync(_settings, cancellation);
        return message;
    }
}