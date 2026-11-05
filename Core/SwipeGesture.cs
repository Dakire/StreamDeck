using System;
using System.Collections.Generic;

namespace StreamDak;

/// <summary>Ce qu'est devenu un doigt posé sur la grille du deck.</summary>
public enum SwipeState
{
    /// <summary>Pas encore décidé : le doigt bouge peu (c'est peut-être un appui).</summary>
    Undecided,
    /// <summary>Balayage horizontal reconnu : la page suit le doigt, la touche d'origine ne fera rien.</summary>
    Swiping,
    /// <summary>Balayage vers le bas reconnu (retour au dossier parent, seulement si <c>allowBack</c>) : la touche d'origine ne fera rien.</summary>
    SwipingDown,
    /// <summary>Ce n'est pas un balayage (geste vertical ou diagonal, doigt tenu immobile, geste annulé) : le deck traite le doigt comme avant.</summary>
    Rejected,
}

/// <summary>
/// Reconnaissance du balayage horizontal qui change de page (ADR 0036). Logique pure, en DIP et millisecondes, sans WPF : <c>MainWindow.Touch.cs</c> la nourrit
/// des positions du doigt. Tous les seuils sont ici.
/// <list type="bullet">
/// <item>Un doigt qui bouge de moins de <see cref="LockDistanceDip"/> reste un appui possible.</item>
/// <item>Au-delà, le geste est un balayage si <c>|dx| &gt; </c><see cref="DirectionRatio"/><c> × |dy|</c>, sinon il est rejeté (défilement vertical, diagonale).</item>
/// <item>Un doigt resté immobile <see cref="HoldRejectMs"/> est un appui long : plus de balayage.</item>
/// <item>Au relâchement, la page change si le déplacement dépasse <see cref="MinDistance"/> et si le geste est assez rapide (<see cref="MinVelocity"/>) ou assez long
/// (<see cref="CommitFraction"/> de la largeur).</item>
/// </list>
/// </summary>
public sealed class SwipeGesture
{
    /// <summary>Déplacement (DIP) à partir duquel le geste est classé : en dessous, c'est encore un appui.</summary>
    public const double LockDistanceDip = 24;
    /// <summary>Rapport minimal entre déplacement horizontal et vertical.</summary>
    public const double DirectionRatio = 1.5;
    /// <summary>Déplacement horizontal minimal (DIP) pour changer de page…</summary>
    public const double MinDistanceDip = 80;
    /// <summary>…ou cette part de la largeur de la grille, si elle est plus grande.</summary>
    public const double MinWidthFraction = 0.08;
    /// <summary>Vitesse (DIP/ms) d'un geste vif : au-dessus, le déplacement minimal suffit.</summary>
    public const double MinVelocity = 0.35;
    /// <summary>Part de la largeur au-delà de laquelle un geste lent change quand même de page.</summary>
    public const double CommitFraction = 0.35;
    /// <summary>Fenêtre (ms) sur laquelle la vitesse est mesurée, juste avant le relâchement.</summary>
    public const long VelocityWindowMs = 100;
    /// <summary>Durée (ms) d'un doigt immobile au-delà de laquelle ce n'est plus un balayage (appui long, déplacement de bouton).</summary>
    public const long HoldRejectMs = 450;
    /// <summary>Résistance au bord (pas de page dans ce sens) : la page suit le doigt à ce taux…</summary>
    public const double EdgeResistance = 0.3;
    /// <summary>…sans dépasser cette part de la largeur.</summary>
    public const double EdgeMaxFraction = 0.12;

    /// <summary>Résultat de <see cref="End"/> pour un balayage vers le bas abouti : retour au dossier parent.</summary>
    public const int Back = 2;
    /// <summary>Déplacement vertical (DIP) d'un geste lent qui revient quand même au parent.</summary>
    public const double BackLongDip = 160;

    readonly List<(long T, double X, double Y)> _samples = new();
    bool _allowBack;
    double _x0, _y0, _width;
    long _t0;

    /// <summary>État du geste en cours.</summary>
    public SwipeState State { get; private set; } = SwipeState.Rejected;
    /// <summary>true entre <see cref="Begin"/> et <see cref="End"/> ou <see cref="Cancel"/>.</summary>
    public bool Active { get; private set; }
    /// <summary>Déplacement horizontal depuis l'appui (DIP).</summary>
    public double DeltaX { get; private set; }
    /// <summary>Déplacement minimal pour une grille de cette largeur.</summary>
    public static double MinDistance(double width) => Math.Max(MinDistanceDip, width * MinWidthFraction);

