using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TouchDeck.Tests;

/// <summary>Une requête reçue par le faux serveur.</summary>
sealed record SeenRequest(string Method, string PathAndQuery, string Body, System.Collections.Specialized.NameValueCollection Headers);

/// <summary>Faux serveur HTTP local : la fonction donnée répond à chaque requête, toutes sont consignées.</summary>
sealed class FakeHttpServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly Task _loop;

    public int Port { get; }
    public ConcurrentQueue<SeenRequest> Requests { get; } = new();
    public Func<SeenRequest, (int Status, string Body)> Handler { get; set; } = _ => (404, "");

    public FakeHttpServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _loop = Task.Run(Loop);
    }

    async Task Loop()
    {
        while (true)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            try
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
                var seen = new SeenRequest(context.Request.HttpMethod, context.Request.Url!.PathAndQuery, body, context.Request.Headers);
                Requests.Enqueue(seen);
                var (status, text) = Handler(seen);
                var bytes = Encoding.UTF8.GetBytes(text);
                context.Response.StatusCode = status;
                context.Response.ContentType = text.TrimStart().StartsWith('<') ? "text/xml" : "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException) { /* client parti */ }
        }
    }

    public void Dispose()
    {
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { /* arrêt */ }
    }
}
