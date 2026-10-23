using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Actions;

/// <summary>Requête web (webhook) : GET sans corps, POST si un corps est indiqué (JSON s'il commence par { ou [).</summary>
public sealed class HttpAction : ActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Http"/>.</summary>
    public HttpAction() : base(ActionTypes.Http, ActionTimeouts.Http) { }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(action.Value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("L'adresse doit commencer par http:// ou https://");
        using var request = new HttpRequestMessage(string.IsNullOrEmpty(action.Args) ? HttpMethod.Get : HttpMethod.Post, uri);
        if (!string.IsNullOrEmpty(action.Args))
        {
            var body = action.Args.TrimStart();
            var type = body.StartsWith('{') || body.StartsWith('[') ? "application/json" : "text/plain";
            request.Content = new StringContent(action.Args, Encoding.UTF8, type);
        }
        using var response = await context.Services.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Le serveur a répondu {(int)response.StatusCode} {response.ReasonPhrase}");
        return null;
    }
}