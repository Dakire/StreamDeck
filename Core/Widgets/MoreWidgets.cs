using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using StreamDak.Actions;

namespace StreamDak;

/// <summary>État de la batterie lu par la plateforme.</summary>
/// <param name="HasBattery">false sur un ordinateur de bureau (aucune batterie).</param>
/// <param name="Percent">Charge de 0 à 100, ou null si Windows ne la connaît pas.</param>
/// <param name="Charging">true sur secteur.</param>
/// <param name="Remaining">Autonomie estimée, ou null (secteur, estimation en cours).</param>
public sealed record BatteryState(bool HasBattery, int? Percent, bool Charging, TimeSpan? Remaining);

/// <summary>Batterie : charge, secteur ou batterie, autonomie estimée. Sur un ordinateur sans batterie, la tuile le dit.</summary>
public sealed class BatteryWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("battery", "Batterie", "Charge de la batterie, secteur ou batterie, autonomie estimée (portable, tablette).",
        "Battery1024", "#15803D", 1, 1, new[] { WidgetSize.One, WidgetSize.Wide }, Array.Empty<WidgetOption>(), RefreshSeconds: 30);

    /// <summary>Charge à partir de laquelle la jauge passe en avertissement, puis en alerte (sur batterie).</summary>
    public const int WarnPercent = 25, DangerPercent = 10;

    readonly Func<BatteryState?> _read;

    /// <summary>Widget branché sur la lecture de la plateforme (null : lecture impossible).</summary>
    public BatteryWidget(Func<BatteryState?> read) { _read = read; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var state = _read() ?? throw new InvalidOperationException("État de l'alimentation illisible");
        if (!state.HasBattery) return Task.FromResult(new WidgetReading("Secteur", "Aucune batterie sur cet ordinateur"));
        if (state.Percent is not { } percent) return Task.FromResult(new WidgetReading("?", state.Charging ? "Sur secteur" : "Sur batterie"));
        var detail = state.Charging ? "En charge, sur secteur"
            : state.Remaining is { } left ? $"Sur batterie · {WidgetFormat.Duration(left, context.Culture)} restantes" : "Sur batterie";
        var level = state.Charging ? WidgetLevel.Good : percent <= DangerPercent ? WidgetLevel.Danger : percent <= WarnPercent ? WidgetLevel.Warning : WidgetLevel.Normal;
        return Task.FromResult(new WidgetReading($"{percent} %", detail, percent, level));
    }
}

/// <summary>Durée de fonctionnement de Windows depuis le dernier démarrage (redémarrage oublié, poste de travail ou serveur).</summary>
public sealed class UptimeWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("uptime", "Durée de fonctionnement", "Temps écoulé depuis le démarrage de Windows, et la date du démarrage.",
        "Timer24", "#475569", 1, 1, new[] { WidgetSize.One, WidgetSize.Wide }, Array.Empty<WidgetOption>(), RefreshSeconds: 60);

    readonly Func<TimeSpan> _uptime;

    /// <summary>Widget branché sur l'horloge système.</summary>
    public UptimeWidget() : this(() => TimeSpan.FromMilliseconds(Environment.TickCount64)) { }

    /// <summary>Widget avec une durée donnée (tests).</summary>
    public UptimeWidget(Func<TimeSpan> uptime) { _uptime = uptime; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var up = _uptime();
        var since = context.Now() - up;
        // Plus de 14 jours sans redémarrage : les mises à jour de Windows attendent sans doute
        var level = up > TimeSpan.FromDays(14) ? WidgetLevel.Warning : WidgetLevel.Normal;
        return Task.FromResult(new WidgetReading(WidgetFormat.Duration(up, context.Culture), "Démarré " + since.ToString("ddd d MMM à HH:mm", context.Culture), null, level));
    }
}

/// <summary>Périphériques audio actifs : la sortie et le micro par défaut de Windows, leur niveau et leur coupure.</summary>
public sealed class AudioDevicesWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("audiodevices", "Périphériques audio actifs", "La sortie et le micro par défaut de Windows, avec leur volume et leur coupure.",
        "Headphones24", "#C2410C", 2, 1, new[] { WidgetSize.Wide, WidgetSize.Large }, Array.Empty<WidgetOption>(), RefreshSeconds: 3);

    readonly IAudioDevices _audio;

    /// <summary>Widget branché sur les périphériques de la plateforme.</summary>
    public AudioDevicesWidget(IAudioDevices audio) { _audio = audio; }

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        IReadOnlyList<AudioEndpoint> endpoints;
        try { endpoints = _audio.Endpoints(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { throw new InvalidOperationException("Périphériques audio illisibles", ex); }
        var output = endpoints.FirstOrDefault(e => e.Flow == AudioFlow.Render && e.IsDefault);
        var input = endpoints.FirstOrDefault(e => e.Flow == AudioFlow.Capture && e.IsDefault);
        if (output == null && input == null) throw new InvalidOperationException("Aucun périphérique audio par défaut");
        string Line(AudioEndpoint? e, string what)
        {
            if (e == null) return $"{what} : aucun";
            try
            {
                var s = _audio.ReadEndpoint(e.Id);
                return $"{what} : {e.Name} · {(s.Muted ? "coupé" : s.Percent + " %")}";
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return $"{what} : {e.Name}"; }
        }
        bool micMuted = input != null && Safe(() => _audio.ReadEndpoint(input.Id).Muted);
        var headline = output != null ? Short(output.Name) : "Aucune sortie";
        return Task.FromResult(new WidgetReading(headline, Line(output, "Sortie") + "\n" + Line(input, "Micro"), null, micMuted ? WidgetLevel.Warning : WidgetLevel.Normal));
    }

    static bool Safe(Func<bool> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; }
    }

    /// <summary>« Haut-parleurs (Realtek(R) Audio) » → « Realtek(R) Audio » : la partie entre parenthèses désigne l'appareil.</summary>
    static string Short(string name)
    {
        int open = name.IndexOf('('), close = name.LastIndexOf(')');
        return open >= 0 && close > open + 1 ? name[(open + 1)..close] : name;
    }
}

