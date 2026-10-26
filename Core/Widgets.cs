using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StreamDak;

/// <summary>Un type de widget : un bouton qui affiche une information au lieu de lancer une action.</summary>
/// <param name="Id">Identifiant stable, écrit dans <see cref="DeckButton.Widget"/>.</param>
/// <param name="Name">Nom affiché dans le catalogue et le panneau de propriétés.</param>
/// <param name="Description">Ce que le widget affiche.</param>
/// <param name="Icon">Icône Fluent du bouton créé.</param>
/// <param name="Color">Couleur du bouton créé.</param>
/// <param name="Cols">Largeur par défaut, en cases.</param>
/// <param name="Rows">Hauteur par défaut, en cases.</param>
/// <param name="Sizes">Tailles proposées dans les propriétés (widget de données) ; null : toutes les tailles courantes.</param>
/// <param name="Options">Réglages texte du widget, affichés dans les propriétés (widget de données).</param>
/// <param name="RefreshSeconds">Intervalle de rafraîchissement par défaut d'un widget de données, en secondes ; 0 : ce n'est pas un widget de données.</param>
public sealed record WidgetInfo(string Id, string Name, string Description, string Icon, string Color, int Cols, int Rows,
    IReadOnlyList<WidgetSize>? Sizes = null, IReadOnlyList<WidgetOption>? Options = null, int RefreshSeconds = 0)
{
    /// <summary>true pour un widget de données (<see cref="IWidget"/>), relevé à intervalle régulier.</summary>
    public bool IsData => RefreshSeconds > 0;
}

/// <summary>Widgets proposés : heure, agenda Google, page web, charge du processeur et mémoire. Un widget est un <see cref="DeckButton"/> dont <see cref="DeckButton.Widget"/> est renseigné.</summary>
public static class WidgetTypes
{
    /// <summary>Horloge : heure en grand, date en dessous.</summary>
    public const string Clock = "clock";
    /// <summary>Agenda : prochains événements de l'agenda Google.</summary>
    public const string Agenda = "agenda";
    /// <summary>Page web : un site affiché dans le deck (chat, tableau de bord…).</summary>
    public const string Web = "web";
    /// <summary>Processeur : charge en % (intégration « Système Windows »).</summary>
    public const string Cpu = "cpu";
    /// <summary>Mémoire : Go utilisés sur le total (intégration « Système Windows »).</summary>
    public const string Ram = "ram";

    /// <summary>Plus grande emprise d'un widget, en cases (par sens).</summary>
    public const int MaxSpan = 8;

    /// <summary>Tous les widgets, dans l'ordre du catalogue.</summary>
    public static readonly IReadOnlyList<WidgetInfo> All = new WidgetInfo[]
    {
        new(Clock, "Horloge", "Affiche l'heure en grand, avec la date.", "Clock24", "#334155", 2, 1),
        new(Agenda, "Agenda", "Affiche les prochains événements de ton agenda Google.", "CalendarLtr24", "#1A73E8", 2, 2),
        new(Web, "Page web", "Affiche un site dans le deck : chat, tableau de bord, météo…", "Globe24", "#0E7490", 3, 2),
        new(Cpu, "Processeur", "Charge du processeur en temps réel (%), rafraîchie chaque seconde.", "DeveloperBoard24", "#0F766E", 2, 1),
        new(Ram, "Mémoire", "Mémoire vive utilisée (Go sur le total), rafraîchie chaque seconde.", "Memory16", "#6D28D9", 2, 1),
        // Widgets de données (IWidget) : relevés à intervalle régulier, seulement tant que leur page est affichée
        DiskWidget.Description, NetworkWidget.Description, PingWidget.Description, ServiceStatusWidget.Description,
        WeatherWidget.Description, NextEventWidget.Description, CounterWidget.Description,
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

/// <summary>Adresse saisie pour une page web : on complète « calendar.google.com » en « https://calendar.google.com ».</summary>
public static class WebAddress
{
    /// <summary>
    /// Adresse complète (http ou https) déduite du texte saisi, ou false. Sans schéma, « https:// » est ajouté ; un autre schéma
    /// (file:, javascript:, ftp://…) est refusé.
    /// </summary>
    public static bool TryParse(string? text, out Uri uri)
    {
        uri = null!;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000) return false;
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // « javascript:alert(1) », « mailto:a@b.c » : un schéma sans « // » n'est jamais une adresse web ; « localhost:8080 » (port) l'est
            int colon = text.IndexOf(':');
            if (colon > 0 && !int.TryParse(text[(colon + 1)..].Split('/')[0], out _)) return false;
            text = "https://" + text;
        }
        return WidgetTypes.IsWebAddress(text, out uri);
    }
}

