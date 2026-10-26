using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Une cible à surveiller : une page web (http, https) ou un port TCP (hôte:port), avec un nom facultatif.</summary>
/// <param name="Name">Nom affiché (« NAS ») ; par défaut la cible elle-même.</param>
/// <param name="Uri">Adresse pour une cible web, sinon null.</param>
/// <param name="Host">Hôte pour une cible TCP.</param>
/// <param name="Port">Port pour une cible TCP.</param>
public sealed record ServiceTarget(string Name, Uri? Uri, string Host, int Port)
{
    /// <summary>true pour une page web.</summary>
    public bool IsWeb => Uri != null;

    /// <summary>
    /// Lit « https://intranet/sante », « nas.local:445 » ou « NAS=nas.local:445 ». Lève <see cref="FormatException"/> avec un message affichable
    /// si la cible n'est ni une adresse http(s) ni un hôte suivi d'un port.
    /// </summary>
    public static ServiceTarget Parse(string item)
    {
        var text = item.Trim();
        string? name = null;
        int equals = text.IndexOf('=');
        if (equals > 0 && !text[..equals].Contains("://", StringComparison.Ordinal) && !text[..equals].Contains('/'))
        {
            name = text[..equals].Trim();
            text = text[(equals + 1)..].Trim();
        }
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!WidgetTypes.IsWebAddress(text, out var uri)) throw new FormatException($"Adresse non valide : « {Short(text)} »");
            return new ServiceTarget(name ?? uri.Host, uri, uri.Host, uri.Port);
        }
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            && WidgetFormat.IsHostName(text[..colon]))
            return new ServiceTarget(name ?? text, null, text[..colon], port);
        throw new FormatException($"« {Short(text)} » : indique une adresse http(s) ou hôte:port (nas.local:445)");
    }

    static string Short(string text) => text.Length <= 60 ? text : text[..57] + "…";
}

/// <summary>État de services et de serveurs : pages web (HTTP) et ports TCP, en ligne ou hors ligne.</summary>
public sealed class ServiceStatusWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("status", "État des services", "Vérifie des pages web (HTTP) ou des ports (RDP, SSH, NAS…) : en ligne ou hors ligne.",
        "ShieldCheckmark24", "#B45309", 2, 1, new[] { WidgetSize.One, WidgetSize.Wide, WidgetSize.Large }, new[]
        {
            new WidgetOption("targets", "Cibles", "https://intranet.local; NAS=nas.local:445", "Adresse http(s) ou hôte:port, séparées par un point-virgule ; « Nom=cible » pour nommer (8 au plus). Une page est en ligne si elle répond avec un code inférieur à 500."),
        }, RefreshSeconds: 30);

    /// <summary>Délai d'une vérification.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(4);

    readonly Func<ServiceTarget, WidgetContext, CancellationToken, Task<bool>> _check;

    /// <summary>Widget qui contacte vraiment les cibles.</summary>
    public ServiceStatusWidget() : this(RealCheckAsync) { }

    /// <summary>Widget avec une vérification donnée (tests) : true si la cible répond.</summary>
    public ServiceStatusWidget(Func<ServiceTarget, WidgetContext, CancellationToken, Task<bool>> check) { _check = check; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    static async Task<bool> RealCheckAsync(ServiceTarget target, WidgetContext context, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            if (target.IsWeb)
            {
                using var response = await context.Http.GetAsync(target.Uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return (int)response.StatusCode < 500;
            }
            using var client = new TcpClient();
            await client.ConnectAsync(target.Host, target.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or OperationCanceledException or InvalidOperationException or System.IO.IOException)
        {
            cancellation.ThrowIfCancellationRequested();   // une page quittée n'est pas un service hors ligne
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var items = WidgetFormat.SplitList(context.Option("targets").Replace(" =", "=", StringComparison.Ordinal).Replace("= ", "=", StringComparison.Ordinal));
        if (items.Count == 0) throw new InvalidOperationException("Indique au moins une cible (https://… ou hôte:port)");
        List<ServiceTarget> targets;
        try { targets = items.Select(ServiceTarget.Parse).ToList(); }
        catch (FormatException ex) { throw new InvalidOperationException(ex.Message, ex); }

        var results = await Task.WhenAll(targets.Select(async t => (Target: t, Up: await _check(t, context, cancellation))));
        int up = results.Count(r => r.Up);
        var down = results.Where(r => !r.Up).Select(r => r.Target.Name).ToList();
        var level = up == results.Length ? WidgetLevel.Good : up == 0 ? WidgetLevel.Danger : WidgetLevel.Warning;
        var detail = down.Count == 0 ? "Tout est en ligne" : string.Join("\n", down.Select(n => n + " · hors ligne"));
        return new WidgetReading($"{up}/{results.Length}", detail, null, level);
    }
}

/// <summary>Un nombre lu dans une réponse JSON (compteur de tickets ouverts, files d'attente, alertes) : page web ou API sans authentification.</summary>
public sealed class CounterWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("counter", "Compteur", "Affiche un nombre lu dans une réponse JSON : tickets ouverts, alertes, file d'attente. Adresse sans authentification.",
        "NumberSymbol24", "#9F1239", 1, 1, new[] { WidgetSize.One, WidgetSize.Wide }, new[]
        {
            new WidgetOption("url", "Adresse de l'API", "https://", "Réponse JSON en GET, sans identifiant (réseau local, ou adresse dédiée). Aucun jeton n'est enregistré."),
            new WidgetOption("path", "Chemin du nombre", "total", "Propriétés séparées par un point : data.open_tickets, items.0.count."),
            new WidgetOption("label", "Libellé", "tickets ouverts", "Texte sous le nombre."),
            new WidgetOption("warn", "Alerte à partir de", "10", "Le nombre passe en alerte à partir de cette valeur (vide : jamais)."),
        }, RefreshSeconds: 120);

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public async Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        if (!WidgetTypes.IsWebAddress(context.Option("url"), out var uri)) throw new InvalidOperationException("Renseigne l'adresse de l'API (https://…)");
        var path = context.Option("path");
        if (path.Length == 0) throw new InvalidOperationException("Indique le chemin du nombre dans la réponse (data.total)");
        JsonNode? json = await WidgetFormat.GetJsonAsync(context, uri, cancellation);
        if (!WidgetFormat.TryGetNumber(json, path, out var number)) throw new InvalidOperationException($"Aucun nombre à « {path} » dans la réponse");
        bool warn = double.TryParse(context.Option("warn"), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) && number >= threshold;
        return new WidgetReading(number.ToString("#,##0.##", context.Culture), context.Option("label"), null, warn ? WidgetLevel.Danger : WidgetLevel.Normal);
    }
}
