using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck;

/// <summary>Client de l'API locale : envoie une requête et lit la réponse. Sert à la seconde instance de l'application et aux scripts.</summary>
public static class LocalApiClient
{
    /// <summary>Envoie <paramref name="requestJson"/> au canal et retourne la réponse. Lève <see cref="TimeoutException"/> si personne n'écoute.</summary>
    public static async Task<string> SendAsync(string pipeName, string requestJson, TimeSpan timeout, CancellationToken cancellation = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(limit.Token);
            var bytes = Encoding.UTF8.GetBytes(requestJson.Replace("\r", "").Replace("\n", " ") + "\n");
            await pipe.WriteAsync(bytes, limit.Token);
            await pipe.FlushAsync(limit.Token);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            return await reader.ReadLineAsync(limit.Token) ?? throw new IOException("L'application a fermé la connexion");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("TouchDeck ne répond pas sur l'API locale");
        }
    }
}
