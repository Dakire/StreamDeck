using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchDeck;

/// <summary>Un type de widget : un bouton qui affiche une information au lieu de lancer une action.</summary>
/// <param name="Id">Identifiant stable, écrit dans <see cref="DeckButton.Widget"/>.</param>
/// <param name="Name">Nom affiché dans le catalogue et le panneau de propriétés.</param>
/// <param name="Description">Ce que le widget affiche.</param>
/// <param name="Icon">Icône Fluent du bouton créé.</param>
/// <param name="Color">Couleur du bouton créé.</param>
/// <param name="Cols">Largeur par défaut, en cases.</param>
/// <param name="Rows">Hauteur par défaut, en cases.</param>
public sealed record WidgetInfo(string Id, string Name, string Description, string Icon, string Color, int Cols, int Rows);

/// <summary>Widgets proposés : heure, agenda Google et page web. Un widget est un <see cref="DeckButton"/> dont <see cref="DeckButton.Widget"/> est renseigné.</summary>
public static class WidgetTypes
{
    /// <summary>Horloge : heure en grand, date en dessous.</summary>
    public const string Clock = "clock";
    /// <summary>Agenda : prochains événements de l'agenda Google.</summary>
    public const string Agenda = "agenda";
    /// <summary>Page web : un site affiché dans le deck (chat, tableau de bord…).</summary>
    public const string Web = "web";

    /// <summary>Plus grande emprise d'un widget, en cases (par sens).</summary>
    public const int MaxSpan = 8;

    /// <summary>Tous les widgets, dans l'ordre du catalogue.</summary>
    public static readonly IReadOnlyList<WidgetInfo> All = new WidgetInfo[]
    {
        new(Clock, "Horloge", "Affiche l'heure en grand, avec la date.", "Clock24", "#334155", 2, 1),
        new(Agenda, "Agenda", "Affiche les prochains événements de ton agenda Google.", "CalendarLtr24", "#1A73E8", 2, 2),
        new(Web, "Page web", "Affiche un site dans le deck : chat, tableau de bord, météo…", "Globe24", "#0E7490", 3, 2),
    };

    /// <summary>Le widget d'identifiant donné, ou null s'il est inconnu.</summary>
    public static WidgetInfo? Find(string? id) => All.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>true si l'adresse est une page web que le widget accepte (http ou https, adresse complète).</summary>
    public static bool IsWebAddress(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host)) return false;
        uri = parsed;
        return true;
    }
}

/// <summary>Clés des réglages d'un widget (<see cref="DeckButton.WidgetOptions"/>) et lecture avec valeur par défaut.</summary>
public static class WidgetOptionKeys
{
    /// <summary>Horloge : « 1 » affiche les secondes.</summary>
    public const string Seconds = "seconds";
    /// <summary>Horloge : « 1 » affiche 12 heures (AM / PM) au lieu de 24.</summary>
    public const string Hour12 = "hour12";
    /// <summary>Horloge : « 0 » masque la date (« 1 » par défaut).</summary>
    public const string Date = "date";
    /// <summary>Agenda : nombre d'événements affichés.</summary>
    public const string Count = "count";
    /// <summary>Agenda : nombre de jours à venir.</summary>
    public const string Days = "days";
    /// <summary>Page web : adresse affichée.</summary>
    public const string Url = "url";

    /// <summary>Valeur d'un réglage, ou <paramref name="fallback"/> s'il est absent ou vide.</summary>
    public static string Option(this DeckButton b, string key, string fallback = "") =>
        b.WidgetOptions != null && b.WidgetOptions.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    /// <summary>Réglage booléen (« 1 » / « 0 »).</summary>
    public static bool OptionFlag(this DeckButton b, string key, bool fallback) => b.Option(key, fallback ? "1" : "0") == "1";

