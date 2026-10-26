using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StreamDak;

/// <summary>Un morceau de texte d'une ligne de notes, avec sa mise en forme.</summary>
/// <param name="Text">Texte.</param>
/// <param name="Bold">Gras (**…**).</param>
/// <param name="Italic">Italique (*…* ou _…_).</param>
/// <param name="Code">Code (`…`).</param>
/// <param name="LinkTarget">Adresse d'un lien [texte](adresse) : affichée en infobulle, jamais suivie automatiquement.</param>
public sealed record MarkdownRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? LinkTarget = null);

/// <summary>Sorte de bloc.</summary>
public enum MarkdownBlockKind
{
    /// <summary>Paragraphe.</summary>
    Paragraph,
    /// <summary>Titre (niveau 1 à 3).</summary>
    Heading,
    /// <summary>Élément de liste à puces ou numérotée.</summary>
    ListItem,
    /// <summary>Bloc de code (```).</summary>
    CodeBlock,
    /// <summary>Séparateur (---).</summary>
    Rule,
}

/// <summary>Un bloc des notes de version.</summary>
/// <param name="Kind">Sorte de bloc.</param>
/// <param name="Level">Niveau d'un titre (1 à 3) ou profondeur d'une liste (0, 1…).</param>
/// <param name="Runs">Contenu mis en forme.</param>
/// <param name="Marker">Puce ou numéro d'un élément de liste (« • », « 1. »).</param>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, int Level, IReadOnlyList<MarkdownRun> Runs, string Marker = "");

/// <summary>
/// Markdown minimal pour les notes d'une release : titres, paragraphes, listes, gras, italique, code, liens, séparateurs. Pur, sans HTML : tout autre balisage est
/// du texte. Rien n'est exécuté ni chargé (images ignorées), les liens ne sont jamais ouverts automatiquement.
/// </summary>
public static class SimpleMarkdown
{
    /// <summary>Nombre maximal de blocs rendus (une note aberrante ne fige pas l'interface).</summary>
    public const int MaxBlocks = 400;

    /// <summary>Analyse le texte en blocs.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(text)) return blocks;
        var paragraph = new List<string>();
        var code = new List<string>();
        bool inCode = false;

        void Flush()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, 0, Inline(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (blocks.Count >= MaxBlocks) break;
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) { blocks.Add(new MarkdownBlock(MarkdownBlockKind.CodeBlock, 0, new[] { new MarkdownRun(string.Join("\n", code), Code: true) })); code.Clear(); }
                else Flush();
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.Add(raw); continue; }
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0) { Flush(); continue; }
            if (trimmed is "---" or "***" or "___") { Flush(); blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, 0, Array.Empty<MarkdownRun>())); continue; }
            int hashes = trimmed.TakeWhile(c => c == '#').Count();
            if (hashes is >= 1 and <= 6 && trimmed.Length > hashes && trimmed[hashes] == ' ')
            {
                Flush();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, Math.Min(hashes, 3), Inline(trimmed[(hashes + 1)..].Trim().TrimEnd('#').TrimEnd())));
                continue;
            }
            int indent = line.Length - trimmed.Length;
            if (ListMarker(trimmed, out var marker, out var rest))
            {
                Flush();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, Math.Min(indent / 2, 3), Inline(rest), marker));
                continue;
            }
            paragraph.Add(trimmed);
        }
        if (inCode && code.Count > 0) blocks.Add(new MarkdownBlock(MarkdownBlockKind.CodeBlock, 0, new[] { new MarkdownRun(string.Join("\n", code), Code: true) }));
        Flush();
        return blocks.Take(MaxBlocks).ToList();
    }

    static bool ListMarker(string line, out string marker, out string rest)
    {
        marker = rest = "";
        if (line.Length > 2 && line[0] is '-' or '*' or '+' && line[1] == ' ') { marker = "•"; rest = line[2..].Trim(); return true; }
        int digits = line.TakeWhile(char.IsAsciiDigit).Count();
        if (digits is >= 1 and <= 3 && line.Length > digits + 1 && line[digits] == '.' && line[digits + 1] == ' ') { marker = line[..(digits + 1)]; rest = line[(digits + 2)..].Trim(); return true; }
        return false;
    }

    /// <summary>Mise en forme d'une ligne : <c>**gras**</c>, <c>*italique*</c>, <c>_italique_</c>, <c>`code`</c>, <c>[texte](adresse)</c>.</summary>
    public static IReadOnlyList<MarkdownRun> Inline(string text)
    {
        var runs = new List<MarkdownRun>();
        var plain = new StringBuilder();
        void Emit(bool bold = false, bool italic = false, bool code = false, string? link = null, string? value = null)
        {
            if (value == null) { if (plain.Length == 0) return; value = plain.ToString(); plain.Clear(); }
            runs.Add(new MarkdownRun(value, bold, italic, code, link));
        }

        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (c == '`' && Closing(text, i + 1, "`") is { } endCode) { Emit(); Emit(code: true, value: text[(i + 1)..endCode]); i = endCode + 1; continue; }
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*' && Closing(text, i + 2, "**") is { } endBold && endBold > i + 2)
            {
                Emit();
                foreach (var inner in Inline(text[(i + 2)..endBold])) runs.Add(inner with { Bold = true });
                i = endBold + 2;
                continue;
            }
            if ((c == '*' || c == '_') && IsEmphasisStart(text, i) && Closing(text, i + 1, c.ToString()) is { } endItalic && endItalic > i + 1)
            {
                Emit();
                foreach (var inner in Inline(text[(i + 1)..endItalic])) runs.Add(inner with { Italic = true });
                i = endItalic + 1;
                continue;
            }
            if (c == '[' && LinkAt(text, i, out var label, out var target, out var next)) { Emit(); Emit(link: target, value: label); i = next; continue; }
            if (c == '!' && i + 1 < text.Length && text[i + 1] == '[' && LinkAt(text, i + 1, out var alt, out _, out var after)) { Emit(); Emit(value: alt); i = after; continue; }   // une image devient son texte alternatif
            plain.Append(c);
            i++;
        }
        Emit();
        return runs;
    }

    static int? Closing(string text, int from, string marker)
    {
        int i = text.IndexOf(marker, from, StringComparison.Ordinal);
        return i < 0 ? null : i;
    }

    // « snake_case_name » n'est pas de l'italique : le soulignement doit commencer un mot
    static bool IsEmphasisStart(string text, int i) => text[i] == '*' || i == 0 || !char.IsLetterOrDigit(text[i - 1]);

    static bool LinkAt(string text, int i, out string label, out string target, out int next)
    {
        label = target = "";
        next = i;
        int close = text.IndexOf(']', i + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;
        int end = text.IndexOf(')', close + 2);
        if (end < 0) return false;
        label = text[(i + 1)..close];
        target = text[(close + 2)..end].Trim();
        next = end + 1;
        return label.Length > 0;
    }
}
