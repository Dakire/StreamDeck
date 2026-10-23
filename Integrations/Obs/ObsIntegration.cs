using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Obs;

/// <summary>
/// OBS Studio : changer de scène, couper un micro, démarrer le direct ou l'enregistrement, afficher une source. États lus en
/// temps réel : scène courante, micros coupés, direct, enregistrement, caméra virtuelle, images par seconde.
/// </summary>
public sealed class ObsIntegration : IntegrationBase, IDisposable
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "obs";
    static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(2);

    ObsClient? _client;
    CancellationTokenSource? _stats;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "OBS Studio";
    /// <inheritdoc/>
    public override string Category => "OBS Studio";
    /// <inheritdoc/>
    public override string Icon => "Video24";
    /// <inheritdoc/>
    public override string Help => "Dans OBS : Outils › Réglages du serveur WebSocket › Activer le serveur WebSocket (OBS 28 ou plus). Le mot de passe est facultatif si l'authentification est désactivée.";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("host", "Adresse", SettingKind.Text, "localhost", "« localhost » si OBS tourne sur ce PC."),
        new SettingField("port", "Port", SettingKind.Number, "4455"),
        new SettingField("password", "Mot de passe", SettingKind.Secret),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("scene", "Changer de scène", "Affiche la scène indiquée en sortie.", "le nom de la scène", true, "scene.{arg}", "Video24", "#7C3AED"),
        new IntegrationCommand("mute", "Couper / rétablir un micro", "Bascule le mute d'une source audio d'OBS ; le voyant est plein quand elle est coupée.", "le nom de la source audio", true, "mute.{arg}", "MicOff24", "#BE123C"),
        new IntegrationCommand("stream", "Démarrer / arrêter le direct", "Bascule la diffusion ; le voyant est plein pendant le direct.", null, false, "streaming", "Live24", "#BE123C"),
        new IntegrationCommand("record", "Démarrer / arrêter l'enregistrement", "Bascule l'enregistrement ; le voyant est plein pendant l'enregistrement.", null, false, "recording", "Record24", "#BE123C"),
        new IntegrationCommand("virtualcam", "Caméra virtuelle", "Bascule la caméra virtuelle d'OBS.", null, false, "virtualcam", "VideoPerson24", "#2563EB"),
        new IntegrationCommand("source", "Afficher / masquer une source", "Bascule la visibilité d'une source de la scène courante.", "le nom de la source", true, null, "Eye24", "#334155"),
        new IntegrationCommand("replay", "Sauvegarder la relecture", "Enregistre le tampon de relecture (il doit être démarré dans OBS).", null, false, null, "ArrowReply24", "#334155"),
    };

    /// <inheritdoc/>
    protected override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var client = new ObsClient();
        try
        {
            await client.ConnectAsync(settings.Get("host", "localhost"), settings.GetInt("port", 4455), settings.Secret("password"), cancellation);
            client.EventReceived += OnEvent;
            client.Closed += reason => { if (ReferenceEquals(_client, client)) ConnectionLost(reason); };
            _client = client;
            await ReadStateAsync(client, cancellation);
        }
        catch
        {
            _client = null;
            await client.DisposeAsync();
            throw;
        }
        _stats = new CancellationTokenSource();
        var statsToken = _stats.Token;
        _ = Task.Run(() => PollStatsAsync(client, statsToken), CancellationToken.None);
    }

    /// <inheritdoc/>
    protected override async Task DisconnectCoreAsync()
    {
        _stats?.Cancel();
        _stats?.Dispose();
        _stats = null;
        var client = _client;
        _client = null;
        if (client != null) await client.DisposeAsync();
    }

    async Task ReadStateAsync(ObsClient client, CancellationToken cancellation)
    {
        var scenes = await client.CallAsync("GetSceneList", null, cancellation);
        var current = scenes["currentProgramSceneName"]?.GetValue<string>() ?? "";
        var values = new List<KeyValuePair<string, string>> { KeyValuePair.Create("scene", current) };
        foreach (var scene in scenes["scenes"]?.AsArray() ?? new JsonArray())
            if (scene?["sceneName"]?.GetValue<string>() is { } name) values.Add(KeyValuePair.Create("scene." + name, Bool(name == current)));

        values.Add(KeyValuePair.Create("streaming", Bool((await client.CallAsync("GetStreamStatus", null, cancellation))["outputActive"]?.GetValue<bool>() == true)));
        values.Add(KeyValuePair.Create("recording", Bool((await client.CallAsync("GetRecordStatus", null, cancellation))["outputActive"]?.GetValue<bool>() == true)));
        var camera = await client.RequestAsync("GetVirtualCamStatus", null, cancellation);   // absente sur certaines installations
        values.Add(KeyValuePair.Create("virtualcam", Bool(camera.Ok && camera.Data?["outputActive"]?.GetValue<bool>() == true)));

        foreach (var input in (await client.CallAsync("GetInputList", null, cancellation))["inputs"]?.AsArray() ?? new JsonArray())
        {
            if (input?["inputName"]?.GetValue<string>() is not { } inputName) continue;
            var mute = await client.RequestAsync("GetInputMute", new JsonObject { ["inputName"] = inputName }, cancellation);
            if (mute.Ok) values.Add(KeyValuePair.Create("mute." + inputName, Bool(mute.Data?["inputMuted"]?.GetValue<bool>() == true)));   // les sources sans audio répondent par une erreur
        }
        SetValues(values);
    }

    async Task PollStatsAsync(ObsClient client, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(StatsInterval, stop);
                var stats = await client.RequestAsync("GetStats", null, stop);
                if (!stats.Ok || stats.Data == null) continue;
                SetValues(new[]
                {
                    KeyValuePair.Create("fps", Math.Round(stats.Data["activeFps"]?.GetValue<double>() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    KeyValuePair.Create("cpu", Math.Round(stats.Data["cpuUsage"]?.GetValue<double>() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                });
            }
        }
        catch (OperationCanceledException) { /* arrêt demandé */ }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { /* connexion perdue : la boucle de réception le signale */ }
    }

    void OnEvent(string type, JsonObject data)
    {
        switch (type)
        {
            case "CurrentProgramSceneChanged" when data["sceneName"]?.GetValue<string>() is { } scene:
                // Toutes les scènes connues repassent à « inactive », puis la nouvelle est active
                var updates = Values.Keys.Where(k => k.StartsWith("scene.", StringComparison.OrdinalIgnoreCase)).Select(k => KeyValuePair.Create(k, "false")).ToList();
                updates.Add(KeyValuePair.Create("scene", scene));
                updates.Add(KeyValuePair.Create("scene." + scene, "true"));
                SetValues(updates);
                break;
            case "InputMuteStateChanged" when data["inputName"]?.GetValue<string>() is { } input:
                SetValue("mute." + input, Bool(data["inputMuted"]?.GetValue<bool>() == true));
                break;
            case "StreamStateChanged":
                SetValue("streaming", Bool(data["outputActive"]?.GetValue<bool>() == true));
                break;
            case "RecordStateChanged":
                SetValue("recording", Bool(data["outputActive"]?.GetValue<bool>() == true));
                break;
            case "VirtualcamStateChanged":
                SetValue("virtualcam", Bool(data["outputActive"]?.GetValue<bool>() == true));
                break;
        }
    }

    static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Arrête le relevé des statistiques.</summary>
    public void Dispose() { _stats?.Cancel(); _stats?.Dispose(); }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        var client = _client ?? throw new InvalidOperationException("OBS n'est pas connecté");
        switch (command.ToLowerInvariant())
        {
            case "scene":
                var scene = RequireArgument(argument, "le nom de la scène");
                await client.CallAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = scene }, cancellation);
                return $"Scène : {scene}";
            case "mute":
                var input = RequireArgument(argument, "le nom de la source audio");
                var muted = (await client.CallAsync("ToggleInputMute", new JsonObject { ["inputName"] = input }, cancellation))["inputMuted"]?.GetValue<bool>() == true;
                return muted ? $"{input} coupé" : $"{input} rétabli";
            case "stream":
                var live = (await client.CallAsync("ToggleStream", null, cancellation))["outputActive"]?.GetValue<bool>() == true;
                return live ? "Direct démarré" : "Direct arrêté";
            case "record":
                var recording = (await client.CallAsync("ToggleRecord", null, cancellation))["outputActive"]?.GetValue<bool>() == true;
                return recording ? "Enregistrement démarré" : "Enregistrement arrêté";
            case "virtualcam":
                var camera = (await client.CallAsync("ToggleVirtualCam", null, cancellation))["outputActive"]?.GetValue<bool>() == true;
                return camera ? "Caméra virtuelle active" : "Caméra virtuelle arrêtée";
            case "replay":
                await client.CallAsync("SaveReplayBuffer", null, cancellation);
                return "Relecture sauvegardée";
            case "source":
                var source = RequireArgument(argument, "le nom de la source");
                var sceneName = Values.GetValueOrDefault("scene") ?? throw new InvalidOperationException("Scène courante inconnue");
                var id = (await client.CallAsync("GetSceneItemId", new JsonObject { ["sceneName"] = sceneName, ["sourceName"] = source }, cancellation))["sceneItemId"]?.GetValue<int>()
                         ?? throw new InvalidOperationException($"Source introuvable dans « {sceneName} » : {source}");
                var enabled = (await client.CallAsync("GetSceneItemEnabled", new JsonObject { ["sceneName"] = sceneName, ["sceneItemId"] = id }, cancellation))["sceneItemEnabled"]?.GetValue<bool>() == true;
                await client.CallAsync("SetSceneItemEnabled", new JsonObject { ["sceneName"] = sceneName, ["sceneItemId"] = id, ["sceneItemEnabled"] = !enabled }, cancellation);
                return enabled ? $"{source} masquée" : $"{source} affichée";
            default:
                throw new ArgumentException($"Commande OBS inconnue : « {command} »");
        }
    }
}
