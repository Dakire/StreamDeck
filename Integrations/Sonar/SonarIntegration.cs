using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Sonar;

/// <summary>
/// SteelSeries Sonar (mélangeur de GG) : volume et coupure des canaux principal, jeu, discussion, média, aux et micro. GG publie son adresse
/// dans <c>coreProps.json</c> ; la liste de ses sous-applications donne l'adresse du serveur web de Sonar, qui offre l'API de volume.
/// <b>NON VÉRIFIÉ contre un vrai GG</b> (voir <see cref="SonarApi"/> et <c>docs/integrations/sonar.md</c>).
/// Garde-fous : seules les adresses locales (127.0.0.1, localhost, ::1) sont jamais jointes, pas de redirection, délais courts, réponses limitées en taille.
/// Valeurs publiées : <c>{sonar.game.volume}</c> (0 à 100), <c>{sonar.game.muted}</c> (true / false), pour chaque canal suivi.
/// </summary>
public sealed class SonarIntegration : PolledHttpIntegration, IBackgroundAware
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "sonar";
    /// <summary>Nom du fichier de diagnostic, écrit dans le dossier de configuration.</summary>
    public const string ReportFileName = "sonar-diagnostic.txt";
    /// <summary>Délai le plus court entre deux écritures du rapport automatique après un échec.</summary>
    static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(5);

    SonarConfig _config = new();
    HttpClient? _diagnoseClient;   // client temporaire du diagnostic quand le connecteur n'est pas connecté
    Uri? _sonar;
    volatile bool _background;
    DateTime _lastReport = DateTime.MinValue;

    /// <summary>Dossier du rapport de diagnostic ; null : le dossier de configuration de StreamDak (les tests donnent un dossier temporaire).</summary>
    public string? ReportDirectory { get; set; }

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "SteelSeries Sonar";
    /// <inheritdoc/>
    public override string Category => "Sonar";
    /// <inheritdoc/>
    public override string Icon => "Speaker224";
    /// <inheritdoc/>
    public override string Help =>
        "Pilote les canaux du mélangeur Sonar (jeu, discussion, média, aux, micro, principal) : volume et coupure. SteelSeries GG doit tourner sur ce PC " +
        "avec Sonar activé. L'adresse de Sonar est trouvée toute seule (coreProps.json). Cette API n'est pas documentée par SteelSeries et n'a pas été " +
        "vérifiée : si rien ne répond, lance la commande « Diagnostic » et lis le fichier sonar-diagnostic.txt du dossier de configuration. " +
        "Le gain du micro d'une carte son (Scarlett…) ne se règle pas ici : il se règle dans Sonar.";

    /// <inheritdoc/>
    protected override TimeSpan PollInterval => _background ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(1500);

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("channels", "Canaux suivis", SettingKind.Text, SonarConfig.DefaultChannels, "Noms Sonar séparés par une virgule : master, game, chatRender, media, aux, chatCapture (surnoms : jeu, chat, musique, micro)."),
        new SettingField("mode", "Mode du mélangeur", SettingKind.Text, "classic", "classic ou streamer."),
        new SettingField("slider", "Curseur (mode streamer)", SettingKind.Text, "streaming", "streaming ou monitoring."),
        new SettingField("baseurl", "Adresse de Sonar (avancé)", SettingKind.Text, null, "Vide : trouvée automatiquement. Sinon http://127.0.0.1:port (adresse locale uniquement)."),
        new SettingField("corepropspath", "Chemin de coreProps.json (avancé)", SettingKind.Text, null, "Vide : %PROGRAMDATA%\\SteelSeries\\SteelSeries Engine 3\\coreProps.json."),
        new SettingField("readroute", "Route de lecture (avancé)", SettingKind.Text, null, "Vide : volumeSettings/classic. Variables : {mode} {slider}."),
        new SettingField("volumeroute", "Route du volume (avancé)", SettingKind.Text, null, "Vide : volumeSettings/classic/{channel}/Volume/{value} (méthode PUT, valeur de 0 à 1)."),
        new SettingField("muteroute", "Route de la coupure (avancé)", SettingKind.Text, null, "Vide : volumeSettings/classic/{channel}/Mute/{value} (méthode PUT, true ou false)."),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("volume", "Volume d'un canal", "Règle le volume d'un canal Sonar : « canal=niveau » (0 à 100) ou « canal=+5 » / « canal=-5 ».",
            "canal=niveau, par exemple game=60 ou chatRender=+5", true, null, "Speaker224", "#C2410C"),
        new IntegrationCommand("mute", "Couper un canal", "Coupe un canal Sonar.", "le canal (game, chatRender, media, aux, chatCapture, master)", true, "{arg}.muted", "SpeakerMute24", "#BE123C"),
        new IntegrationCommand("unmute", "Rétablir un canal", "Rétablit le son d'un canal Sonar.", "le canal", true, null, "Speaker224", "#334155"),
        new IntegrationCommand("togglemute", "Couper / rétablir un canal", "Bascule la coupure d'un canal ; le voyant est plein quand il est coupé.", "le canal", true, "{arg}.muted", "SpeakerMute24", "#BE123C"),
        new IntegrationCommand("diagnose", "Diagnostic Sonar", "Lecture seule : liste les canaux que GG expose et leurs niveaux, et écrit sonar-diagnostic.txt dans le dossier de configuration.",
            null, false, null, "Stethoscope24", "#475569", Offline: true),
    };

    /// <inheritdoc/>
    public void SetBackground(bool background) => _background = background;

    /// <inheritdoc/>
    protected override string Unreachable(HttpRequestException ex) => "Sonar est injoignable : SteelSeries GG est-il lancé, Sonar activé ?";

    /// <inheritdoc/>
    protected override HttpClient CreateClient(IntegrationSettings settings)
    {
        // Redirections suivies : jamais (une réponse de GG ne peut pas nous envoyer ailleurs). Certificat auto-signé de GG accepté pour une adresse
        // locale seulement : chaque requête passe par Local(), qui refuse tout autre hôte avant l'envoi.
        return new HttpClient(SonarApi.CreateLocalHandler()) { Timeout = TimeSpan.FromSeconds(3) };
    }

    // ---------------------------------------------------------------- découverte et lecture

    /// <inheritdoc/>
    protected override async Task PollAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var config = _config = SonarConfig.From(settings);
        var trace = new List<string>();
        try
        {
            await EnsureSonarAsync(config, trace, cancellation);
            var (_, root) = await GetJsonAsync(new Uri(_sonar!, SonarApi.BuildRoute(config.EffectiveReadRoute, config)), trace, cancellation);
            var levels = SonarApi.ParseLevels(root, config.Channels, config.LevelKey);
            Publish(config, levels, trace);
            if (levels.Count == 0)
                throw new InvalidOperationException("Sonar répond, mais aucun canal n'est reconnu : lance « Diagnostic » et lis sonar-diagnostic.txt");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (config.BaseUrl.Length == 0) _sonar = null;   // Sonar a pu redémarrer sur un autre port : on le cherche de nouveau
            trace.Add("Échec réseau : " + ex.Message);
            ReportFailure(config, trace);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            if (config.BaseUrl.Length == 0) _sonar = null;
            trace.Add("Échec : " + ex.Message);
            ReportFailure(config, trace);
            throw ex is InvalidOperationException ? ex : new InvalidOperationException("Sonar : réponse ou fichier de GG illisible (" + ex.Message + ")", ex);
        }
    }

    void Publish(SonarConfig config, Dictionary<string, SonarLevel> levels, List<string> trace)
    {
        var values = new List<KeyValuePair<string, string>> { KeyValuePair.Create("mode", config.Mode) };
        foreach (var (channel, level) in levels)
        {
            values.Add(KeyValuePair.Create(channel + ".volume", level.Percent.ToString(CultureInfo.InvariantCulture)));
            if (level.Muted is { } muted) values.Add(KeyValuePair.Create(channel + ".muted", muted ? "true" : "false"));
        }
        SetValues(values);
        foreach (var channel in config.Channels.Where(c => !levels.ContainsKey(c))) RemoveValues(channel + ".");
        trace.Add("Canaux reconnus : " + (levels.Count == 0 ? "aucun" : string.Join(", ", levels.Select(l => $"{l.Key} {l.Value.Percent} %" + (l.Value.Muted == true ? " (coupé)" : "")))));
        var missing = config.Channels.Where(c => !levels.ContainsKey(c)).ToList();
        if (missing.Count > 0) trace.Add("Canaux demandés mais absents de la réponse : " + string.Join(", ", missing));
    }

    async Task EnsureSonarAsync(SonarConfig config, List<string> trace, CancellationToken cancellation)
    {
        if (_sonar != null) return;
        if (config.BaseUrl.Length > 0)
        {
            _sonar = SonarApi.ParseLocalAddress(config.BaseUrl, false) ?? throw new InvalidOperationException("L'adresse de Sonar doit être locale (127.0.0.1 ou localhost)");
            trace.Add("Adresse de Sonar réglée à la main : " + _sonar);
            return;
        }
        trace.Add($"coreProps.json : {config.CorePropsPath}");
        if (!File.Exists(config.CorePropsPath))
        {
            trace.Add("coreProps.json absent.");
            throw new InvalidOperationException("SteelSeries GG est introuvable ou fermé (coreProps.json absent)");
        }
        if (new FileInfo(config.CorePropsPath).Length > 65_536) throw new InvalidOperationException("coreProps.json est anormalement gros : ignoré");
        JsonNode? coreProps;
        try { coreProps = JsonNode.Parse(await File.ReadAllTextAsync(config.CorePropsPath, cancellation)); }
        catch (JsonException) { throw new InvalidOperationException("coreProps.json est illisible"); }
        var candidates = SonarApi.GgAddresses(coreProps);
        trace.Add("Adresses locales de GG : " + (candidates.Count == 0 ? "aucune" : string.Join(", ", candidates)));
        if (candidates.Count == 0) throw new InvalidOperationException("coreProps.json ne donne aucune adresse locale de GG");

        string last = "";
        foreach (var gg in candidates)
        {
            try
            {
                var (_, subApps) = await GetJsonAsync(new Uri(gg, SonarApi.BuildRoute(config.SubAppsRoute, config)), trace, cancellation);
                _sonar = SonarApi.SonarAddress(subApps);
                trace.Add("Adresse de Sonar : " + _sonar);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException && !cancellation.IsCancellationRequested)
            {
                last = ex is HttpRequestException or TaskCanceledException ? "SteelSeries GG ne répond pas (fermé ?)" : ex.Message;
                trace.Add($"{gg} : {ex.Message}");
            }
        }
        throw new InvalidOperationException(last);
    }

    /// <summary>Envoie une requête à une adresse locale et retourne le statut et le corps lu (limité à <see cref="SonarApi.MaxResponseBytes"/>).</summary>
    async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, Uri uri, List<string>? trace, CancellationToken cancellation)
    {
        if (!SonarApi.IsLocalAddress(uri)) throw new InvalidOperationException("Adresse refusée : Sonar ne se joint qu'en local");
        using var request = new HttpRequestMessage(method, uri);
        using var response = await (_diagnoseClient ?? Http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.Content.Headers.ContentLength > SonarApi.MaxResponseBytes) throw new InvalidOperationException("Réponse trop grosse : ce n'est pas l'API de Sonar");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > SonarApi.MaxResponseBytes) throw new InvalidOperationException("Réponse trop grosse : ce n'est pas l'API de Sonar");
        }
        var body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        trace?.Add($"{method} {uri.AbsoluteUri} -> {(int)response.StatusCode}, {buffer.Length} octets");
        return (response.StatusCode, body);
    }

    async Task<(HttpStatusCode Status, JsonNode? Json)> GetJsonAsync(Uri uri, List<string>? trace, CancellationToken cancellation)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, uri, trace, cancellation);
        trace?.Add("Début de la réponse : " + LogRedactor.Redact(body.Length > 1500 ? body[..1500] + "…" : body));
        if ((int)status is < 200 or > 299) throw new InvalidOperationException($"Sonar a répondu {(int)status} à {uri.AbsolutePath}");
        return (status, JsonNode.Parse(body));
    }

    // ---------------------------------------------------------------- commandes

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        var config = _config;
        switch (command.ToLowerInvariant())
        {
            case "volume":
            {
                var (channel, level) = SplitChannelLevel(argument);
                int current = Values.TryGetValue(channel + ".volume", out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
                int percent = SonarApi.ParseLevel(level, current);
                await PutAsync(SonarApi.BuildRoute(config.EffectiveVolumeRoute, config, channel, SonarApi.FormatVolume(percent)), cancellation);
                SetValue(channel + ".volume", percent.ToString(CultureInfo.InvariantCulture));
                return $"{SonarApi.Label(channel)} : {percent} %";
            }
            case "mute" or "unmute" or "togglemute":
            {
                var channel = RequireChannel(argument);
                bool muted = command.Equals("mute", StringComparison.OrdinalIgnoreCase)
                    || (command.Equals("togglemute", StringComparison.OrdinalIgnoreCase) && !(Values.TryGetValue(channel + ".muted", out var m) && m == "true"));
                await PutAsync(SonarApi.BuildRoute(config.EffectiveMuteRoute, config, channel, muted ? "true" : "false"), cancellation);
                SetValue(channel + ".muted", muted ? "true" : "false");
                return $"{SonarApi.Label(channel)} {(muted ? "coupé" : "rétabli")}";
            }
            case "diagnose":
                return await DiagnoseAsync(config, cancellation);
            default:
                throw new ArgumentException($"Commande inconnue : « {command} »");
        }
    }

    static (string Channel, string Level) SplitChannelLevel(string? argument)
    {
        var text = RequireArgument(argument, "canal=niveau (par exemple game=60)");
        var eq = text.IndexOf('=');
        if (eq <= 0 || eq == text.Length - 1) throw new ArgumentException("Écris canal=niveau, par exemple game=60 ou chatRender=+5");
        return (RequireChannel(text[..eq]), text[(eq + 1)..].Trim());
    }

    static string RequireChannel(string? argument)
    {
        var channel = SonarApi.Normalize(RequireArgument(argument, "le canal (game, chatRender, media, aux, chatCapture, master)"));
        if (channel.Length > 40 || channel.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("Nom de canal invalide : lettres et chiffres seulement");
        return channel;
    }

    async Task PutAsync(string route, CancellationToken cancellation)
    {
        var sonar = _sonar ?? throw new InvalidOperationException("Sonar n'est pas connecté");
        try
        {
            var (status, _) = await SendAsync(HttpMethod.Put, new Uri(sonar, route), null, cancellation);
            if ((int)status is < 200 or > 299) throw new InvalidOperationException($"Sonar a refusé la commande ({(int)status}) : vérifie la route dans les réglages avancés");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Sonar ne répond plus : SteelSeries GG est-il lancé ?"); }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException("Sonar ne répond pas (délai dépassé)"); }
    }

    // ---------------------------------------------------------------- diagnostic (lecture seule)

    async Task<string> DiagnoseAsync(SonarConfig config, CancellationToken cancellation)
    {
        var trace = new List<string>();
        _sonar = null;
        HttpClient? temporary = null;
        try { _ = Http; }
        catch (InvalidOperationException) { _diagnoseClient = temporary = CreateClient(new IntegrationSettings()); }   // pas connecté : le diagnostic sert justement à savoir pourquoi
        try
        {
            await EnsureSonarAsync(config, trace, cancellation);
            var (_, root) = await GetJsonAsync(new Uri(_sonar!, SonarApi.BuildRoute(config.EffectiveReadRoute, config)), trace, cancellation);
            trace.Add("Clés de premier niveau : " + (root is JsonObject o ? string.Join(", ", o.Select(p => p.Key)) : "(pas un objet)"));
            var levels = SonarApi.ParseLevels(root, config.Channels, config.LevelKey);
            Publish(config, levels, trace);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or JsonException)
        {
            trace.Add("Échec : " + ex.Message);
            WriteReport(config, trace);
            throw new InvalidOperationException("Diagnostic Sonar : " + ex.Message + " (détails dans " + ReportFileName + ")", ex);
        }
        finally
        {
            _diagnoseClient = null;
            temporary?.Dispose();
        }
        var path = WriteReport(config, trace);
        var summary = trace.LastOrDefault(l => l.StartsWith("Canaux reconnus", StringComparison.Ordinal)) ?? "Canaux reconnus : aucun";
        return summary + (path != null ? $". Détails : {path}" : "");
    }

    void ReportFailure(SonarConfig config, List<string> trace)
    {
        if (DateTime.UtcNow - _lastReport < ReportEvery) return;
        WriteReport(config, trace);
    }

    /// <summary>Écrit le rapport (journal et fichier) ; retourne le chemin du fichier, ou null s'il n'a pas pu être écrit. Aucun secret : adresses locales et niveaux seulement.</summary>
    string? WriteReport(SonarConfig config, List<string> trace)
    {
        _lastReport = DateTime.UtcNow;
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"StreamDak : diagnostic Sonar (lecture seule), {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine(CultureInfo.InvariantCulture, $"Mode : {config.Mode}{(config.Streamer ? ", curseur " + config.Slider : "")} ; canaux demandés : {string.Join(", ", config.Channels)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Routes : lecture {config.EffectiveReadRoute} ; volume {config.EffectiveVolumeRoute} ; coupure {config.EffectiveMuteRoute}")
            .AppendLine("API Sonar vérifiée en mode classic (30 octobre 2026) ; en cas d'écart, envoie ce fichier pour ajuster les routes.")
            .AppendLine();
        foreach (var line in trace) text.AppendLine(line);
        var report = text.ToString();
        foreach (var line in report.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Log.Info("[sonar] " + line);
        try
        {
            var dir = ReportDirectory ?? ConfigStore.Dir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ReportFileName);
            File.WriteAllText(path, report, Encoding.UTF8);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Sonar : rapport de diagnostic impossible à écrire : " + ex.Message);
            return null;
        }
    }
}