    /// <summary>Réglage numérique borné.</summary>
    public static int OptionInt(this DeckButton b, string key, int fallback, int min, int max) =>
        int.TryParse(b.Option(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, min, max) : fallback;

    /// <summary>Écrit un réglage ; une valeur vide le retire (et le dictionnaire s'il devient vide : rien dans config.json).</summary>
    public static void SetOption(this DeckButton b, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (b.WidgetOptions == null) return;
            b.WidgetOptions.Remove(key);
            if (b.WidgetOptions.Count == 0) b.WidgetOptions = null;
            return;
        }
        (b.WidgetOptions ??= new Dictionary<string, string>())[key] = value.Trim();
    }

    /// <summary>Largeur et hauteur demandées d'un bouton, en cases (au moins 1, au plus <see cref="WidgetTypes.MaxSpan"/>).</summary>
    public static (int Cols, int Rows) Span(this DeckButton b) =>
        (Math.Clamp(b.ColSpan, 1, WidgetTypes.MaxSpan), Math.Clamp(b.RowSpan, 1, WidgetTypes.MaxSpan));
}

/// <summary>Emprise d'un widget sur la grille : jamais hors de la page, jamais sur un bouton voisin.</summary>
public static class TileSpans
{
    /// <summary>
    /// Emprise réellement utilisable d'un widget ancré à la case <paramref name="cell"/> (numérotée ligne par ligne, 0 = en haut à gauche
    /// de la page affichée) : l'emprise demandée est d'abord ramenée dans la grille, puis réduite tant qu'elle recouvre une case occupée.
    /// Au pire, une seule case.
    /// </summary>
    /// <param name="cell">Case d'ancrage.</param>
    /// <param name="wantCols">Largeur demandée.</param>
    /// <param name="wantRows">Hauteur demandée.</param>
    /// <param name="gridCols">Colonnes de la grille.</param>
    /// <param name="gridRows">Lignes de la grille.</param>
    /// <param name="isOccupied">true si la case donnée (autre que l'ancrage) contient déjà quelque chose.</param>
    public static (int Cols, int Rows) Effective(int cell, int wantCols, int wantRows, int gridCols, int gridRows, Func<int, bool> isOccupied)
    {
        if (gridCols < 1 || gridRows < 1) return (1, 1);
        int col = cell % gridCols, row = cell / gridCols;
        int cols = Math.Clamp(wantCols, 1, Math.Max(1, gridCols - col));
        int rows = Math.Clamp(wantRows, 1, Math.Max(1, gridRows - row));
        // La plus grande emprise libre contenue dans la demande (à surface égale, la plus large) ; une case au pire
        (int Cols, int Rows) best = (1, 1);
        for (int c = cols; c >= 1; c--)
            for (int r = rows; r >= 1; r--)
            {
                if (c * r <= best.Cols * best.Rows) continue;
                if (!Covered(cell, c, r, gridCols).Any(isOccupied)) best = (c, r);
            }
        return best;
    }

    /// <summary>Cases recouvertes par l'emprise, hors la case d'ancrage.</summary>
    public static IEnumerable<int> Covered(int cell, int cols, int rows, int gridCols)
    {
        for (int dy = 0; dy < rows; dy++)
            for (int dx = 0; dx < cols; dx++)
                if (dx != 0 || dy != 0) yield return cell + dx + dy * gridCols;
    }
}

/// <summary>Texte de l'horloge et de la date d'un widget (pur, testable).</summary>
public static class WidgetClock
{
    /// <summary>Heure : « 14:05 » ou « 14:05:09 » ; en 12 heures, « 2:05 PM ».</summary>
    public static string Time(DateTime now, bool seconds, bool hour12, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string format = hour12 ? (seconds ? "h:mm:ss tt" : "h:mm tt") : (seconds ? "HH:mm:ss" : "HH:mm");
        return now.ToString(format, culture);
    }

    /// <summary>Date : « Jeudi 22 octobre » (première lettre en majuscule).</summary>
    public static string Date(DateTime now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var text = now.ToString("dddd d MMMM", culture);
        return text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];
    }
}
