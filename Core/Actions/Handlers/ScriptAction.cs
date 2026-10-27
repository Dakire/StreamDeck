using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDak.Actions;

/// <summary>
/// Exécute un fichier .ps1, .bat ou .cmd, ou une commande PowerShell, avec les droits de l'utilisateur. Le script est observé <see cref="Observe"/> :
/// terminé en erreur, la touche affiche l'échec avec son code de sortie et sa dernière ligne d'erreur ; terminé, sa première ligne de sortie ; toujours en
/// cours, il continue seul. La sortie n'est jamais journalisée (elle peut contenir des données de l'utilisateur).
/// </summary>
public sealed class ScriptAction : ActionHandler
{
    /// <summary>Temps pendant lequel le script est observé avant de le laisser continuer seul.</summary>
    public static readonly TimeSpan Observe = TimeSpan.FromSeconds(3);

    /// <summary>Type <see cref="ActionTypes.Script"/>.</summary>
    public ScriptAction() : base(ActionTypes.Script, ActionTimeouts.Launch) { }

    /// <inheritdoc/>
    public override async Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation)
    {
        var result = await Task.Run(() => context.Services.Platform.Launcher.RunScriptObserved(action.Value, action.Args, Observe), cancellation).WaitAsync(cancellation);
        return Describe(result);
    }

    /// <summary>Message de la touche pour un résultat ; lève <see cref="InvalidOperationException"/> si le script a échoué.</summary>
    public static string? Describe(ScriptResult result)
    {
        if (!result.Finished) return "Script lancé : il continue en arrière-plan";
        if (result.ExitCode != 0)
        {
            var why = LastLine(result.Error) ?? LastLine(result.Output);
            throw new InvalidOperationException($"Le script a échoué (code {result.ExitCode})" + (why != null ? " : " + why : ""));
        }
        return FirstLine(result.Output) is { } first ? "Script terminé : " + first : "Script terminé";
    }

    static string? FirstLine(string text) => Short(text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0));
    static string? LastLine(string text) => Short(text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0));
    static string? Short(string? line) => line == null ? null : line.Length <= 160 ? line : line[..157] + "…";
}
