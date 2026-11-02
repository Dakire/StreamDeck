using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Actions;

/// <summary>
/// Requête web (webhook) : GET sans corps, POST si un corps est indiqué (JSON s'il commence par { ou [). Méthode (<see cref="DeckAction.Method"/>) et en-têtes
/// (<see cref="DeckAction.Headers"/>) facultatifs.
/// </summary>
public sealed class HttpAction : ActionHandler
{
    /// <summary>Type <see cref="ActionTypes.Http"/>.</summary>
    public HttpAction() : base(ActionTypes.Http, ActionTimeouts.Http) { }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(action.Value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("L'adresse doit commencer par http:// ou https://");
        if (!HttpHeaderSpec.IsValidMethod(action.Method)) throw new InvalidOperationException("Méthode non acceptée : GET, POST, PUT, PATCH ou DELETE");
        if (!HttpHeaderSpec.TryParse(action.Headers, out var headers, out var why)) throw new InvalidOperationException(why);
        var method = string.IsNullOrWhiteSpace(action.Method) ? (string.IsNullOrEmpty(action.Args) ? HttpMethod.Get : HttpMethod.Post) : new HttpMethod(action.Method.Trim().ToUpperInvariant());
        using var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrEmpty(action.Args))
        {
            var body = action.Args.TrimStart();
            var type = body.StartsWith('{') || body.StartsWith('[') ? "application/json" : "text/plain";
            request.Content = new StringContent(action.Args, Encoding.UTF8, type);
        }
        foreach (var (name, value) in headers)
        {
            // Content-Type remplace celui qui est deviné ; les autres en-têtes « Content-* » vont avec le corps, les autres avec la requête
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content != null)
            {
                request.Content.Headers.Remove("Content-Type");
                request.Content.Headers.TryAddWithoutValidation("Content-Type", value);
            }
            else if (!request.Headers.TryAddWithoutValidation(name, value) && request.Content != null)
                request.Content.Headers.TryAddWithoutValidation(name, value);
        }
        using var response = await context.Services.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Le serveur a répondu {(int)response.StatusCode} {response.ReasonPhrase}");
        return null;
    }
}