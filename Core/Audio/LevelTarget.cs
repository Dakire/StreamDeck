using System;
using System.Collections.Generic;
using System.Linq;

namespace StreamDak.Audio;

/// <summary>Forme du contrôle tactile d'un niveau audio.</summary>
public enum LevelShape
{
    /// <summary>Roue qu'on tourne au doigt, niveau au centre.</summary>
    Wheel,
    /// <summary>Curseur vertical.</summary>
    VerticalSlider,
    /// <summary>Curseur horizontal.</summary>
    HorizontalSlider,
}

/// <summary>Formes proposées, leurs clés (réglage <c>shape</c> du widget) et leurs noms affichés.</summary>
public static class LevelShapes
{
    /// <summary>Clés écrites dans la configuration : stables.</summary>
    public const string Wheel = "wheel", Vertical = "vertical", Horizontal = "horizontal";

    /// <summary>Forme d'après sa clé ; la roue si la clé est absente ou inconnue.</summary>
    public static LevelShape Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        Vertical => LevelShape.VerticalSlider,
        Horizontal => LevelShape.HorizontalSlider,
        _ => LevelShape.Wheel,
    };

    /// <summary>Clé écrite dans la configuration ; null pour la roue (valeur par défaut, rien n'est écrit).</summary>
    public static string? KeyOf(LevelShape shape) => shape switch
    {
        LevelShape.VerticalSlider => Vertical,
        LevelShape.HorizontalSlider => Horizontal,
        _ => null,
    };

    /// <summary>Formes dans l'ordre de l'éditeur.</summary>
    public static readonly IReadOnlyList<(LevelShape Shape, string Label)> All = new[]
    {
        (LevelShape.Wheel, "Roue"), (LevelShape.VerticalSlider, "Curseur vertical"), (LevelShape.HorizontalSlider, "Curseur horizontal"),
    };
}

/// <summary>Ce que le contrôle règle : un canal Sonar ou un périphérique audio de Windows.</summary>
public enum LevelBackend
{
    /// <summary>Canal du mélangeur Sonar (connecteur « sonar »).</summary>
    Sonar,
    /// <summary>Périphérique audio de Windows, choisi par son nom.</summary>
    Windows,
}

/// <summary>
/// Cible d'un contrôle de niveau, écrite en texte dans le réglage <c>source</c> du widget : « sonar:game », « windows:Focusrite »,
/// « windows:capture:Focusrite » (sens facultatif). Une cible illisible est refusée par <see cref="TryParse"/>.
/// </summary>
/// <param name="Backend">Sonar ou Windows.</param>
/// <param name="Name">Canal Sonar (nom canonique) ou désignation du périphérique Windows (« capture:Focusrite »).</param>
public sealed record LevelTarget(LevelBackend Backend, string Name)
{
    /// <summary>Cible d'après le texte du réglage ; false si le texte est vide, de forme inconnue ou sans nom.</summary>
    public static bool TryParse(string? text, out LevelTarget target)
    {
        target = null!;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        var kind = text[..colon].Trim();
        var name = text[(colon + 1)..].Trim();
        if (name.Length == 0 || name.Length > 200) return false;
        if (kind.Equals("sonar", StringComparison.OrdinalIgnoreCase)) { target = new LevelTarget(LevelBackend.Sonar, name); return true; }
        if (kind.Equals("windows", StringComparison.OrdinalIgnoreCase)) { target = new LevelTarget(LevelBackend.Windows, name); return true; }
        return false;
    }

    /// <summary>Texte écrit dans le réglage.</summary>
    public string ToText() => (Backend == LevelBackend.Sonar ? "sonar:" : "windows:") + Name;
}

/// <summary>Niveau lu : pourcentage (0 à 100) et coupure (null si inconnue).</summary>
/// <param name="Percent">Niveau de 0 à 100.</param>
/// <param name="Muted">true si coupé ; null si inconnu.</param>
public sealed record LevelReading(int Percent, bool? Muted);

/// <summary>Une source de niveau que le contrôle lit et règle, sans connaître Sonar ni Windows.</summary>
public interface ILevelSource
{
    /// <summary>Nom affiché sur la tuile (« Jeu », « Focusrite USB Audio »).</summary>
    string Title { get; }
    /// <summary>Quand relire : null si la source prévient d'elle-même par <see cref="Changed"/> ; sinon l'intervalle de relevé.</summary>
    TimeSpan? PollInterval { get; }
    /// <summary>Le niveau a peut-être changé (peut être levé depuis n'importe quel thread).</summary>
    event Action? Changed;
    /// <summary>Niveau actuel, ou null avec <paramref name="problem"/> qui dit pourquoi (non connecté, périphérique introuvable). Ne lève jamais d'exception.</summary>
    LevelReading? Read(out string problem);
    /// <summary>Règle le niveau (0 à 100). Lève une exception à message affichable en cas d'échec.</summary>
    System.Threading.Tasks.Task SetLevelAsync(int percent);
    /// <summary>Coupe ou rétablit. Lève une exception à message affichable en cas d'échec.</summary>
    System.Threading.Tasks.Task SetMutedAsync(bool muted);
}

/// <summary>Envoi des niveaux pendant qu'on tourne la roue : un seul envoi à la fois, toujours le dernier niveau demandé, avec un petit délai entre deux.</summary>
public sealed class LevelSender
{
    readonly Func<int, System.Threading.Tasks.Task> _send;
    readonly Func<TimeSpan, System.Threading.Tasks.Task> _delay;
    readonly Action<Exception> _failed;
    readonly object _gate = new();
    int? _pending;
    bool _running;

    /// <summary>Délai minimal entre deux envois.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(70);

