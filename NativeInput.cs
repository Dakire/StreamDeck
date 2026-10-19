using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TouchDeck;

/// <summary>Envoi de touches / texte au système via SendInput.</summary>
public static class NativeInput
{
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

    static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
        { "ctrl", "control", "shift", "alt", "win" };

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = (Extended.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0),
            }
        }
    };

    static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    static ushort ParseKey(string k)
    {
        if (Named.TryGetValue(k, out var vk)) return vk;
        if (k.Length >= 2 && (k[0] == 'f' || k[0] == 'F') && int.TryParse(k.AsSpan(1), out var n) && n is >= 1 and <= 24)
            return (ushort)(0x6F + n);
        if (k.Length == 1)
        {
            var r = VkKeyScan(k[0]);
            if (r != -1) return (ushort)(r & 0xFF);
        }
        throw new ArgumentException($"Touche inconnue : « {k} »");
    }

    /// <summary>Vérifie une combinaison sans l'envoyer. error = raison en français si invalide.</summary>
    public static bool TryValidateHotkey(string combo, out string error)
    {
        error = "";
        var parts = (combo ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { error = "Le raccourci est vide"; return false; }
        if (parts.All(p => Modifiers.Contains(p))) { error = "Ajoute une touche en plus des modificateurs"; return false; }
        foreach (var p in parts)
        {
            try { ParseKey(p); }
            catch (ArgumentException) { error = $"Touche inconnue : « {p} »"; return false; }
        }
        return true;
    }

    /// <summary>Ex : "ctrl+shift+m", "win+d", "f13", "alt+tab".</summary>
    public static void Hotkey(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Raccourci vide");
        var mods = new List<ushort>();
        var keys = new List<ushort>();
        foreach (var p in parts)
            (Modifiers.Contains(p) ? mods : keys).Add(ParseKey(p));

        var seq = new List<INPUT>();
        foreach (var m in mods) seq.Add(Key(m, false));
        foreach (var k in keys) { seq.Add(Key(k, false)); seq.Add(Key(k, true)); }
        for (int i = mods.Count - 1; i >= 0; i--) seq.Add(Key(mods[i], true));
        Send(seq);
    }

    /// <summary>Touche média / simple (playpause, mute, volup…).</summary>
    public static void Media(string name) => Hotkey(name);

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
