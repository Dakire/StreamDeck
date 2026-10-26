using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak;

/// <summary>Emprise d'un widget en cases (largeur × hauteur).</summary>
/// <param name="Cols">Largeur en cases.</param>
/// <param name="Rows">Hauteur en cases.</param>
public readonly record struct WidgetSize(int Cols, int Rows)
{
    /// <summary>Une case.</summary>
    public static readonly WidgetSize One = new(1, 1);
    /// <summary>Deux cases de large, une de haut.</summary>
    public static readonly WidgetSize Wide = new(2, 1);
    /// <summary>Deux cases de large, deux de haut.</summary>
    public static readonly WidgetSize Large = new(2, 2);

    /// <summary>« 2 × 1 ».</summary>
    public override string ToString() => $"{Cols} × {Rows}";
}

/// <summary>Gravité de ce que le widget affiche : elle colore la jauge et la valeur.</summary>
public enum WidgetLevel
{
    /// <summary>Information neutre.</summary>
    Normal,
    /// <summary>Tout va bien (service en ligne, latence basse).</summary>
    Good,
    /// <summary>À surveiller (disque presque plein, service partiellement en ligne).</summary>
    Warning,
    /// <summary>Problème (hôte injoignable, disque plein).</summary>
    Danger,
}

/// <summary>Ce qu'un widget de données affiche après un relevé.</summary>
/// <param name="Headline">Valeur en grand (« 23 ms », « 62 % », « 18 °C »).</param>
/// <param name="Detail">Précisions dessous, une ligne par information (peut être vide).</param>
/// <param name="Percent">Remplissage de la jauge (0 à 100), ou null pour ne pas en afficher.</param>
/// <param name="Level">Gravité : couleur de la jauge et de la valeur.</param>
public sealed record WidgetReading(string Headline, string Detail = "", double? Percent = null, WidgetLevel Level = WidgetLevel.Normal);

/// <summary>Un réglage texte d'un widget, saisi dans le panneau de propriétés et enregistré dans <see cref="DeckButton.WidgetOptions"/>.</summary>
/// <param name="Key">Clé stable du réglage.</param>
/// <param name="Label">Libellé affiché.</param>
/// <param name="Placeholder">Exemple grisé dans le champ vide (valeur utilisée par défaut, le cas échéant).</param>
/// <param name="Hint">Explication sous le champ.</param>
public sealed record WidgetOption(string Key, string Label, string Placeholder = "", string Hint = "");

/// <summary>Ce qu'un relevé reçoit : les réglages du bouton, un espace d'état propre à la tuile, le réseau et les valeurs des intégrations.</summary>
public sealed class WidgetContext
{
    /// <summary>Réglages du bouton (clé → valeur), copie de <see cref="DeckButton.WidgetOptions"/>.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    /// <summary>Mémoire d'une tuile entre deux relevés (valeurs précédentes d'un compteur, position géographique trouvée…) : jamais partagée entre tuiles.</summary>
    public IDictionary<string, object> State { get; } = new Dictionary<string, object>();

    /// <summary>Client HTTP partagé de l'application (sans délai propre : se limiter avec le jeton d'annulation).</summary>
    public HttpClient Http { get; init; } = null!;

    /// <summary>Valeur publiée par une intégration (« google.calendar.events »), ou null.</summary>
    public Func<string, string?> GetValue { get; init; } = _ => null;

    /// <summary>Heure courante (remplaçable dans les tests).</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    /// <summary>Langue des nombres et des dates affichés.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>Réglage lu, ou <paramref name="fallback"/> s'il est absent ou vide.</summary>
    public string Option(string key, string fallback = "") =>
        Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
}

/// <summary>
/// Un widget de données : un bouton qui affiche une information relevée à intervalle régulier (latence, météo, état de services…).
/// Ajouter un widget : une classe qui implémente cette interface, sa description (<see cref="WidgetInfo"/>) dans <see cref="WidgetTypes.All"/>
/// et une ligne dans <c>AppHost.Build</c>. Les relevés ne tournent que pour les tuiles de la page affichée, et se suspendent quand le deck est masqué.
/// </summary>
public interface IWidget
{
    /// <summary>Description : identifiant stable, nom, tailles permises, réglages, intervalle de rafraîchissement.</summary>
    WidgetInfo Info { get; }

    /// <summary>
    /// Un relevé. Lève une exception avec un message affichable en français si la mesure est impossible : la tuile garde alors son dernier
    /// résultat et affiche le message. Doit respecter <paramref name="cancellation"/> (page quittée, deck masqué).
    /// </summary>
    Task<WidgetReading> ReadAsync(WidgetContext context, CancellationToken cancellation);
}

/// <summary>Widgets de données disponibles, retrouvés par identifiant.</summary>
public sealed class WidgetRegistry
{
    readonly Dictionary<string, IWidget> _byId;

    /// <summary>Registre des widgets donnés ; deux widgets ne peuvent pas partager un identifiant.</summary>
    public WidgetRegistry(IEnumerable<IWidget> widgets)
    {
        _byId = new Dictionary<string, IWidget>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in widgets)
            if (!_byId.TryAdd(widget.Info.Id, widget))
                throw new InvalidOperationException($"Deux widgets portent l'identifiant « {widget.Info.Id} »");
    }

    /// <summary>Tous les widgets, dans l'ordre d'enregistrement.</summary>
    public IReadOnlyCollection<IWidget> All => _byId.Values;

    /// <summary>Le widget d'identifiant donné, ou null (horloge, agenda et page web ne sont pas des widgets de données).</summary>
    public IWidget? Find(string? id) => id != null && _byId.TryGetValue(id, out var w) ? w : null;

    /// <summary>Intervalle de rafraîchissement d'une tuile : le réglage « interval » (secondes) s'il est valide, sinon celui du widget ; borné de 1 s à 1 h.</summary>
    public static TimeSpan IntervalFor(WidgetInfo info, IReadOnlyDictionary<string, string>? options)
    {
        int seconds = info.RefreshSeconds > 0 ? info.RefreshSeconds : 60;
        if (options != null && options.TryGetValue(WidgetOptionKeys.Interval, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) && chosen > 0)
            seconds = chosen;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 3600));
    }
}
