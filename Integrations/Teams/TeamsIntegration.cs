using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Integrations.Teams;

/// <summary>
/// Microsoft Teams (nouvelle version) par son API locale « application tierce » : WebSocket sur le port 8124, association par un
/// jeton que Teams délivre après l'accord de l'utilisateur. Pilote micro, caméra, main levée, flou d'arrière-plan,
/// enregistrement, réactions et raccrochage, et lit l'état de la réunion en cours. À activer dans Teams :
/// Paramètres › Confidentialité › Gérer l'API.
/// </summary>
public sealed class TeamsIntegration : IntegrationBase, IPairableIntegration, IDisposable
{
    /// <summary>Identifiant et type d'action.</summary>
    public const string IntegrationId = "teams";
    static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(4);

    ClientWebSocket? _socket;
    CancellationTokenSource? _lifetime;
    readonly SemaphoreSlim _send = new(1, 1);
    readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    int _nextId;

    /// <inheritdoc/>
    public override string Id => IntegrationId;
    /// <inheritdoc/>
    public override string Name => "Microsoft Teams";
    /// <inheritdoc/>
    public override string Category => "Microsoft Teams";
    /// <inheritdoc/>
    public override string Icon => "People24";
    /// <inheritdoc/>
    public override string Help => "Dans Teams : Paramètres › Confidentialité › Gérer l'API › Activer l'API. Puis clique sur « Associer à Teams » ici et accepte la demande dans Teams. Les commandes n'agissent que pendant une réunion.";

    /// <inheritdoc/>
    public override IReadOnlyList<SettingField> Fields { get; } = new[]
    {
        new SettingField("port", "Port", SettingKind.Number, "8124"),
        new SettingField("token", "Jeton d'association", SettingKind.Secret, null, "Rempli par le bouton « Associer à Teams »."),
    };

    /// <inheritdoc/>
    public string PairLabel => "Associer à Teams";
    /// <inheritdoc/>
    public string PairHelp => "Clique sur « Associer à Teams », puis accepte la demande qui s'affiche dans Teams (30 secondes).";
    /// <inheritdoc/>
    public string PairedSecretField => "token";

    /// <inheritdoc/>
    public override IReadOnlyList<IntegrationCommand> Commands { get; } = new[]
    {
        new IntegrationCommand("mute", "Micro", "Coupe ou rétablit le micro ; le voyant est plein quand il est coupé.", null, false, "muted", "MicOff24", "#6264A7"),
        new IntegrationCommand("camera", "Caméra", "Active ou coupe la caméra ; le voyant est plein quand elle est allumée.", null, false, "camera", "Video24", "#6264A7"),
        new IntegrationCommand("hand", "Lever la main", "Lève ou baisse la main.", null, false, "hand", "HandLeft24", "#6264A7"),
        new IntegrationCommand("blur", "Flou d'arrière-plan", "Active ou coupe le flou d'arrière-plan.", null, false, "blur", "PersonCircle24", "#6264A7"),
        new IntegrationCommand("record", "Enregistrement", "Démarre ou arrête l'enregistrement de la réunion.", null, false, "recording", "Record24", "#BE123C"),
        new IntegrationCommand("react", "Réaction", "Envoie une réaction.", "applause, laugh, like, love ou wow", true, null, "Emoji24", "#6264A7"),
        new IntegrationCommand("leave", "Quitter la réunion", "Raccroche.", null, false, null, "CallEnd24", "#BE123C"),
    };

