using System;

namespace StreamDak.Hotkeys;

/// <summary>Résultat de l'enregistrement d'un raccourci global.</summary>
/// <param name="Ok">true si le système l'a accepté.</param>
/// <param name="Error">Raison lisible en cas d'échec (déjà pris par une autre application, touche inconnue…).</param>
public sealed record HotkeyResult(bool Ok, string? Error = null);

/// <summary>
/// Raccourcis clavier globaux (<c>RegisterHotKey</c> sous Windows) : déclenchent un bouton du deck depuis n'importe quelle application. Chaque raccourci a un
/// identifiant choisi par l'appelant. <see cref="Pressed"/> est levé sur un thread quelconque.
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Un raccourci enregistré a été pressé (identifiant).</summary>
    event Action<int>? Pressed;
    /// <summary>Enregistre <paramref name="combo"/> sous cet identifiant ; remplace un enregistrement précédent du même identifiant.</summary>
    HotkeyResult Register(int id, string combo);
    /// <summary>Retire un raccourci (sans effet s'il n'existe pas).</summary>
    void Unregister(int id);
}
