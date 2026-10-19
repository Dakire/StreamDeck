using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TouchDeck.Integrations.Obs;

namespace TouchDeck.Tests;

/// <summary>Faux serveur obs-websocket v5 : identification avec défi, requêtes, événements. Aucun OBS n'est nécessaire.</summary>
sealed class FakeObsServer : IDisposable
{
    const string Salt = "sel-de-test", Challenge = "defi-de-test";
    readonly HttpListener _listener = new();
    readonly CancellationTokenSource _stop = new();
    readonly ConcurrentDictionary<WebSocket, byte> _sockets = new();
    readonly Task _accept;

    public int Port { get; }
    public string? Password { get; init; }
    public ConcurrentQueue<string> Requests { get; } = new();
    public string Scene = "Jeu";
    public List<string> Scenes = new() { "Jeu", "Discussion", "Pause" };
    public bool Streaming, Recording, VirtualCam;
    public Dictionary<string, bool> Inputs = new() { ["Micro"] = false, ["Bureau"] = true };
    public Dictionary<string, bool> Items = new() { ["Webcam"] = true };
    public string[] RefuseRequests = Array.Empty<string>();

    public FakeObsServer(string? password = null)
    {
        Password = password;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _accept = Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            if (!context.Request.IsWebSocketRequest) { context.Response.StatusCode = 400; context.Response.Close(); continue; }
            var ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
            _sockets[ws] = 0;
            _ = Task.Run(() => Serve(ws));
        }
    }

    static async Task<JsonNode?> Receive(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray());
    }

    static Task Send(WebSocket ws, JsonObject message) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    async Task Serve(WebSocket ws)
    {
        try
        {
            var hello = new JsonObject { ["obsWebSocketVersion"] = "5.5.0", ["rpcVersion"] = 1 };
            if (Password != null) hello["authentication"] = new JsonObject { ["challenge"] = Challenge, ["salt"] = Salt };
            await Send(ws, new JsonObject { ["op"] = 0, ["d"] = hello });

            var identify = await Receive(ws, _stop.Token);
            if (identify?["op"]?.GetValue<int>() != 1) { ws.Abort(); return; }
            if (Password != null && identify["d"]?["authentication"]?.GetValue<string>() != ObsClient.Authenticate(Password, Salt, Challenge))
            {
                await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed", CancellationToken.None);
                return;
            }
            await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });

            while (ws.State == WebSocketState.Open)
            {
                var message = await Receive(ws, _stop.Token);
                if (message == null) break;
                if (message["op"]?.GetValue<int>() != 6) continue;
                var d = message["d"]!;
                var type = d["requestType"]!.GetValue<string>();
                Requests.Enqueue(type + (d["requestData"] != null ? " " + d["requestData"]!.ToJsonString() : ""));
                var (ok, code, comment, data) = RefuseRequests.Contains(type) ? (false, 600, "refusé par le faux OBS", null) : Handle(type, d["requestData"]?.AsObject());
                var status = new JsonObject { ["result"] = ok, ["code"] = code };
                if (comment != null) status["comment"] = comment;
                var response = new JsonObject { ["requestType"] = type, ["requestId"] = d["requestId"]!.GetValue<string>(), ["requestStatus"] = status };
                if (data != null) response["responseData"] = data;
                await Send(ws, new JsonObject { ["op"] = 7, ["d"] = response });
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { /* client parti */ }
        finally { _sockets.TryRemove(ws, out _); }
    }

    (bool Ok, int Code, string? Comment, JsonObject? Data) Handle(string type, JsonObject? data)
    {
        JsonObject Ok(JsonObject? d = null) => d ?? new JsonObject();
        switch (type)
        {
            case "GetSceneList":
                return (true, 100, null, new JsonObject
                {
                    ["currentProgramSceneName"] = Scene,
                    ["scenes"] = new JsonArray(Scenes.Select(s => (JsonNode)new JsonObject { ["sceneName"] = s }).ToArray()),
                });
            case "SetCurrentProgramScene":
            {
                var name = data!["sceneName"]!.GetValue<string>();
                if (!Scenes.Contains(name)) return (false, 600, "Scène introuvable", null);
                Scene = name;
                _ = Emit("CurrentProgramSceneChanged", new JsonObject { ["sceneName"] = name });
                return (true, 100, null, null);
            }
            case "GetStreamStatus": return (true, 100, null, new JsonObject { ["outputActive"] = Streaming });
            case "GetRecordStatus": return (true, 100, null, new JsonObject { ["outputActive"] = Recording });
            case "GetVirtualCamStatus": return (true, 100, null, new JsonObject { ["outputActive"] = VirtualCam });
            case "ToggleStream": Streaming = !Streaming; _ = Emit("StreamStateChanged", new JsonObject { ["outputActive"] = Streaming }); return (true, 100, null, new JsonObject { ["outputActive"] = Streaming });
            case "ToggleRecord": Recording = !Recording; _ = Emit("RecordStateChanged", new JsonObject { ["outputActive"] = Recording }); return (true, 100, null, new JsonObject { ["outputActive"] = Recording });
            case "ToggleVirtualCam": VirtualCam = !VirtualCam; return (true, 100, null, new JsonObject { ["outputActive"] = VirtualCam });
            case "SaveReplayBuffer": return (true, 100, null, null);
            case "GetInputList": return (true, 100, null, new JsonObject { ["inputs"] = new JsonArray(Inputs.Keys.Append("Image").Select(i => (JsonNode)new JsonObject { ["inputName"] = i }).ToArray()) });
            case "GetInputMute":
            {
                var name = data!["inputName"]!.GetValue<string>();
                return Inputs.TryGetValue(name, out var muted) ? (true, 100, null, new JsonObject { ["inputMuted"] = muted }) : (false, 604, "Cette source n'a pas d'audio", null);
            }
            case "ToggleInputMute":
            {
                var name = data!["inputName"]!.GetValue<string>();
                if (!Inputs.TryGetValue(name, out var current)) return (false, 600, "Source introuvable", null);
                Inputs[name] = !current;
                _ = Emit("InputMuteStateChanged", new JsonObject { ["inputName"] = name, ["inputMuted"] = !current });
                return (true, 100, null, new JsonObject { ["inputMuted"] = !current });
            }
            case "GetSceneItemId":
                return Items.ContainsKey(data!["sourceName"]!.GetValue<string>()) ? (true, 100, null, new JsonObject { ["sceneItemId"] = 7 }) : (false, 600, "Source introuvable", null);
            case "GetSceneItemEnabled": return (true, 100, null, new JsonObject { ["sceneItemEnabled"] = Items.Values.First() });
            case "SetSceneItemEnabled": Items[Items.Keys.First()] = data!["sceneItemEnabled"]!.GetValue<bool>(); return (true, 100, null, null);
            case "GetStats": return (true, 100, null, new JsonObject { ["activeFps"] = 59.94, ["cpuUsage"] = 3.2 });
            default: return (false, 204, "Requête inconnue : " + type, Ok());
        }
    }

    public async Task Emit(string type, JsonObject data)
    {
        var message = new JsonObject { ["op"] = 5, ["d"] = new JsonObject { ["eventType"] = type, ["eventIntent"] = 1, ["eventData"] = data } };
        foreach (var ws in _sockets.Keys.ToArray())
        {
            try { if (ws.State == WebSocketState.Open) await Send(ws, message); }
            catch (WebSocketException) { /* client parti */ }
        }
    }

    /// <summary>Coupe brutalement toutes les connexions (OBS qui se ferme).</summary>
    public void DropConnections()
    {
        foreach (var ws in _sockets.Keys.ToArray()) ws.Abort();
    }

    public void Dispose()
    {
        _stop.Cancel();
        DropConnections();
        _listener.Close();
        try { _accept.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { /* arrêt */ }
    }
}