/// <summary>Niveaux de zoom d'un cadre web (boutons − et +, pincement du doigt) et leur stockage.</summary>
public static class WebZoom
{
    /// <summary>Zoom minimal et maximal, en % (limites de WebView2).</summary>
    public const int Min = 25, Max = 500;
    /// <summary>Zoom normal.</summary>
    public const int Normal = 100;

    /// <summary>Paliers des boutons − et +, comme un navigateur.</summary>
    public static readonly IReadOnlyList<int> Steps = new[] { 25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500 };

    /// <summary>Zoom ramené dans les limites.</summary>
    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>Palier suivant (<paramref name="direction"/> &gt; 0) ou précédent ; reste sur place aux extrémités. Part de n'importe quel pourcentage (pincement).</summary>
    public static int Next(int percent, int direction)
    {
        percent = Clamp(percent);
        return direction >= 0 ? Steps.FirstOrDefault(s => s > percent, Max) : Steps.LastOrDefault(s => s < percent, Min);
    }

    /// <summary>Valeur écrite dans la configuration : 0 pour 100 % (rien n'est écrit), sinon le zoom borné.</summary>
    public static int ForStorage(int percent) => percent is 0 or Normal ? 0 : Clamp(percent);
}

/// <summary>Où s'ouvre un lien « nouvel onglet » (target="_blank", window.open) d'un cadre web : dans le cadre, ou dans le navigateur de l'utilisateur.</summary>
public static class WebLinks
{
    /// <summary>Pages de connexion : elles doivent rester dans le cadre, sinon la session ouverte n'arrive pas au cadre (bouton « Sign in » de Google Agenda).</summary>
    static readonly HashSet<string> SignInHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com", "login.microsoftonline.com", "login.live.com", "account.live.com",
    };

    /// <summary>
    /// true si le lien reste dans le cadre : même hôte que l'adresse saisie ou que la page affichée, ou page de connexion. Tout autre lien (un document
    /// Google envoyé dans Chat, un site externe) s'ouvre dans le navigateur par défaut : le cadre n'a ni onglets ni barre d'adresse pour en revenir.
    /// </summary>
    public static bool OpensInFrame(Uri target, Uri configured, Uri? current) =>
        SameHost(target, configured) || (current != null && SameHost(target, current)) || SignInHosts.Contains(target.Host);

    static bool SameHost(Uri a, Uri b) => string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Qui a le droit d'utiliser la caméra, le micro et les notifications dans un cadre web (décision de l'utilisateur, ADR 0018).</summary>
public static class WebPermissions
{
    /// <summary>Domaines publics à deux niveaux les plus courants (« co.uk ») : le « site » est alors l'hôte à trois étiquettes.</summary>
    static readonly HashSet<string> TwoLevelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "co.jp", "co.nz", "co.za", "co.in", "com.br", "com.mx", "com.cn", "com.tr",
    };

    /// <summary>« Site » d'un hôte : les deux dernières étiquettes (trois pour « co.uk » et consorts) ; une adresse IP ou un nom simple vaut lui-même.</summary>
    public static string Site(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _)) return host;
        var labels = host.Split('.');
        if (labels.Length <= 2) return host;
        int keep = TwoLevelSuffixes.Contains(string.Join('.', labels[^2..])) ? 3 : 2;
        return string.Join('.', labels[^Math.Min(keep, labels.Length)..]);
    }

    /// <summary>true si les deux hôtes sont du même site (<c>calendar.google.com</c> et <c>meet.google.com</c>).</summary>
    public static bool SameSite(string hostA, string hostB) => Site(hostA) == Site(hostB);

    /// <summary>
    /// true si la page demandeuse peut obtenir la caméra, le micro ou les notifications : connexion sécurisée (https, ou la machine locale) et
    /// même site que l'adresse saisie par l'utilisateur pour ce cadre. Une page vers laquelle un lien a mené ailleurs n'obtient rien.
    /// </summary>
    public static bool MayUseDevices(Uri requester, Uri configured) =>
        (requester.Scheme == Uri.UriSchemeHttps || requester.IsLoopback) && SameSite(requester.Host, configured.Host);
}

