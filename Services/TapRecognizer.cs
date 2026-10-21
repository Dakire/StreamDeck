using System;

namespace TouchDeck;

/// <summary>
/// Reconnaît un appui bref d'un seul doigt (pixels physiques) : appui, éventuel mouvement, relâchement. Un doigt qui dépasse
/// la tolérance n'est plus un appui (c'est un glissement). Les autres doigts sont ignorés tant qu'un doigt est suivi.
/// </summary>
internal sealed class TapRecognizer
{
    uint? _id;
    int _x, _y;

    /// <summary>Distance maximale (pixels) entre l'appui et le relâchement pour rester un appui.</summary>
    public int Slop { get; set; } = 16;

    /// <summary>Vrai si le doigt suivi a dépassé la tolérance.</summary>
    public bool Moved { get; private set; }

    /// <summary>Position de l'appui du doigt suivi.</summary>
    public (int X, int Y) Origin => (_x, _y);

    public void Down(uint id, int x, int y)
    {
        if (_id != null) return;
        _id = id; _x = x; _y = y; Moved = false;
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
        bool tap = !Moved && Distance(x, y) <= Slop;
        _id = null; Moved = false;
        return tap;
    }

    public void Cancel() { _id = null; Moved = false; }

    int Distance(int x, int y) => Math.Max(Math.Abs(x - _x), Math.Abs(y - _y));
}