    /// <summary>Un doigt se pose sur la grille (largeur de la grille en DIP).</summary>
    public void Begin(double x, double y, long nowMs, double gridWidth, bool allowBack = false)
    {
        _x0 = x; _y0 = y; _t0 = nowMs; _width = Math.Max(1, gridWidth);
        _allowBack = allowBack;
        DeltaX = 0;
        _samples.Clear();
        _samples.Add((nowMs, x, y));
        State = SwipeState.Undecided;
        Active = true;
    }

    /// <summary>Le doigt bouge ; retourne le nouvel état.</summary>
    public SwipeState Move(double x, double y, long nowMs)
    {
        if (!Active || State == SwipeState.Rejected) return State;
        _samples.Add((nowMs, x, y));
        if (_samples.Count > 64) _samples.RemoveRange(0, _samples.Count - 64);
        double dx = x - _x0, dy = y - _y0;
        if (State == SwipeState.Undecided)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < LockDistanceDip)
            {
                if (nowMs - _t0 >= HoldRejectMs) State = SwipeState.Rejected;   // doigt tenu : c'est un appui long, pas un balayage
                return State;
            }
            bool quick = nowMs - _t0 < HoldRejectMs;
            State = !quick ? SwipeState.Rejected
                : Math.Abs(dx) > DirectionRatio * Math.Abs(dy) ? SwipeState.Swiping
                : _allowBack && dy > 0 && dy > DirectionRatio * Math.Abs(dx) ? SwipeState.SwipingDown
                : SwipeState.Rejected;
        }
        if (State == SwipeState.Swiping) DeltaX = dx;
        return State;
    }

    /// <summary>
    /// Relâchement : +1 (page suivante, balayage vers la gauche), -1 (page précédente, vers la droite) ou 0 (rien : geste trop court, trop lent, pas horizontal).
    /// </summary>
    public int End(double x, double y, long nowMs)
    {
        if (!Active) return 0;
        Move(x, y, nowMs);
        Active = false;
        double dx = x - _x0, dy = y - _y0;
        if (State == SwipeState.SwipingDown)
        {
            if (dy < MinDistanceDip || dy <= DirectionRatio * Math.Abs(dx)) return 0;
            double vy = Velocity(nowMs, vertical: true);
            return vy >= MinVelocity || dy >= BackLongDip ? Back : 0;
        }
        if (State != SwipeState.Swiping) return 0;
        if (Math.Abs(dx) < MinDistance(_width) || Math.Abs(dx) <= DirectionRatio * Math.Abs(dy)) return 0;
        bool far = Math.Abs(dx) >= _width * CommitFraction;
        double v = Velocity(nowMs, vertical: false);
        bool quick = Math.Abs(v) >= MinVelocity && Math.Sign(v) == Math.Sign(dx);
        return far || quick ? -Math.Sign(dx) : 0;
    }

    /// <summary>Abandon (doigt perdu, menu ouvert, autre geste prioritaire) : rien ne change de page.</summary>
    public void Cancel()
    {
        Active = false;
        State = SwipeState.Rejected;
        DeltaX = 0;
    }

    /// <summary>Vitesse horizontale (DIP/ms) sur les <see cref="VelocityWindowMs"/> dernières millisecondes.</summary>
    double Velocity(long nowMs, bool vertical)
    {
        var last = _samples[^1];
        int i = _samples.Count - 1;
        while (i > 0 && nowMs - _samples[i - 1].T <= VelocityWindowMs) i--;
        var first = _samples[i];
        return last.T > first.T ? ((vertical ? last.Y - first.Y : last.X - first.X)) / (last.T - first.T) : 0;
    }

    /// <summary>
    /// Décalage affiché de la grille pour un déplacement <paramref name="dx"/> : le doigt est suivi tel quel s'il y a une page dans ce sens, avec résistance sinon
    /// (rebond élastique au bord, borné à <see cref="EdgeMaxFraction"/> de la largeur).
    /// </summary>
    public static double Follow(double dx, double width, bool hasPageThatWay)
    {
        if (hasPageThatWay) return dx;
        double max = Math.Max(1, width) * EdgeMaxFraction;
        return Math.Sign(dx) * Math.Min(Math.Abs(dx) * EdgeResistance, max);
    }
}