/// <summary>Lecture en cours (Spotify, navigateur, lecteur Windows…), lue par l'intégration « Système Windows » (contrôles multimédias de Windows).</summary>
public sealed class NowPlayingWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("nowplaying", "Lecture en cours", "Titre et artiste du média en cours (Spotify, navigateur, lecteur…), sans compte ni réglage.",
        "MusicNote224", "#047857", 2, 1, new[] { WidgetSize.Wide, WidgetSize.Large }, Array.Empty<WidgetOption>(), RefreshSeconds: 2);

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var title = context.GetValue(SystemMeter.IntegrationId + ".media.title");
        if (title == null) throw new InvalidOperationException("Active « Système Windows » dans Paramètres › Intégrations");
        if (title.Length == 0) return Task.FromResult(new WidgetReading("Rien en lecture", ""));
        var artist = context.GetValue(SystemMeter.IntegrationId + ".media.artist") ?? "";
        var app = context.GetValue(SystemMeter.IntegrationId + ".media.app") ?? "";
        bool playing = context.GetValue(SystemMeter.IntegrationId + ".media.playing") == "true";
        var detail = string.Join(" · ", new[] { artist, playing ? "" : "en pause", app }.Where(s => s.Length > 0));
        return Task.FromResult(new WidgetReading(title, detail, null, playing ? WidgetLevel.Good : WidgetLevel.Normal));
    }
}

/// <summary>Flux RSS ou Atom : les derniers titres d'un site d'actualités, d'un blog, d'un suivi d'incidents.</summary>
public sealed class FeedWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("feed", "Flux d'actualités", "Les derniers titres d'un flux RSS ou Atom (actualités, blog, état d'un service en ligne).",
        "Rss24", "#B45309", 2, 2, new[] { WidgetSize.Wide, WidgetSize.Large }, new[]
        {
            new WidgetOption("url", "Adresse du flux", "https://", "Flux RSS ou Atom public (http ou https)."),
            new WidgetOption("count", "Nombre de titres", "4", "De 1 à 10."),
        }, RefreshSeconds: 900);

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public async Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        if (!WidgetTypes.IsWebAddress(context.Option("url"), out var uri)) throw new InvalidOperationException("Renseigne l'adresse du flux (https://…)");
        int count = int.TryParse(context.Option("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, 10) : 4;
        var bytes = await WidgetFormat.GetBytesAsync(context, uri, cancellation, 2 * 1024 * 1024);   // un flux pèse plus qu'une réponse JSON
        var (feedTitle, items) = Parse(bytes, count);
        if (items.Count == 0) throw new InvalidOperationException("Le flux ne contient aucun article");
        return new WidgetReading(items[0], string.Join("\n", items.Skip(1)) + (feedTitle.Length > 0 ? (items.Count > 1 ? "\n" : "") + "— " + feedTitle : ""));
    }

    /// <summary>
    /// Titres d'un flux RSS (item/title) ou Atom (entry/title). Lecture sûre : aucune DTD, aucune ressource externe, taille déjà bornée par le
    /// téléchargement. Lève <see cref="InvalidOperationException"/> si le document n'est pas un flux lisible.
    /// </summary>
    public static (string FeedTitle, List<string> Items) Parse(byte[] xml, int count)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true, MaxCharactersInDocument = 4_000_000 };
        var items = new List<string>();
        string feedTitle = "";
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml), settings);
            bool inItem = false;   // le premier titre d'un article est le sien (les suivants : média, catégorie…)
            reader.MoveToContent();
            while (!reader.EOF && items.Count < count)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "item" or "entry") { inItem = true; reader.Read(); continue; }
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "title")
                {
                    var text = Clean(reader.ReadElementContentAsString());   // avance après la balise fermante : pas de Read() en plus
                    if (inItem) { if (text.Length > 0) items.Add(text); inItem = false; }
                    else if (feedTitle.Length == 0) feedTitle = text;
                    continue;
                }
                reader.Read();
            }
        }
        catch (XmlException ex) { throw new InvalidOperationException("Ce n'est pas un flux RSS ou Atom lisible", ex); }
        return (feedTitle, items);
    }

    static string Clean(string text)
    {
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 140 ? t : t[..137] + "…";
    }
}
