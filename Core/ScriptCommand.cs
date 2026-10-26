using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StreamDak;

/// <summary>Programme à lancer et ses arguments, un par élément : jamais une ligne de commande assemblée à la main.</summary>
/// <param name="FileName">Programme (powershell.exe, cmd.exe ou le script lui-même).</param>
/// <param name="Arguments">Arguments, transmis un à un (<c>ProcessStartInfo.ArgumentList</c>) : le système les échappe.</param>
public sealed record ScriptLaunch(string FileName, IReadOnlyList<string> Arguments);

/// <summary>
/// Construit le lancement d'une action « script » sans concaténer de ligne de commande : un fichier .ps1 passe à PowerShell par
/// <c>-File</c>, un .bat ou .cmd est lancé directement, une commande en ligne est un seul argument de <c>-Command</c>.
/// Les arguments saisis sont découpés (espaces, guillemets) puis transmis séparément. Pour un .bat ou .cmd, que cmd.exe
/// interprète lui-même, les caractères de commande (<c>&amp; | &lt; &gt; ^ %</c>) sont refusés.
/// </summary>
public static class ScriptCommand
{
    /// <summary>Longueur maximale d'une commande ou d'un argument.</summary>
    public const int MaxLength = 8192;

    const string CmdMetaCharacters = "&|<>^%\"";

    /// <summary>Lancement d'un script ou d'une commande ; lève <see cref="ArgumentException"/> avec un message affichable si la valeur est refusée.</summary>
    public static ScriptLaunch Build(string? value, string? arguments)
    {
        value = value?.Trim() ?? "";
        if (value.Length == 0) throw new ArgumentException("Indique une commande PowerShell ou un fichier de script");
        if (value.Length > MaxLength || (arguments?.Length ?? 0) > MaxLength) throw new ArgumentException("Commande ou arguments trop longs");
        if (value.Any(c => c == '\0')) throw new ArgumentException("La commande contient un caractère interdit");

        var tokens = SplitArguments(arguments);
        switch (Path.GetExtension(value).ToLowerInvariant())
        {
            case ".ps1":
                RejectLineBreaks(value);
                // « -File » : le chemin est un seul argument, pas du code ; les arguments suivent un à un
                return new ScriptLaunch("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", value }.Concat(tokens).ToList());
            case ".bat" or ".cmd":
                RejectLineBreaks(value);
                RejectCmdMeta(value, "Le chemin du script");
                foreach (var t in tokens) RejectCmdMeta(t, "Les arguments d'un .bat ou .cmd");
                // Lancé directement (pas de « cmd /c » assemblé) : le système appelle cmd.exe avec les arguments échappés
                return new ScriptLaunch(value, tokens);
            default:
                // Commande en ligne : le code est celui de l'utilisateur, passé en un seul argument ; les arguments saisis ne s'y ajoutent pas
                if (tokens.Count > 0) throw new ArgumentException("Une commande PowerShell en ligne n'accepte pas d'arguments : mets-les dans la commande");
                return new ScriptLaunch("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", value });
        }
    }

    /// <summary>Découpe des arguments saisis : séparés par des espaces, un groupe entre guillemets reste ensemble (les guillemets disparaissent).</summary>
    public static List<string> SplitArguments(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var current = new StringBuilder();
        bool quoted = false, started = false;
        foreach (char c in text)
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started) { result.Add(current.ToString()); current.Clear(); started = false; }
            }
            else { current.Append(c); started = true; }
        }
        if (started) result.Add(current.ToString());
        return result;
    }

    static void RejectLineBreaks(string value)
    {
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Le chemin du script ne peut pas contenir de saut de ligne");
    }

    static void RejectCmdMeta(string text, string what)
    {
        foreach (char c in text)
            if (CmdMetaCharacters.Contains(c) || c is '\n' or '\r')
                throw new ArgumentException($"{what} ne peut pas contenir « {c} » (caractère interprété par cmd.exe)");
    }
}
