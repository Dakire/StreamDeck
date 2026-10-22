using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TouchDeck;

/// <summary>Un événement d'agenda, tel que le widget l'affiche.</summary>
/// <param name="Start">Début (heure locale pour un événement horaire, minuit local du premier jour pour une journée entière).</param>
/// <param name="End">Fin (exclusive pour une journée entière : minuit local du lendemain du dernier jour).</param>
/// <param name="AllDay">true pour un événement sur la journée entière.</param>
/// <param name="Title">Titre.</param>
public sealed record CalendarEvent(DateTime Start, DateTime End, bool AllDay, string Title);

/// <summary>
/// Passage entre l'API Google Agenda, la valeur d'état d'intégration (texte JSON compact) et la liste affichée par le widget.
/// Pur : aucun accès réseau ni WPF, testé avec de faux échanges.
/// </summary>
public static class CalendarFeed
{
    /// <summary>Clé de la valeur d'état de l'intégration Google qui porte les événements (JSON).</summary>
    public const string EventsKey = "calendar.events";
    /// <summary>Clé de la valeur d'état qui porte le dernier message d'erreur de lecture de l'agenda, absente quand tout va bien.</summary>
    public const string ErrorKey = "calendar.error";

    /// <summary>Événements d'une réponse <c>events.list</c> de Google Agenda ; les événements annulés et sans date sont ignorés.</summary>
    public static IReadOnlyList<CalendarEvent> FromGoogle(JsonNode? response)
    {
        var events = new List<CalendarEvent>();
        if (response?["items"] is not JsonArray items) return events;
        foreach (var item in items)
        {
            if (item == null || item["status"]?.ToString() == "cancelled") continue;
            var start = item["start"];
            var end = item["end"];
            var title = item["summary"]?.ToString() is { Length: > 0 } s ? s : "(sans titre)";
            if (start?["dateTime"]?.ToString() is { } startText && DateTimeOffset.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startAt))
            {
                var endAt = end?["dateTime"]?.ToString() is { } endText && DateTimeOffset.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : startAt;
                events.Add(new CalendarEvent(startAt.LocalDateTime, endAt.LocalDateTime, false, title));
            }
            else if (start?["date"]?.ToString() is { } day && DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDay))
            {
                var last = end?["date"]?.ToString() is { } lastText && DateTime.TryParseExact(lastText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exclusive)
                    ? exclusive : firstDay.AddDays(1);
                events.Add(new CalendarEvent(firstDay, last, true, title));
            }
        }
        return events.OrderBy(e => e.Start).ToList();
    }

    /// <summary>Texte JSON compact des événements (valeur d'état de l'intégration).</summary>
    public static string Serialize(IEnumerable<CalendarEvent> events) =>
        new JsonArray(events.Select(e => (JsonNode?)new JsonObject
        {
            ["s"] = e.Start.ToString("s", CultureInfo.InvariantCulture),
            ["e"] = e.End.ToString("s", CultureInfo.InvariantCulture),
            ["d"] = e.AllDay,
            ["t"] = e.Title,
        }).ToArray()).ToJsonString();

    /// <summary>Relit <see cref="Serialize"/> ; un texte illisible donne une liste vide.</summary>
    public static IReadOnlyList<CalendarEvent> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<CalendarEvent>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray array) return Array.Empty<CalendarEvent>();
            var list = new List<CalendarEvent>();
            foreach (var n in array)
            {
                if (n?["s"]?.ToString() is not { } s || n["e"]?.ToString() is not { } e) continue;
                if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !DateTime.TryParse(e, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) continue;
                list.Add(new CalendarEvent(start, end, n["d"]?.GetValue<bool>() ?? false, n["t"]?.ToString() ?? ""));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return Array.Empty<CalendarEvent>(); }
    }
}

/// <summary>Ligne de l'agenda affichée : jour (si nouveau), heure ou « Journée », titre, en cours ou non.</summary>
/// <param name="Day">Libellé du jour (« Aujourd'hui », « Demain », « Lundi 26 »), vide si le même que la ligne précédente.</param>
/// <param name="When">Heure de début (« 14:30 ») ou « Journée ».</param>
/// <param name="Title">Titre.</param>
/// <param name="IsNow">true si l'événement est en cours.</param>
public sealed record AgendaLine(string Day, string When, string Title, bool IsNow);

/// <summary>Choix et mise en forme des prochains événements du widget agenda.</summary>
public static class AgendaFormatter
{
    /// <summary>
    /// Prochains événements : ceux qui ne sont pas finis et qui commencent dans les <paramref name="days"/> jours (aujourd'hui compris),
    /// dans l'ordre, au plus <paramref name="count"/>. Le jour n'est répété que quand il change.
    /// </summary>
    public static IReadOnlyList<AgendaLine> Upcoming(IEnumerable<CalendarEvent> events, DateTime now, int days, int count, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var limit = now.Date.AddDays(Math.Max(1, days));
        var lines = new List<AgendaLine>();
        string lastDay = "";
        foreach (var e in events.Where(e => e.End > now && e.Start < limit).OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCulture).Take(Math.Max(1, count)))
        {
            var shownDay = e.Start < now.Date ? now.Date : e.Start.Date;   // un événement commencé avant aujourd'hui compte pour aujourd'hui
            var label = DayLabel(shownDay, now, culture);
            lines.Add(new AgendaLine(label == lastDay ? "" : label, e.AllDay ? "Journée" : e.Start.ToString("HH:mm", culture), e.Title, !e.AllDay && e.Start <= now && e.End > now));
            lastDay = label;
        }
        return lines;
    }

    /// <summary>« Aujourd'hui », « Demain » ou « Lundi 26 ».</summary>
    public static string DayLabel(DateTime day, DateTime now, CultureInfo culture)
    {
        if (day.Date == now.Date) return "Aujourd'hui";
        if (day.Date == now.Date.AddDays(1)) return "Demain";
        var text = day.ToString("dddd d", culture);
        return text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];
    }
}
