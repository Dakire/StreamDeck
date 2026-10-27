using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Envoi de touches / texte au système via SendInput. Chaque touche porte son code de balayage (les crochets clavier globaux, comme celui des raccourcis de
/// Discord, et les jeux en entrée brute lisent ce code, absent jusqu'ici) et reste enfoncée <see cref="HoldMs"/> ms : un appui et un relâchement dans la même
/// microseconde passent inaperçus pour les applications qui relèvent l'état du clavier. Un envoi refusé par Windows (application au premier plan exécutée en
/// administrateur : UIPI) lève une exception au message lisible, affiché sur la touche, au lieu d'échouer en silence.
/// </summary>
internal static class NativeInput
{
    /// <summary>Durée pendant laquelle la combinaison reste enfoncée.</summary>
    internal const int HoldMs = 30;

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    const uint KEYEVENTF_KEYUP = 0x2;
    const uint KEYEVENTF_UNICODE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);
    const uint MAPVK_VK_TO_VSC = 0;

    static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2, ["control"] = 0xA2, ["shift"] = 0xA0, ["alt"] = 0xA4, ["win"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09,
        ["space"] = 0x20, ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["printscreen"] = 0x2C,
        // médias
        ["playpause"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1, ["stop"] = 0xB2,
        ["mute"] = 0xAD, ["voldown"] = 0xAE, ["volup"] = 0xAF,
    };

    static readonly HashSet<ushort> Extended = new()
    {
        0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0x5B,
    };

    /// <summary>Noms de touches nommées reconnus par la plateforme (doivent être exactement <see cref="HotkeySyntax.KeyNames"/>).</summary>
    public static IReadOnlyCollection<string> KnownNames => Named.Keys;

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),   // 0 pour les touches média : Windows s'en contente
                dwFlags = (Extended.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0),
            }
        }
    };

    /// <summary>Envoie les événements ; false si Windows en a refusé (en général : application au premier plan élevée, UIPI).</summary>
    static bool TrySend(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return true;
        var arr = inputs.ToArray();
        uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        if (sent == arr.Length) return true;
        Log.Warn($"SendInput : {sent} événement(s) sur {arr.Length} acceptés (premier plan : {WindowFocus.ForegroundDescription()})");
        return false;
    }

    static void Send(List<INPUT> inputs)
    {
        if (!TrySend(inputs)) throw Blocked();
    }

    /// <summary>Message affiché sur la touche quand Windows refuse les touches simulées.</summary>
    static InvalidOperationException Blocked() => new(WindowFocus.ForegroundIsElevated()
        ? "Windows a bloqué les touches : l'application au premier plan est exécutée en administrateur (lance StreamDak en administrateur, ou cette application sans)"
        : "Windows a refusé les touches simulées (écran de verrouillage, contrôle de compte ou bureau sécurisé au premier plan ?)");

    static ushort ParseKey(string k)
    {
        if (Named.TryGetValue(k, out var vk)) return vk;
        if (HotkeySyntax.IsFunctionKey(k, out var n)) return (ushort)(0x6F + n);
        if (k.Length == 1)
        {
            var r = VkKeyScan(k[0]);
            if (r != -1) return (ushort)(r & 0xFF);
        }
        throw new ArgumentException($"Touche inconnue : « {k} »");
    }

    /// <summary>Vérifie une combinaison sans l'envoyer : syntaxe commune, les caractères seuls doivent exister sur le clavier courant.</summary>
    public static bool TryValidateHotkey(string combo, out string error) =>
        HotkeySyntax.TryValidate(combo, out error, ch => VkKeyScan(ch) != -1);

    /// <summary>Ex : "ctrl+shift+m", "win+d", "f13", "alt+tab".</summary>
    public static void Hotkey(string combo)
    {
        var parts = HotkeySyntax.Split(combo);
        if (parts.Length == 0) throw new ArgumentException("Raccourci vide");
        var mods = new List<ushort>();
        var keys = new List<ushort>();
        foreach (var p in parts)
            (HotkeySyntax.Modifiers.Contains(p) ? mods : keys).Add(ParseKey(p));

        // Appui (modificateurs puis touches), maintien, relâchement dans l'ordre inverse : relâché même si l'appui a été refusé à moitié
        var down = new List<INPUT>();
        foreach (var m in mods) down.Add(Key(m, false));
        foreach (var k in keys) down.Add(Key(k, false));
        var up = new List<INPUT>();
        for (int i = keys.Count - 1; i >= 0; i--) up.Add(Key(keys[i], true));
        for (int i = mods.Count - 1; i >= 0; i--) up.Add(Key(mods[i], true));
        bool pressed = TrySend(down);
        if (pressed) System.Threading.Thread.Sleep(HoldMs);
        bool released = TrySend(up);
        if (!pressed || !released) throw Blocked();
    }

    /// <summary>Touche média / simple (playpause, mute, volup…).</summary>
    public static void Media(string name) => Hotkey(name);

    /// <summary>Tape un texte en caractères Unicode (indépendant de la disposition du clavier).</summary>
    public static void TypeText(string text)
    {
        var seq = new List<INPUT>();
        foreach (var ch in text)
        {
            foreach (var up in new[] { false, true })
            {
                seq.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) }
                    }
                });
            }
        }
        Send(seq);
    }
}
