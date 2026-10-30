using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Integrations.Discord;

/// <summary>Erreur renvoyée par Discord (événement ERROR, fermeture du canal) : code et message de Discord.</summary>
public sealed class DiscordRpcException : Exception
{
    /// <summary>Erreur de code <paramref name="code"/>.</summary>
    public DiscordRpcException(int code, string message) : base(message) { Code = code; }
    /// <summary>Erreur sans code.</summary>
    public DiscordRpcException() : base("Erreur Discord") { }
    /// <summary>Erreur sans code.</summary>
    public DiscordRpcException(string message) : base(message) { }
    /// <summary>Erreur sans code, avec sa cause.</summary>
    public DiscordRpcException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Code d'erreur de Discord (4000 et plus : RPC, 5000 et plus : OAuth2), 0 s'il est inconnu.</summary>
    public int Code { get; }
}

/// <summary>
/// Canal local du client de bureau Discord (<c>\\.\pipe\discord-ipc-0</c> à <c>-9</c>), documenté par Discord (RPC). Trame : code d'opération (int32 petit-boutiste),
/// longueur (int32), JSON en UTF-8. Opérations : 0 poignée de main, 1 trame, 2 fermeture, 3 ping, 4 pong. Chaque commande porte un « nonce » auquel la réponse
/// fait écho ; les événements arrivent en <c>DISPATCH</c>. Une seule lecture en cours, écritures sérialisées, réponses limitées à 1 Mo.
/// </summary>
internal sealed class DiscordIpc : IAsyncDisposable
{
    const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    const int MaxFrame = 1 << 20;

    readonly Stream _stream;
    readonly SemaphoreSlim _write = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    readonly TaskCompletionSource<JsonObject> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _life = new();
    Task _reader = Task.CompletedTask;
    int _closed;

    DiscordIpc(Stream stream) { _stream = stream; }

    /// <summary>Un événement est arrivé : nom (« VOICE_SETTINGS_UPDATE ») et données (thread du lecteur).</summary>
    public event Action<string, JsonObject>? Event;
    /// <summary>Le canal est fermé (Discord quitté, connexion refusée) : la raison, une seule fois (thread du lecteur).</summary>
    public event Action<string>? Closed;

    /// <summary>Nom du canal ouvert (« discord-ipc-0 »).</summary>
    public string PipeName { get; private set; } = "";

    /// <summary>Noms des canaux nommés présents sur ce PC, ou null si la liste n'est pas lisible (tous les canaux sont alors essayés).</summary>
    static HashSet<string>? ExistingPipes()
    {
        try { return new HashSet<string>(Directory.EnumerateFiles(@"\\.\pipe\").Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Ouvre le premier canal Discord disponible (<paramref name="prefix"/> suivi de 0 à 9), ou null si Discord n'est pas lancé.</summary>
    public static async Task<DiscordIpc?> OpenAsync(string prefix, CancellationToken cancellation)
    {
        var existing = ExistingPipes();
        for (int i = 0; i < 10; i++)
        {
            var name = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Un canal qui n'existe pas est écarté tout de suite ; un canal qui existe (serveur occupé, machine chargée) a le temps de répondre.
            // La liste est lue sans se connecter : sonder un canal (File.Exists) peut consommer son unique instance côté serveur.
            if (existing != null && !existing.Contains(name)) continue;
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(1500, cancellation);
                return new DiscordIpc(pipe) { PipeName = name };
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync();
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }
        return null;
    }

    /// <summary>Poignée de main avec l'identifiant de l'application, puis attente de l'événement READY. Retourne ses données (utilisateur, configuration).</summary>
    public async Task<JsonObject> HandshakeAsync(string clientId, CancellationToken cancellation)
    {
        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);   // le lecteur vit jusqu'à la fermeture du canal
        await WriteAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId }, cancellation);
        return await _ready.Task.WaitAsync(cancellation);
    }

    /// <summary>Envoie une commande et attend sa réponse. Lève <see cref="DiscordRpcException"/> si Discord répond par une erreur.</summary>
    public async Task<JsonObject> RequestAsync(string command, JsonObject? args, CancellationToken cancellation, string? evt = null)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var reply = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = reply;
        try
        {
            var payload = new JsonObject { ["cmd"] = command, ["args"] = args ?? new JsonObject(), ["nonce"] = nonce };
            if (evt != null) payload["evt"] = evt;
            await WriteAsync(OpFrame, payload, cancellation);
            var message = await reply.Task.WaitAsync(cancellation);
            if (message["evt"]?.GetValue<string>() == "ERROR") throw ErrorOf(message["data"] as JsonObject);
            return message["data"] as JsonObject ?? new JsonObject();
        }
        finally { _pending.TryRemove(nonce, out _); }
    }