/// <summary>Reconnaît la page par laquelle Google refuse la connexion dans un navigateur intégré (cadre d'une application).</summary>
public static class EmbeddedBrowserRefusal
{
    /// <summary>Message affiché à la place de la page : ce qui se passe et les deux issues propres (jamais de contournement).</summary>
    public const string Message =
        "Google refuse la connexion dans un cadre intégré à une application. Pour voir ton agenda ici, connecte Google dans " +
        "Paramètres › Intégrations (widget Agenda) ; sinon ouvre la page dans ton navigateur avec le bouton prévu à cet effet.";

    /// <summary>true pour l'adresse de refus de Google : page <c>signin/rejected</c> d'accounts.google.com, ou erreur OAuth <c>disallowed_useragent</c>.</summary>
    public static bool IsRefusal(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        bool google = uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);
        if (!google) return false;
        return uri.AbsolutePath.Contains("/signin/rejected", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Contains("disallowed_useragent", StringComparison.OrdinalIgnoreCase);
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
    /// <summary>Page web : zoom en pourcentage (25 à 500), absent = 100 %.</summary>
    public const string Zoom = "zoom";
    /// <summary>Widget de données : intervalle de rafraîchissement en secondes (1 à 3600) ; absent = celui du widget.</summary>
    public const string Interval = "interval";

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

/// <summary>Ce qu'un widget système affiche : valeur en grand, détail dessous, remplissage de la jauge (0 à 100) et alerte si la charge est forte.</summary>
/// <param name="Percent">Charge en pourcentage, entre 0 et 100.</param>
/// <param name="Headline">Valeur en grand (« 37 % », « 12,4 Go »).</param>
/// <param name="Detail">Précision sous la valeur (« sur 31,9 Go · 39 % »), vide pour le processeur.</param>
/// <param name="IsHigh">true à partir de <see cref="SystemMeter.HighPercent"/> : la jauge passe en couleur d'alerte.</param>
public sealed record SystemReading(double Percent, string Headline, string Detail, bool IsHigh);

/// <summary>
/// Mise en forme des relevés de l'intégration « Système Windows » pour les widgets Processeur et Mémoire (pur, testable).
/// Les valeurs viennent des clés <c>sys.cpu</c> (%), <c>sys.ram.used</c> et <c>sys.ram.total</c> (Go, point décimal).
/// </summary>
public static class SystemMeter
{
    /// <summary>Identifiant de l'intégration qui publie les mesures.</summary>
    public const string IntegrationId = "sys";
    /// <summary>Charge à partir de laquelle la jauge passe en alerte.</summary>
    public const double HighPercent = 85;

    /// <summary>Processeur : « 37 % ». Null si la valeur manque ou n'est pas un nombre.</summary>
    public static SystemReading? Cpu(string? percent, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!TryNumber(percent, out var p)) return null;
        p = Math.Clamp(p, 0, 100);
        return new SystemReading(p, $"{p.ToString("0", culture)} %", "", p >= HighPercent);
    }

    /// <summary>Mémoire : « 12,4 Go » en grand, « sur 31,9 Go · 39 % » dessous. Null si une valeur manque ou est incohérente (total nul).</summary>
    public static SystemReading? Ram(string? usedGb, string? totalGb, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!TryNumber(usedGb, out var used) || !TryNumber(totalGb, out var total) || used < 0 || total <= 0) return null;
        var p = Math.Clamp(used * 100 / total, 0, 100);
        return new SystemReading(p, $"{used.ToString("0.0", culture)} Go", $"sur {total.ToString("0.0", culture)} Go · {p.ToString("0", culture)} %", p >= HighPercent);
    }

    static bool TryNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
