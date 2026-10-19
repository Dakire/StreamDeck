using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TouchDeck.Actions;

public enum ActionOutcome { Success, Failed, Cancelled, TimedOut }

/// <summary>Résultat de l'exécution d'un bouton. <see cref="Step"/> : étape en échec (0 = première), -1 sinon.</summary>
public sealed record ActionResult(ActionOutcome Outcome, string? Message = null, int Step = -1)
{
    public bool Ok => Outcome == ActionOutcome.Success;
}

/// <summary>Navigation demandée par les actions « page » et « profil » : c'est le deck qui l'applique.</summary>
public interface INavigator
{
    void GoToPage(string name);
    void SwitchProfile(string name);
}

/// <summary>Services partagés par toutes les exécutions.</summary>
public sealed class ActionContext
{
    public required PlatformServices Platform { get; init; }
    public required HttpClient Http { get; init; }
    public Countdowns Countdowns { get; init; } = new();
    /// <summary>Attente annulable (remplaçable dans les tests pour ne pas attendre réellement).</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}

/// <summary>Ce qu'une étape connaît de son exécution : le bouton pressé, la navigation et les services.</summary>
public sealed record StepContext(DeckButton Button, INavigator Navigator, ActionContext Services);

/// <summary>Un type d'action (« hotkey », « http »…). Le type est l'identifiant stable écrit dans config.json.</summary>
public interface IActionHandler
{
    string Type { get; }
    /// <summary>Durée maximale d'une étape ; null = sans limite (pause, minuteur).</summary>
    TimeSpan? Timeout { get; }
    /// <summary>Exécute l'étape ; retourne un message d'information à afficher (état du micro…) ou null. Lève une exception en cas d'échec.</summary>
    Task<string?> ExecuteAsync(DeckAction action, StepContext context, CancellationToken cancellation);
}

/// <summary>
/// Exécute les étapes d'un bouton dans l'ordre : chaque étape a un délai maximal, la première en échec arrête la séquence,
/// et tout peut être annulé (fermeture de l'application). Ne dépend ni de WPF ni de Windows.
/// </summary>
public sealed class ActionEngine : IDisposable
{
    readonly Dictionary<string, IActionHandler> _handlers;
    CancellationTokenSource _life = new();

    public ActionContext Context { get; }

    public ActionEngine(ActionContext context, IEnumerable<IActionHandler>? handlers = null)
    {
        Context = context;
        _handlers = (handlers ?? BuiltInActions.All).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Types => _handlers.Keys;

    public async Task<ActionResult> RunAsync(DeckButton button, INavigator navigator, CancellationToken cancellation = default)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, cancellation);
        var ctx = new StepContext(button, navigator, Context);
        string? info = null;
        // Copie : l'éditeur peut modifier le bouton pendant une longue séquence
        var steps = button.Actions.ToList();
        for (int i = 0; i < steps.Count; i++)
        {
            var action = steps[i];
            if (!_handlers.TryGetValue(action.Type ?? "", out var handler))
                return new ActionResult(ActionOutcome.Failed, $"Type d'action inconnu : « {action.Type} »", i);

            using var step = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            if (handler.Timeout is { } limit) step.CancelAfter(limit);
            try
            {
                run.Token.ThrowIfCancellationRequested();
                info = await handler.ExecuteAsync(action, ctx, step.Token) ?? info;
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
                return new ActionResult(ActionOutcome.Cancelled, "Exécution annulée", i);
            }
            catch (OperationCanceledException) when (step.IsCancellationRequested)
            {
                return new ActionResult(ActionOutcome.TimedOut, $"Délai dépassé ({handler.Timeout!.Value.TotalSeconds:0} s)", i);
            }
            catch (Exception ex)
            {
                return new ActionResult(ActionOutcome.Failed, ex.Message, i);
            }
        }
        return new ActionResult(ActionOutcome.Success, info);
    }

    /// <summary>Interrompt toutes les exécutions en cours (les suivantes fonctionnent normalement).</summary>
    public void CancelAll()
    {
        var old = _life;
        _life = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
    }

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
    }
}