    static DiscordRpcException ErrorOf(JsonObject? data)
    {
        int code = data?["code"] is JsonValue c && c.TryGetValue(out int n) ? n : 0;
        var text = data?["message"]?.ToString();
        return new DiscordRpcException(code, string.IsNullOrWhiteSpace(text) ? "Discord a refusé la commande" : text);
    }

    async Task WriteAsync(int op, JsonObject payload, CancellationToken cancellation)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame, 8);
        await _write.WaitAsync(cancellation);
        try
        {
            await _stream.WriteAsync(frame, cancellation);
            await _stream.FlushAsync(cancellation);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Close("Discord a fermé la connexion");
            throw new DiscordRpcException("Discord a fermé la connexion", ex);
        }
        finally { _write.Release(); }
    }

    async Task ReadLoopAsync()
    {
        var header = new byte[8];
        string reason = "Discord a fermé la connexion (Discord quitté ?)";
        try
        {
            while (!_life.IsCancellationRequested)
            {
                await _stream.ReadExactlyAsync(header, _life.Token);
                int op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
                if (length is < 0 or > MaxFrame) { reason = "Trame de Discord illisible"; break; }
                var body = new byte[length];
                await _stream.ReadExactlyAsync(body, _life.Token);
                JsonObject? message;
                try { message = JsonNode.Parse(body) as JsonObject; }
                catch (JsonException) { continue; }
                if (message == null) continue;
                switch (op)
                {
                    case OpPing:
                        await WriteAsync(OpPong, message, _life.Token);
                        break;
                    case OpClose:
                        var error = ErrorOf(message);
                        reason = error.Message;
                        _ready.TrySetException(error);
                        return;
                    case OpFrame:
                        Dispatch(message);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException or ObjectDisposedException or DiscordRpcException) { /* fin du canal */ }
        finally { Close(reason); }
    }

    void Dispatch(JsonObject message)
    {
        var nonce = message["nonce"]?.ToString();
        if (!string.IsNullOrEmpty(nonce) && _pending.TryGetValue(nonce, out var reply)) { reply.TrySetResult(message); return; }
        if (message["cmd"]?.ToString() != "DISPATCH") return;
        var evt = message["evt"]?.ToString() ?? "";
        var data = message["data"] as JsonObject ?? new JsonObject();
        if (evt == "READY") _ready.TrySetResult(data);
        else if (evt == "ERROR") _ready.TrySetException(ErrorOf(data));
        else Event?.Invoke(evt, data);
    }

    void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        var error = new DiscordRpcException(reason);
        _ready.TrySetException(error);
        foreach (var pending in _pending.Values) pending.TrySetException(error);
        Closed?.Invoke(reason);
    }

    /// <summary>Ferme le canal (sans lever l'événement <see cref="Closed"/> s'il n'était pas déjà fermé : c'est une fermeture voulue).</summary>
    public async ValueTask DisposeAsync()
    {
        Closed = null;
        await _life.CancelAsync();
        await _stream.DisposeAsync();
        try { await _reader; } catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { /* déjà fermé */ }
        Close("Connexion fermée");
        _life.Dispose();
        _write.Dispose();
    }
}
