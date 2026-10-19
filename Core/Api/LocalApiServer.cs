using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck;

/// <summary>
/// Serveur de l'API locale : un canal nommé réservé à l'utilisateur courant (<see cref="PipeOptions.CurrentUserOnly"/>), jamais
/// ouvert au réseau. Chaque connexion envoie des requêtes JSON, une par ligne, et reçoit une réponse par ligne.
/// </summary>
public sealed class LocalApiServer : IDisposable
{
    /// <summary>Taille maximale d'une requête (une action ou une courte liste d'actions).</summary>
    public const int MaxRequestBytes = 64 * 1024;
    const int MaxClients = 8;

    readonly LocalApi _api;
    readonly CancellationTokenSource _stop = new();
    Task? _accept;

    /// <summary>Nom du canal : un par utilisateur Windows, pour que deux sessions ne se mélangent pas.</summary>
    public static string DefaultPipeName => "TouchDeck.Api." + Environment.UserName + "." + InstanceKey();

    /// <summary>Clé courte du dossier de configuration : deux instances qui n'ont pas le même dossier (essais) ne se partagent ni canal ni verrou.</summary>
    public static string InstanceKey() =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ConfigStore.Dir.TrimEnd('\\').ToLowerInvariant())))[..8];

    /// <summary>Serveur de <paramref name="api"/> sur le canal <paramref name="pipeName"/>.</summary>
    public LocalApiServer(LocalApi api, string? pipeName = null)
    {
        _api = api;
        PipeName = pipeName ?? DefaultPipeName;
    }

    /// <summary>Nom du canal écouté.</summary>
    public string PipeName { get; }

    /// <summary>Commence à écouter.</summary>
    public void Start()
    {
        if (_accept != null) return;
        _accept = Task.Run(AcceptLoopAsync);
        Log.Info($"API locale à l'écoute sur le canal {PipeName}");
    }

    async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxClients,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(_stop.Token); }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); return; }
            catch (IOException ex) { Log.Warn($"API locale : connexion refusée : {ex.Message}"); await pipe.DisposeAsync(); continue; }
            _ = Task.Run(() => ServeAsync(pipe), CancellationToken.None);
        }
    }

    async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                while (!_stop.IsCancellationRequested && await ReadLineAsync(reader) is { } line)
                {
                    if (line.Length == 0) continue;
                    await writer.WriteLineAsync(await _api.HandleAsync(line, _stop.Token));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { /* client parti */ }
        }
    }

    // Lit une ligne en refusant les requêtes démesurées
    async Task<string?> ReadLineAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var one = new char[1];
        bool tooBig = false;
        while (await reader.ReadAsync(one.AsMemory(), _stop.Token) == 1)
        {
            if (one[0] == '\n') return tooBig ? "{\"cmd\":\"too-large\"}" : text.ToString().TrimEnd('\r');
            if (text.Length >= MaxRequestBytes) tooBig = true; else text.Append(one[0]);
        }
        return text.Length > 0 ? text.ToString() : null;
    }

    /// <summary>Arrête l'écoute.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        try { _accept?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { /* arrêt */ }
        _stop.Dispose();
    }
}