    static readonly Dictionary<string, string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mute"] = "toggle-mute", ["camera"] = "toggle-video", ["hand"] = "toggle-hand", ["blur"] = "toggle-background-blur",
        ["record"] = "toggle-recording", ["leave"] = "leave-call", ["react"] = "react",
    };
    static readonly string[] Reactions = { "applause", "laugh", "like", "love", "wow" };

    static Uri BuildUri(int port, string token) =>
        new($"ws://127.0.0.1:{port}?token={Uri.EscapeDataString(token)}&protocol-version=2.0.0&manufacturer=TouchDeck&device=PC&app=TouchDeck&app-version=1.0");

    /// <inheritdoc/>
    protected override async Task ConnectCoreAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        var token = settings.Secret("token");
        if (token.Length == 0) throw new InvalidOperationException("TouchDeck n'est pas encore associé à Teams : clique sur « Associer à Teams » dans les réglages");
        var socket = new ClientWebSocket();
        try { await socket.ConnectAsync(BuildUri(settings.GetInt("port", 8124), token), cancellation); }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            throw new InvalidOperationException("Teams ne répond pas. Vérifie que Teams est lancé et que l'API est activée (Paramètres › Confidentialité › Gérer l'API).", ex);
        }
        _socket = socket;
        _lifetime = new CancellationTokenSource();
        var stop = _lifetime.Token;
        _ = Task.Run(() => ReceiveLoopAsync(socket, stop), CancellationToken.None);
        await SendRawAsync(new JsonObject { ["action"] = "query-meeting-state", ["parameters"] = new JsonObject(), ["requestId"] = Interlocked.Increment(ref _nextId) }, cancellation);
    }

    /// <inheritdoc/>
    public async Task<string> PairAsync(IntegrationSettings settings, CancellationToken cancellation)
    {
        using var socket = new ClientWebSocket();
        try { await socket.ConnectAsync(BuildUri(settings.GetInt("port", 8124), ""), cancellation); }
        catch (WebSocketException ex)
        {
            throw new InvalidOperationException("Teams ne répond pas. Vérifie que Teams est lancé et que l'API est activée (Paramètres › Confidentialité › Gérer l'API).", ex);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var buffer = new byte[8 * 1024];
        try
        {
            while (true)
            {
                using var message = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, timeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Teams a refusé l'association");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                if (JsonNode.Parse(message.ToArray())?["tokenRefresh"]?.GetValue<string>() is { Length: > 0 } token) return token;
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Aucune réponse de Teams : accepte la demande d'association affichée dans Teams, puis réessaie.");
        }
        catch (WebSocketException ex) { throw new InvalidOperationException("Teams a refusé l'association", ex); }
    }

    async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken stop)
    {
        var buffer = new byte[16 * 1024];
        string reason = "Teams a fermé la connexion";
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var message = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, stop);
                    if (result.MessageType == WebSocketMessageType.Close) goto closed;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                Dispatch(JsonNode.Parse(message.ToArray()));
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is WebSocketException or JsonException or System.IO.IOException) { reason = ex.Message; }
        closed:
        foreach (var pending in _pending.Values) pending.TrySetException(new InvalidOperationException("Connexion à Teams perdue"));
        if (!stop.IsCancellationRequested) ConnectionLost(reason);
    }

    void Dispatch(JsonNode? message)
    {
        if (message == null) return;
        if (message["requestId"]?.GetValue<int>() is int id && _pending.TryGetValue(id, out var pending))
            pending.TrySetResult(message["response"]?.ToString() ?? "");
        if (message["meetingUpdate"]?["meetingState"] is JsonObject state) Publish(state);
    }

    static string Flag(JsonObject state, params string[] names)
    {
        foreach (var name in names)
            if (state[name] is JsonValue v && v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        return "false";
    }

    void Publish(JsonObject state) => SetValues(new[]
    {
        KeyValuePair.Create("inMeeting", Flag(state, "isInMeeting")),
        KeyValuePair.Create("muted", Flag(state, "isMuted")),
        KeyValuePair.Create("camera", Flag(state, "isCameraOn", "isVideoOn")),
        KeyValuePair.Create("hand", Flag(state, "isHandRaised")),
        KeyValuePair.Create("blur", Flag(state, "isBackgroundBlurred")),
        KeyValuePair.Create("recording", Flag(state, "isRecordingOn")),
        KeyValuePair.Create("sharing", Flag(state, "isSharing")),
    });

    async Task SendRawAsync(JsonObject message, CancellationToken cancellation)
    {
        var socket = _socket ?? throw new InvalidOperationException("Teams n'est pas connecté");
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _send.WaitAsync(cancellation);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellation); }
        finally { _send.Release(); }
    }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(string command, string? argument, CancellationToken cancellation)
    {
        if (!Actions.TryGetValue(command, out var action)) throw new ArgumentException($"Commande Teams inconnue : « {command} »");
        if (Values.TryGetValue("inMeeting", out var inMeeting) && inMeeting != "true")
            throw new InvalidOperationException("Aucune réunion Teams en cours : les commandes n'agissent que pendant une réunion");

        var parameters = new JsonObject();
        if (command.Equals("react", StringComparison.OrdinalIgnoreCase))
        {
            var reaction = RequireArgument(argument, "la réaction (applause, laugh, like, love, wow)").ToLowerInvariant();
            if (Array.IndexOf(Reactions, reaction) < 0) throw new ArgumentException($"Réaction inconnue : « {reaction} ». Choisis parmi {string.Join(", ", Reactions)}");
            parameters["type"] = reaction;
        }

        int id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            await SendRawAsync(new JsonObject { ["action"] = action, ["parameters"] = parameters, ["requestId"] = id }, cancellation);
            var response = await pending.Task.WaitAsync(ActionTimeout, cancellation);
            if (response.Length > 0 && !response.Equals("Success", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Teams a refusé la commande : {response}");
        }
        catch (TimeoutException) { throw new InvalidOperationException("Teams n'a pas répondu à la commande"); }
        finally { _pending.TryRemove(id, out _); }
        return command.ToLowerInvariant() switch
        {
            "mute" => "Micro basculé", "camera" => "Caméra basculée", "hand" => "Main basculée", "blur" => "Flou basculé",
            "record" => "Enregistrement basculé", "react" => $"Réaction {parameters["type"]}", _ => "Réunion quittée",
        };
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _socket?.Dispose();
        _socket = null;
        return Task.CompletedTask;
    }

    /// <summary>Ferme la connexion.</summary>
    public void Dispose()
    {
        DisconnectCoreAsync().GetAwaiter().GetResult();
        _send.Dispose();
        GC.SuppressFinalize(this);
    }
}
