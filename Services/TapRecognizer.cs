using System;

namespace TouchDeck;

/// <summary>
/// Reconnaît un appui bref d'un seul doigt (pixels physiques) : appui, éventuel mouvement, relâchement. Un doigt qui dépasse
/// la tolérance n'est plus un appui (c'est un glissement). Les autres doigts sont ignorés tant qu'un doigt est suivi.
/// Un doigt immobile depuis <see cref="LongPressMs"/> est un appui long : il n'est alors plus un appui bref au relâchement.
/// </summary>
internal sealed class TapRecognizer
{
    /// <summary>Durée (ms) d'un appui immobile pour qu'il compte comme appui long.</summary>
    public const int LongPressMs = 550;

    uint? _id;
    int _x, _y;
    long _downAt;
    bool _longFired;

    /// <summary>Distance maximale (pixels) entre l'appui et le relâchement pour rester un appui.</summary>
    public int Slop { get; set; } = 16;

    /// <summary>Vrai tant qu'un doigt est suivi (posé, pas encore relâché).</summary>
    public bool Tracking => _id != null;

    /// <summary>Vrai si le doigt suivi a dépassé la tolérance.</summary>
    public bool Moved { get; private set; }

    /// <summary>Position de l'appui du doigt suivi.</summary>
    public (int X, int Y) Origin => (_x, _y);

    /// <summary>Un doigt se pose (ignoré si un autre doigt est déjà suivi).</summary>
    /// <param name="id">Identifiant du pointeur.</param>
    /// <param name="x">Abscisse, pixels physiques.</param>
    /// <param name="y">Ordonnée, pixels physiques.</param>
    /// <param name="nowMs">Instant de l'appui (ms, horloge monotone) ; sert à reconnaître l'appui long.</param>
    public void Down(uint id, int x, int y, long nowMs = 0)
    {
        if (_id != null) return;
        _id = id; _x = x; _y = y; Moved = false; _downAt = nowMs; _longFired = false;
    }

    /// <summary>
    /// Vrai une seule fois, quand le doigt suivi est resté immobile assez longtemps : l'appui est alors consommé
    /// (son relâchement n'est plus un appui bref). Faux si le doigt a glissé ou n'est pas posé.
    /// </summary>
    public bool TryLongPress(long nowMs)
    {
        if (_id == null || Moved || _longFired || nowMs - _downAt < LongPressMs) return false;
        _longFired = true;
        return true;
    }

    /// <summary>Mouvement du doigt suivi ; vrai si ce doigt a dépassé la tolérance.</summary>
    public bool Move(uint id, int x, int y)
    {
        if (_id != id) return false;
        if (Distance(x, y) > Slop) Moved = true;
        return Moved;
    }

    /// <summary>Relâchement : vrai si c'était un appui (le doigt suivi n'a pas bougé au-delà de la tolérance).</summary>
    public bool Up(uint id, int x, int y)
    {
        if (_id != id) return false;
        bool tap = !Moved && !_longFired && Distance(x, y) <= Slop;
        _id = null; Moved = false; _longFired = false;
        return tap;
    }

    public void Cancel() { _id = null; Moved = false; _longFired = false; }

    int Distance(int x, int y) => Math.Max(Math.Abs(x - _x), Math.Abs(y - _y));
}