    /// <summary>Expéditeur : <paramref name="send"/> envoie un niveau, <paramref name="delay"/> attend (injecté pour les tests), <paramref name="failed"/> reçoit les erreurs.</summary>
    public LevelSender(Func<int, System.Threading.Tasks.Task> send, Func<TimeSpan, System.Threading.Tasks.Task>? delay = null, Action<Exception>? failed = null)
    {
        _send = send;
        _delay = delay ?? (t => System.Threading.Tasks.Task.Delay(t));
        _failed = failed ?? (_ => { });
    }

    /// <summary>true tant qu'un envoi est en cours ou en attente.</summary>
    public bool IsBusy { get { lock (_gate) return _running; } }

    /// <summary>Demande l'envoi de ce niveau ; s'il y en a déjà un en cours, seul le dernier niveau demandé sera envoyé ensuite.</summary>
    public void Post(int percent)
    {
        lock (_gate)
        {
            _pending = Math.Clamp(percent, 0, 100);
            if (_running) return;
            _running = true;
        }
        _ = System.Threading.Tasks.Task.Run(LoopAsync);
    }

    async System.Threading.Tasks.Task LoopAsync()
    {
        while (true)
        {
            int next;
            lock (_gate)
            {
                if (_pending is not { } value) { _running = false; return; }
                next = value;
                _pending = null;
            }
            try { await _send(next); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _failed(ex); }
            await _delay(MinInterval);
        }
    }
}

/// <summary>Géométrie de la roue : angle du doigt, rotation continue sans saut, correspondance angle et niveau.</summary>
public sealed class WheelGesture
{
    /// <summary>Course de la roue, en degrés (comme un potentiomètre : 270°, le bas reste libre).</summary>
    public const double Sweep = 270;
    /// <summary>Angle du niveau 0, en degrés depuis le haut dans le sens horaire (−135° : en bas à gauche).</summary>
    public const double StartAngle = -135;

    double _lastAngle;
    double _position;   // position de la roue en degrés depuis 0, bornée à [0, Sweep]

    /// <summary>Niveau actuel de la roue, de 0 à 100 (fractionnaire).</summary>
    public double Level => _position / Sweep * 100;

    /// <summary>Angle d'un point par rapport au centre, en degrés depuis le haut dans le sens horaire (de −180 à 180).</summary>
    public static double AngleOf(double dx, double dy) => Math.Atan2(dx, -dy) * 180 / Math.PI;

    /// <summary>Angle d'une aiguille pour un niveau (−135° à +135°).</summary>
    public static double AngleOfLevel(double level) => StartAngle + Math.Clamp(level, 0, 100) / 100 * Sweep;

    /// <summary>Différence d'angle ramenée dans l'intervalle de −180° à 180° : franchir le haut ou le bas ne fait jamais sauter la valeur.</summary>
    public static double Delta(double from, double to)
    {
        var d = (to - from) % 360;
        if (d > 180) d -= 360;
        else if (d <= -180) d += 360;
        return d;
    }

    /// <summary>Pose le doigt : la roue garde son niveau (aucun saut), le doigt devient le point de départ de la rotation.</summary>
    public void Begin(double fingerAngle, double level)
    {
        _lastAngle = fingerAngle;
        _position = Math.Clamp(level, 0, 100) / 100 * Sweep;
    }

    /// <summary>
    /// Le doigt tourne : la roue suit de la différence d'angle, bornée à la course. Aux butées la position est bornée aussi, donc dès que le doigt
    /// revient en arrière la roue repart, sans zone morte. Retourne le niveau (0 à 100).
    /// </summary>
    public double Move(double fingerAngle)
    {
        _position = Math.Clamp(_position + Delta(_lastAngle, fingerAngle), 0, Sweep);
        _lastAngle = fingerAngle;
        return Level;
    }

    /// <summary>Niveau entier (0 à 100) pour l'envoi.</summary>
    public int Percent => (int)Math.Round(Level);
}

/// <summary>Correspondance entre la position du doigt sur un curseur et le niveau.</summary>
public static class SliderMath
{
    /// <summary>Niveau (0 à 100) pour une position sur la piste ; <paramref name="reversed"/> : le haut (ou la gauche) est le maximum quand c'est le cas du curseur vertical.</summary>
    public static double LevelAt(double position, double start, double length, bool reversed)
    {
        if (length <= 0) return 0;
        var t = Math.Clamp((position - start) / length, 0, 1);
        return (reversed ? 1 - t : t) * 100;
    }

    /// <summary>Position sur la piste d'un niveau (inverse de <see cref="LevelAt"/>).</summary>
    public static double PositionOf(double level, double start, double length, bool reversed)
    {
        var t = Math.Clamp(level, 0, 100) / 100;
        return start + (reversed ? 1 - t : t) * length;
    }
}

/// <summary>Noms et ordre des réglages d'un widget « Niveau audio ».</summary>
public static class LevelOptions
{
    /// <summary>Réglage : cible (<see cref="LevelTarget.ToText"/>).</summary>
    public const string Source = "source";
    /// <summary>Réglage : forme (<see cref="LevelShapes"/>).</summary>
    public const string Shape = "shape";

    /// <summary>Canaux Sonar proposés dans l'éditeur, dans l'ordre : (nom canonique, libellé).</summary>
    public static readonly IReadOnlyList<(string Channel, string Label)> SonarChannels = new[]
    {
        ("master", "Principal"), ("game", "Jeu"), ("chatRender", "Discussion"), ("media", "Média"), ("aux", "Aux"), ("chatCapture", "Micro"),
    };

    /// <summary>Libellé d'un canal Sonar.</summary>
    public static string ChannelLabel(string channel) => SonarChannels.FirstOrDefault(c => c.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)).Label ?? channel;
}
