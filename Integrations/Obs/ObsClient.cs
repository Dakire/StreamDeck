using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Obs;

/// <summary>Réponse d'une requête obs-websocket.</summary>
/// <param name="Ok">true si OBS a exécuté la requête.</param>
/// <param name="Code">Code de statut d'OBS (100 : succès).</param>
/// <param name="Comment">Explication d'OBS en cas d'échec.</param>
/// <param name="Data">Données de la réponse, ou null.</param>
public sealed record ObsResponse(bool Ok, int Code, string? Comment, JsonObject? Data);

/// <summary>
/// Client du protocole obs-websocket v5 : identification avec défi SHA-256, requêtes corrélées par identifiant, événements.
/// Protocole : https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md
/// </summary>
public sealed class ObsClient : IAsyncDisposable
{
    /// <summary>Abonnements aux événements : scènes (4), entrées (8) et sorties (64). Pas les niveaux sonores, très bavards.</summary>
    public const int EventSubscriptions = 4 | 8 | 64;

    const int OpHello = 0, OpIdentify = 1, OpIdentified = 2, OpEvent = 5, OpRequest = 6, OpResponse = 7;
    const int AuthenticationFailed = 4009;

    readonly ClientWebSocket _socket = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<ObsResponse>> _pending = new();
    readonly SemaphoreSlim _send = new(1, 1);
    CancellationTokenSource? _lifetime;
    Task? _receiver;
    int _nextId;

    /// <summary>Événement d'OBS : type et données.</summary>
    public event Action<string, JsonObject>? EventReceived;
    /// <summary>La connexion s'est interrompue (OBS fermé, réseau coupé) ; le texte dit pourquoi.</summary>
    public event Action<string>? Closed;

    /// <summary>Version d'obs-websocket annoncée par OBS.</summary>
    public string ServerVersion { get; private set; } = "";

    /// <summary>
    /// Ouvre la connexion et s'identifie. Lève <see cref="InvalidOperationException"/> avec un message affichable si OBS est
    /// injoignable, demande un mot de passe qu'on n'a pas, ou refuse celui qu'on a.
    /// </summary>
    public async Task ConnectAsync(string host, int port, string password, CancellationToken cancellation)
    {
        try { await _socket.ConnectAsync(new Uri($"ws://{host}:{port}"), cancellation); }
        catch (WebSocketException ex) { throw new InvalidOperationException($"OBS ne répond pas sur {host}:{port}. Vérifie qu'OBS est lancé et que le serveur WebSocket est activé (Outils › Réglages du serveur WebSocket).", ex); }

        var hello = await ReceiveAsync(cancellation) ?? throw new InvalidOperationException("OBS a fermé la connexion");
        if (hello["op"]?.GetValue<int>() != OpHello) throw new InvalidOperationException("Réponse inattendue : ce n'est pas un serveur obs-websocket v5");
        var data = hello["d"]!.AsObject();
        ServerVersion = data["obsWebSocketVersion"]?.GetValue<string>() ?? "";

        var identify = new JsonObject { ["rpcVersion"] = Math.Min(1, data["rpcVersion"]?.GetValue<int>() ?? 1), ["eventSubscriptions"] = EventSubscriptions };
        if (data["authentication"] is JsonObject auth)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("OBS demande un mot de passe : renseigne-le dans les réglages de l'intégration.");
            identify["authentication"] = Authenticate(password, auth["salt"]!.GetValue<string>(), auth["challenge"]!.GetValue<string>());
        }
        await SendAsync(new JsonObject { ["op"] = OpIdentify, ["d"] = identify }, cancellation);

        JsonNode? reply;
        try { reply = await ReceiveAsync(cancellation); }
        catch (WebSocketException) { reply = null; }
        if (reply == null || reply["op"]?.GetValue<int>() != OpIdentified)
            throw new InvalidOperationException(_socket.CloseStatus == (WebSocketCloseStatus)AuthenticationFailed ? "Mot de passe OBS refusé" : "OBS a refusé l'identification");

        _lifetime = new CancellationTokenSource();
        _receiver = Task.Run(() => ReceiveLoopAsync(_lifetime.Token), CancellationToken.None);
    }

    /// <summary>Réponse d'authentification : base64(sha256(base64(sha256(mot de passe + sel)) + défi)).</summary>
    public static string Authenticate(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <summary>Envoie une requête et attend sa réponse (10 s au plus).</summary>
    public async Task<ObsResponse> RequestAsync(string type, JsonObject? data, CancellationToken cancellation)
    {
        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pending = new TaskCompletionSource<ObsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            var request = new JsonObject { ["requestType"] = type, ["requestId"] = id };
            if (data != null) request["requestData"] = data;
            await SendAsync(new JsonObject { ["op"] = OpRequest, ["d"] = request }, cancellation);
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
        }
        catch (TimeoutException) { throw new TimeoutException($"OBS n'a pas répondu à « {type} »"); }
        finally { _pending.TryRemove(id, out _); }
    }

    /// <summary>Requête qui doit réussir : lève <see cref="InvalidOperationException"/> avec la raison d'OBS sinon.</summary>
    public async Task<JsonObject> CallAsync(string type, JsonObject? data, CancellationToken cancellation)
    {
        var response = await RequestAsync(type, data, cancellation);
        if (!response.Ok) throw new InvalidOperationException(response.Comment is { Length: > 0 } c ? $"OBS : {c}" : $"OBS a refusé « {type} » (code {response.Code})");
        return response.Data ?? new JsonObject();
    }

    async Task SendAsync(JsonObject message, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _send.WaitAsync(cancellation);
        try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellation); }
        finally { _send.Release(); }
    }

    async Task<JsonNode?> ReceiveAsync(CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        using var message = new System.IO.MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cancellation);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonNode.Parse(message.ToArray());
    }

    async Task ReceiveLoopAsync(CancellationToken stop)
    {
        string reason = "connexion fermée";
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var message = await ReceiveAsync(stop);
                if (message == null) { reason = "OBS a fermé la connexion"; break; }
                Dispatch(message);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is WebSocketException or JsonException or System.IO.IOException) { reason = ex.Message; }
        foreach (var pending in _pending.Values) pending.TrySetException(new InvalidOperationException("Connexion à OBS perdue"));
        if (!stop.IsCancellationRequested) Closed?.Invoke(reason);
    }

    void Dispatch(JsonNode message)
    {
        var data = message["d"]?.AsObject();
        if (data == null) return;
        switch (message["op"]?.GetValue<int>())
        {
            case OpEvent:
                EventReceived?.Invoke(data["eventType"]?.GetValue<string>() ?? "", data["eventData"]?.AsObject() ?? new JsonObject());
                break;
            case OpResponse:
                var status = data["requestStatus"]?.AsObject();
                if (data["requestId"]?.GetValue<string>() is { } id && _pending.TryGetValue(id, out var pending))
                    pending.TrySetResult(new ObsResponse(status?["result"]?.GetValue<bool>() ?? false, status?["code"]?.GetValue<int>() ?? 0,
                        status?["comment"]?.GetValue<string>(), data["responseData"]?.AsObject()));
                break;
        }
    }

    /// <summary>Ferme la connexion proprement.</summary>
    public async ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "StreamDak", timeout.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { /* déjà fermée ou OBS ne répond plus */ }
        _socket.Dispose();
        if (_receiver != null) { try { await _receiver; } catch (OperationCanceledException) { /* arrêt */ } }
        _lifetime?.Dispose();
        _send.Dispose();
    }
}
