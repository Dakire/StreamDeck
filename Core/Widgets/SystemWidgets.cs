using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Espace disque : pourcentage utilisé d'un lecteur, et ce qu'il reste.</summary>
public sealed partial class DiskWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("disk", "Disque", "Espace utilisé d'un lecteur : pourcentage, espace libre, alerte quand il est presque plein.",
        "Storage24", "#475569", 1, 1, new[] { WidgetSize.One, WidgetSize.Wide }, new[]
        {
            new WidgetOption("drive", "Lecteur", "C:", "Lettre du lecteur (vide : lecteur de Windows)."),
        }, RefreshSeconds: 30);

    /// <summary>Utilisation à partir de laquelle la jauge passe en avertissement, puis en alerte.</summary>
    public const double WarnPercent = 85, DangerPercent = 95;

    readonly Func<string, (long Total, long Free)?> _space;

    /// <summary>Widget branché sur les lecteurs de l'ordinateur.</summary>
    public DiskWidget() : this(RealSpace) { }

    /// <summary>Widget avec une source d'espace disque donnée (tests) : octets au total et libres, ou null si le lecteur n'existe pas.</summary>
    public DiskWidget(Func<string, (long Total, long Free)?> space) { _space = space; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <summary>Lettre du lecteur saisie (« c », « C: », « C:\ ») ramenée à « C: » ; null si le texte n'en est pas une.</summary>
    public static string? NormalizeDrive(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
        return DrivePattern().IsMatch(text) ? char.ToUpperInvariant(text[0]) + ":" : null;
    }

    [GeneratedRegex(@"^[A-Za-z]:?\\?$", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePattern();

    static (long Total, long Free)? RealSpace(string drive)
    {
        try
        {
            var info = new DriveInfo(drive);
            return info.IsReady ? (info.TotalSize, info.AvailableFreeSpace) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var drive = NormalizeDrive(context.Option("drive")) ?? throw new InvalidOperationException("Indique une lettre de lecteur (C:)");
        if (_space(drive) is not { Total: > 0 } space) throw new InvalidOperationException($"Le lecteur {drive} est introuvable ou n'est pas prêt");
        double percent = Math.Clamp((space.Total - space.Free) * 100.0 / space.Total, 0, 100);
        var level = percent >= DangerPercent ? WidgetLevel.Danger : percent >= WarnPercent ? WidgetLevel.Warning : WidgetLevel.Normal;
        return Task.FromResult(new WidgetReading($"{percent.ToString("0", context.Culture)} %",
            $"{drive} · {WidgetFormat.Bytes(space.Free, context.Culture)} libres sur {WidgetFormat.Bytes(space.Total, context.Culture)}", percent, level));
    }
}

/// <summary>Réseau : débit descendant et montant de l'ordinateur (toutes les interfaces actives).</summary>
public sealed class NetworkWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("network", "Réseau", "Débit en réception et en émission de l'ordinateur, en temps réel.",
        "ArrowSwap24", "#0369A1", 1, 1, new[] { WidgetSize.One, WidgetSize.Wide }, null, RefreshSeconds: 2);

    const string StateKey = "network.previous";

    readonly Func<(long Received, long Sent)> _counters;
    readonly Func<long> _ticks;

    /// <summary>Widget branché sur les interfaces réseau de l'ordinateur.</summary>
    public NetworkWidget() : this(RealCounters, System.Diagnostics.Stopwatch.GetTimestamp) { }

    /// <summary>Widget avec des compteurs d'octets et une horloge données (tests).</summary>
    public NetworkWidget(Func<(long Received, long Sent)> counters, Func<long> ticks) { _counters = counters; _ticks = ticks; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    static (long Received, long Sent) RealCounters()
    {
        long received = 0, sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException) { /* interface sans statistiques IPv4 */ }
        }
        return (received, sent);
    }

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var (received, sent) = _counters();
        var now = new Sample(received, sent, _ticks());
        var reading = context.State.TryGetValue(StateKey, out var boxed) && boxed is Sample before
            ? Compute(before, now, context)
            : new WidgetReading("…", "Première mesure");
        context.State[StateKey] = now;
        return Task.FromResult(reading);
    }

    /// <summary>Compteurs d'octets à un instant (ticks de <see cref="System.Diagnostics.Stopwatch"/>).</summary>
    sealed record Sample(long Received, long Sent, long Ticks);

    static WidgetReading Compute(Sample before, Sample after, WidgetContext context)
    {
        double seconds = (after.Ticks - before.Ticks) / (double)System.Diagnostics.Stopwatch.Frequency;
        var down = WidgetFormat.BytesPerSecond(before.Received, after.Received, seconds);
        var up = WidgetFormat.BytesPerSecond(before.Sent, after.Sent, seconds);
        return new WidgetReading("↓ " + WidgetFormat.Rate(down, context.Culture), "↑ " + WidgetFormat.Rate(up, context.Culture));
    }
}

