using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Météo actuelle d'une ville (Open-Meteo : sans clé d'API ni compte, HTTPS).</summary>
public sealed class WeatherWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("weather", "Météo", "Température et conditions actuelles d'une ville (service Open-Meteo, sans compte).",
        "WeatherSunny24", "#0E7490", 2, 1, new[] { WidgetSize.One, WidgetSize.Wide, WidgetSize.Large }, new[]
        {
            new WidgetOption("city", "Ville", "Lyon", "Le nom de la ville est envoyé à geocoding-api.open-meteo.com pour trouver sa position."),
        }, RefreshSeconds: 900);

    const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    const string PlaceKey = "weather.place";

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <summary>Position et nom officiel d'une ville.</summary>
    /// <param name="Query">Texte saisi (sert à ne pas géocoder deux fois).</param>
    /// <param name="Name">Nom trouvé.</param>
    /// <param name="Latitude">Latitude.</param>
    /// <param name="Longitude">Longitude.</param>
    public sealed record Place(string Query, string Name, double Latitude, double Longitude);

    /// <summary>Premier résultat d'une réponse de géocodage Open-Meteo, ou null si la ville est inconnue.</summary>
    public static Place? ParsePlace(string query, JsonNode? response)
    {
        if (response?["results"] is not JsonArray { Count: > 0 } results || results[0] is not JsonNode first) return null;
        if (!WidgetFormat.TryGetNumber(first, "latitude", out var lat) || !WidgetFormat.TryGetNumber(first, "longitude", out var lon)) return null;
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;
        return new Place(query, first["name"]?.ToString() is { Length: > 0 } name ? name : query, lat, lon);
    }

    /// <summary>Lecture d'une réponse de prévision Open-Meteo (valeurs « current ») ; null si elle est incomplète.</summary>
    public static WidgetReading? ParseCurrent(string city, JsonNode? response, CultureInfo culture)
    {
        var current = response?["current"];
        if (!WidgetFormat.TryGetNumber(current, "temperature_2m", out var temperature)) return null;
        WidgetFormat.TryGetNumber(current, "weather_code", out var code);
        var detail = Describe((int)code);
        if (WidgetFormat.TryGetNumber(current, "wind_speed_10m", out var wind)) detail += $" · vent {wind.ToString("0", culture)} km/h";
        return new WidgetReading($"{temperature.ToString("0", culture)} °C", city + "\n" + detail);
    }

    /// <summary>Texte d'un code météo OMM (WMO) de l'API.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Ciel dégagé",
        1 => "Plutôt dégagé",
        2 => "Partiellement nuageux",
        3 => "Couvert",
        45 or 48 => "Brouillard",
        >= 51 and <= 57 => "Bruine",
        >= 61 and <= 67 => "Pluie",
        >= 71 and <= 77 => "Neige",
        >= 80 and <= 82 => "Averses",
        85 or 86 => "Averses de neige",
        95 => "Orage",
        96 or 99 => "Orage et grêle",
        _ => "Conditions inconnues",
    };

    /// <inheritdoc/>
    public async Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        var city = context.Option("city");
        if (city.Length == 0) throw new InvalidOperationException("Indique une ville dans les propriétés");
        if (city.Length > 80) throw new InvalidOperationException("Nom de ville trop long");

        if (context.State.TryGetValue(PlaceKey, out var cached) && cached is Place known && known.Query == city) { /* position déjà trouvée */ }
        else
        {
            var found = await WidgetFormat.GetJsonAsync(context, new Uri($"{GeocodingUrl}?name={Uri.EscapeDataString(city)}&count=1&language=fr&format=json"), cancellation);
            known = ParsePlace(city, found) ?? throw new InvalidOperationException($"Ville introuvable : « {city} »");
            context.State[PlaceKey] = known;
        }
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{ForecastUrl}?latitude={known.Latitude:0.####}&longitude={known.Longitude:0.####}&current=temperature_2m,weather_code,wind_speed_10m&timezone=auto");
        var forecast = await WidgetFormat.GetJsonAsync(context, new Uri(url), cancellation);
        return ParseCurrent(known.Name, forecast, context.Culture) ?? throw new InvalidOperationException("Réponse météo incomplète");
    }
}

/// <summary>Prochain événement de l'agenda Google : titre en grand, jour et heure dessous.</summary>
public sealed class NextEventWidget : IWidget
{
    /// <summary>Description.</summary>
    public static readonly WidgetInfo Description = new("nextevent", "Prochain événement", "Le prochain événement de ton agenda Google (intégration Google Workspace).",
        "CalendarClock24", "#1A73E8", 2, 1, new[] { WidgetSize.One, WidgetSize.Wide, WidgetSize.Large }, null, RefreshSeconds: 60);

    const string GoogleId = "google";

    /// <inheritdoc/>
    public WidgetInfo Info => Description;

    /// <inheritdoc/>
    public Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation)
    {
        if (context.GetValue($"{GoogleId}.{CalendarFeed.ErrorKey}") is { Length: > 0 } error) throw new InvalidOperationException(error);
        var json = context.GetValue($"{GoogleId}.{CalendarFeed.EventsKey}")
            ?? throw new InvalidOperationException("Connecte Google Workspace dans Paramètres › Intégrations pour voir ton agenda");
        var next = AgendaFormatter.Upcoming(CalendarFeed.Parse(json), context.Now(), 14, 1, context.Culture);
        if (next.Count == 0) return Task.FromResult(new WidgetReading("Rien de prévu", "Aucun événement dans les 14 jours"));
        var line = next[0];
        var when = line.IsNow ? "En cours" : line.Day + " · " + line.When;
        return Task.FromResult(new WidgetReading(line.Title, when, null, line.IsNow ? WidgetLevel.Good : WidgetLevel.Normal));
    }
}
