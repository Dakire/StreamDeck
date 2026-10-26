using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StreamDak;

/// <summary>
/// Numéro de version SemVer 2.0 (« 1.4.0 », « v1.5.0-beta.2+build7 ») et son ordre : une pré-version précède sa version finale (1.5.0-beta.2 &lt; 1.5.0),
/// les identifiants numériques se comparent comme des nombres et précèdent les identifiants alphanumériques ; les métadonnées de build (« +… ») sont ignorées.
/// </summary>
public sealed partial class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    /// <summary>Longueur maximale acceptée (un tag aberrant n'est pas une version).</summary>
    public const int MaxLength = 64;

    SemVer(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    /// <summary>Numéro majeur.</summary>
    public int Major { get; }
    /// <summary>Numéro mineur.</summary>
    public int Minor { get; }
    /// <summary>Correctif.</summary>
    public int Patch { get; }
    /// <summary>Identifiants de pré-version (« beta », « 2 »), vide pour une version finale.</summary>
    public IReadOnlyList<string> PreRelease { get; }
    /// <summary>true pour une pré-version (beta, rc…).</summary>
    public bool IsPreRelease => PreRelease.Count > 0;

    /// <summary>
    /// Lit une version ; un « v » initial est accepté (tag Git), « 1.4 » vaut « 1.4.0 ». Refuse tout ce qui n'est pas
    /// <c>MAJEUR.MINEUR[.CORRECTIF][-préversion][+build]</c>.
    /// </summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = null!;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength) return false;
        var m = Pattern().Match(text);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["maj"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(m.Groups["min"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return false;
        int patch = 0;
        if (m.Groups["pat"].Success && !int.TryParse(m.Groups["pat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
        var pre = m.Groups["pre"].Success ? m.Groups["pre"].Value.Split('.') : Array.Empty<string>();
        version = new SemVer(major, minor, patch, pre);
        return true;
    }

    /// <summary>Lit une version ou lève <see cref="FormatException"/>.</summary>
    public static SemVer Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Version illisible : « {text} »");

    // identifiants de pré-version : chiffres sans zéro initial, ou lettres, chiffres et tirets
    [GeneratedRegex(@"^[vV]?(?<maj>\d{1,9})\.(?<min>\d{1,9})(?:\.(?<pat>\d{1,9}))?(?:-(?<pre>(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();

    /// <inheritdoc/>
    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (!IsPreRelease || !other.IsPreRelease) return IsPreRelease == other.IsPreRelease ? 0 : IsPreRelease ? -1 : 1;
        for (int i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    static int CompareIdentifier(string a, string b)
    {
        bool aNumeric = a.All(char.IsAsciiDigit), bNumeric = b.All(char.IsAsciiDigit);
        if (aNumeric && bNumeric) return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        if (aNumeric != bNumeric) return aNumeric ? -1 : 1;
        return string.CompareOrdinal(a, b);
    }

    /// <inheritdoc/>
    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as SemVer);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease));
    /// <summary>« 1.4.0 » ou « 1.5.0-beta.2 ».</summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + string.Join('.', PreRelease) : "");

    /// <summary>true si <paramref name="a"/> est strictement plus récente que <paramref name="b"/>.</summary>
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    /// <summary>true si <paramref name="a"/> est strictement plus ancienne que <paramref name="b"/>.</summary>
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    /// <summary>true si <paramref name="a"/> est au moins aussi récente que <paramref name="b"/>.</summary>
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    /// <summary>true si <paramref name="a"/> est au plus aussi récente que <paramref name="b"/>.</summary>
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    /// <summary>Égalité de version (métadonnées de build ignorées).</summary>
    public static bool operator ==(SemVer? a, SemVer? b) => a is null ? b is null : a.Equals(b);
    /// <summary>Inégalité de version.</summary>
    public static bool operator !=(SemVer? a, SemVer? b) => !(a == b);
}