/// <summary>Latence vers des hôtes (ping), pour voir tout de suite si un serveur ou une box répond.</summary>
public sealed class PingWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("ping", "Ping", "Latence vers un ou plusieurs hôtes (adresse IP ou nom), rafraîchie toutes les 5 secondes.",
        "Pulse24", "#047857", 2, 1, new[] { WidgetSize.One, WidgetSize.Wide, WidgetSize.Large }, new[]
        {
            new WidgetOption("hosts", "Hôtes", "1.1.1.1; 8.8.8.8", "Séparés par un point-virgule ou un espace (8 au plus)."),
        }, RefreshSeconds: 5);

    /// <summary>Latence à partir de laquelle un hôte est signalé comme lent.</summary>
    public const long SlowMilliseconds = 150;
    /// <summary>Hôtes testés quand rien n'est saisi.</summary>
    public const string DefaultHosts = "1.1.1.1; 8.8.8.8";

    readonly Func<string, CancellationToken, Task<long?>> _probe;

    /// <summary>Widget qui envoie de vrais paquets ICMP.</summary>
    public PingWidget() : this(RealPingAsync) { }

    /// <summary>Widget avec une sonde donnée (tests) : latence en millisecondes, ou null si l'hôte ne répond pas.</summary>
    public PingWidget(Func<string, CancellationToken, Task<long?>> probe) { _probe = probe; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    static async Task<long?> RealPingAsync(string host, CancellationToken cancellation)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(1500), cancellationToken: cancellation);
            return reply.Status == IPStatus.Success ? Math.Max(1, reply.RoundtripTime) : null;
        }
        catch (PingException) { return null; }   // nom introuvable, réseau coupé
        catch (InvalidOperationException) { return null; }
    }

    /// <inheritdoc/>
    public async Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var hosts = WidgetFormat.SplitList(context.Option("hosts", DefaultHosts));
        if (hosts.Count == 0) throw new InvalidOperationException("Indique au moins un hôte à tester");
        var bad = hosts.FirstOrDefault(h => !WidgetFormat.IsHostName(h));
        if (bad != null) throw new InvalidOperationException($"« {bad} » n'est pas un nom d'hôte ni une adresse IP");

        var results = await Task.WhenAll(hosts.Select(async h => (Host: h, Latency: await _probe(h, cancellation))));
        int up = results.Count(r => r.Latency != null);
        string Line((string Host, long? Latency) r) => r.Latency is { } ms ? $"{r.Host} · {ms.ToString(context.Culture)} ms" : $"{r.Host} · hors ligne";
        var detail = string.Join("\n", results.Select(Line));

        if (results.Length == 1)
        {
            var only = results[0];
            return only.Latency is { } ms
                ? new WidgetReading($"{ms.ToString(context.Culture)} ms", only.Host, null, ms >= SlowMilliseconds ? WidgetLevel.Warning : WidgetLevel.Good)
                : new WidgetReading("Hors ligne", only.Host, null, WidgetLevel.Danger);
        }
        var level = up == 0 ? WidgetLevel.Danger : up < results.Length ? WidgetLevel.Warning
            : results.Any(r => r.Latency >= SlowMilliseconds) ? WidgetLevel.Warning : WidgetLevel.Good;
        return new WidgetReading($"{up}/{results.Length}", detail, null, level);
    }
}
